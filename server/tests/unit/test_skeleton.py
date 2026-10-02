"""스켈레톤 점검: 모든 모듈 import, 그래프 조립, 헬스 체크, 불변 조건 검사기."""
import importlib
import pytest

MODULES = [
    "config", "schema.grid_map", "schema.scenario", "schema.sim_log", "schema.messages",
    "services.llm", "services.stt", "services.tts", "db.repo",
    "tools.map_generator", "tools.map_validator", "tools.orders", "tools.simulation",
    "tools.aggregate", "tools.compare", "tools.proposals",
    "engine", "engine.collision", "engine.replan", "engine.planner.prioritized",
    "agent.state", "agent.routing", "agent.graph", "api.main",
]


@pytest.mark.parametrize("name", MODULES)
def test_import(name):
    importlib.import_module(name)


def test_fixture_matches_schema(map_w1):
    from schema.grid_map import GridMap
    GridMap.model_validate(map_w1)


def test_graph_compiles():
    from agent.graph import build_graph
    assert build_graph() is not None


def test_health(tmp_path):
    from fastapi.testclient import TestClient
    from api.main import create_app
    c = TestClient(create_app(db_path=tmp_path / "t.db"))
    assert c.get("/health").json() == {"status": "ok"}


def test_ws_hello(tmp_path):
    from fastapi.testclient import TestClient
    from api.main import create_app
    c = TestClient(create_app(db_path=tmp_path / "t.db"))
    with c.websocket_connect("/ws") as ws:
        m = ws.receive_json()
        assert m["type"] == "hello" and m["session_id"]


def test_invariants_detect_swap():
    from engine.collision import check_invariants
    grid = {"width": 3, "height": 1, "cells": []}
    log = {"frames": [
        {"t": 0, "robots": [{"id": 0, "x": 0, "y": 0}, {"id": 1, "x": 1, "y": 0}]},
        {"t": 1, "robots": [{"id": 0, "x": 1, "y": 0}, {"id": 1, "x": 0, "y": 0}]},
    ], "orders": []}
    assert any(v["type"] == "swap" for v in check_invariants(log, grid))


def test_improvement_pct():
    from tools.compare import improvement_pct
    assert improvement_pct(100, 80) == 20.0
    assert improvement_pct(0, 0) == 0.0
