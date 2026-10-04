"""처리 못 한 주문: 결과 전송 -> 원인 표시 -> 분석의 재시뮬레이션 권고 -> 승인하면 다 처리. WS 재연결 시 결과 보관."""
from tests.integration.test_flow_map_to_sim import SID, make_client, make_map, until  # noqa: F401

SMALL = "랙 2줄 3단, 통로 폭 3m, 입하 도크 1개, 출하 도크 1개, 충전 구역은 오른쪽, 구역은 하나"


def _scenario(c, ws, inbound=150, outbound=0):   # 서버 상한 200건, 랙 2줄은 입고 약 30건만 받음
    mr = make_map(c, ws, SMALL)
    body = {"session_id": SID, "map_version": mr["map_version"], "robots": 4, "inbound": inbound, "outbound": outbound}
    sim_id = c.post("/scenario", json=body).json()["sim_id"]
    sr, seen = until(ws, "sim_ready", "error")
    return sim_id, sr, seen


def test_unfinished_sent_with_cause_and_fixed_by_resim(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        sim_id, sr, seen = _scenario(c, ws)
        assert sr["type"] == "sim_ready" and sr["completed"] is False
        assert "보관 공간 부족" in sr["summary"]["처리 못 함"] and sr["issues"][0]["code"] == "no_capacity"
        assert any(m["type"] == "status" and m["node"] == "결과 점검" for m in seen)

        c.post("/analyze", json={"session_id": SID, "sim_id": sim_id})
        an, _ = until(ws, "analysis")
        assert an["explanation"].startswith("보관 공간 부족") and an["issues"]
        assert an["proposals"][0]["type"] in ("map_resize", "order_count")
        fix = next(p for p in an["proposals"] if p["type"] == "order_count")
        assert fix["effects"]

        c.post("/improve/approve", json={"session_id": SID, "proposal_id": fix["proposal_id"]})
        after, _ = until(ws, "sim_ready")
        assert after["completed"] is True and "처리 못 함" not in after["summary"]


def test_rack_resize_recommendation_regenerates_map(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        sim_id, sr, _ = _scenario(c, ws, inbound=60)
        c.post("/analyze", json={"session_id": SID, "sim_id": sim_id})
        an, _ = until(ws, "analysis")
        resize = next(p for p in an["proposals"] if p["type"] == "map_resize")
        c.post("/improve/approve", json={"session_id": SID, "proposal_id": resize["proposal_id"]})
        mr, _ = until(ws, "map_ready", "error")
        assert mr["type"] == "map_ready" and mr.get("confirmed") is True
        after, _ = until(ws, "sim_ready", "error")
        assert after["type"] == "sim_ready" and after["completed"] is True


def test_ws_reconnect_gets_results_sent_while_disconnected(make_client):
    c = make_client()
    c.post("/map/text", json={"session_id": "offline00001", "text": SMALL})   # 연결 전에 끝난 결과
    with c.websocket_connect("/ws/offline00001") as ws:
        m, _ = until(ws, "map_ready", "question")
        assert m["type"] in ("map_ready", "question")
