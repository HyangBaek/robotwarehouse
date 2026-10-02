"""python planner/tests/test_service.py  (SC-04, SC-10 연결 계층)"""
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))  # planner/ 의 상위 폴더

import time
from planner import resimulate, run_simulation
from planner.testmap import W1, make_warehouse

M = make_warehouse(**W1)
RAW = {"map_version": "v1", "robots": 2, "inbound": 10, "outbound": 10}


def test_run_simulation_ok():
    r = run_simulation(M, RAW)
    assert r["ok"] and r["error"] is None
    assert r["log"]["map_version"] == "v1" and len(r["log"]["orders"]) == 20


def test_run_simulation_reports_engine_error_not_exception():
    r = run_simulation({"width": 3, "height": 3, "cells": []}, RAW)   # 도크 없음
    assert not r["ok"] and r["error"]["code"] == "ENGINE_ERROR"
    r = run_simulation(M, {"robots": 0, "inbound": 1, "outbound": 1})
    assert not r["ok"] and r["error"]["code"] == "ENGINE_ERROR"


def test_robots_dict_form_accepted():
    r = run_simulation(M, {**RAW, "robots": {"count": 3}})
    assert r["ok"] and len(r["log"]["frames"][0]["robots"]) == 3


def test_resimulate_same_orders_and_no_change_is_identical():
    base = run_simulation(M, RAW)["log"]
    x = resimulate(M, RAW, base, {"type": "storage_weight", "value": 3.0})   # 기본값과 같은 설정
    assert x["ok"] and x["after"]["total_steps"] == base["total_steps"]
    ids = lambda lg: [(o["order_id"], o["type"], o["arrival_t"]) for o in lg["orders"]]
    assert ids(x["log_after"]) == ids(base)


def test_resimulate_changes_apply_and_stay_collision_free():
    base = run_simulation(M, RAW)["log"]
    for ch in ({"type": "robots", "value": 3}, {"type": "storage_weight", "value": 0},
               {"type": "one_way", "from": [5, 1], "to": [5, 15]}):
        x = resimulate(M, RAW, base, ch)
        assert x["ok"] and x["after"]["collisions"] == 0, ch
    assert resimulate(M, RAW, base, {"type": "robots", "value": 3})["after"] is not None


def test_unknown_change_type_raises():
    base = run_simulation(M, RAW)["log"]
    try:
        resimulate(M, RAW, base, {"type": "magic"})
    except ValueError:
        return
    raise AssertionError("지원하지 않는 유형인데 예외가 없음")


if __name__ == "__main__":
    for n, fn in list(globals().items()):
        if n.startswith("test_"):
            t = time.time(); fn(); print("PASS", n, f"{time.time()-t:.1f}s")
