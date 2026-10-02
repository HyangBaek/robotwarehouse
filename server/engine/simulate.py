"""엔진 진입점: 보관 위치 선정 → 작업 할당 → 다중 로봇 경로 → 충돌 검사."""


def simulate(grid: dict, scenario: dict, strategy: str = "optimized",
             seed: int = 42, orders: list[dict] | None = None,
             event: dict | None = None) -> dict:
    """반환: DR-03 로그 {"frames", "orders", "cell_stats", "collisions"}"""
    raise NotImplementedError  # TODO(하재윤)
