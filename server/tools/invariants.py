"""불변 조건 검사 (NFR-04). 경로 엔진(planner)과 독립된 검사기로 시뮬레이션 로그를 다시 확인한다.

planner.find_collisions(점·맞교환 충돌)에 더해 금지 칸 진입, 순간이동, 주문 누락·중복 완료까지 본다.
"""
BLOCKED = {"wall", "rack"}


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
    done = [o["order_id"] for o in log.get("orders", []) if o.get("done_t") is not None]
    if len(done) != len(set(done)) or len(done) != len(log.get("orders", [])):
        v.append({"type": "orders"})
    return v
