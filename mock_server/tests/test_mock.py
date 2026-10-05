"""모의 서버 테스트 - 개발자 테스트 시나리오의 TC 번호를 이름에 붙였다.
실행: cd mock_server && pytest -q
"""
import io
import math
import struct
import sys
import wave
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from mock.engine import simulate, check_collisions  # noqa: E402
from mock.mapgen import generate_map  # noqa: E402
from mock.parse_text import extract_requirements, fill_defaults  # noqa: E402
from mock.validator import validate_map, grid_of  # noqa: E402

W = {
    "W1": "랙 4줄, 통로 폭 3m, 입하 도크 1개, 출하 도크 1개로 만들어줘",
    "W2": "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽 벽 쪽에",
    "W3": "랙 6줄을 좌우 두 구역으로 나누고 가운데 큰 통로는 북쪽 방향 일방통행",
    "W4": "랙 10줄을 통로 없이 붙여서 배치해줘",
    "W5": "랙 6줄, 통로 폭 3m",
    "W6": "적당한 크기로 창고 하나 만들어줘",
}


def make(key):
    req, applied = fill_defaults(extract_requirements(W[key]))
    return generate_map(req), applied


def codes(r):
    return {e["code"] for e in r["errors"]}


# ---------------------------------------------------------------- 지도

@pytest.mark.parametrize("key", ["W1", "W2", "W3", "W6"])
def test_map_valid(key):  # TC-MAP-01, 02
    m, _ = make(key)
    assert validate_map(m)["valid"]


def test_map_w4_unreachable():  # TC-MAP-05
    m, _ = make("W4")
    r = validate_map(m)
    assert "UNREACHABLE" in codes(r)


def test_map_w5_no_dock():  # TC-MAP-06, 08
    m, _ = make("W5")
    assert {"NO_DOCK_IN", "NO_DOCK_OUT"} <= codes(validate_map(m))


def test_map_deterministic():  # TC-MAP-12
    assert make("W2")[0] == make("W2")[0]


def test_extract_w2():  # TC-MAP-13 요구사항
    r = extract_requirements(W["W2"])
    assert (r["racks"], r["aisle_width"], r["dock_in"], r["dock_out"]) == (8, 3, 2, 1)


def test_defaults_w6():  # TC-MAP-15
    _, applied = make("W6")
    assert any("랙 6줄" in a for a in applied)


def test_aisle_width_3():  # TC-MAP-14
    m, _ = make("W1")
    g = grid_of(m)
    racks_x = sorted({x for (x, y), t in g.items() if t == "rack"})
    assert all(b - a == 4 for a, b in zip(racks_x, racks_x[1:]))  # 랙 1칸 + 통로 3칸


def test_bad_schema():  # TC-MAP-03
    assert codes(validate_map({"height": 3})) == {"SCHEMA_INVALID"}


# ---------------------------------------------------------------- 엔진

def assert_invariants(log, m):
    g = grid_of(m)
    frames = log["frames"]
    for f in frames:
        pos = [(r["x"], r["y"]) for r in f["robots"]]
        assert len(pos) == len(set(pos)), f"칸 충돌 t={f['t']}"
        for p in pos:
            assert g.get(p) not in ("wall", "rack"), f"금지 칸 t={f['t']} {p}"
    for a, b in zip(frames, frames[1:]):
        prev = {r["id"]: (r["x"], r["y"]) for r in a["robots"]}
        for r in b["robots"]:
            if r["id"] in prev:
                px, py = prev[r["id"]]
                assert abs(r["x"] - px) + abs(r["y"] - py) <= 1, f"순간이동 t={b['t']}"
    assert check_collisions(frames) == []
    done = [o["order_id"] for o in log["orders"] if o["done_t"] is not None]
    assert len(done) == len(set(done)) == len(log["orders"]), "주문 누락"


CASES = [("W1", 2, 10), ("W2", 4, 25), ("W2", 8, 25), ("W3", 4, 25)]


