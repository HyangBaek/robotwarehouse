"""개선안 후보 생성(규칙)과 적용 (FR-29). LLM은 후보 중에서만 고른다."""
PROPOSAL_TYPES = ("ONE_WAY", "STORAGE_WEIGHT", "ROBOT_COUNT", "ADD_DOCK")


def build_candidates(stats: dict, grid: dict, scenario: dict) -> list[dict]:
    raise NotImplementedError  # TODO(방유진)


def apply_proposal(proposal: dict, grid: dict, scenario: dict) -> dict:
    """반환: {"changed": "map" | "scenario", "grid", "scenario"}"""
    if proposal.get("type") not in PROPOSAL_TYPES:
        raise ValueError(f"허용되지 않은 개선안 유형: {proposal.get('type')}")
    raise NotImplementedError  # TODO(방유진·하재윤)
