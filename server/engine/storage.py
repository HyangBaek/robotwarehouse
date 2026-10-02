"""보관 위치 선정 (FR-15)."""


def choose_slot(order: dict, grid: dict, occupancy: dict, strategy: str) -> str:
    raise NotImplementedError  # TODO(하재윤): optimized=도크 거리·용량 기준, baseline=무작위
