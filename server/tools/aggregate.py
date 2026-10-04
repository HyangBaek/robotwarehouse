"""aggregate_logs: 처리 시간, 칸별 통과·대기, 병목 후보 (FR-25, FR-26). 숫자는 모두 여기서 계산한다."""


def aggregate_logs(log: dict, grid: dict, top_k: int = 10) -> dict:
    """반환: {"total_steps", "total_wait", "top5_share_pct", "bottlenecks"[{x,y,wait,pass}], "nearest_dock"}"""
    stats = log.get("cell_stats", [])
    top = sorted(stats, key=lambda c: (-c["wait"], c["x"], c["y"]))[:top_k]
    top = [{"x": c["x"], "y": c["y"], "wait": c["wait"], "pass": c.get("pass", 0)} for c in top if c["wait"] > 0]
    total = sum(c["wait"] for c in stats)
    share = round(sum(c["wait"] for c in top[:5]) / total * 100, 1) if total else 0.0
    near = None
    if top and grid.get("docks"):
        t0 = top[0]
        d = min(grid["docks"], key=lambda d: abs(d["x"] - t0["x"]) + abs(d["y"] - t0["y"]))
        near = {"x": d["x"], "y": d["y"], "type": d["type"], "dist": abs(d["x"] - t0["x"]) + abs(d["y"] - t0["y"])}
    return {"total_steps": log.get("total_steps"), "total_wait": total, "top5_share_pct": share,
            "bottlenecks": top, "nearest_dock": near}
