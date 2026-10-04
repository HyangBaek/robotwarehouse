"""조건 분기 (에이전트 구성 문서 5.3). LLM 없이 상태 값으로만 정한다."""
from config import settings
from .state import AgentState


def route_entry(s: AgentState) -> str:
    """요청 종류 -> 첫 노드."""
    return {"compose": "interpret", "answer": "interpret", "edit": "validate", "simulate": "simulate",
            "analyze": "analyze", "approve": "apply"}[s["intent"]]


def route_after_interpret(s: AgentState) -> str:
    return "generate" if s.get("req_full") else "ask"


def route_after_validate(s: AgentState) -> str:
    if s["validation"]["valid"]:
        return "ok"
    if s.get("intent") == "approve":
        return "fail"                                   # 개선안 적용 지도는 묻지 않고 실패로 끝냄
    if s.get("intent") != "edit" and s.get("question_count", 0) >= settings.max_questions:
        return "abort"
    return "ask"


def route_after_emit(s: AgentState) -> str:
    return "simulate" if s.get("intent") == "approve" else "end"


def route_after_apply(s: AgentState) -> str:
    if s.get("error"):
        return "end"
    return "map_changed" if s.get("changed") == "map" else "scenario_changed"
