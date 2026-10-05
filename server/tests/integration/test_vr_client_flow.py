"""VR 클라이언트가 보낼 수 있는 잘못된 요청이 서버를 멈추게 하지 않는지 (VR 은 WS 결과를 기다리므로 오류도 WS 로 와야 한다).

랙 옮기기 형식 오류·잘못된 위치 → BAD_EDIT, 재계획 입력 오류 → 400, 재계획 중복 → 409, 정상 랙 옮기기 → map_ready,
개선안 "도크 추가" 승인 뒤에도 VR 에서 옮긴 랙 위치 유지.
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


def test_dock_add_keeps_rack_moved_in_vr(make_client):
    """VR 랙 옮기기 -> 확인 -> 시뮬레이션 -> 개선안 '도크 추가' 승인: 새 지도에 옮긴 위치가 그대로 남는다."""
    c = make_client()
    with c.websocket_connect(f"/ws/{SID}") as ws:
        m = _map(c, ws)
        frm, to = _rack_and_aisle(m)
        c.post("/map/edit", json={"session_id": SID, "map_version": m["map_version"],
                                  "moves": [{"rack_id": "R01", "from": frm, "to": to}]})
        m, _ = until(ws, "map_ready", "question", "error")
        if m["type"] != "map_ready":                    # 옮긴 위치가 막히면 되묻기 (이 테스트 범위 밖)
            return
        v = m["map_version"]
        c.post("/map/confirm", json={"session_id": SID, "map_version": v})
        c.post("/scenario", json={"session_id": SID, "map_version": v, "robots": 4, "inbound": 6, "outbound": 6})
        s, _ = until(ws, "sim_ready", "error")
        assert s["type"] == "sim_ready"
        store = c.app.state.deps.store                  # 분석 결과와 무관하게 도크 추가 개선안을 직접 넣는다
        e = store.get_sim(s["sim_id"])
        e.proposals["PTEST"] = {"proposal_id": "PTEST", "type": "dock_add", "text": "출하 도크 1개 추가",
                                "apply": {"dock": "dock_out"}, "reason": "", "effects": []}
        store.proposals["PTEST"] = e.sim_id
        c.post("/improve/approve", json={"session_id": SID, "proposal_id": "PTEST"})
        m2, seen = until(ws, "map_ready", "error")
        assert m2["type"] == "map_ready" and m2.get("confirmed") is True
        cells = {(x["x"], x["y"]): x["type"] for x in m2["map"]["cells"]}
        assert cells.get(tuple(to)) == "rack" and cells.get(tuple(frm), "aisle") != "rack"
        assert any("위치를 그대로 유지" in (x.get("message") or "") for x in seen if x["type"] == "status")
        s2, _ = until(ws, "sim_ready", "error")
        assert s2["type"] == "sim_ready"
