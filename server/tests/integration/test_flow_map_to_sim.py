"""ITC-01~07, 17 (통합 테스트 케이스 7장). VR 계약(docs/vr_client_api.md) 기준 E2E: 문장 -> 지도 -> 확인 -> 시뮬레이션."""
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
FULL = "랙 8줄 3단, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽, 구역은 하나"


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


def make_map(c, ws, text=FULL, confirm=True):
    """문장 -> (필요하면 '나머지는 기본값'으로 답해) map_ready -> 확인. 반환: map_ready 메시지"""
    c.post("/map/text", json={"session_id": SID, "text": text})
    m, _ = until(ws, "map_ready", "question", "error")
    if m["type"] == "question":
        c.post("/map/answer", json={"session_id": SID, "question_id": m["question_id"], "text": "나머지는 기본값"})
        m, _ = until(ws, "map_ready", "question", "error")
    assert m["type"] == "map_ready", m
    if confirm:
        c.post("/map/confirm", json={"session_id": SID, "map_version": m["map_version"]})
    return m


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
        assert m["type"] == "question" and m["errors"] == ["MISSING_INFO"]
        assert set(m["missing"]) == {"aisle_width", "dock_in", "dock_out", "levels", "charge", "zones"}


def test_empty_text_400(make_client):
    assert make_client().post("/map/text", json={"session_id": SID, "text": " "}).status_code == 400


def test_full_sentence_makes_map_without_question(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": FULL})
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and m["defaults_applied"] == []
        assert m["scenario_defaults"]["robots"] == 10          # 도크 3개 x 2 + 4


def test_use_defaults_sentence_skips_question(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "알아서 기본값으로 만들어줘"})
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and m["defaults_applied"]


def test_missing_info_asked_until_complete(make_client):
    """W2: 단 수, 구역이 빠짐 -> 되묻기 -> 일부만 답하면 남은 항목만 다시 묻기 -> 완성."""
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": W2})
        q, _ = until(ws, "question", "map_ready")
        assert q["type"] == "question" and q["missing"] == ["levels", "zones"]
        c.post("/map/answer", json={"session_id": SID, "question_id": q["question_id"], "text": "3단으로 해줘"})
        q2, _ = until(ws, "question", "map_ready")
        assert q2["type"] == "question" and q2["missing"] == ["zones"]
        c.post("/map/answer", json={"session_id": SID, "question_id": q2["question_id"], "text": "구역은 하나"})
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and len(m["map"]["racks"]) == 8 and m["defaults_applied"] == []


def test_question_limit_fills_defaults(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "랙 4줄"})
        for _ in range(3):
            q, _ = until(ws, "question", "map_ready")
            assert q["type"] == "question"
            c.post("/map/answer", json={"session_id": SID, "question_id": q["question_id"], "text": "음 글쎄요"})
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and m["defaults_applied"]


def test_e2e_w2_text_to_sim_compare_analyze(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": W2})
        q, seen = until(ws, "question", "map_ready", "error")
        assert q["type"] == "question" and any(x["type"] == "status" for x in seen)
        c.post("/map/answer", json={"session_id": SID, "question_id": q["question_id"], "text": "나머지는 기본값"})
        m, _ = until(ws, "map_ready", "question", "error")
        assert m["type"] == "map_ready" and m["map"]["width"] == 38
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
        assert m["type"] == "compare" and m["improvement_pct"] > 0

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


def test_m02_validation_question_then_answer(make_client):
    """도크를 0개로 말하면(정보는 다 있음) 검증 오류 -> 되묻기."""
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "랙 6줄 3단, 통로 폭 3미터, 도크는 빼고, 충전은 오른쪽, 구역은 하나"})
        q, _ = until(ws, "question", "map_ready")
        assert q["type"] == "question" and set(q["errors"]) == {"NO_DOCK_IN", "NO_DOCK_OUT"}
        c.post("/map/answer", json={"session_id": SID, "question_id": q["question_id"], "text": "입하 도크 1개, 출하 도크 1개로 해줘"})
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready"
        assert c.post("/map/answer", json={"session_id": SID, "question_id": q["question_id"], "text": "x"}).status_code == 409


