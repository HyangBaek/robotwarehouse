"""
cbs.py - CBS(Conflict-Based Search) 비교용 구현 (로봇웨어하우스, 하재윤 담당)

용도: "우선순위 계획(PP), PP+LNS의 해 품질이 최적해에 얼마나 가까운가"를 소규모 인스턴스에서 보이는 비교 기준선.
      엔진(engine.py)의 격자, 일방통행, 충돌 규칙(점 충돌 + 맞교환)을 그대로 쓴다.

참고: Sharon, Stern, Felner, Sturtevant (2015). Conflict-Based Search for Optimal Multi-Agent Pathfinding. AI 219.

구조
  low_level   : 제약(점/간선)을 지키는 시공간 A* (단일 로봇)
  solve_cbs   : 상위 탐색. 가장 이른 충돌 -> 두 로봇에 제약을 하나씩 걸어 분기. 비용 합(SOC) 최소 우선
  solve_pp    : 같은 인스턴스를 PP (+LNS)로 푼다 (engine의 plan_window, lns_improve 재사용)
  benchmark   : 무작위 인스턴스로 CBS 대비 PP, PP+LNS의 품질(SOC 비율)과 시간을 표로 만든다

가정: 로봇은 목표에 도착하면 그 칸에 계속 머문다 (엔진의 목표 보유 규칙과 같다).
      비용 = 목표에 마지막으로 도착한 스텝. 비용 합(SOC)과 makespan을 함께 기록한다.
"""
from __future__ import annotations

import heapq
import random
import time
from itertools import count

from .engine import INF, Grid, lns_improve, plan_window


# --------------------------------------------------------------------------- 공통
def path_cost(p):
    """목표에 마지막으로 도착한 스텝 (뒤쪽 대기는 제외)."""
    i = len(p) - 1
    while i > 0 and p[i] == p[i - 1]:
        i -= 1
    return i


def _at(p, t):
    return p[t] if t < len(p) else p[-1]


def find_conflict(paths: dict):
    """가장 이른 충돌 하나. 없으면 None.
    ("v", a, b, 칸, t) 또는 ("e", a, b, (a이전, a현재), (b이전, b현재), t-1)"""
    ids = list(paths)
    T = max(len(p) for p in paths.values())
    for t in range(T):
        for x in range(len(ids)):
            a = ids[x]
            pa = _at(paths[a], t)
            for y in range(x + 1, len(ids)):
                b = ids[y]
                pb = _at(paths[b], t)
                if pa == pb:
                    return ("v", a, b, pa, t)
                if t > 0:
                    qa, qb = _at(paths[a], t - 1), _at(paths[b], t - 1)
                    if qa == pb and qb == pa:
                        return ("e", a, b, (qa, pa), (qb, pb), t - 1)
    return None


def count_conflicts(paths: dict) -> int:
    ids = list(paths)
    T = max(len(p) for p in paths.values())
    n = 0
    for t in range(T):
        for x in range(len(ids)):
            pa = _at(paths[ids[x]], t)
            for y in range(x + 1, len(ids)):
                pb = _at(paths[ids[y]], t)
                if pa == pb:
                    n += 1
                elif t > 0 and _at(paths[ids[x]], t - 1) == pb and _at(paths[ids[y]], t - 1) == pa:
                    n += 1
    return n


def valid_paths(g: Grid, paths: dict, starts: dict, goals: dict) -> bool:
    """이동이 그래프 간선인지, 시작, 목표가 맞는지 검사."""
    for a, p in paths.items():
        if p[0] != starts[a] or p[-1] != goals[a]:
            return False
        for u, v in zip(p, p[1:]):
            if v != u and v not in g.adj[u]:
                return False
    return True


# --------------------------------------------------------------------------- 저수준: 제약 시공간 A*
def low_level(g: Grid, start: int, goal: int, cv: frozenset, ce: frozenset, max_t: int):
    """cv: {(칸, t)} 그 시각에 그 칸에 있을 수 없음. ce: {(u, v, t)} t->t+1에 u->v 이동 불가.
    반환: 경로(마지막 도착 스텝까지) 또는 None."""
    h = g.dist(goal)
    if h[start] >= INF:
        return None
    glast = max((t for (c, t) in cv if c == goal), default=-1)
    adjw = g.adjw
    heap = [(h[start], 0, start)]
    par = {(start, 0): None}
    while heap:
        f, nt, i = heapq.heappop(heap)
        t = -nt
        if i == goal and t > glast:
            path, k = [], (i, t)
            while k is not None:
                path.append(k[0])
                k = par[k]
            path.reverse()
            return path
        if t >= max_t:
            continue
        for j in adjw[i]:
            if (j, t + 1) in cv or (j != i and (i, j, t) in ce):
                continue
            k1 = (j, t + 1)
            if k1 in par or h[j] >= INF:
                continue
            par[k1] = (i, t)
            heapq.heappush(heap, (t + 1 + h[j], -(t + 1), j))
    return None


