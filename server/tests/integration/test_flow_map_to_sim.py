"""ITC-01~07, 17 (통합 테스트 케이스 7장). VR 계약(docs/vr_client_api.md) 기준 E2E: 문장 → 지도 → 확인 → 시뮬레이션."""
import io
import math
import struct
import wave

import pytest
from fastapi.testclient import TestClient

from api.main import create_app
from tests.fakes import FakeLLM, FakeSTT

SID = "testsession1"
W2 = "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽 벽 쪽에"


@pytest.fixture
def make_client(tmp_path):
    """with 블록 안에서 써야 한 이벤트 루프를 공유해 백그라운드 Agent 작업이 끝까지 돈다."""
    opened = []

    def make(llm=None, stt=None):
        c = TestClient(create_app(db_path=tmp_path / "t.db", llm=llm, stt=stt, node_delay=0))
        c.__enter__()
        opened.append(c)
        return c
    yield make
    for c in opened:
        c.__exit__(None, None, None)


def until(ws, *types):
    """status 등은 건너뛰고 원하는 type 메시지를 받는다. 지나간 메시지도 함께 돌려준다."""
    seen = []
    while True:
        m = ws.receive_json()
        seen.append(m)
        if m["type"] in types:
            return m, seen


def wav(seconds=0.5, amp=8000):
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1), w.setsampwidth(2), w.setframerate(16000)
        n = int(16000 * seconds)
        w.writeframes(struct.pack(f"<{n}h", *(int(amp * math.sin(i / 8)) for i in range(n))))
    return buf.getvalue()


def test_itc02_map_text(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        r = c.post("/map/text", json={"session_id": SID, "text": "랙 4줄"})
        assert r.status_code == 200 and r.json()["request_id"]
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "question"          # 도크 미언급 → 되묻기 (M02)


def test_empty_text_400(make_client):
    assert make_client().post("/map/text", json={"session_id": SID, "text": " "}).status_code == 400


def test_e2e_w2_text_to_sim_compare_analyze(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": W2})
        m, seen = until(ws, "map_ready", "question", "error")
        assert m["type"] == "map_ready" and m["map"]["width"] == 38
        assert any(x["type"] == "status" for x in seen)
        v = m["map_version"]
        assert c.post("/scenario", json={"session_id": SID, "map_version": v, "robots": 4}).status_code == 409
        assert c.post("/map/confirm", json={"session_id": SID, "map_version": v}).json()["status"] == "confirmed"
        r = c.post("/scenario", json={"session_id": SID, "map_version": v, "robots": 4, "inbound": 10,
                                      "outbound": 10, "strategy": "optimized"})
        sim_id = r.json()["sim_id"]
        m, _ = until(ws, "sim_ready", "error")
        assert m["type"] == "sim_ready" and m["sim_id"] == sim_id and m["total_steps"] > 0
        frames = c.get(f"/sim/{sim_id}/frames", params={"from": 0, "to": 199}).json()
        assert frames[0]["t"] == 0 and {"id", "x", "y", "state", "task_id"} <= set(frames[0]["robots"][0])
        stats = c.get(f"/sim/{sim_id}/stats").json()
        assert stats and {"x", "y", "pass", "wait"} <= set(stats[0])

        c.post("/compare", json={"session_id": SID, "sim_id": sim_id})
        m, _ = until(ws, "compare", "error")
        assert m["type"] == "compare" and "improvement_pct" in m

        c.post(f"/sim/{sim_id}/event", json={"session_id": SID, "t": 20, "add_orders": 6, "robots": 6})
        m, _ = until(ws, "sim_ready", "error")
        assert m["type"] == "sim_ready" and m["replan_from"] == 20
        f2 = c.get(f"/sim/{sim_id}/frames", params={"from": 0, "to": 20}).json()
        assert f2 == frames[:21]                # 이벤트 이전 프레임은 그대로

        c.post("/analyze", json={"session_id": SID, "sim_id": sim_id})
        m, _ = until(ws, "analysis", "error")
        assert m["type"] == "analysis" and m["proposals"]
        r = c.post("/improve/approve", json={"session_id": SID, "proposal_id": m["proposals"][0]["proposal_id"]})
        m, _ = until(ws, "sim_ready", "error")
        assert m["type"] == "sim_ready" and m["sim_id"] == r.json()["sim_id"]


def test_m02_question_then_answer(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "랙 6줄, 통로 폭 3미터"})
        q, _ = until(ws, "question", "map_ready")
        assert q["type"] == "question" and set(q["errors"]) == {"NO_DOCK_IN", "NO_DOCK_OUT"}
        c.post("/map/answer", json={"session_id": SID, "question_id": q["question_id"], "text": "입하 도크 1개, 출하 도크 1개로 해줘"})
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready"
        assert c.post("/map/answer", json={"session_id": SID, "question_id": q["question_id"], "text": "x"}).status_code == 409


def test_w3_one_way_simulates(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "랙 6줄을 좌우 두 구역으로 나누고, 가운데 큰 통로는 북쪽 방향 일방통행"})
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and m["map"]["rules"]["one_way"]
        c.post("/map/confirm", json={"session_id": SID, "map_version": m["map_version"]})
        c.post("/scenario", json={"session_id": SID, "map_version": m["map_version"], "robots": 4})
        m, _ = until(ws, "sim_ready", "error")
        assert m["type"] == "sim_ready"


def test_llm_used_and_fallback(make_client):
    reply = {"racks": 4, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "levels": None, "zones": None,
             "charge": None, "one_way": None, "use_defaults": False}
    c = make_client(llm=FakeLLM([reply, {"racks": "많이"}]))
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "선반 네 줄"})
        m, seen = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and len(m["map"]["racks"]) == 4
        assert not any(x.get("node") == "regex_fallback" for x in seen)
        c.post("/map/text", json={"session_id": SID, "text": W2})      # 스키마 오류 응답 → 정규식
        m, seen = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and len(m["map"]["racks"]) == 8
        assert any(x.get("node") == "regex_fallback" for x in seen)


def test_voice_stt(make_client):
    c = make_client(stt=FakeSTT({"audio.wav": W2}))
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/voice", data={"session_id": SID}, files={"audio": ("a.wav", wav(), "audio/wav")})
        t, _ = until(ws, "transcript")
        assert t["text"] == W2
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready"
        c.post("/map/voice", data={"session_id": SID}, files={"audio": ("a.wav", wav(amp=0), "audio/wav")})
        t, _ = until(ws, "transcript")
        e, _ = until(ws, "error")
        assert t["text"] == "" and e["code"] == "STT_EMPTY"
