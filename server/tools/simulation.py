"""run_simulation: 경로 엔진 호출 + DB 저장 (B4)."""


def run_simulation(grid: dict, scenario: dict, orders: list[dict],
                   strategy: str = "optimized", event: dict | None = None) -> dict:
    """반환: {"sim_id", "total_steps", "summary", "collisions"}. 충돌이 있으면 결과를 VR로 보내지 않는다."""
    raise NotImplementedError  # TODO(하재윤·방유진)
