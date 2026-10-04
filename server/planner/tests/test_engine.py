"""pytest -q test_engine.py  (또는 python test_engine.py)"""
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))  # planner/ 의 상위 폴더

import time
from planner.engine import simulate, Sim, find_collisions
from planner.testmap import make_warehouse, W1, W2

M1, M2 = make_warehouse(**W1), make_warehouse(**W2)
S1 = dict(robots=2, inbound=10, outbound=10)
S2 = dict(robots=4, inbound=25, outbound=25)
S2H = dict(robots=8, inbound=25, outbound=25)


def _ok(r, n=None):
    assert r["completed"], "미완료 주문 있음"
    assert r["collisions"] == [], r["collisions"][:3]
    if n:
        assert r["meta"]["orders_done"] == n


def test_collision_free_all_scenarios_and_seeds():
    for seed in (1, 7, 42):
        _ok(simulate(M1, S1, "optimized", seed), 20)
        _ok(simulate(M2, S2, "optimized", seed), 50)
        _ok(simulate(M2, S2H, "optimized", seed), 50)


def test_every_robot_moves_one_cell_at_most():
    r = simulate(M2, S2H)
    prev = None
    for fr in r["frames"]:
        cur = {x["id"]: (x["x"], x["y"]) for x in fr["robots"]}
        if prev:
            for k, (x, y) in cur.items():
                assert abs(x - prev[k][0]) + abs(y - prev[k][1]) <= 1
        prev = cur


def test_log_consistency():          # ITC-18
    r = simulate(M2, S2)
    assert len(r["frames"]) == r["total_steps"] + 1
    assert len(r["orders"]) == 50
    moves = sum(1 for a, b in zip(r["frames"], r["frames"][1:])
                for p, q in zip(a["robots"], b["robots"]) if (p["x"], p["y"]) != (q["x"], q["y"]))
    assert sum(c["pass"] for c in r["cell_stats"]) == moves


def test_deterministic():
    a, b = simulate(M2, S2), simulate(M2, S2)
    assert a["frames"] == b["frames"]


def test_one_way_respected():
    col = 6
    m = make_warehouse(**W2, one_way_col=col)
    r = simulate(m, S2H)
    _ok(r, 50)
    prev = None
    for fr in r["frames"]:
        cur = {x["id"]: (x["x"], x["y"]) for x in fr["robots"]}
        if prev:
            for k, (x, y) in cur.items():
                px, py = prev[k]
                if x == col and px == col:
                    assert y >= py, "일방통행 역주행"
        prev = cur


def test_rolling_replan_keeps_prefix():   # ITC-20
    sim = Sim(M2, dict(robots=4, inbound=25, outbound=25), "optimized", 42).run()
    before = [f for f in sim.result()["frames"]]
    res = sim.add_event(dict(t=100, add_orders=10, robots=6))
    _ok(res, 60)
    assert res["frames"][:101] == before[:101], "t<=100 프레임이 바뀜"
    assert len(res["frames"][101]["robots"]) == 6
    assert res["meta"]["events"][0]["replan_s"] is not None


def test_baseline_also_collision_free():
    _ok(simulate(M2, S2, "baseline"), 50)


def test_collision_checker_detects():
    f = [dict(t=0, robots=[dict(id="a", x=1, y=1), dict(id="b", x=2, y=1)]),
         dict(t=1, robots=[dict(id="a", x=2, y=1), dict(id="b", x=1, y=1)])]
    assert find_collisions(f)[0]["type"] == "swap"
    f2 = [dict(t=0, robots=[dict(id="a", x=1, y=1), dict(id="b", x=1, y=1)])]
    assert find_collisions(f2)[0]["type"] == "vertex"


if __name__ == "__main__":
    for n, fn in list(globals().items()):
        if n.startswith("test_"):
            t = time.time(); fn(); print("PASS", n, f"{time.time()-t:.1f}s")