@pytest.mark.parametrize("key,robots,n", CASES)
@pytest.mark.parametrize("strategy", ["optimized", "baseline"])
def test_engine_invariants(key, robots, n, strategy):  # TC-ENG-08, 09, 11
    m, _ = make(key)
    log = simulate(m, {"robots": robots, "inbound": n, "outbound": n}, strategy, 42)
    assert_invariants(log, m)


def test_one_way_respected():  # TC-ENG-10
    m, _ = make("W3")
    ow = {(x, y) for seg in m["rules"]["one_way"] for x in [seg["from"][0]] for y in range(seg["from"][1], seg["to"][1] + 1)}
    assert ow
    log = simulate(m, {"robots": 4, "inbound": 25, "outbound": 25}, "optimized", 42)
    for a, b in zip(log["frames"], log["frames"][1:]):
        prev = {r["id"]: (r["x"], r["y"]) for r in a["robots"]}
        for r in b["robots"]:
            p = prev.get(r["id"])
            if p and p in ow and (r["x"], r["y"]) in ow:
                assert r["y"] >= p[1], f"일방통행 역주행 t={b['t']}"


def test_rolling_replan_prefix_unchanged():  # TC-ENG-13
    m, _ = make("W2")
    sc = {"robots": 4, "inbound": 25, "outbound": 25}
    base = simulate(m, sc, "optimized", 42)
    re = simulate(m, sc, "optimized", 42, [{"t": 100, "add_orders": 10, "robots": 6}])
    assert base["frames"][:100] == re["frames"][:100]
    assert_invariants(re, m)
    assert len(re["orders"]) == 60
    assert max(len(f["robots"]) for f in re["frames"]) == 6


def test_optimized_not_worse_than_baseline():  # TC-ANL-05 전제
    m, _ = make("W2")
    sc = {"robots": 4, "inbound": 25, "outbound": 25}
    assert simulate(m, sc, "optimized", 42)["total_steps"] <= simulate(m, sc, "baseline", 42)["total_steps"]


def test_zero_orders():  # TC-ENG-17
    m, _ = make("W1")
    log = simulate(m, {"robots": 2, "inbound": 0, "outbound": 0}, "optimized", 42)
    assert log["orders"] == []


# ---------------------------------------------------------------- API 흐름 (TestClient 사용)

def _wav(amplitude: float) -> bytes:
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(16000)
        w.writeframes(b"".join(struct.pack("<h", int(amplitude * 32767 * math.sin(i / 8))) for i in range(16000)))
    return buf.getvalue()


@pytest.fixture
def client(monkeypatch):
    monkeypatch.setenv("MOCK_NODE_DELAY", "0")
    import importlib
    import server
    importlib.reload(server)
    from fastapi.testclient import TestClient
    with TestClient(server.app) as c:
        yield c


def recv_until(ws, type_, limit=30):
    for _ in range(limit):
        msg = ws.receive_json()
        if msg["type"] == type_:
            return msg
        assert msg["type"] in {"status", "transcript", "map_ready", "question", "sim_ready", "compare", "analysis", "error"}
    raise AssertionError(f"{type_} 수신 못 함")


def test_api_e2e_flow(client):  # TC-API-01, 04~07, 09, TC-E2E 서버 부분
    with client.websocket_connect("/ws/s1") as ws:
        assert client.post("/map/text", json={"session_id": "s1", "text": ""}).status_code == 400  # TC-API-02
        r = client.post("/map/text", json={"session_id": "s1", "text": W["W2"]})
        assert r.status_code == 200 and "request_id" in r.json()
        ready = recv_until(ws, "map_ready")
        v = ready["map_version"]
        assert client.post("/scenario", json={"session_id": "s1", "map_version": v, "robots": 4, "inbound": 25, "outbound": 25}).status_code == 409  # TC-API-05
        assert client.post("/map/confirm", json={"map_version": "999"}).status_code == 404  # TC-API-04
        assert client.post("/map/confirm", json={"map_version": v}).json()["status"] == "confirmed"
        sim_id = client.post("/scenario", json={"session_id": "s1", "map_version": v, "robots": 4, "inbound": 25, "outbound": 25}).json()["sim_id"]
        sim = recv_until(ws, "sim_ready")
        assert sim["sim_id"] == sim_id
        frames = client.get(f"/sim/{sim_id}/frames?from=0&to=50").json()
        assert len(frames) == 51 and [f["t"] for f in frames] == list(range(51))  # TC-API-07
        assert all(len(f["robots"]) == 4 for f in frames)
        stats = client.get(f"/sim/{sim_id}/stats").json()
        assert all(s["pass"] >= 0 and s["wait"] >= 0 for s in stats)  # TC-API-08

        client.post("/compare", json={"sim_id": sim_id})
        cmp = recv_until(ws, "compare")
        assert "improvement_pct" in cmp

        client.post("/analyze", json={"sim_id": sim_id})
        an = recv_until(ws, "analysis")
        assert an["proposals"]
        new_sim = client.post("/improve/approve", json={"proposal_id": an["proposals"][0]["proposal_id"]}).json()["sim_id"]
        assert recv_until(ws, "sim_ready")["sim_id"] == new_sim


