"""pytest -q test_cbs.py  (또는 python test_cbs.py)"""
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))  # planner/ 의 상위 폴더

import itertools, random, time
from planner.engine import Grid, INF
from planner.cbs import solve_cbs, solve_pp, find_conflict, valid_paths, path_cost, random_instance
from planner.testmap import make_warehouse, W1


def tiny_map(open_cells, w=8, h=5):
    open_cells = set(open_cells)
    cells = [dict(x=x, y=y, type="wall") for x in range(w) for y in range(h) if (x, y) not in open_cells]
    return dict(width=w, height=h, cells=cells, racks=[], docks=[
        dict(dock_id="I", type="dock_in", x=0, y=0), dict(dock_id="O", type="dock_out", x=0, y=1)])


def brute_force_soc(g, starts, goals, T=14):
    """두 로봇의 전 상태를 시간 순으로 훑는 완전탐색. (위치a, 위치b, 마지막 비목표 시각a, b) -> 최소 SOC 확인"""
    (a, b), (ga, gb) = list(starts.values()), list(goals.values())
    la0 = 0 if a != ga else -1
    lb0 = 0 if b != gb else -1
    layer = {(a, b, la0, lb0)}
    best = INF
    for t in range(T):
        nxt = set()
        for (pa, pb, la, lb) in layer:
            for na in g.adjw[pa]:
                for nb in g.adjw[pb]:
                    if na == nb or (na == pb and nb == pa and na != nb):
                        continue
                    nla = t + 1 if na != ga else la
                    nlb = t + 1 if nb != gb else lb
                    nxt.add((na, nb, nla, nlb))
        layer = nxt
        for (pa, pb, la, lb) in layer:
            if pa == ga and pb == gb:
                best = min(best, (la + 1) + (lb + 1))
    return best


def test_optimal_vs_bruteforce():
    # 1칸 복도 + 옆 포켓 + 막다른 길 : 비켜 주기가 필요한 구조
    cells = [(x, 1) for x in range(1, 6)] + [(3, 2), (3, 3), (6, 1)]
    g = Grid(tiny_map(cells))
    xy = lambda x, y: y * g.W + x
    cases = [
        ({"A": xy(1, 1), "B": xy(5, 1)}, {"A": xy(5, 1), "B": xy(1, 1)}),   # 정면 교행
        ({"A": xy(1, 1), "B": xy(4, 1)}, {"A": xy(6, 1), "B": xy(2, 1)}),
        ({"A": xy(2, 1), "B": xy(3, 1)}, {"A": xy(3, 1), "B": xy(2, 1)}),   # 맞교환 필요
        ({"A": xy(1, 1), "B": xy(3, 3)}, {"A": xy(3, 3), "B": xy(1, 1)}),
    ]
    for s, gl in cases:
        r = solve_cbs(g, s, gl, timeout=20)
        assert r["status"] == "optimal", r["status"]
        assert find_conflict(r["paths"]) is None and valid_paths(g, r["paths"], s, gl)
        assert r["soc"] == brute_force_soc(g, s, gl), (r["soc"], brute_force_soc(g, s, gl))


def test_cbs_never_worse_than_pp_and_conflict_free():
    g = Grid(make_warehouse(**W1))
    rng = random.Random(5)
    for n in (2, 3, 4, 5):
        for _ in range(15):
            s, gl = random_instance(g, n, rng)
            c = solve_cbs(g, s, gl, timeout=10)
            p = solve_pp(g, s, gl, 120, 20)
            assert c["status"] == "optimal"
            assert find_conflict(c["paths"]) is None and valid_paths(g, c["paths"], s, gl)
            if p["paths"] is not None:
                assert find_conflict(p["paths"]) is None
                assert c["soc"] <= p["soc"], "CBS가 PP보다 나쁨(최적성 위반)"


def test_one_way_respected_by_cbs():
    m = make_warehouse(**W1, one_way_col=6)
    g = Grid(m)
    rng = random.Random(3)
    for _ in range(10):
        s, gl = random_instance(g, 3, rng)
        r = solve_cbs(g, s, gl, timeout=10)
        assert r["status"] == "optimal"
        for p in r["paths"].values():
            for u, v in zip(p, p[1:]):
                if u % g.W == 6 and v % g.W == 6:
                    assert v // g.W >= u // g.W


def test_timeout_reported_not_hung():
    g = Grid(make_warehouse(n_lines=8, aisle=1, line_len=12, n_in=1, n_out=1, n_charge=8))
    rng = random.Random(1)
    s, gl = random_instance(g, 10, rng)
    t = time.time()
    r = solve_cbs(g, s, gl, timeout=1.0)
    assert time.time() - t < 3
    assert r["status"] in ("optimal", "timeout", "node_limit")


if __name__ == "__main__":
    for n, fn in list(globals().items()):
        if n.startswith("test_"):
            t = time.time(); fn(); print("PASS", n, f"{time.time()-t:.1f}s")
