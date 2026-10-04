"""TC-ENG-01~17 (개발자 테스트 시나리오 2.4). 엔진 = server/planner, 도구 = tools/simulation, 검사기 = tools/invariants."""
from planner import simulate
from tools.invariants import check_invariants
from planner.testmap import make_warehouse
from tools.orders import create_orders
from tools.simulation import run_simulation


def test_tc_eng_08_no_collision(map_w1):
    log = simulate(map_w1, {"robots": 2, "inbound": 10, "outbound": 10}, "optimized", seed=42)
    assert check_invariants(log, map_w1) == []


def test_tc_eng_02_orders_deterministic():
    sc = {"inbound": 5, "outbound": 5, "seed": 7}
    assert create_orders(sc) == create_orders(sc)
    assert len(create_orders(sc)) == 10


def test_tool_run_simulation_ok(map_w1):
    r = run_simulation(map_w1, {"map_version": "t", "robots": 2, "inbound": 5, "outbound": 5})
    assert r["ok"] and r["error"] is None and r["log"]["collisions"] == []


def test_tool_run_simulation_fixed_orders(map_w1):
    orders = [{"order_id": "O001", "type": "inbound", "arrival_t": 0}]
    r = run_simulation(map_w1, {"map_version": "t", "robots": 1}, orders=orders)
    assert r["ok"] and [o["order_id"] for o in r["log"]["orders"]] == ["O001"]


def test_lns_no_crash_when_all_wait():
    """3장 #7: '전원 대기'(비용 0)로 넘어온 경로에서 지연값이 음수여도 LNS 가 ValueError 없이 끝나야 한다."""
    import random
    from planner.engine import Grid, lns_improve
    g = Grid(make_warehouse(n_lines=2, line_len=4, n_in=1, n_out=1, n_charge=2))
    W = 8
    free = [i for i in range(g.N) if g.ok[i]]
    rs = {"R1": (free[0], free[-1], False), "R2": (free[1], free[-2], False)}
    paths = {r: [v[0]] * (W + 1) for r, v in rs.items()}
    costs = {r: 0 for r in rs}
    for seed in range(20):
        lns_improve(g, rs, list(rs), dict(paths), dict(costs), W, 5, random.Random(seed))
