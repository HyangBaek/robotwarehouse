"""validate_map: 형식 + 도달 가능성 검사 (FR-06, FR-07). 오류를 모두 모아 반환한다 (TC-MAP-08).

mock_server/mock/validator.py 이식 + 일방통행을 반영한 방향 그래프 검사 (3장 #2).
방향 그래프는 경로 엔진과 같은 규칙(planner.Grid)으로 만든다. 엔진이 못 가는 지도는 여기서 걸러진다.
"""
from __future__ import annotations

from collections import deque
from typing import Any

from planner import Grid
from .map_generator import SCHEMA_VERSION

ERROR_CODES = ("SCHEMA_INVALID", "UNREACHABLE", "NO_DOCK_IN", "NO_DOCK_OUT", "SIZE_EXCEEDED")
CELL_TYPES = {"aisle", "rack", "wall", "dock_in", "dock_out", "charge"}
PASSABLE = {"aisle", "dock_in", "dock_out", "charge"}
MAX_CELLS = 80 * 80


def grid_of(m: dict[str, Any]) -> dict[tuple[int, int], str]:
    g = {(x, y): "aisle" for x in range(m["width"]) for y in range(m["height"])}
    for c in m.get("cells", []):
        g[(c["x"], c["y"])] = c["type"]
    for d in m.get("docks", []):
        g[(d["x"], d["y"])] = d["type"]
    return g


def neighbors(x: int, y: int):
    return ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))


def _err(code: str, message: str, cells: list | None = None) -> dict:
    return {"code": code, "cells": cells or [], "message": message}


def validate_map(grid: dict) -> dict:
    """반환: {"valid": bool, "errors": [{"code", "cells", "message"}]}"""
    m = grid
    errors: list[dict[str, Any]] = []
    missing = [k for k in ("schema_version", "width", "height", "cells", "racks", "docks") if k not in m]
    if missing:
        return {"valid": False, "errors": [_err("SCHEMA_INVALID", f"필수 필드 없음: {missing}")]}
    if m["schema_version"] != SCHEMA_VERSION:
        errors.append(_err("SCHEMA_INVALID", f"schema_version {m['schema_version']} != {SCHEMA_VERSION}"))
    w, h = m["width"], m["height"]
    if not (isinstance(w, int) and isinstance(h, int) and w > 0 and h > 0):
        return {"valid": False, "errors": errors + [_err("SCHEMA_INVALID", f"크기 오류 {w}x{h}")]}
    if w * h > MAX_CELLS:
        errors.append(_err("SIZE_EXCEEDED", f"{w}x{h} > {MAX_CELLS}칸"))
        return {"valid": False, "errors": errors}
    bad_type = [c.get("type") for c in m["cells"] if c.get("type") not in CELL_TYPES]
    out_of_range = [[c["x"], c["y"]] for c in m["cells"] if not (0 <= c["x"] < w and 0 <= c["y"] < h)]
    if bad_type or out_of_range:
        errors.append(_err("SCHEMA_INVALID", f"잘못된 칸 종류 {bad_type} / 범위 밖 칸", out_of_range))
        return {"valid": False, "errors": errors}

    g = grid_of(m)
    if not any(t == "dock_in" for t in g.values()):
        errors.append(_err("NO_DOCK_IN", "입하 도크가 없습니다"))
    if not any(t == "dock_out" for t in g.values()):
        errors.append(_err("NO_DOCK_OUT", "출하 도크가 없습니다"))

    # 1) 무방향 도달 가능성: 도크(없으면 가장 큰 통로 영역)에서 통과 가능한 칸으로 BFS
    starts = [p for p, t in g.items() if t in ("dock_in", "dock_out")] or _largest_component(g)
    seen = _bfs(g, starts)
    unreachable = []
    for p, t in g.items():
        if t == "rack" and not any(n in seen for n in neighbors(*p)):
            unreachable.append(list(p))
        elif t in ("dock_in", "dock_out") and p not in seen:
            unreachable.append(list(p))
    if unreachable:
        errors.append(_err("UNREACHABLE", "통로가 없어 로봇이 닿지 못하는 칸이 있습니다", sorted(unreachable)))
    elif m.get("rules", {}).get("one_way"):
        # 2) 방향 그래프: 모든 도크와 랙이 '갔다가 돌아올 수 있는' 영역에 있어야 한다
        bad = _one_way_unreachable(m, g)
        if bad:
            errors.append({**_err("UNREACHABLE", "일방통행 때문에 들어갔다 나올 수 없는 칸이 있습니다", bad),
                           "reason": "one_way"})
    return {"valid": not errors, "errors": errors}


