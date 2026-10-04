"""compare_strategies: 기준 전략 대비 개선율 (FR-28)."""
from planner import compare as _compare


def improvement_pct(baseline: float, optimized: float) -> float:
    """(기준 - 최적화) / 기준 * 100. 기준이 0이면 0."""
    if baseline == 0:
        return 0.0
    return round((baseline - optimized) / baseline * 100, 1)


def compare_logs(baseline: dict, optimized: dict) -> dict:
    """같은 지도·주문으로 돌린 두 로그 → VR compare 메시지 본문."""
    def row(log):
        m = log["meta"]
        return {"총 스텝": log["total_steps"], "주문당 평균": m["avg_order_time"], "대기 합계": m["total_waits"]}
    return {"baseline": row(baseline), "optimized": row(optimized),
            "improvement_pct": improvement_pct(baseline["total_steps"], optimized["total_steps"])}


def compare_strategies(grid: dict, scenario: dict, seed: int = 42) -> dict:
    """기준·최적화를 같은 시드·주문으로 실행 (planner.compare). 반환: {"baseline", "optimized", "summary"}"""
    return _compare(grid, scenario, seed)
