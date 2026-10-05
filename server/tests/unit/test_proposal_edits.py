"""개선안 '도크 추가'를 적용해도 VR 에서 옮긴 랙 위치가 남는지."""
import pytest

from tools.map_generator import generate_map
from tools.map_validator import validate_map
from tools.proposals import EditConflict, apply_proposal, carry_edits, vr_edits

REQ = {"racks": 8, "aisle_width": 3, "dock_in": 2, "dock_out": 1, "levels": 3, "charge": "right", "zones": 1}


def _move(m, frm, to):
    rid = next(c["rack_id"] for c in m["cells"] if (c["x"], c["y"]) == frm)
    cells = [c for c in m["cells"] if (c["x"], c["y"]) not in (frm, to)]
    return {**m, "cells": cells + [{"x": to[0], "y": to[1], "type": "rack", "rack_id": rid}]}


def _rack_at(m, p):
    return any(c["type"] == "rack" and (c["x"], c["y"]) == p for c in m["cells"])


def _movable(m):
    kind = {(c["x"], c["y"]): c["type"] for c in m["cells"]}
    for (x, y), t in sorted(kind.items()):
        if t == "rack":
            for nx in (x - 1, x + 1):
                if kind.get((nx, y), "aisle") == "aisle" and 0 <= nx < m["width"]:
                    if validate_map(_move(m, (x, y), (nx, y)))["valid"]:
                        return (x, y), (nx, y)
    raise AssertionError("옮길 랙 없음")


def test_vr_edits_found_from_map_alone():
    m = generate_map(REQ)
    frm, to = _movable(m)
    removed, added = vr_edits(_move(m, frm, to), REQ)
    assert removed == {frm} and set(added) == {to}
    assert vr_edits(m, REQ) == (set(), {})                  # 편집 안 한 지도는 변화 없음


@pytest.mark.parametrize("dock", ["dock_in", "dock_out"])
def test_dock_add_keeps_moved_rack(dock):
    m = generate_map(REQ)
    frm, to = _movable(m)
    edited = _move(m, frm, to)
    res = apply_proposal({"type": "dock_add", "apply": {"dock": dock}}, edited, {"robots": 4}, REQ)
    g = res["grid"]
    assert res["kept_edits"] == 1 and res["req"][dock] == REQ[dock] + 1
    assert _rack_at(g, to) and not _rack_at(g, frm)
    assert sum(1 for d in g["docks"] if d["type"] == dock) == REQ[dock] + 1
    assert validate_map(g)["valid"]


def test_dock_add_without_edits_is_plain_regeneration():
    m = generate_map(REQ)
    res = apply_proposal({"type": "dock_add", "apply": {"dock": "dock_out"}}, m, {"robots": 4}, REQ)
    assert res["kept_edits"] == 0 and res["grid"] == generate_map({**REQ, "dock_out": 2})


def test_conflict_is_reported_not_dropped():
    m = generate_map(REQ)
    wall = next((c["x"], c["y"]) for c in m["cells"] if c["type"] in ("wall", "dock_in", "dock_out", "charge"))
    with pytest.raises(EditConflict):
        carry_edits(m, set(), {wall: "R01"})
