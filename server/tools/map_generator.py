"""generate_grid_map: 요구사항 JSON → 격자 지도 (FR-04, FR-05). 좌표 계산은 LLM이 아니라 여기서 한다.

mock_server/mock/mapgen.py + parse_text.fill_defaults 이식. 배치 규칙
- 바깥 둘레는 벽. 랙 줄은 남북 방향(세로)으로 길게, 줄 사이에 통로 폭만큼 통로 칸.
- 남·북 끝에 가로 통로(폭 = 통로 폭). 입하 도크는 남쪽 벽, 출하 도크는 북쪽 벽.
- 충전 구역은 오른쪽(동쪽) 또는 왼쪽 벽을 따라 한 줄.
- 두 구역이면 가운데에 넓은 통로(통로 폭 + 1)를 두고, 일방통행이면 그 통로의 랙 구간에만 rules.one_way.
  가로 통로는 양방향으로 남겨 두고 도크도 가운데 통로 밖에 둔다 (일방통행 막다른 길 방지, 3장 #2).
"""
from __future__ import annotations

from typing import Any

SCHEMA_VERSION = "1.0"
RACK_LENGTH = 10
SLOT_SPEC = "pallet_1100x1100"

DEFAULTS = {"racks": 6, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "levels": 3, "charge": "right"}  # SC-12
REQ_KEYS = ("racks", "aisle_width", "dock_in", "dock_out", "levels", "zones", "charge", "one_way")
_LABELS = {"racks": "랙 {}줄", "aisle_width": "통로 폭 {}m", "dock_in": "입하 {}", "dock_out": "출하 {}",
           "levels": "랙 {}단"}


def fill_defaults(req: dict[str, Any]) -> tuple[dict[str, Any], list[str]]:
    """누락 항목을 기본값으로 채운다 (SC-12). 반환: (요구사항, defaults_applied).

    랙·통로만 말하고 도크를 말하지 않았으면(W5) 도크를 0으로 두어 검증 실패 → 수정 질문(M02).
    아무 항목도 없거나(W6) 구역·일방통행·충전처럼 다른 구성을 말했으면(W3) 도크도 기본값으로 채운다.
    """
    out = {k: v for k, v in req.items() if v is not None}
    applied: list[str] = []
    explicit_other = any(k in out for k in ("zones", "one_way", "charge"))
    nothing = not any(k in out for k in ("racks", "aisle_width", "dock_in", "dock_out", "zones", "one_way"))
    use_defaults = bool(out.get("use_defaults", False))

    for key in ("racks", "aisle_width", "levels"):
        if key not in out:
            out[key] = DEFAULTS[key]
            applied.append(_LABELS[key].format(DEFAULTS[key]))
    for key in ("dock_in", "dock_out"):
        if key not in out:
            if nothing or explicit_other or use_defaults:
                out[key] = DEFAULTS[key]
                applied.append(_LABELS[key].format(DEFAULTS[key]))
            else:
                out[key] = 0
    out.setdefault("charge", DEFAULTS["charge"])
    out.setdefault("zones", 1)
    out.pop("use_defaults", None)
    return out, applied


def generate_map(req: dict[str, Any]) -> dict[str, Any]:
    """기본값이 채워진 요구사항 → DR-01 격자 지도 JSON. 같은 입력이면 같은 결과 (TC-MAP-12)."""
    racks = max(0, int(req.get("racks", 6)))
    a = max(0, int(req.get("aisle_width", 3)))
    levels = max(1, int(req.get("levels", 3)))
    dock_in = max(0, int(req.get("dock_in", 1)))
    dock_out = max(0, int(req.get("dock_out", 1)))
    charge = req.get("charge", "right")
    zones = 2 if int(req.get("zones", 1) or 1) >= 2 and racks >= 2 else 1
    one_way = req.get("one_way")

    center_w = a + 1 if zones == 2 else 0

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
    rid = None
    for x, kind in enumerate(columns):
        if kind == "rack":
            rack_no += 1
            rid = f"R{rack_no:02d}"
            rack_list.append({"rack_id": rid, "levels": levels, "slot_spec": SLOT_SPEC,
                              "capacity": RACK_LENGTH * levels})
        for y in range(height):
            border = x in (0, width - 1) or y in (0, height - 1)
            if border:
                cells.append({"x": x, "y": y, "type": "wall"})
            elif kind == "rack" and y0 <= y <= y1:
                cells.append({"x": x, "y": y, "type": "rack", "rack_id": rid})
            elif kind == "charge" and y0 <= y <= y1:
                cells.append({"x": x, "y": y, "type": "charge"})

    interior = [x for x in range(1, width - 1) if columns[x] == "aisle"] or list(range(1, width - 1))
    if one_way and center_cols:
        interior = [x for x in interior if x not in center_cols] or interior
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
        lo, hi = (y0, y1) if a > 0 else (1, height - 2)
        d = "S" if one_way in ("S", "W") else "N"     # 가운데 통로는 세로라서 남북만 의미가 있음
        for cx in center_cols:
            frm, to = ([cx, lo], [cx, hi]) if d == "N" else ([cx, hi], [cx, lo])
            rules["one_way"].append({"from": frm, "to": to, "dir": d})

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


def generate_grid_map(requirements: dict) -> tuple[dict, list[str]]:
    """반환: (격자 지도 dict, defaults_applied 항목 목록). 같은 입력이면 같은 결과 (TC-MAP-12)."""
    req, applied = fill_defaults(requirements)
    return generate_map(req), applied


def summarize(m: dict[str, Any]) -> str:
    types = [c["type"] for c in m["cells"]]
    s = (f"{m['width']}×{m['height']} 격자, 랙 {len(m['racks'])}줄({types.count('rack')}칸), "
         f"입하 {types.count('dock_in')} · 출하 {types.count('dock_out')}, 충전 {types.count('charge')}칸")
    if m.get("rules", {}).get("one_way"):
        s += f", 일방통행 {m['rules']['one_way'][0]['dir']}"
    return s
