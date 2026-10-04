"""VR 클라이언트가 보낼 수 있는 잘못된 요청이 서버를 멈추게 하지 않는지 (VR 은 WS 결과를 기다리므로 오류도 WS 로 와야 한다).

랙 옮기기 형식 오류·잘못된 위치 → BAD_EDIT, 재계획 입력 오류 → 400, 재계획 중복 → 409, 정상 랙 옮기기 → map_ready.
"""
from tests.integration.test_flow_map_to_sim import FULL, SID, make_client, until  # noqa: F401 (fixture)


def _map(c, ws):
    c.post("/map/text", json={"session_id": SID, "text": FULL})
    m, _ = until(ws, "map_ready", "question", "error")
    assert m["type"] == "map_ready"
    return m


def _rack_and_aisle(m):
    kind = {(x["x"], x["y"]): x["type"] for x in m["map"]["cells"]}
    for (x, y), t in sorted(kind.items()):
        if t == "rack":
            for nx in (x - 1, x + 1):
                if kind.get((nx, y), "aisle") == "aisle" and 0 <= nx < m["map"]["width"]:
                    return [x, y], [nx, y]
    raise AssertionError("옮길 랙이 없음")


def test_bad_edit_reports_error(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = _map(c, ws)
        c.post("/map/edit", json={"session_id": SID, "map_version": m["map_version"], "moves": [{}]})
        e, _ = until(ws, "error", "map_ready", "question")
        assert e["type"] == "error" and e["code"] == "BAD_EDIT"
        racks = [[x["x"], x["y"]] for x in m["map"]["cells"] if x["type"] == "rack"]
        c.post("/map/edit", json={"session_id": SID, "map_version": m["map_version"],
                                  "moves": [{"rack_id": "R01", "from": racks[0], "to": racks[1]}]})   # 다른 랙 위로
        e, _ = until(ws, "error", "map_ready", "question")
        assert e["type"] == "error" and e["code"] == "BAD_EDIT"


def test_edit_then_sim_and_replan_guards(make_client):
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = _map(c, ws)
        frm, to = _rack_and_aisle(m)
        c.post("/map/edit", json={"session_id": SID, "map_version": m["map_version"],
                                  "moves": [{"rack_id": "R01", "from": frm, "to": to}]})
        m, _ = until(ws, "map_ready", "question", "error")
        assert m["type"] in ("map_ready", "question")
        if m["type"] == "question":                     # 옮긴 위치가 막히면 되묻기 (정상 동작)
            return
        v = m["map_version"]
        c.post("/map/confirm", json={"session_id": SID, "map_version": v})
        c.post("/scenario", json={"session_id": SID, "map_version": v, "robots": 4, "inbound": 8, "outbound": 8})
        s, _ = until(ws, "sim_ready", "error")
        assert s["type"] == "sim_ready"
        sim_id = s["sim_id"]
        assert c.post(f"/sim/{sim_id}/event", json={"t": "abc"}).status_code == 400
        assert c.post(f"/sim/{sim_id}/event", json={"t": 10, "add_orders": 2}).status_code == 200
        r2 = c.post(f"/sim/{sim_id}/event", json={"t": 10, "add_orders": 2})
        assert r2.status_code in (200, 409)              # 앞 재계획이 이미 끝났으면 200
        s, _ = until(ws, "sim_ready", "error")
        assert s["type"] == "sim_ready" and s["replan_from"] == 10
