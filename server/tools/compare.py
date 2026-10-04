"""compare_strategies: 기준 전략 대비 개선율 (FR-28)."""
from planner import compare as _compare


def improvement_pct(baseline: float, optimized: float) -> float:
    """(기준 - 최적화) / 기준 * 100. 기준이 0이면 0."""
    if baseline == 0:
        return 0.0
    return round((baseline - optimized) / baseline * 100, 1)


def compare_logs(baseline: dict, optimized: dict) -> dict:
    """같은 지도, 주문으로 돌린 두 로그 -> VR compare 메시지 본문."""
    def row(log):
        m = log["meta"]
        r = {"총 스텝": log["total_steps"], "주문당 평균": m["avg_order_time"], "대기 합계": m["total_waits"]}
        if not log.get("completed", True):
            r["완료 주문"] = f"{m['orders_done']}/{m['orders_total']}"
        return r
    both = baseline.get("completed", True) and optimized.get("completed", True)
    out = {"baseline": row(baseline), "optimized": row(optimized),
           "improvement_pct": improvement_pct(baseline["total_steps"], optimized["total_steps"]) if both else None}
    if not both:   # 짧게 끊기거나 일부만 처리한 실행이 좋아 보이는 착시 방지
        out["note"] = ("비교 불가: 기준 전략이 주문을 다 처리하지 못함" if not baseline.get("completed", True)
                       else "비교 불가: 우리 전략이 주문을 다 처리하지 못함")
    return out


def compare_strategies(grid: dict, scenario: dict, seed: int = 42) -> dict:
    """기준, 최적화를 같은 시드, 주문으로 실행 (planner.compare). 반환: {"baseline", "optimized", "summary"}"""
    return _compare(grid, scenario, seed)
