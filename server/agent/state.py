"""AgentState: LangGraph 가 세션(thread_id = session_id)마다 체크포인터에 유지하는 대화 상태.

한 번의 요청(턴)은 START 에서 intent 로 갈라져 END 까지 진행한다. 다음 턴에도 요구사항, 질문 횟수,
지도 버전 같은 값은 그대로 남아 있어 수정 질문 이력을 이어 간다 (제출물 양식 Memory 항목).
"""
from typing import Literal, Optional, TypedDict

Intent = Literal["compose", "answer", "edit", "simulate", "analyze", "approve"]


class AgentState(TypedDict, total=False):
    session_id: str
    intent: Intent
    trace: list[str]                 # 이번 턴에 지난 노드 순서 (디버그, 테스트용)
    # 창고 구성 (지시 해석 -> 지도 생성 -> 지도 검증 -> 수정 질문)
    text: str                        # compose: 창고 설명 문장
    answer: str                      # answer: 수정 질문에 대한 답변
    requirements: dict               # 지금까지 모은 요구사항 (빠진 항목은 키 없음)
    req_full: Optional[dict]         # 기본값까지 채운 요구사항. 빠진 항목이 있으면 None
    missing: list[str]               # 아직 말하지 않은 필수 항목
    defaults_applied: list[str]
    question_text: Optional[str]
    question_count: int
    llm_source: str                  # 해석 방식: llm, regex(정규식), regex_fallback(LLM 실패 후 정규식)
    map: dict                        # 검증할 격자 지도
    map_note: str                    # map_ready 요약 앞에 붙일 말 (편집 반영, 개선안 적용)
    validation: dict
    map_version: Optional[str]
    confirmed: bool                  # 개선안 적용 지도는 바로 확정
    # 시뮬레이션
    scenario: dict
    strategy: str
    sim_id: Optional[str]            # 실행할(또는 분석할) 시뮬레이션
    # 분석, 개선
    analysis: dict                   # 집계 수치와 설명
    candidates: list[dict]
    proposal_id: Optional[str]       # approve: 승인한 개선안
    proposal: dict                   # approve: 승인한 개선안 내용 (승인 기록 저장용)
    new_sim_id: Optional[str]        # approve: 재시뮬레이션 결과 ID
    changed: Optional[str]           # approve: 바뀐 대상 map(지도) 또는 scenario(시나리오)
    error: Optional[str]
