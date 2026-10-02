"""validate_map: 형식 + 도달 가능성 검사 (FR-06, FR-07). 오류를 모두 모아 반환한다 (TC-MAP-08)."""
ERROR_CODES = ("SCHEMA_INVALID", "UNREACHABLE", "NO_DOCK_IN", "NO_DOCK_OUT", "SIZE_EXCEEDED")
MAX_CELLS = 60 * 60  # TODO(팀): 격자 상한 확정


def validate_map(grid: dict) -> dict:
    """반환: {"valid": bool, "errors": [{"code", "cells", "message"}]}"""
    raise NotImplementedError  # TODO(방유진): BFS로 통로 연결 확인, 랙은 인접 통로 칸 기준