def test_api_question_flow(client):  # TC-AGT-02, 03 (모의)
    with client.websocket_connect("/ws/s2") as ws:
        client.post("/map/text", json={"session_id": "s2", "text": W["W5"]})
        q = recv_until(ws, "question")
        assert "도크" in q["text"]
        client.post("/map/answer", json={"question_id": q["question_id"], "text": "입하 도크 1개, 출하 도크 1개로 해줘"})
        assert recv_until(ws, "map_ready")["map"]["docks"]


def test_api_voice_silence(client):  # TC-EX-03
    with client.websocket_connect("/ws/s3") as ws:
        client.post("/map/voice", data={"session_id": "s3"}, files={"audio": ("v.wav", _wav(0.0), "audio/wav")})
        assert recv_until(ws, "transcript")["text"] == ""
        assert recv_until(ws, "error")["code"] == "STT_EMPTY"


def test_api_voice_ok(client):  # TC-API-03
    with client.websocket_connect("/ws/s4") as ws:
        client.post("/map/voice", data={"session_id": "s4"}, files={"audio": ("v.wav", _wav(0.3), "audio/wav")})
        assert recv_until(ws, "transcript")["text"]
        recv_until(ws, "map_ready")


def test_api_voice_stt_only(client):  # 사용자 UI: 인식 결과 확인 후 생성
    with client.websocket_connect("/ws/s5") as ws:
        client.post("/map/voice", data={"session_id": "s5", "stt_only": "true"},
                    files={"audio": ("v.wav", _wav(0.3), "audio/wav")})
        msg = recv_until(ws, "transcript")
        assert msg["text"] and msg["stt_only"] is True


def test_question_has_options(client):
    with client.websocket_connect("/ws/s6") as ws:
        client.post("/map/text", json={"session_id": "s6", "text": "랙 10줄을 통로 없이 붙여서 배치해줘"})
        q = recv_until(ws, "question")
        assert q["options"] and all("label" in o and "text" in o for o in q["options"])


def test_api_report(client):  # FR-25, FR-28: 리포트 (실제 서버와 같은 필드)
    with client.websocket_connect("/ws/s7") as ws:
        client.post("/map/text", json={"session_id": "s7", "text": "랙 4줄, 통로 폭 3m, 입하 도크 1개, 출하 도크 1개"})
        m = recv_until(ws, "map_ready")
        client.post("/map/confirm", json={"session_id": "s7", "map_version": m["map_version"]})
        client.post("/scenario", json={"session_id": "s7", "map_version": m["map_version"], "robots": 3, "inbound": 6, "outbound": 6})
        sim = recv_until(ws, "sim_ready")
        client.post("/report", json={"session_id": "s7", "sim_id": sim["sim_id"]})
        rep = recv_until(ws, "report")
    assert rep["sim_id"] == sim["sim_id"]
    for key in ("kpis", "robots", "orders", "timeline", "comparison", "insights", "baseline", "scenario"):
        assert key in rep
    assert rep["kpis"]["orders_done"] == rep["orders"]["done"] == 12
    assert len(rep["robots"]) == 3 and rep["comparison"][0]["key"] == "total_steps"
