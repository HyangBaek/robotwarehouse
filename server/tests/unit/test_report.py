"""최종 리포트: 숫자가 로그와 일치하는지, 비교, 인사이트, HTML."""
from api.report_html import render
from planner import simulate
from tools.map_generator import generate_grid_map, summarize
from tools.report import build_report


def _report(robots=4):
    m, _ = generate_grid_map({"racks": 4, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "use_defaults": True})
    sc = {"map_version": "t", "robots": robots, "inbound": 8, "outbound": 8}
    o, b = simulate(m, sc, "optimized", 7), simulate(m, sc, "baseline", 7)
    return build_report(o, m, sc, b, summarize(m), "S1"), o, b


def test_report_numbers_match_log():
    r, o, _ = _report()
    k = r["kpis"]
    assert k["total_steps"] == o["total_steps"] and k["orders_done"] == o["meta"]["orders_done"] == 16
    assert len(r["robots"]) == 4
    for rb in r["robots"]:
        assert rb["steps"] == o["total_steps"]
        assert rb["move_loaded"] + rb["move_empty"] + rb["handling"] + rb["wait"] + rb["idle"] == rb["steps"]
        assert abs(rb["utilization_pct"] + rb["wait_pct"] + rb["idle_pct"] - 100) < 0.5
    assert sum(rb["orders"] for rb in r["robots"]) == 16
    assert sum(h["count"] for h in r["orders"]["histogram"]) == 16
    assert r["timeline"][-1]["done"] == 16 and r["timeline"][-1]["t"] == o["total_steps"]
    assert r["orders"]["lead_min"] <= r["orders"]["lead_median"] <= r["orders"]["lead_p90"] <= r["orders"]["lead_max"]


def test_report_comparison_and_insights():
    r, o, b = _report()
    row = r["comparison"][0]
    assert row["key"] == "total_steps" and (row["baseline"], row["optimized"]) == (b["total_steps"], o["total_steps"])
    assert row["better"] == (o["total_steps"] < b["total_steps"])
    pct_rows = [c for c in r["comparison"] if c["unit"] == "%"]
    assert pct_rows and all(c["change_text"].endswith("%p") for c in pct_rows)
    assert any("기준 전략 대비" in i["text"] for i in r["insights"])


def test_report_html_renders():
    r, _, _ = _report()
    html = render(r)
    assert html.startswith("<!doctype html>") and "시뮬레이션 리포트" in html and "<svg" in html
