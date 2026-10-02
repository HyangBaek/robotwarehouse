"""분석·개선 Agent: analyze(LLM), propose(LLM) + 도구 노드 apply."""
from ..state import AgentState


def analyze(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): aggregate_logs → LLM 설명, 설명 속 수치 검증


def propose(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): build_candidates → LLM 선택, 후보 밖 id 버림


def apply(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진·하재윤): tools.proposals.apply_proposal
