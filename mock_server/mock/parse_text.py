"""지시 해석 모의 구현 (LLM 대신 정규식).

실제 Agent(방유진 담당)는 LLM으로 요구사항을 뽑는다. 여기서는 VR 클라이언트 검증용으로
W1~W6 테스트 문장과 그 변형을 규칙으로 해석한다.
"""
from __future__ import annotations

import re
from typing import Any

NUM_WORDS = {
    "한": 1, "하나": 1, "두": 2, "둘": 2, "세": 3, "셋": 3, "네": 4, "넷": 4,
    "다섯": 5, "여섯": 6, "일곱": 7, "여덟": 8, "아홉": 9, "열": 10,
}
_NUM = r"(\d+|" + "|".join(sorted(NUM_WORDS, key=len, reverse=True)) + r")"

DEFAULTS = {"racks": 6, "aisle_width": 3, "dock_in": 1, "dock_out": 1, "levels": 3, "charge": "right"}


def _num(s: str) -> int:
    return int(s) if s.isdigit() else NUM_WORDS[s]


def extract_requirements(text: str) -> dict[str, Any]:
    """문장에서 찾은 항목만 담아 돌려준다 (없는 항목은 키가 없음)."""
    t = text.replace(" ", " ")
    req: dict[str, Any] = {}

    m = re.search(r"(?:랙|선반)\s*" + _NUM + r"\s*(?:줄|열|개)", t)
    if m:
        req["racks"] = _num(m.group(1))

    if re.search(r"통로\s*(?:없이|없게|0)", t):
        req["aisle_width"] = 0
    else:
        m = re.search(r"통로(?:\s*폭)?(?:은|는|을)?\s*" + _NUM + r"\s*(?:m|미터|M)", t)
        if m:
            req["aisle_width"] = _num(m.group(1))

    m = re.search(r"(?:입하\s*도크|입하|들어오는\s*곳)\s*(?:는|은)?\s*" + _NUM + r"\s*(?:개|곳)?", t)
    if m:
        req["dock_in"] = _num(m.group(1))
    m = re.search(r"(?:출하\s*도크|출하|나가는\s*곳)\s*(?:는|은)?\s*" + _NUM + r"\s*(?:개|곳)?", t)
    if m:
        req["dock_out"] = _num(m.group(1))
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


def fill_defaults(req: dict[str, Any]) -> tuple[dict[str, Any], list[str]]:
    """누락 항목을 기본값으로 채운다 (SC-12).

    모의 규칙: 랙, 통로만 말하고 도크를 말하지 않았으면(W5) 도크를 0으로 두어 검증 실패 -> 수정 질문.
    아무 항목도 없거나(W6) 구역, 일방통행, 충전처럼 다른 구성을 말했으면(W3) 도크도 기본값으로 채운다.
    """
    out = dict(req)
    applied: list[str] = []
    explicit_other = any(k in req for k in ("zones", "one_way", "charge"))
    nothing = not any(k in req for k in ("racks", "aisle_width", "dock_in", "dock_out", "zones", "one_way"))
    use_defaults = req.get("use_defaults", False)

    labels = {"racks": "랙 {}줄", "aisle_width": "통로 폭 {}m", "dock_in": "입하 {}", "dock_out": "출하 {}", "levels": "랙 {}단"}
    for key in ("racks", "aisle_width", "levels"):
        if key not in out:
            out[key] = DEFAULTS[key]
            applied.append(labels[key].format(DEFAULTS[key]))
    for key in ("dock_in", "dock_out"):
        if key not in out:
            if nothing or explicit_other or use_defaults:
                out[key] = DEFAULTS[key]
                applied.append(labels[key].format(DEFAULTS[key]))
            else:
                out[key] = 0
    out.setdefault("charge", DEFAULTS["charge"])
    out.setdefault("zones", 1)
    out.pop("use_defaults", None)
    return out, applied


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
    # "입하, 출하 1개씩" 같은 표현
    if re.search(r"(?:각각|씩)", answer) and "도크" in answer:
        m = re.search(_NUM + r"\s*개", answer)
        if m:
            merged["dock_in"] = merged["dock_out"] = _num(m.group(1))
    return merged
