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


def test_pibt_step_never_collides():
    """PIBT 한 스텝: 무작위 배치, 목표 200회에서 점, 맞교환 충돌 없음, 이웃 칸으로만 이동, 고정 로봇은 제자리."""
    import random
    from planner.engine import Grid, find_collisions, pibt_step
    g = Grid(make_warehouse(n_lines=4, line_len=8, n_in=1, n_out=1, n_charge=8))
    free = [i for i in range(g.N) if g.ok[i]]
    for trial in range(200):
        rng = random.Random(trial)
        n = rng.randint(2, 25)
        cells = rng.sample(free, n)
        rs = {f"R{i}": (cells[i], rng.choice(free) if rng.random() < 0.8 else -1, rng.random() < 0.1) for i in range(n)}
        nx = pibt_step(g, rs, {r: rng.random() for r in rs}, rng)
        xy = lambda c: (c % g.W, c // g.W)
        frames = [{"t": t, "robots": [{"id": r, "x": xy(c)[0], "y": xy(c)[1]} for r, c in cs.items()]}
                  for t, cs in ((0, {r: v[0] for r, v in rs.items()}), (1, nx))]
        assert find_collisions(frames) == []
        assert all(nx[r] == v[0] or nx[r] in g.adj[v[0]] for r, v in rs.items())
        assert all(nx[r] == v[0] for r, v in rs.items() if v[2])


def test_fallback_and_dock_options_keep_invariants(map_w1):
    sc = {"map_version": "t", "robots": 3, "inbound": 8, "outbound": 8}
    base = simulate(map_w1, sc, "optimized", seed=3, config={"fallback": "wait"})
    assert simulate(map_w1, sc, "optimized", seed=3)["frames"] == base["frames"]   # 평소엔 대체 경로 미사용
    for cfg in ({"dock_limit": 1}, {"dock_limit": 2, "dock_select": "least_loaded"}):
        log = simulate(map_w1, sc, "optimized", seed=3, config=cfg)
        assert log["completed"] and check_invariants(log, map_w1) == []
