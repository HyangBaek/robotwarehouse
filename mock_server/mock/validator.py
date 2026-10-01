"""지도 검증 도구 (FR-06, FR-07). 첫 오류에서 멈추지 않고 모든 오류를 모은다 (TC-MAP-08)."""
from __future__ import annotations

from collections import deque
from typing import Any

from .mapgen import SCHEMA_VERSION

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


def validate_map(m: dict[str, Any]) -> dict[str, Any]:
    errors: list[dict[str, Any]] = []
    missing = [k for k in ("schema_version", "width", "height", "cells", "racks", "docks") if k not in m]
    if missing:
        return {"valid": False, "errors": [{"code": "SCHEMA_INVALID", "fields": missing}]}
    if m["schema_version"] != SCHEMA_VERSION:
        errors.append({"code": "SCHEMA_INVALID", "message": f"schema_version {m['schema_version']} != {SCHEMA_VERSION}"})
    w, h = m["width"], m["height"]
    if w * h > MAX_CELLS:
        errors.append({"code": "SIZE_EXCEEDED", "message": f"{w}x{h} > {MAX_CELLS}"})
        return {"valid": False, "errors": errors}
    bad_type = [c for c in m["cells"] if c.get("type") not in CELL_TYPES]
    out_of_range = [[c["x"], c["y"]] for c in m["cells"] if not (0 <= c["x"] < w and 0 <= c["y"] < h)]
    if bad_type or out_of_range:
        errors.append({"code": "SCHEMA_INVALID", "bad_types": [c.get("type") for c in bad_type], "cells": out_of_range})
        return {"valid": False, "errors": errors}

    g = grid_of(m)
    if not any(t == "dock_in" for t in g.values()):
        errors.append({"code": "NO_DOCK_IN"})
    if not any(t == "dock_out" for t in g.values()):
        errors.append({"code": "NO_DOCK_OUT"})

    # 도달 가능성: 도크(없으면 가장 큰 통로 영역)에서 통과 가능한 칸으로 BFS
    starts = [p for p, t in g.items() if t in ("dock_in", "dock_out")]
    if not starts:
        starts = _largest_component(g)
    seen = set(starts)
    q = deque(starts)
    while q:
        x, y = q.popleft()
        for n in neighbors(x, y):
            if n not in seen and g.get(n) in PASSABLE:
                seen.add(n)
                q.append(n)
    unreachable = []
    for p, t in g.items():
        if t == "rack" and not any(n in seen for n in neighbors(*p)):
            unreachable.append(list(p))
        elif t in ("dock_in", "dock_out") and p not in seen:
            unreachable.append(list(p))
    if unreachable:
        errors.append({"code": "UNREACHABLE", "cells": sorted(unreachable)})
    return {"valid": not errors, "errors": errors}


def _largest_component(g):
    best: list = []
    seen: set = set()
    for p, t in g.items():
        if t not in PASSABLE or p in seen:
            continue
        comp = [p]
        seen.add(p)
        q = deque([p])
        while q:
            x, y = q.popleft()
            for n in neighbors(x, y):
                if n not in seen and g.get(n) in PASSABLE:
                    seen.add(n)
                    comp.append(n)
                    q.append(n)
        if len(comp) > len(best):
            best = comp
    return best


QUESTIONS = {
    "UNREACHABLE": "랙 사이에 통로가 없어 로봇이 닿지 못하는 칸이 있습니다. 통로 폭을 몇 m로 할까요?",
    "NO_DOCK_IN": "입하 도크가 없습니다. 입하 도크는 몇 개로 할까요?",
    "NO_DOCK_OUT": "출하 도크가 없습니다. 출하 도크는 몇 개로 할까요?",
    "SIZE_EXCEEDED": "창고가 너무 큽니다. 랙 줄 수를 줄일까요?",
}


def question_for(errors: list[dict[str, Any]]) -> tuple[str, list[list[int]]]:
    """오류 코드 → 사용자 질문 (LLM 대신 템플릿). 문제 칸 좌표도 함께."""
    codes = [e["code"] for e in errors]
    parts = [QUESTIONS[c] for c in dict.fromkeys(codes) if c in QUESTIONS]
    if "NO_DOCK_IN" in codes and "NO_DOCK_OUT" in codes:
        parts = [p for p in parts if "도크가 없습니다" not in p]
        parts.append("입하·출하 도크가 없습니다. 입하 도크와 출하 도크는 각각 몇 개로 할까요?")
    cells: list[list[int]] = []
    for e in errors:
        if e["code"] == "UNREACHABLE":
            cells += e["cells"]
    return " ".join(parts), cells
