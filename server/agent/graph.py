"""LangGraph 오케스트레이터 (아키텍처 2장, 에이전트 구성 문서 10장).

  지시 해석 -> (빠진 정보) 수정 질문
          -> 격자 지도 생성 -> 지도 검증 -> (오류) 수정 질문 / (통과) 지도 전송
  시뮬레이션 실행
  로그 분석 -> 개선안 제안
  개선안 적용 -> (지도 변경) 지도 검증 -> 지도 전송 -> 시뮬레이션 실행 / (시나리오 변경) 시뮬레이션 실행

세션마다 thread_id 로 상태를 이어 간다. 노드는 config["configurable"]["flow"] 로 도구와 WebSocket 에 접근한다.
"""
from langgraph.checkpoint.memory import MemorySaver
from langgraph.graph import END, START, StateGraph

from . import routing as r
from .nodes import analyst as an
from .nodes import composer as c
from .nodes import simulation as sm
from .state import AgentState

NODES = {
    "interpret": c.interpret, "generate_map": c.generate_map, "validate": c.validate,
    "ask_question": c.ask_question, "give_up": c.give_up, "emit_map": c.emit_map,
    "simulate": sm.simulate, "analyze": an.analyze, "propose": an.propose, "apply": an.apply,
}


def build_graph(nodes: dict | None = None, checkpointer=None):
    """nodes: 테스트에서 특정 노드를 가짜 함수로 바꿀 때 사용."""
    impl = {**NODES, **(nodes or {})}
    g = StateGraph(AgentState)
    for name, fn in impl.items():
        g.add_node(name, fn)

    g.add_conditional_edges(START, r.route_entry, {n: n for n in ("interpret", "validate", "simulate", "analyze", "apply")})
    g.add_conditional_edges("interpret", r.route_after_interpret, {"generate": "generate_map", "ask": "ask_question"})
    g.add_edge("generate_map", "validate")
    g.add_conditional_edges("validate", r.route_after_validate,
                            {"ok": "emit_map", "ask": "ask_question", "abort": "give_up", "fail": "give_up"})
    g.add_edge("ask_question", END)
    g.add_edge("give_up", END)
    g.add_conditional_edges("emit_map", r.route_after_emit, {"simulate": "simulate", "end": END})
    g.add_edge("simulate", END)
    g.add_edge("analyze", "propose")
    g.add_edge("propose", END)
    g.add_conditional_edges("apply", r.route_after_apply,
                            {"map_changed": "validate", "scenario_changed": "simulate", "end": END})
    return g.compile(checkpointer=checkpointer or MemorySaver())
