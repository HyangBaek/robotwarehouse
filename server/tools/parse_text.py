"""지시 해석 정규식 대체 경로 (mock_server/mock/parse_text.py 이식).

LLM 호출이 실패(예외·시간 초과·스키마 오류)하면 이 규칙으로 요구사항을 뽑는다 (WS status: regex_fallback).
출력 키는 LLM 요구사항 스키마(services/interpreter.py)와 같다.
"""
from __future__ import annotations

import re
from typing import Any

from .map_generator import DEFAULTS

NUM_WORDS = {
    "한": 1, "하나": 1, "두": 2, "둘": 2, "세": 3, "셋": 3, "네": 4, "넷": 4,
    "다섯": 5, "여섯": 6, "일곱": 7, "여덟": 8, "아홉": 9, "열": 10,
}
_NUM = r"(\d+|" + "|".join(sorted(NUM_WORDS, key=len, reverse=True)) + r")"


SINO = {"일": 1, "이": 2, "삼": 3, "사": 4, "오": 5, "육": 6, "칠": 7, "팔": 8, "구": 9, "십": 10}
_NUM_M = r"(\d+|" + "|".join(sorted(NUM_WORDS, key=len, reverse=True)) + "|" + "|".join(SINO) + r")"  # "삼 미터"


def _num(s: str) -> int:
    return int(s) if s.isdigit() else NUM_WORDS.get(s) or SINO[s]


def extract_requirements(text: str) -> dict[str, Any]:
    """문장에서 찾은 항목만 담아 돌려준다 (없는 항목은 키가 없음)."""
    t = text.replace(" ", " ")
    req: dict[str, Any] = {}

    m = re.search(r"(?:랙|선반)(?:은|는|을|이)?\s*" + _NUM + r"\s*(?:줄|열|개)", t)
    if m:
        req["racks"] = _num(m.group(1))

    if re.search(r"통로\s*(?:없이|없게|0)", t):
        req["aisle_width"] = 0
    else:
        m = re.search(r"통로(?:\s*폭)?(?:은|는|을)?\s*" + _NUM_M + r"\s*(?:m|미터|M)", t)
        if m:
            req["aisle_width"] = _num(m.group(1))

    m = re.search(r"(?:입하\s*도크|입하|들어오는\s*곳)\s*(?:는|은)?\s*" + _NUM + r"\s*(?:개|곳)?", t)
    if m:
        req["dock_in"] = _num(m.group(1))
    m = re.search(r"(?:출하\s*도크|출하|나가는\s*곳)\s*(?:는|은)?\s*" + _NUM + r"\s*(?:개|곳)?", t)
    if m:
        req["dock_out"] = _num(m.group(1))
    m = re.search(r"입하\s*[·,/및와과]?\s*출하(?:\s*도크)?\s*(?:를|는|은)?\s*(?:각각|모두)?\s*" + _NUM + r"\s*개", t)
    if m:                                                      # "입하·출하 도크 각각 1개씩"
        req["dock_in"] = req["dock_out"] = _num(m.group(1))
    if re.search(r"도크(?:는|를)?\s*(?:빼|없)", t):
        req["dock_in"] = 0
        req["dock_out"] = 0

    if "충전" in t:
        req["charge"] = "left" if "왼" in t else "right"

    m = re.search(_NUM + r"\s*구역", t)
    if m:
        req["zones"] = _num(m.group(1))
    if "일방통행" in t:
        req["one_way"] = "S" if "남" in t else "N"

    if re.search(r"기본값|알아서|아무거나", t):
        req["use_defaults"] = True
    return req


def merge_answer(prev: dict[str, Any], answer: str) -> dict[str, Any]:
    """수정 질문 답변을 기존 요구사항에 합친다 (SC-03 7단계)."""
    new = extract_requirements(answer)
    merged = dict(prev)
    merged.update(new)
    if new.get("use_defaults"):
        for k in ("dock_in", "dock_out"):
            if merged.get(k, 0) == 0:
                merged[k] = DEFAULTS[k]
        if merged.get("aisle_width", 0) == 0:
            merged["aisle_width"] = DEFAULTS["aisle_width"]
    # "입하·출하 1개씩", "하나씩 해줘" 같은 표현
    if re.search(r"(?:각각|씩)", answer) and ("도크" in answer or not new):
        m = re.search(_NUM + r"\s*(?:개|곳)?\s*씩", answer) or re.search(_NUM + r"\s*개", answer)
        if m:
            merged["dock_in"] = merged["dock_out"] = _num(m.group(1))
    elif not new:
        # 숫자만 답한 경우: 비어 있는 항목(도크 0개 → 도크, 통로 0 → 통로 폭)에 넣는다
        m = re.search(r"^\D*?" + _NUM_M + r"\s*(?:개|곳|m|미터|M)?", answer.strip())
        if m:
            n = _num(m.group(1))
            if merged.get("dock_in", 1) == 0 or merged.get("dock_out", 1) == 0:
                for k in ("dock_in", "dock_out"):
                    if merged.get(k, 1) == 0:
                        merged[k] = n
            elif merged.get("aisle_width", 1) == 0:
                merged["aisle_width"] = n
    return merged