def _bfs(g, starts):
    seen = set(starts)
    q = deque(starts)
    while q:
        x, y = q.popleft()
        for n in neighbors(x, y):
            if n not in seen and g.get(n) in PASSABLE:
                seen.add(n)
                q.append(n)
    return seen


def _one_way_unreachable(m: dict, g: dict) -> list[list[int]]:
    G = Grid(m)
    W = G.W
    docks = [y * W + x for (x, y), t in g.items() if t in ("dock_in", "dock_out")]
    if not docks:
        return []

    def reach(src, adj):
        seen = {src}
        q = deque([src])
        while q:
            i = q.popleft()
            for j in adj[i]:
                if j not in seen:
                    seen.add(j)
                    q.append(j)
        return seen

    core = reach(docks[0], G.adj) & reach(docks[0], G.radj)   # 첫 도크가 속한 강연결 영역
    bad = [[i % W, i // W] for i in docks if i not in core]
    for cells in G.racks.values():
        if not any(a in core for c in cells for a in G.access[c]):
            bad += [[c % W, c // W] for c in cells]
    return sorted(bad)


def _largest_component(g):
    best: list = []
    seen: set = set()
    for p, t in g.items():
        if t not in PASSABLE or p in seen:
            continue
        comp = list(_bfs(g, [p]))
        seen.update(comp)
        if len(comp) > len(best):
            best = comp
    return best


QUESTIONS = {
    "UNREACHABLE": "랙 사이에 통로가 없어 로봇이 닿지 못하는 칸이 있습니다. 통로 폭을 몇 m로 할까요?",
    "NO_DOCK_IN": "입하 도크가 없습니다. 입하 도크는 몇 개로 할까요?",
    "NO_DOCK_OUT": "출하 도크가 없습니다. 출하 도크는 몇 개로 할까요?",
    "SIZE_EXCEEDED": "창고가 너무 큽니다. 랙 줄 수를 줄일까요?",
    "SCHEMA_INVALID": "지도를 만드는 중 형식 오류가 났습니다. 창고 설명을 다시 말씀해 주세요.",
}


def question_for(errors: list[dict[str, Any]]) -> tuple[str, list[list[int]]]:
    """오류 코드 → 사용자 질문 (템플릿). 문제 칸 좌표도 함께."""
    codes = [e["code"] for e in errors]
    parts = [QUESTIONS[c] for c in dict.fromkeys(codes) if c in QUESTIONS]
    if any(e.get("reason") == "one_way" for e in errors):
        parts = [p for p in parts if p != QUESTIONS["UNREACHABLE"]]
        parts.insert(0, "일방통행 때문에 들어갔다 나올 수 없는 칸이 있습니다. 일방통행을 빼거나 방향을 바꿀까요?")
    if "NO_DOCK_IN" in codes and "NO_DOCK_OUT" in codes:
        parts = [p for p in parts if "도크가 없습니다" not in p]
        parts.append("입하·출하 도크가 없습니다. 입하 도크와 출하 도크는 각각 몇 개로 할까요?")
    cells: list[list[int]] = []
    for e in errors:
        if e["code"] == "UNREACHABLE":
            cells += e.get("cells", [])
    return " ".join(parts), cells
