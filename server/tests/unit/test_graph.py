"""LangGraph Agent: 요청마다 지나는 노드 순서, 세션 상태 유지, 개선안 적용 경로."""
import asyncio

from api.flow import Flow
from api.store import Store
from tools.proposals import apply_proposal, build_candidates

FULL = "랙 8줄 3단, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽, 구역은 하나"


class _Store(Store):
    def __init__(self):
        super().__init__(None)
        self.sent = []

    async def emit(self, s, msg):
        self.sent.append(msg)


def _flow():
    f = Flow(_Store(), llm=None, stt=None, node_delay=0)
    return f, f.store.session("g1")


def run(coro):
    return asyncio.run(coro)


def test_compose_question_answer_trace_and_memory():
    f, s = _flow()

    async def go():
        a = await f.run_graph(s, intent="compose", text="랙 6줄, 통로 폭 3미터")
        assert a["trace"] == ["interpret", "ask_question"] and a["question_count"] == 1
        assert f.store.sent[-1]["type"] == "question"
        b = await f.run_graph(s, intent="answer", answer="입하 출하 하나씩, 나머지는 기본값")
        # 앞 턴의 요구사항(랙 6줄, 통로 3m)을 그래프 상태에서 이어받아 지도를 만든다
        assert b["trace"] == ["interpret", "generate_map", "validate", "emit_map"]
        assert b["requirements"]["racks"] == 6 and b["requirements"]["aisle_width"] == 3
        assert f.store.sent[-1]["type"] == "map_ready"
    run(go())


def test_validation_question_limit_keeps_session_open():
    f, s = _flow()

    async def go():
        a = await f.run_graph(s, intent="compose", text="랙 6줄 3단, 통로 없이, 입하 1개 출하 1개, 충전 오른쪽, 구역은 하나")
        assert a["trace"] == ["interpret", "generate_map", "validate", "ask_question"]
        for i in range(2):                                      # 질문 3회까지는 계속 묻는다
            a = await f.run_graph(s, intent="answer", answer="그대로 해줘")
            assert a["trace"][-1] == "ask_question" and a["question_count"] == i + 2
        a = await f.run_graph(s, intent="answer", answer="그대로 해줘")
        # 한도에 닿아도 끝내지 않고, 계속 수정할지 다시 입력할지 고르게 하는 질문을 낸다 (횟수는 새로 셈)
        q = f.store.sent[-1]
        assert a["trace"][-1] == "limit_question" and a["question_count"] == 0
        assert q["type"] == "question" and q["limit_reached"] is True and s.question_id == q["question_id"]
        b = await f.run_graph(s, intent="answer", answer="통로 폭 3m로 해줘")     # 계속 수정하기
        assert b["trace"][-1] == "emit_map" and f.store.sent[-1]["type"] == "map_ready"
    run(go())


def test_simulate_analyze_approve_paths():
    f, s = _flow()

    async def go():
        await f.run_graph(s, intent="compose", text=FULL)
        v = f.store.sent[-1]["map_version"]
        f.store.confirm_map(v)
        a = await f.run_graph(s, intent="simulate", map_version=v, sim_id="S100", strategy="optimized",
                              scenario={"robots": 12, "inbound": 15, "outbound": 15})
        assert a["trace"] == ["simulate"] and f.store.sent[-1]["type"] == "sim_ready"
        b = await f.run_graph(s, intent="analyze", sim_id="S100")
        assert b["trace"] == ["analyze", "propose"]
        msg = f.store.sent[-1]
        assert msg["type"] == "analysis" and msg["proposals"] and str(msg["bottlenecks"][0]["x"]) in msg["explanation"]
        e = f.store.sims["S100"]
        by_type = {p["type"]: pid for pid, p in e.proposals.items()}
        assert "robot_count" in by_type                         # 권장 10대보다 많음
        c = await f.run_graph(s, intent="approve", sim_id="S100", proposal_id=by_type["robot_count"], new_sim_id="S101")
        assert c["trace"] == ["apply", "simulate"] and f.store.sims["S101"].scenario["robots"] == 10
        if "dock_add" in by_type:                               # 지도가 바뀌는 개선안: 검증 -> 전송 -> 재실행
            d = await f.run_graph(s, intent="approve", sim_id="S100", proposal_id=by_type["dock_add"], new_sim_id="S102")
            assert d["trace"] == ["apply", "validate", "emit_map", "simulate"]
            assert any(m["type"] == "map_ready" and m.get("confirmed") for m in f.store.sent)
    run(go())


def test_proposal_types_apply():
    from tools.map_generator import generate_grid_map
    grid, _ = generate_grid_map({"racks": 6, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "use_defaults": True})
    req = {"racks": 6, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "levels": 3, "charge": "right", "zones": 1}
    x = next(c["x"] for c in grid["cells"] if c["type"] == "rack") + 1
    agg = {"bottlenecks": [{"x": x, "y": 6, "wait": 9, "pass": 3}], "total_wait": 20, "top5_share_pct": 60.0,
           "nearest_dock": {"x": 4, "y": 0, "type": "dock_in", "dist": 3}}
    cands = build_candidates(agg, grid, {"robots": 3}, None)
    types = {c["type"] for c in cands}
    assert {"dock_add", "robot_count", "storage_weight", "one_way"} <= types
    for c in cands:
        res = apply_proposal(c, grid, {"robots": 3, "inbound": 5, "outbound": 5}, req)
        if c["type"] == "dock_add":
            assert res["changed"] == "map" and res["req"]["dock_in"] == 2
        elif c["type"] == "one_way":
            assert res["changed"] == "map" and res["grid"]["rules"]["one_way"]
        elif c["type"] == "storage_weight":
            assert res["scenario"]["engine_config"]["storage_load_weight"] == 6.0
        else:
            assert res["scenario"]["robots"] == 5


def test_numbers_check():
    from agent.nodes.analyst import numbers_in, numbers_ok
    facts = {"a": 37, "b": 41.5, "top": [{"x": 18, "y": 16}]}
    allowed = numbers_in(facts)
    assert numbers_ok("(18,16) 칸이 37스텝, 41.5%", allowed)
    assert not numbers_ok("대기가 99스텝", allowed)
