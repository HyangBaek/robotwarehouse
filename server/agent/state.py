"""AgentState (에이전트 구성 문서 4장)."""
from typing import Literal, Optional, TypedDict

Phase = Literal["INPUT", "WAIT_ANSWER", "WAIT_CONFIRM", "WAIT_SCENARIO",
                "SIMULATED", "WAIT_APPROVAL", "DONE", "ABORT"]


class AgentState(TypedDict, total=False):
    session_id: str
    phase: Phase
    user_text: str
    answers: list[str]
    requirements: dict
    map: dict
    map_version: str
    defaults_applied: list[str]
    validation: dict
    question: Optional[str]
    question_count: int
    llm_retry: int
    scenario: dict
    sim_ids: dict
    summary: dict
    analyze_requested: bool
    stats: dict
    explanation: str
    proposals: list[dict]
    approved_proposal: Optional[str]
    history: list[dict]
