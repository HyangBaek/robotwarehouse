"""처리 못 한 주문의 원인 분류와 재시뮬레이션 권고 (tools/outcome.py)."""
from planner import simulate
from tools.compare import compare_logs
from tools.map_generator import fill_defaults, generate_map
from tools.outcome import diagnose, explanation_lines, facts_of, summary_line
from tools.parse_text import extract_requirements
from tools.proposals import PROPOSAL_TYPES, apply_proposal

SMALL = "랙 2줄 3단, 통로 폭 3m, 입하 도크 1개, 출하 도크 1개, 충전 구역은 오른쪽, 구역은 하나"


def _small():
    req, _ = fill_defaults(extract_requirements(SMALL))
    return req, generate_map(req)


def _log(done, total, orders, t=100, stopped=None):
    return {"completed": False, "total_steps": t, "orders": orders, "stopped": stopped,
            "meta": {"orders_done": done, "orders_total": total, "config": {"max_steps": 3000}}}


def test_completed_has_no_issue():
    req, m = _small()
    sc = {"robots": 2, "inbound": 5, "outbound": 5}
    d = diagnose(simulate(m, sc), sc, req, m)
    assert d["completed"] and d["issues"] == [] and summary_line(d) == {} and facts_of(d) is None


def test_no_capacity_recommends_more_racks_or_less_inbound():
    req, m = _small()
    sc = {"robots": 4, "inbound": 300, "outbound": 0}
    log = simulate(m, sc)
    d = diagnose(log, sc, req, m)
    it = d["issues"][0]
    assert it["code"] == "no_capacity" and it["owner"] == "창고 조건"
    assert it["count"] == log["meta"]["orders_total"] - log["meta"]["orders_done"]
    types = {r["type"]: r["apply"] for r in d["recommendations"]}
    assert types["map_resize"]["racks"] > req["racks"] and types["order_count"]["inbound"] < 300
    assert all(r["type"] in PROPOSAL_TYPES and r["effects"] for r in d["recommendations"])
    assert "보관 공간 부족" in summary_line(d)["처리 못 함"]
    assert facts_of(d)["원인"][0]["누구_문제"] == "창고 조건"


def test_no_stock_recommends_less_outbound():
    req, m = _small()
    sc = {"robots": 4, "inbound": 0, "outbound": 200}
    d = diagnose(simulate(m, sc), sc, req, m)
    assert d["issues"][0]["code"] == "no_stock"
    assert any(r["apply"].get("outbound", 999) < 200 for r in d["recommendations"])


def test_max_steps_and_engine_reasons():
    d = diagnose(_log(8, 10, [{"status": "done"}] * 8, t=3000), {"robots": 1})
    assert d["issues"][0]["code"] == "max_steps" and d["recommendations"][0]["apply"] == {"robots": 3}
    dl = diagnose(_log(8, 12, [{"status": "done"}] * 8, t=420, stopped="deadlock"),
                  {"robots": 6}, {"aisle_width": 2})                     # PIBT 엔진처럼 stopped 를 주는 경우
    assert dl["issues"][0]["owner"] == "알고리즘 한계" and dl["issues"][0]["t"] == 420
    assert [r["apply"] for r in dl["recommendations"]] == [{"robots": 4}, {"aisle_width": 3}]
    un = diagnose(_log(0, 3, [{"status": "rejected", "type": "inbound", "reason": "unreachable"}] * 3), {"robots": 2})
    assert un["issues"][0]["owner"] == "지도" and un["recommendations"] == []
    assert any("지도를 직접 고쳐야" in x for x in explanation_lines(un))


def test_spec_named_when_not_default():
    orders = [{"status": "rejected", "type": "inbound", "spec": "pallet_1200x1000"}] * 2
    d = diagnose(_log(0, 2, orders), {"robots": 2, "inbound": 2})
    assert "1200x1000 규격 2건" in d["issues"][0]["label"]


def test_new_proposal_types_apply():
    req, m = _small()
    sc = {"robots": 4, "inbound": 30, "outbound": 10}
    r1 = apply_proposal({"type": "map_resize", "apply": {"racks": 4}}, m, sc, req)
    assert r1["changed"] == "map" and r1["req"]["racks"] == 4 and r1["grid"]["width"] > m["width"]
    r2 = apply_proposal({"type": "order_count", "apply": {"inbound": 10}}, m, sc, req)
    assert r2["changed"] == "scenario" and r2["scenario"]["inbound"] == 10 and sc["inbound"] == 30


def test_compare_not_misleading_when_one_side_unfinished():
    meta = {"avg_order_time": 10, "total_waits": 1, "orders_done": 3, "orders_total": 5}
    done = {"total_steps": 100, "completed": True, "meta": meta}
    cut = {"total_steps": 3000, "completed": False, "meta": meta}
    out = compare_logs(cut, done)
    assert out["improvement_pct"] is None and "기준 전략" in out["note"] and out["baseline"]["완료 주문"] == "3/5"
