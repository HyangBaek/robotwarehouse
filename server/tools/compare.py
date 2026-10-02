"""compare_strategies: 기준 전략 대비 개선율 (FR-28)."""


def improvement_pct(baseline: float, optimized: float) -> float:
    """(기준 - 최적화) / 기준 * 100. 기준이 0이면 0."""
    if baseline == 0:
        return 0.0
    return round((baseline - optimized) / baseline * 100, 1)


def compare_strategies(sim_id_baseline: str, sim_id_optimized: str) -> dict:
    raise NotImplementedError  # TODO(방유진·하재윤)
