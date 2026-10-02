"""격자 유틸: 통행 가능 칸, 이웃 칸, 랙 접근 칸."""
BLOCKED = {"wall", "rack"}
MOVES = [(0, 1), (1, 0), (0, -1), (-1, 0), (0, 0)]  # 상하좌우 + 대기


def cell_types(grid: dict) -> dict[tuple[int, int], str]:
    return {(c["x"], c["y"]): c["type"] for c in grid["cells"]}


def passable(grid: dict, types: dict | None = None):
    types = types or cell_types(grid)
    w, h = grid["width"], grid["height"]
    return lambda x, y: 0 <= x < w and 0 <= y < h and types.get((x, y), "aisle") not in BLOCKED
