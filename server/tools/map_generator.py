"""generate_grid_map: 요구사항 JSON → 격자 지도 (FR-04, FR-05). 좌표 계산은 LLM이 아니라 여기서 한다."""

DEFAULTS = {  # TODO(팀): 기본값 확정 (구현 시나리오 SC-12)
    "racks": 6, "aisle_width_m": 3, "rack_levels": 3,
    "dock_in": {"count": 1, "side": "left"},
    "dock_out": {"count": 1, "side": "right"},
}


def generate_grid_map(requirements: dict) -> tuple[dict, list[str]]:
    """반환: (격자 지도 dict, defaults_applied 항목 목록). 같은 입력이면 같은 결과 (TC-MAP-12)."""
    raise NotImplementedError  # TODO(방유진)
