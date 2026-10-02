"""조건 분기 (에이전트 구성 문서 5.3). LLM 없이 상태 값으로만 정한다."""
from config import settings
from .state import AgentState


def route_after_interpret(s: AgentState) -> str:
    if s.get("requirements") is not None:
        return "ok"
    return "retry" if s.get("llm_retry", 0) < settings.max_llm_retry else "ask"


def route_after_validate(s: AgentState) -> str:
    v = s["validation"]
    if v["valid"]:
        return "ok"
    codes = {e["code"] for e in v["errors"]}
    if codes == {"SCHEMA_INVALID"} and s.get("llm_retry", 0) < settings.max_llm_retry:
        return "retry"
    if s.get("question_count", 0) >= settings.max_questions:
        return "abort"
    return "ask"


def route_after_compare(s: AgentState) -> str:
    return "analyze" if s.get("analyze_requested") else "end"


def route_after_approval(s: AgentState) -> str:
    return "apply" if s.get("approved_proposal") else "end"


def route_after_apply(s: AgentState) -> str:
    return "map_changed" if s.get("history", [{}])[-1].get("changed") == "map" else "scenario_changed"
