"""LangGraph 오케스트레이터 (에이전트 구성 문서 10장)."""
from langgraph.graph import StateGraph, END
from langgraph.checkpoint.memory import MemorySaver

from .state import AgentState
from . import routing as r
from .nodes import composer as c, simulation as sm, analyst as an

WAIT_NODES = ["wait_answer", "wait_confirm", "wait_approval"]


def build_graph(nodes: dict | None = None, checkpointer=None):
    """nodes: 테스트에서 특정 노드를 가짜 함수로 바꿀 때 사용."""
    impl = {
        "interpret": c.interpret, "generate_map": c.generate_map, "validate": c.validate,
        "ask_question": c.ask_question, "emit_map": c.emit_map,
        "prepare_sim": sm.prepare_sim, "simulate": sm.simulate, "compare": sm.compare,
        "analyze": an.analyze, "propose": an.propose, "apply": an.apply,
        "wait_answer": lambda s: {"phase": "WAIT_ANSWER"},
        "wait_confirm": lambda s: {"phase": "WAIT_CONFIRM"},
        "wait_approval": lambda s: {"phase": "WAIT_APPROVAL"},
    }
    impl.update(nodes or {})

    g = StateGraph(AgentState)
    for name, fn in impl.items():
        g.add_node(name, fn)

    g.set_entry_point("interpret")
    g.add_conditional_edges("interpret", r.route_after_interpret,
                            {"retry": "interpret", "ok": "generate_map", "ask": "ask_question"})
    g.add_edge("generate_map", "validate")
    g.add_conditional_edges("validate", r.route_after_validate,
                            {"ok": "emit_map", "ask": "ask_question", "retry": "interpret", "abort": END})
    g.add_edge("ask_question", "wait_answer")
    g.add_edge("wait_answer", "interpret")
    g.add_edge("emit_map", "wait_confirm")
    g.add_edge("wait_confirm", "prepare_sim")
    g.add_edge("prepare_sim", "simulate")
    g.add_edge("simulate", "compare")
    g.add_conditional_edges("compare", r.route_after_compare, {"analyze": "analyze", "end": END})
    g.add_edge("analyze", "propose")
    g.add_edge("propose", "wait_approval")
    g.add_conditional_edges("wait_approval", r.route_after_approval, {"apply": "apply", "end": END})
    g.add_conditional_edges("apply", r.route_after_apply,
                            {"map_changed": "validate", "scenario_changed": "simulate"})

    return g.compile(checkpointer=checkpointer or MemorySaver(), interrupt_after=WAIT_NODES)