# --------------------------------------------------------------------------- 상위: CBS
def solve_cbs(g: Grid, starts: dict, goals: dict, timeout: float = 10.0, max_t: int = 150,
              max_nodes: int = 200000) -> dict:
    """반환: status(optimal/timeout/node_limit/no_solution), paths, soc, makespan, nodes, sec"""
    t0 = time.perf_counter()
    ids = list(starts)
    empty = (frozenset(), frozenset())
    cons = {a: empty for a in ids}
    paths = {}
    for a in ids:
        p = low_level(g, starts[a], goals[a], *cons[a], max_t)
        if p is None:
            return dict(status="no_solution", paths=None, soc=None, makespan=None, nodes=0,
                        sec=round(time.perf_counter() - t0, 4))
        paths[a] = p
    tie = count()
    soc = sum(len(p) - 1 for p in paths.values())
    heap = [(soc, count_conflicts(paths), next(tie), cons, paths)]
    nodes = 0
    while heap:
        if time.perf_counter() - t0 > timeout:
            return dict(status="timeout", paths=None, soc=None, makespan=None, nodes=nodes,
                        sec=round(time.perf_counter() - t0, 4))
        if nodes >= max_nodes:
            return dict(status="node_limit", paths=None, soc=None, makespan=None, nodes=nodes,
                        sec=round(time.perf_counter() - t0, 4))
        soc, _, _, cons, paths = heapq.heappop(heap)
        nodes += 1
        c = find_conflict(paths)
        if c is None:
            return dict(status="optimal", paths=paths, soc=soc,
                        makespan=max(len(p) - 1 for p in paths.values()),
                        nodes=nodes, sec=round(time.perf_counter() - t0, 4))
        if c[0] == "v":
            _, a, b, cell, t = c
            options = [(a, ("v", cell, t)), (b, ("v", cell, t))]
        else:
            _, a, b, (au, av), (bu, bv), t = c
            options = [(a, ("e", au, av, t)), (b, ("e", bu, bv, t))]
        for ag, con in options:
            cv, ce = cons[ag]
            if con[0] == "v":
                cv = cv | {(con[1], con[2])}
            else:
                ce = ce | {(con[1], con[2], con[3])}
            p = low_level(g, starts[ag], goals[ag], cv, ce, max_t)
            if p is None:
                continue
            ncons = dict(cons)
            ncons[ag] = (cv, ce)
            npaths = dict(paths)
            npaths[ag] = p
            nsoc = soc - (len(paths[ag]) - 1) + (len(p) - 1)
            heapq.heappush(heap, (nsoc, count_conflicts(npaths), next(tie), ncons, npaths))
    return dict(status="no_solution", paths=None, soc=None, makespan=None, nodes=nodes,
                sec=round(time.perf_counter() - t0, 4))


# --------------------------------------------------------------------------- 비교 대상: PP, PP+LNS
def solve_pp(g: Grid, starts: dict, goals: dict, W: int = 120, lns_iters: int = 0, seed: int = 0) -> dict:
    t0 = time.perf_counter()
    rs = {a: (starts[a], goals[a], False) for a in starts}
    order = sorted(rs, key=lambda a: -g.dist(goals[a])[starts[a]])
    rng = random.Random(seed)
    out, attempts = None, 0
    while True:
        out, failed = plan_window(g, rs, order, W)
        if out:
            break
        attempts += 1
        if attempts <= len(order):
            order.remove(failed)
            order.insert(0, failed)
        elif attempts <= len(order) + 5:
            rng.shuffle(order)
        else:
            return dict(status="fail", paths=None, soc=None, makespan=None,
                        sec=round(time.perf_counter() - t0, 4))
    paths, costs = out
    if lns_iters and len(order) > 1:
        paths, costs = lns_improve(g, rs, order, paths, costs, W, lns_iters, rng)
    trimmed = {a: p[:path_cost(p) + 1] for a, p in paths.items()}
    return dict(status="ok", paths=trimmed, soc=sum(len(p) - 1 for p in trimmed.values()),
                makespan=max(len(p) - 1 for p in trimmed.values()),
                sec=round(time.perf_counter() - t0, 4))


