"""generate_grid_map: 요구사항 JSON -> 격자 지도 (FR-04, FR-05). 좌표 계산은 LLM이 아니라 여기서 한다.

mock_server/mock/mapgen.py + parse_text.fill_defaults 이식. 배치 규칙
- 바깥 둘레는 벽. 랙 줄은 남북 방향(세로)으로 길게, 줄 사이에 통로 폭만큼 통로 칸.
- 남, 북 끝에 가로 통로(폭 = 통로 폭). 입하 도크는 남쪽 벽, 출하 도크는 북쪽 벽.
- 충전 구역은 오른쪽(동쪽) 또는 왼쪽 벽을 따라 한 줄.
- 두 구역이면 가운데에 넓은 통로(통로 폭 + 1)를 두고, 일방통행이면 그 통로의 랙 구간에만 rules.one_way.
  가로 통로는 양방향으로 남겨 두고 도크도 가운데 통로 밖에 둔다 (일방통행 막다른 길 방지, 3장 #2).
"""
from __future__ import annotations

from typing import Any

SCHEMA_VERSION = "1.0"
RACK_LENGTH = 10
SLOT_SPEC = "pallet_1100x1100"      # 한국 표준 파레트 (KS T-11), 기본 랙 규격
SLOT_SPEC_B = "pallet_1200x1000"    # 1200x1000 파레트 (ISO 6780), racks_b 줄만큼 오른쪽 랙에 지정
SPEC_LABEL = {SLOT_SPEC: "1100x1100", SLOT_SPEC_B: "1200x1000"}

DEFAULTS = {"racks": 6, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "levels": 3, "charge": "right"}  # SC-12
_LABELS = {"racks": "랙 {}줄", "aisle_width": "통로 폭 {}m", "dock_in": "입하 {}", "dock_out": "출하 {}",
           "levels": "랙 {}단"}


# 디지털 트윈에 필요한 지도 정보 (VR 첫 화면 안내와 같은 순서). 빠지면 되묻는다.
REQUIRED_ITEMS = [
    ("racks", "랙 줄 수"),
    ("aisle_width", "통로 폭(m)"),
    ("dock_in", "입하 도크 수"),
    ("dock_out", "출하 도크 수"),
    ("levels", "랙 단 수"),
    ("charge", "충전 구역 위치(왼쪽/오른쪽)"),
    ("zones", "구역 분할(하나/좌우 두 구역)"),
    ("one_way", "가운데 통로 일방통행(없음/북쪽/남쪽)"),   # 두 구역일 때만
]


SHORT = {"racks": "랙", "aisle_width": "통로 폭", "dock_in": "입하 도크", "dock_out": "출하 도크", "levels": "랙",
         "charge": "충전 구역", "zones": "구역", "one_way": "일방통행"}


def missing_items(req: dict[str, Any]) -> list[str]:
    """아직 말하지 않은 필수 항목 키. 일방통행은 두 구역일 때만 묻는다."""
    out = [k for k, _ in REQUIRED_ITEMS if req.get(k) is None and k != "one_way"]
    if (req.get("zones") or 1) >= 2 and req.get("one_way") is None:
        out.append("one_way")
    return out


def missing_options(keys: list[str]) -> list[dict[str, str]]:
    """빠진 정보 질문의 선택 버튼 (최대 3개): 자주 쓰는 답 두 개 + '나머지는 기본값'."""
    picks = []
    if "dock_in" in keys and "dock_out" in keys:
        picks.append({"label": "도크 각각 1개", "text": "입하 도크 1개, 출하 도크 1개로 해줘"})
    if "levels" in keys:
        picks.append({"label": "랙 3단", "text": "랙은 3단으로 해줘"})
    if "zones" in keys:
        picks.append({"label": "구역 하나", "text": "구역은 하나로 해줘"})
    if "charge" in keys:
        picks.append({"label": "충전 오른쪽", "text": "충전 구역은 오른쪽"})
    if "one_way" in keys:
        picks.append({"label": "일방통행 없음", "text": "일방통행은 없음"})
    return picks[:2] + [{"label": "나머지는 기본값", "text": "나머지는 기본값으로 해줘"}]


def missing_question(keys: list[str], known: dict[str, Any] | None = None) -> str:
    labels = dict(REQUIRED_ITEMS)
    head = ""
    if known:
        got = [f"{SHORT[k]} {_fmt(k, known[k])}" for k, _ in REQUIRED_ITEMS if known.get(k) is not None]
        if got:
            head = "알아들은 내용: " + ", ".join(got) + ". "
    return (head + "다음 정보가 더 필요해요: " + ", ".join(labels[k] for k in keys) +
            ". 말씀해 주시거나, 모르면 '나머지는 기본값'이라고 해 주세요.")


def _fmt(k: str, v: Any) -> str:
    if k == "charge":
        return {"left": "왼쪽", "right": "오른쪽"}.get(v, str(v))
    if k == "one_way":
        return {"N": "북쪽", "S": "남쪽", "E": "동쪽", "W": "서쪽", "none": "없음"}.get(v, str(v))
    if k == "zones":
        return "두 구역" if v and v >= 2 else "하나"
    return {"racks": "{}줄", "aisle_width": "{}m", "dock_in": "{}개", "dock_out": "{}개", "levels": "{}단"}[k].format(v)


def fill_defaults(req: dict[str, Any]) -> tuple[dict[str, Any], list[str]]:
    """누락 항목을 기본값으로 채운다 (SC-12). 반환: (요구사항, defaults_applied).

    랙, 통로만 말하고 도크를 말하지 않았으면(W5) 도크를 0으로 두어 검증 실패 -> 수정 질문(M02).
    아무 항목도 없거나(W6) 구역, 일방통행, 충전처럼 다른 구성을 말했으면(W3) 도크도 기본값으로 채운다.
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
    if out.get("one_way") == "none":
        out.pop("one_way")
    out.pop("use_defaults", None)
    return out, applied


def generate_map(req: dict[str, Any]) -> dict[str, Any]:
    """기본값이 채워진 요구사항 -> DR-01 격자 지도 JSON. 같은 입력이면 같은 결과 (TC-MAP-12)."""
    racks = max(0, int(req.get("racks", 6)))
    racks_b = max(0, min(racks, int(req.get("racks_b", 0) or 0)))
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
            spec = SLOT_SPEC_B if rack_no > racks - racks_b else SLOT_SPEC
            rack_list.append({"rack_id": rid, "levels": levels, "slot_spec": spec,
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
    nb = sum(1 for r in m.get("racks", []) if r.get("slot_spec") == SLOT_SPEC_B)
    if nb:
        s += f", 1200x1000 규격 랙 {nb}줄"
    return s
