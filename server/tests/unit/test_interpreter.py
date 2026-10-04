"""지시 해석: LLM 응답 정리, 검증과 정규식 대체 경로."""
import pytest

from agent.interpreter import InterpretError, Interpreter, clean
from tests.fakes import FakeLLM

NULLS = {"racks": None, "aisle_width": None, "dock_in": None, "dock_out": None, "levels": None,
         "zones": None, "charge": None, "one_way": None, "use_defaults": False}


def test_clean_drops_nulls_and_minus_one():
    assert clean({**NULLS, "racks": 6, "aisle_width": 3, "dock_in": -1}) == {"racks": 6, "aisle_width": 3}


@pytest.mark.parametrize("bad", [{"racks": "6"}, {"racks": 99}, {"charge": "top"}, {"foo": 1}, ["x"]])
def test_clean_rejects(bad):
    with pytest.raises(InterpretError):
        clean(bad if isinstance(bad, list) else {**NULLS, **bad})


def test_extract_llm_then_fallback():
    class Boom:
        def invoke(self, *a):
            raise TimeoutError("8s")
    req, src, _ = Interpreter(FakeLLM([{**NULLS, "racks": 4}])).extract("x")
    assert (req, src) == ({"racks": 4}, "llm")
    req, src, reason = Interpreter(Boom()).extract("랙 4줄, 통로 폭 3m")
    assert src == "regex_fallback" and req == {"racks": 4, "aisle_width": 3} and "Timeout" in reason


def test_merge_answer_overwrites_only_answered():
    prev = {"racks": 6, "aisle_width": 3, "dock_in": 0, "dock_out": 0, "levels": 3, "charge": "right", "zones": 1}
    req, src, _ = Interpreter(FakeLLM([{**NULLS, "dock_in": 1, "dock_out": 1}])).merge(prev, "몇 개?", "하나씩")
    assert src == "llm" and req == {**prev, "dock_in": 1, "dock_out": 1}
    req, src, _ = Interpreter(None).merge(prev, None, "입하·출하 도크 각각 2개씩")
    assert src == "regex" and req["dock_in"] == req["dock_out"] == 2
