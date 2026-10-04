"""격자 지도 생성 도구 (결정적 함수). 요구사항 -> DR-01 격자 지도 JSON.

배치 규칙 (모의 서버 기준, 실제 도구와 다를 수 있음)
- 바깥 둘레는 벽. 랙 줄은 남북 방향(세로)으로 길게, 줄 사이에 통로 폭만큼 통로 칸.
- 남, 북 끝에 가로 통로(폭 = 통로 폭). 입하 도크는 남쪽 벽, 출하 도크는 북쪽 벽.
- 충전 구역은 오른쪽(동쪽) 또는 왼쪽 벽을 따라 한 줄.
- 두 구역이면 가운데에 넓은 통로(통로 폭 + 1)를 두고, 일방통행이면 그 통로에 rules.one_way.
"""
from __future__ import annotations

from typing import Any

SCHEMA_VERSION = "1.0"
RACK_LENGTH = 10
SLOT_SPEC = "pallet_1100x1100"


def generate_map(req: dict[str, Any]) -> dict[str, Any]:
    racks = max(0, int(req.get("racks", 6)))
    a = max(0, int(req.get("aisle_width", 3)))
    levels = max(1, int(req.get("levels", 3)))
    dock_in = max(0, int(req.get("dock_in", 1)))
    dock_out = max(0, int(req.get("dock_out", 1)))
    charge = req.get("charge", "right")
    zones = 2 if int(req.get("zones", 1)) >= 2 and racks >= 2 else 1
    one_way = req.get("one_way")

    center_w = a + 1 if zones == 2 else 0
    charge_w = 1 if charge in ("right", "left") else 0

    # x 방향 구성: 벽 | (충전) | 통로 | 랙 | 통로 | 랙 ... | 통로 | (충전) | 벽
    columns: list[str] = ["wall"]
    if charge == "left":
        columns.append("charge")
    columns += ["aisle"] * a
    left_count = racks // 2 if zones == 2 else racks
    center_cols: list[int] = []
    for i in range(racks):
        columns.append("rack")
        if zones == 2 and i == left_count - 1:
            start = len(columns)
            columns += ["aisle"] * center_w
            center_cols = list(range(start, start + center_w))
        else:
            columns += ["aisle"] * a
    if charge == "right":
        columns.append("charge")
    columns.append("wall")

    width = len(columns)
    height = 1 + a + RACK_LENGTH + a + 1
    y0, y1 = 1 + a, 1 + a + RACK_LENGTH - 1  # 랙이 놓이는 y 범위

    cells: list[dict[str, Any]] = []
    rack_list: list[dict[str, Any]] = []
    rack_no = 0
    for x, kind in enumerate(columns):
        if kind == "rack":
            rack_no += 1
            rid = f"R{rack_no:02d}"
            rack_list.append({"rack_id": rid, "levels": levels, "slot_spec": SLOT_SPEC, "capacity": RACK_LENGTH * levels})
        for y in range(height):
            border = x in (0, width - 1) or y in (0, height - 1)
            if border:
                cells.append({"x": x, "y": y, "type": "wall"})
            elif kind == "rack" and y0 <= y <= y1:
                cells.append({"x": x, "y": y, "type": "rack", "rack_id": rid})
            elif kind == "charge" and y0 <= y <= y1:
                cells.append({"x": x, "y": y, "type": "charge"})
            elif kind in ("charge", "rack"):
                pass  # 가로 통로 구간 -> aisle(생략)

    interior = [x for x in range(1, width - 1) if columns[x] == "aisle"] or list(range(1, width - 1))
    docks: list[dict[str, Any]] = []

    def spread(n: int) -> list[int]:
        if n <= 0:
            return []
        step = len(interior) / (n + 1)
        return sorted({interior[min(len(interior) - 1, int(step * (i + 1)))] for i in range(n)})

    for i, x in enumerate(spread(dock_in)):
        docks.append({"dock_id": f"IN{i + 1}", "type": "dock_in", "x": x, "y": 0})
    for i, x in enumerate(spread(dock_out)):
        docks.append({"dock_id": f"OUT{i + 1}", "type": "dock_out", "x": x, "y": height - 1})
    dock_pos = {(d["x"], d["y"]): d["type"] for d in docks}
    cells = [c for c in cells if (c["x"], c["y"]) not in dock_pos]
    cells += [{"x": x, "y": y, "type": t} for (x, y), t in dock_pos.items()]

    rules: dict[str, Any] = {"one_way": [], "passing_allowed": []}
    if one_way and center_cols:
        for cx in center_cols:
            rules["one_way"].append({"from": [cx, 1], "to": [cx, height - 2], "dir": one_way})

    cells.sort(key=lambda c: (c["y"], c["x"]))
    return {
        "schema_version": SCHEMA_VERSION,
        "cell_size_m": 1.0,
        "width": width,
        "height": height,
        "cells": cells,
        "racks": rack_list,
        "docks": docks,
        "rules": rules,
    }


def summarize(m: dict[str, Any]) -> str:
    types = [c["type"] for c in m["cells"]]
    return (f"{m['width']}×{m['height']} 격자, 랙 {len(m['racks'])}줄({types.count('rack')}칸), "
            f"입하 {types.count('dock_in')} · 출하 {types.count('dock_out')}, 충전 {types.count('charge')}칸")
