"""불변 조건 검사 (NFR-04, FR-17). 경로 엔진(planner)과 독립된 검사기로 시뮬레이션 로그를 다시 확인한다.

planner.find_collisions(점, 맞교환 충돌)에 더해 금지 칸 진입, 순간이동, 주문 누락, 중복 완료,
운영 규칙 위반(일방통행 역주행, 엇갈림 가능 구간은 예외)까지 본다.
"""
BLOCKED = {"wall", "rack"}


def one_way_map(grid: dict) -> dict[tuple[int, int], tuple[int, int]]:
    """칸 -> 허용 방향 (dx, dy). rules.one_way 의 from -> to 벡터로 방향을 정하고,
    rules.passing_allowed 구간(두 점씩 묶은 직선)은 양방향이라 뺀다."""
    ow = {}
    rules = grid.get("rules") or {}
    for r in rules.get("one_way", []) or []:
        (x1, y1), (x2, y2) = r["from"], r["to"]
        d = ((x2 > x1) - (x2 < x1), (y2 > y1) - (y2 < y1))
        for y in range(min(y1, y2), max(y1, y2) + 1):
            for x in range(min(x1, x2), max(x1, x2) + 1):
                ow[(x, y)] = d
    pts = [tuple(p) for p in rules.get("passing_allowed", []) or []]
    for k in range(0, len(pts), 2):
        (x1, y1), (x2, y2) = pts[k], pts[min(k + 1, len(pts) - 1)]
        if x1 != x2 and y1 != y2:
            ow.pop((x1, y1), None)
            ow.pop((x2, y2), None)
            continue
        for y in range(min(y1, y2), max(y1, y2) + 1):
            for x in range(min(x1, x2), max(x1, x2) + 1):
                ow.pop((x, y), None)
    return ow


def rule_violations(log: dict, grid: dict) -> list[dict]:
    """일방통행 역주행. 두 칸이 모두 일방통행 구간이면 그 방향으로만 움직여야 한다 (엔진과 같은 정의)."""
    ow = one_way_map(grid)
    if not ow:
        return []
    out = []
    frames = log["frames"]
    for a, b in zip(frames, frames[1:]):
        prev = {r["id"]: (r["x"], r["y"]) for r in a["robots"]}
        for r in b["robots"]:
            p, q = prev.get(r["id"]), (r["x"], r["y"])
            if p is None or p == q or p not in ow or q not in ow:
                continue
            if (q[0] - p[0], q[1] - p[1]) != ow[p]:
                out.append({"t": b["t"], "type": "one_way", "robot": r["id"], "from": p, "to": q})
    return out


def check_invariants(log: dict, grid: dict) -> list[dict]:
    """위반 목록을 반환한다. 빈 목록이면 통과."""
    types = {(c["x"], c["y"]): c["type"] for c in grid["cells"]}
    frames = log["frames"]
    v = []
    for f in frames:
        pos = [(r["x"], r["y"]) for r in f["robots"]]
        if len(pos) != len(set(pos)):
            v.append({"t": f["t"], "type": "vertex"})
        for p in pos:
            if types.get(p, "aisle") in BLOCKED:
                v.append({"t": f["t"], "type": "blocked", "cell": p})
    for a, b in zip(frames, frames[1:]):
        prev = {r["id"]: (r["x"], r["y"]) for r in a["robots"]}
        curr = {r["id"]: (r["x"], r["y"]) for r in b["robots"]}
        for rid, (x, y) in curr.items():
            if rid in prev:
                px, py = prev[rid]
                if abs(x - px) + abs(y - py) > 1:
                    v.append({"t": b["t"], "type": "teleport", "robot": rid})
        ids = sorted(set(curr) & set(prev))
        for i, r1 in enumerate(ids):
            for r2 in ids[i + 1:]:
                if prev[r1] == curr[r2] and prev[r2] == curr[r1] and prev[r1] != prev[r2]:
                    v.append({"t": b["t"], "type": "swap", "robots": (r1, r2)})
    v += rule_violations(log, grid)
    done = [o["order_id"] for o in log.get("orders", []) if o.get("done_t") is not None]
    if len(done) != len(set(done)) or len(done) != len(log.get("orders", [])):
        v.append({"type": "orders"})
    return v