def test_w3_one_way_simulates(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = make_map(c, ws, "랙 6줄을 좌우 두 구역으로 나누고, 가운데 큰 통로는 북쪽 방향 일방통행")
        assert m["map"]["rules"]["one_way"]
        c.post("/scenario", json={"session_id": SID, "map_version": m["map_version"], "robots": 4})
        m, _ = until(ws, "sim_ready", "error")
        assert m["type"] == "sim_ready"


def test_scenario_text_uses_recommendation_and_runs_flag(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = make_map(c, ws, confirm=False)
        v = m["map_version"]
        assert c.post("/scenario/text", json={"session_id": SID, "map_version": v, "text": "로봇 6대"}).status_code == 409
        c.post("/map/confirm", json={"session_id": SID, "map_version": v})
        c.post("/scenario/text", json={"session_id": SID, "map_version": v, "text": "입하 30건 출하 20건으로 실행해줘"})
        sc, _ = until(ws, "scenario", "error")
        assert sc["type"] == "scenario" and sc["run"] is True
        assert (sc["robots"], sc["inbound"], sc["outbound"]) == (10, 30, 20)   # 로봇은 권장값
        # VR 현재 값이 있으면 말한 항목만 바뀐다
        c.post("/scenario/text", json={"session_id": SID, "map_version": v, "text": "로봇 열두 대",
                                       "robots": 6, "inbound": 40, "outbound": 40})
        sc, _ = until(ws, "scenario", "error")
        assert (sc["robots"], sc["inbound"], sc["outbound"], sc["run"]) == (12, 40, 40, False)


def test_llm_used_and_fallback(make_client):
    reply = {"racks": 4, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "levels": 3, "zones": 1,
             "charge": "right", "one_way": None, "use_defaults": False}
    c = make_client(llm=FakeLLM([reply, {"racks": "많이"}]))
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "선반 네 줄"})
        m, seen = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and len(m["map"]["racks"]) == 4
        assert not any(x.get("node") == "regex_fallback" for x in seen)
        c.post("/map/text", json={"session_id": SID, "text": FULL})     # 스키마 오류 응답 -> 정규식
        m, seen = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready" and len(m["map"]["racks"]) == 8
        assert any(x.get("node") == "regex_fallback" for x in seen)


def test_voice_stt(make_client):
    c = make_client(stt=FakeSTT({"audio.wav": FULL}))
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/voice", data={"session_id": SID}, files={"audio": ("a.wav", wav(), "audio/wav")})
        t, _ = until(ws, "transcript")
        assert t["text"] == FULL
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] == "map_ready"
        c.post("/map/voice", data={"session_id": SID}, files={"audio": ("a.wav", wav(amp=0), "audio/wav")})
        t, _ = until(ws, "transcript")
        e, _ = until(ws, "error")
        assert t["text"] == "" and e["code"] == "STT_EMPTY"


def test_scenario_voice(make_client):
    c = make_client(stt=FakeSTT({"audio.wav": "로봇 여섯 대로 돌려줘"}))
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = make_map(c, ws)
        c.post("/scenario/voice", data={"session_id": SID, "map_version": m["map_version"], "robots": "4",
                                        "inbound": "25", "outbound": "25"},
               files={"audio": ("a.wav", wav(), "audio/wav")})
        t, _ = until(ws, "transcript")
        sc, _ = until(ws, "scenario", "error")
        assert t["text"] == "로봇 여섯 대로 돌려줘" and (sc["robots"], sc["run"]) == (6, True)


