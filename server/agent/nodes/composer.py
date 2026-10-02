"""창고 구성 Agent: interpret(LLM), ask_question(LLM) + 도구 노드 generate_map, validate."""
from ..state import AgentState


def interpret(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): user_text + answers → requirements, 실패 시 llm_retry += 1


def generate_map(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): tools.map_generator + DB 저장 → map_version


def validate(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): tools.map_validator


def ask_question(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): 오류 코드 → 질문, question_count += 1


def emit_map(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): WS map_ready
