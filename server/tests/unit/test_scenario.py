"""권장 시나리오, 시나리오 문장 해석, 필수 지도 정보 판정."""
import pytest

from agent.interpreter import Interpreter, clean_scenario
from tests.fakes import FakeLLM
from tools.map_generator import fill_defaults, generate_grid_map, missing_items
from tools.parse_text import extract_requirements
from tools.scenario import extract_scenario, merge_scenario, recommend_scenario


@pytest.mark.parametrize("req, robots", [
    ({"racks": 4, "aisle_width": 3, "dock_in": 1, "dock_out": 1}, 8),
    ({"racks": 8, "aisle_width": 3, "dock_in": 2, "dock_out": 1}, 10),
    ({"racks": 12, "aisle_width": 3, "dock_in": 3, "dock_out": 2}, 14),
    ({"racks": 1, "aisle_width": 3, "dock_in": 1, "dock_out": 1}, 4),      # 랙이 적으면 랙 수 x 2 + 2
])
def test_recommend_robots_by_docks(req, robots):
    m, _ = generate_grid_map(req)
    assert recommend_scenario(m)["robots"] == robots


@pytest.mark.parametrize("text, want", [
    ("로봇 6대, 입하 30건 출하 20건으로 실행해줘", {"robots": 6, "inbound": 30, "outbound": 20, "run": True}),
    ("로봇 열두 대", {"robots": 12}),
    ("주문 총 61건", {"inbound": 31, "outbound": 30}),
    ("권장값으로 돌려", {"use_defaults": True, "run": True}),
])
def test_extract_scenario(text, want):
    assert extract_scenario(text) == want


def test_merge_scenario_clamps():
    base = {"robots": 8, "inbound": 25, "outbound": 25}
    assert merge_scenario(base, {"robots": 99, "inbound": -3, "spec_b_pct": 140}) == \
        {"robots": 16, "inbound": 0, "outbound": 25, "spec_b_pct": 100}


def test_llm_scenario_and_fallback():
    r, src, _ = Interpreter(FakeLLM([{"robots": 6, "inbound": None, "outbound": None, "run": True,
                                      "use_defaults": False}])).extract_scenario("x")
    assert (r, src) == ({"robots": 6, "run": True}, "llm")
    r, src, _ = Interpreter(FakeLLM([{"robots": "많이"}])).extract_scenario("로봇 3대")
    assert (r, src) == ({"robots": 3}, "regex_fallback")
    with pytest.raises(ValueError):
        clean_scenario({"robots": 500, "inbound": None, "outbound": None, "run": False, "use_defaults": False})


def test_missing_items():
    assert missing_items(extract_requirements("랙 8줄 3단, 통로 3m, 입하 2개 출하 1개, 충전 오른쪽, 구역은 하나")) == []
    assert missing_items({"racks": 6, "zones": 2}) == ["aisle_width", "dock_in", "dock_out", "levels", "charge", "one_way"]
    assert "one_way" not in missing_items({"zones": 1})


def test_one_way_none_means_no_rule():
    req, _ = fill_defaults({"racks": 6, "zones": 2, "one_way": "none", "use_defaults": True})
    m, _ = generate_grid_map(req)
    assert "one_way" not in req and m["rules"]["one_way"] == []
