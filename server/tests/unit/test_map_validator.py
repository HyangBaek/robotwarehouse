"""TC-MAP-01~09 (개발자 테스트 시나리오 2.1) + 일방통행 방향 검사 (3장 #2)."""
import copy

from tools.map_generator import fill_defaults, generate_grid_map, generate_map
from tools.map_validator import question_for, validate_map
from tools.parse_text import extract_requirements


def test_tc_map_01_valid(map_w1):
    r = validate_map(map_w1)
    assert r["valid"] and r["errors"] == []


def test_missing_docks_collects_all_errors(map_w1):
    m = copy.deepcopy(map_w1)
    m["docks"] = []
    m["cells"] = [c for c in m["cells"] if c["type"] not in ("dock_in", "dock_out")]
    codes = {e["code"] for e in validate_map(m)["errors"]}
    assert {"NO_DOCK_IN", "NO_DOCK_OUT"} <= codes


def test_w4_no_aisle_unreachable():
    m, _ = generate_grid_map(extract_requirements("랙 8줄, 통로 없이, 입하 도크 1개, 출하 도크 1개"))
    r = validate_map(m)
    assert not r["valid"] and r["errors"][0]["code"] == "UNREACHABLE" and r["errors"][0]["cells"]


def test_w5_docks_missing_asks_question():
    m, _ = generate_grid_map(extract_requirements("랙 6줄, 통로 폭 3미터"))
    r = validate_map(m)
    text, _ = question_for(r["errors"])
    assert not r["valid"] and "각각 몇 개" in text


def test_w3_one_way_valid_and_docks_outside_lane():
    m, _ = generate_grid_map(extract_requirements("랙 6줄을 좌우 두 구역으로 나누고, 가운데 큰 통로는 북쪽 방향 일방통행"))
    assert validate_map(m)["valid"]
    lane_x = {r["from"][0] for r in m["rules"]["one_way"]}
    assert lane_x and not any(d["x"] in lane_x for d in m["docks"])


def test_one_way_dead_end_detected():
    """옛 mapgen 처럼 일방통행을 가운데 통로 전체 높이에 걸면 막다른 길 -> UNREACHABLE."""
    req, _ = fill_defaults({"racks": 6, "zones": 2, "one_way": "N"})
    m = generate_map(req)
    h = m["height"]
    m["rules"]["one_way"] = [{"from": [r["from"][0], 1], "to": [r["from"][0], h - 2], "dir": "N"}
                             for r in m["rules"]["one_way"]]
    lane = m["rules"]["one_way"][1]["from"][0]
    m["docks"][0]["x"] = lane
    m["cells"] = [c for c in m["cells"] if c["type"] not in ("dock_in",)] + [{"x": lane, "y": 0, "type": "dock_in"}]
    r = validate_map(m)
    assert not r["valid"] and r["errors"][0]["code"] == "UNREACHABLE"


def test_generate_deterministic():
    req = {"racks": 8, "aisle_width": 3, "dock_in": 2, "dock_out": 1}
    assert generate_grid_map(req) == generate_grid_map(req)
