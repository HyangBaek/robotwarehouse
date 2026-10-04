"""LangGraph 노드 공용 도우미. 노드는 config["configurable"]["flow"] 로 도구, 저장소, WebSocket 에 접근한다."""
from typing import Any

SOURCE_LABEL = {"llm": "LLM", "regex": "정규식", "regex_fallback": "regex_fallback", "rule": "규칙"}


def flow_of(config) -> Any:
    return config["configurable"]["flow"]


def session_of(flow, state):
    return flow.store.session(state["session_id"])


def step(state, name: str) -> list[str]:
    return list(state.get("trace") or []) + [name]