# --------------------------------------------------------------------------- 인스턴스 생성, 벤치마크
def random_instance(g: Grid, n: int, rng: random.Random, pool: list[int] | None = None):
    pool = pool or [i for i in range(g.N) if g.ok[i] and g.type[i] == "aisle"]
    for _ in range(1000):
        starts = rng.sample(pool, n)
        goals = rng.sample(pool, n)
        if all(g.dist(goals[k])[starts[k]] < INF for k in range(n)):
            return {f"R{k + 1}": starts[k] for k in range(n)}, {f"R{k + 1}": goals[k] for k in range(n)}
    raise RuntimeError("도달 가능한 인스턴스를 만들지 못함")


def benchmark(map_json: dict, agents=(2, 3, 4, 5, 6), instances: int = 20, seed: int = 0,
              timeout: float = 10.0, lns_iters: int = 50, region: tuple | None = None) -> list[dict]:
    """
    region=(x0, x1, y0, y1)로 시작, 목표 칸을 좁은 구역에 몰아 혼잡을 높일 수 있다.
    품질 비교는 CBS가 최적해를 찾은 인스턴스만 대상으로, 같은 인스턴스끼리 합산해 계산한다.
    soc_ratio = PP의 SOC 합 / CBS의 SOC 합 (1.00이면 최적과 같음)
    """
    g = Grid(map_json)
    pool = None
    if region:
        x0, x1, y0, y1 = region
        pool = [i for i in range(g.N) if g.ok[i] and g.type[i] == "aisle"
                and x0 <= i % g.W <= x1 and y0 <= i // g.W <= y1]
    rows = []
    for n in agents:
        rng = random.Random(seed * 1000 + n)
        recs, unsolved, pp_fail, invalid = [], 0, 0, 0
        for k in range(instances):
            starts, goals = random_instance(g, n, rng, pool)
            c = solve_cbs(g, starts, goals, timeout)
            p0 = solve_pp(g, starts, goals, 120, 0, seed + k)
            p1 = solve_pp(g, starts, goals, 120, lns_iters, seed + k)
            for r in (p0, p1):
                if r["paths"] is None:
                    pp_fail += 1
                elif find_conflict(r["paths"]) or not valid_paths(g, r["paths"], starts, goals):
                    invalid += 1
            if c["status"] != "optimal":
                unsolved += 1
                continue
            assert find_conflict(c["paths"]) is None and valid_paths(g, c["paths"], starts, goals)
            if p0["paths"] is not None and p1["paths"] is not None:
                recs.append((c, p0, p1))
        tot = lambda key, idx: sum(r[idx][key] for r in recs)
        rows.append(dict(
            agents=n, instances=instances, cbs_solved=instances - unsolved, cbs_unsolved=unsolved,
            pp_failures=pp_fail, invalid=invalid, compared=len(recs),
            cbs_soc=_avg([r[0]["soc"] for r in recs]),
            pp_soc=_avg([r[1]["soc"] for r in recs]),
            pp_lns_soc=_avg([r[2]["soc"] for r in recs]),
            pp_ratio=round(tot("soc", 1) / tot("soc", 0), 3) if recs else None,
            pp_lns_ratio=round(tot("soc", 2) / tot("soc", 0), 3) if recs else None,
            pp_optimal_pct=round(100 * sum(r[1]["soc"] == r[0]["soc"] for r in recs) / len(recs), 1) if recs else None,
            pp_lns_optimal_pct=round(100 * sum(r[2]["soc"] == r[0]["soc"] for r in recs) / len(recs), 1) if recs else None,
            cbs_sec=_avg([r[0]["sec"] for r in recs], 3), pp_sec=_avg([r[1]["sec"] for r in recs], 4),
            pp_lns_sec=_avg([r[2]["sec"] for r in recs], 3), cbs_nodes=_avg([r[0]["nodes"] for r in recs], 1)))
    return rows


def _avg(xs, nd=2):
    return round(sum(xs) / len(xs), nd) if xs else None


def print_table(rows):
    print(f"{'로봇':>4} {'CBS해결':>7} {'비교':>4} {'PP/CBS':>7} {'PP+LNS/CBS':>10} {'PP최적%':>7} {'LNS최적%':>8} "
          f"{'CBS초':>7} {'PP초':>7} {'LNS초':>7} {'노드':>8} {'오류':>4}")
    for r in rows:
        print(f"{r['agents']:>4} {r['cbs_solved']:>4}/{r['instances']:<2} {r['compared']:>4} {str(r['pp_ratio']):>7} "
              f"{str(r['pp_lns_ratio']):>10} {str(r['pp_optimal_pct']):>7} {str(r['pp_lns_optimal_pct']):>8} "
              f"{str(r['cbs_sec']):>7} {str(r['pp_sec']):>7} {str(r['pp_lns_sec']):>7} {str(r['cbs_nodes']):>8} "
              f"{r['pp_failures'] + r['invalid']:>4}")