def test_final_report_ws_and_html(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = make_map(c, ws)
        r = c.post("/scenario", json={"session_id": SID, "map_version": m["map_version"], "robots": 4,
                                      "inbound": 10, "outbound": 10})
        sim_id = r.json()["sim_id"]
        until(ws, "sim_ready")
        c.post("/report", json={"session_id": SID, "sim_id": sim_id})
        rep, _ = until(ws, "report", "error")
        assert rep["type"] == "report" and rep["sim_id"] == sim_id
        assert rep["kpis"]["orders_done"] == 20 and len(rep["robots"]) == 4 and rep["comparison"]
        assert rep["html_url"] == f"/sim/{sim_id}/report.html"
        html = c.get(rep["html_url"])
        assert html.status_code == 200 and "text/html" in html.headers["content-type"]
        assert c.get(f"/sim/{sim_id}/report").json()["kpis"] == rep["kpis"]
        # 이벤트 뒤에는 다시 계산
        c.post(f"/sim/{sim_id}/event", json={"session_id": SID, "t": 10, "add_orders": 4, "robots": 4})
        until(ws, "sim_ready")
        assert c.get(f"/sim/{sim_id}/report").json()["kpis"]["orders_done"] == 24


def test_history_saved_and_reloaded_after_restart(tmp_path):
    """시뮬레이션이 SQLite 에 저장되고, 서버를 새로 띄워도 목록, 동선, 리포트, 다시 불러오기, 재계획이 된다."""
    def app():
        c = TestClient(create_app(db_path=tmp_path / "h.db", llm=None, stt=None, node_delay=0))
        c.__enter__()
        return c
    c = app()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = make_map(c, ws)
        sim_id = c.post("/scenario", json={"session_id": SID, "map_version": m["map_version"], "robots": 3,
                                           "inbound": 6, "outbound": 6}).json()["sim_id"]
        until(ws, "sim_ready")
        c.post("/report", json={"session_id": SID, "sim_id": sim_id})
        until(ws, "report")
    c.__exit__(None, None, None)

    c2 = app()                                              # 서버 재시작
    try:
        sims = c2.get("/sims").json()["sims"]
        assert sims[0]["sim_id"] == sim_id and sims[0]["completed"]
        frames = c2.get(f"/sim/{sim_id}/frames", params={"from": 0, "to": 5}).json()
        assert frames[0]["t"] == 0 and len(frames) == 6
        routes = c2.get(f"/sim/{sim_id}/routes").json()["robots"]
        assert len(routes) == 3 and all(r["path"][0][0] == 0 for r in routes)
        csv_text = c2.get(f"/sim/{sim_id}/routes.csv").text
        assert csv_text.lstrip("\ufeff").startswith("t,robot,x,y,state,task_id")
        assert "로봇 동선" in c2.get(f"/sim/{sim_id}/routes.html").text
        assert sim_id in c2.get("/sims.html").text
        assert c2.get(f"/sim/{sim_id}/report").json()["kpis"]["orders_done"] == 12
        new_sid = "afterrestart1"
        with c2.websocket_connect(f"/ws/{new_sid}") as ws:
            c2.post(f"/sim/{sim_id}/load", json={"session_id": new_sid})
            mr, _ = until(ws, "map_ready")
            sr, _ = until(ws, "sim_ready")
            assert mr["confirmed"] is True and sr["sim_id"] == sim_id and sr["history"] is True
            c2.post(f"/sim/{sim_id}/event", json={"session_id": new_sid, "t": 5, "add_orders": 2, "robots": 3})
            ev, _ = until(ws, "sim_ready", "error")
            assert ev["type"] == "sim_ready" and ev["replan_from"] == 5
        new = c2.post("/scenario", json={"session_id": new_sid, "map_version": m["map_version"], "robots": 2,
                                         "inbound": 2, "outbound": 2}).json()["sim_id"]
        assert new != sim_id                                # 재시작 뒤에도 ID 가 겹치지 않음
    finally:
        c2.__exit__(None, None, None)


def test_scenario_spec_mix(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        plain = make_map(c, ws)
        r = c.post("/scenario", json={"session_id": SID, "map_version": plain["map_version"], "robots": 3, "spec_b_pct": 30})
        assert r.status_code == 400 and r.json()["error"]["code"] == "SPEC_UNAVAILABLE"
        m = make_map(c, ws, "랙 8줄 3단, 그중 1200 규격 랙 2줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전은 오른쪽, 구역은 하나")
        assert m["scenario_defaults"]["spec_b_pct"] == 25
        sim_id = c.post("/scenario", json={"session_id": SID, "map_version": m["map_version"], "robots": 6,
                                           "inbound": 10, "outbound": 10, "spec_b_pct": 30}).json()["sim_id"]
        until(ws, "sim_ready")
        rep = c.get(f"/sim/{sim_id}/report").json()
        assert rep["orders"]["by_spec"]["pallet_1200x1000"]["total"] == 6 and rep["kpis"]["rule_violations"] == 0
        c.post("/scenario/text", json={"session_id": SID, "map_version": m["map_version"], "text": "1200 규격 50%"})
        sc, _ = until(ws, "scenario")
        assert sc["spec_b_pct"] == 50


def test_vr_user_panel_contract(make_client):
    """백향 VR 사용자 패널이 쓰는 선택 항목: question.options, /map/voice stt_only, proposal.effects"""
    c = make_client(stt=FakeSTT({"audio.wav": FULL}))
    with c.websocket_connect(f"/ws/{SID}") as ws:
        c.post("/map/text", json={"session_id": SID, "text": "랙 6줄, 통로 폭 3미터"})
        q, _ = until(ws, "question")
        assert 1 <= len(q["options"]) <= 3 and q["options"][-1]["label"] == "나머지는 기본값"
        c.post("/map/text", json={"session_id": SID, "text": "랙 6줄 3단, 통로 폭 3미터, 도크는 빼고, 충전은 오른쪽, 구역은 하나"})
        q, _ = until(ws, "question")
        assert q["options"][0]["label"] == "각각 1개"
        c.post("/map/voice", data={"session_id": SID, "stt_only": "true"}, files={"audio": ("a.wav", wav(), "audio/wav")})
        t, seen = until(ws, "transcript")
        assert t["text"] == FULL and t["stt_only"] is True
        m = make_map(c, ws)
        sim_id = c.post("/scenario", json={"session_id": SID, "map_version": m["map_version"], "robots": 12,
                                           "inbound": 15, "outbound": 15}).json()["sim_id"]
        until(ws, "sim_ready")
        c.post("/analyze", json={"session_id": SID, "sim_id": sim_id})
        a, _ = until(ws, "analysis")
        assert a["proposals"] and all(p["effects"] for p in a["proposals"])
