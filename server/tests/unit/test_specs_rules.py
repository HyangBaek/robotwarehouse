"""제품 규격(FR-11, 12, 15), 엇갈림 가능 구간(FR-17), 규칙 위반 검사, 규격 랙 생성."""
import copy

from planner import Grid, Sim, create_orders
from tools.invariants import check_invariants, rule_violations
from tools.map_generator import generate_grid_map, summarize
from tools.parse_text import extract_requirements
from tools.scenario import check_spec, recommend_scenario, spec_mix

A, B = "pallet_1100x1100", "pallet_1200x1000"


def test_create_orders_spec_mix_exact_and_default_unchanged():
    plain = create_orders(10, 10, 5, 60)
    assert all(o["spec"] == A and o["qty"] == 1 for o in plain)
    mixed = create_orders(10, 10, 5, 60, spec_mix={A: 0.7, B: 0.3})
    assert [(o["type"], o["arrival_t"]) for o in mixed] == [(o["type"], o["arrival_t"]) for o in plain]
    for ty in ("inbound", "outbound"):
        assert sum(1 for o in mixed if o["type"] == ty and o["spec"] == B) == 3


def test_storage_respects_spec():
    """같은 규격 랙에만 보관: 랙별 재고 변화가 그 규격 주문 수와 맞아야 한다."""
    m, _ = generate_grid_map({"racks": 6, "racks_b": 2, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "use_defaults": True})
    sc = {"map_version": "t", "robots": 4, "inbound": 12, "outbound": 12, "spec_mix": spec_mix(30)}
    sim = Sim(m, sc, "optimized", 3)
    before = dict(sim.stock)
    log = sim.run().result()
    assert log["completed"] and log["collisions"] == []
    spec_of = {r["rack_id"]: r["slot_spec"] for r in m["racks"]}
    for spec in (A, B):
        delta = sum(sim.stock[r] - before[r] for r in sim.stock if spec_of[r] == spec)
        n_in = sum(1 for o in log["orders"] if o["spec"] == spec and o["type"] == "inbound")
        n_out = sum(1 for o in log["orders"] if o["spec"] == spec and o["type"] == "outbound")
        assert delta == n_in - n_out


def test_racks_b_generation_and_parsing():
    assert extract_requirements("랙 8줄, 그중 1200 규격 랙 2줄") == {"racks": 8, "racks_b": 2}
    m, _ = generate_grid_map({"racks": 8, "racks_b": 2, "use_defaults": True})
    assert [r["slot_spec"] for r in m["racks"]].count(B) == 2 and "1200x1000 규격 랙 2줄" in summarize(m)
    assert recommend_scenario(m)["spec_b_pct"] == 25
    plain, _ = generate_grid_map({"racks": 8, "use_defaults": True})
    assert check_spec(plain, 30) and check_spec(plain, 0) is None and check_spec(m, 30) is None


def test_passing_allowed_overrides_one_way():
    m, _ = generate_grid_map({"racks": 6, "zones": 2, "one_way": "N", "use_defaults": True})
    lane = m["rules"]["one_way"][0]
    x, y0 = lane["from"]
    y1 = lane["to"][1]
    g = Grid(m)
    i, j = (y0 + 2) * g.W + x, (y0 + 1) * g.W + x
    assert j not in g.adj[i]                                   # 일방통행: 남쪽으로 못 감
    m2 = copy.deepcopy(m)
    m2["rules"]["passing_allowed"] = [[x, y0], [x, y1]]
    g2 = Grid(m2)
    assert j in g2.adj[i]                                      # 엇갈림 가능 구간: 양방향


def test_rule_violation_detected():
    m, _ = generate_grid_map({"racks": 6, "zones": 2, "one_way": "N", "use_defaults": True})
    x, y0 = m["rules"]["one_way"][0]["from"]
    log = {"frames": [{"t": 0, "robots": [{"id": "R1", "x": x, "y": y0 + 2}]},
                      {"t": 1, "robots": [{"id": "R1", "x": x, "y": y0 + 1}]}], "orders": []}
    assert [v["type"] for v in rule_violations(log, m)] == ["one_way"]
    assert any(v["type"] == "one_way" for v in check_invariants(log, m))
    m["rules"]["passing_allowed"] = [[x, y0], [x, y0 + 5]]
    assert rule_violations(log, m) == []
