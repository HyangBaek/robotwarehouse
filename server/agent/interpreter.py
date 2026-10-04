"""지시 해석 (FR-03): 문장 -> 요구사항 JSON. LLM 우선, 실패하면 정규식 대체 경로.

LLM 은 요구사항 키만 채운다. 좌표, 지도는 tools.map_generator 가 계산한다.
출력 키는 tools.parse_text.extract_requirements 와 같아서 fill_defaults, generate_map 을 그대로 쓴다.
"""
from __future__ import annotations

import json
import logging
from typing import Any

from tools.map_generator import DEFAULTS
from tools.parse_text import extract_requirements, merge_answer
from tools.scenario import extract_scenario
from .prompts import load

log = logging.getLogger("agent.interpret")

INT_KEYS = ["racks", "aisle_width", "dock_in", "dock_out", "levels", "zones", "racks_b"]
LIMITS = {"racks": (0, 20), "aisle_width": (0, 6), "dock_in": (0, 6), "dock_out": (0, 6), "racks_b": (0, 20),
          "levels": (1, 10), "zones": (1, 2)}
REQ_SCHEMA = {
    "type": "object", "additionalProperties": False,
    "properties": {**{k: {"type": ["integer", "null"]} for k in INT_KEYS},
                   "charge": {"type": ["string", "null"], "enum": ["left", "right", None]},
                   "one_way": {"type": ["string", "null"], "enum": ["N", "S", "E", "W", "none", None]},
                   "use_defaults": {"type": "boolean"}},
    "required": INT_KEYS + ["charge", "one_way", "use_defaults"],
}

SCN_SCHEMA = {
    "type": "object", "additionalProperties": False,
    "properties": {"robots": {"type": ["integer", "null"]}, "inbound": {"type": ["integer", "null"]},
                   "outbound": {"type": ["integer", "null"]}, "spec_b_pct": {"type": ["integer", "null"]},
                   "run": {"type": "boolean"},
                   "use_defaults": {"type": "boolean"}},
    "required": ["robots", "inbound", "outbound", "spec_b_pct", "run", "use_defaults"],
}


class InterpretError(ValueError):
    pass


def clean(raw: Any) -> dict[str, Any]:
    """LLM 응답 검증 + 정리. null, False, -1 은 '말하지 않음'으로 보고 키째 제거한다. 범위 밖이면 InterpretError."""
    if not isinstance(raw, dict):
        raise InterpretError(f"객체가 아님: {type(raw).__name__}")
    extra = set(raw) - set(REQ_SCHEMA["properties"])
    if extra:
        raise InterpretError(f"모르는 키: {sorted(extra)}")
    out: dict[str, Any] = {}
    for k in INT_KEYS:
        v = raw.get(k)
        if v is None or v == -1:
            continue
        if isinstance(v, bool) or not isinstance(v, (int, float)) or int(v) != v:
            raise InterpretError(f"{k} 가 정수가 아님: {v!r}")
        lo, hi = LIMITS[k]
        if not lo <= int(v) <= hi:
            raise InterpretError(f"{k}={v} 범위 밖 ({lo}~{hi})")
        out[k] = int(v)
    for k, allowed in (("charge", ("left", "right")), ("one_way", ("N", "S", "E", "W", "none"))):
        v = raw.get(k)
        if v in (None, "", "null"):
            continue
        if v not in allowed:
            raise InterpretError(f"{k}={v!r} 허용 값 아님")
        out[k] = v
    if raw.get("use_defaults") is True:
        out["use_defaults"] = True
    return out


def clean_scenario(raw: Any) -> dict[str, Any]:
    if not isinstance(raw, dict) or set(raw) - set(SCN_SCHEMA["properties"]):
        raise InterpretError(f"시나리오 형식 오류: {raw!r}"[:120])
    out: dict[str, Any] = {}
    for k, hi in (("robots", 30), ("inbound", 200), ("outbound", 200), ("spec_b_pct", 100)):
        v = raw.get(k)
        if v is None or v == -1:
            continue
        if isinstance(v, bool) or not isinstance(v, (int, float)) or int(v) != v or not 0 <= v <= hi:
            raise InterpretError(f"{k}={v!r} 범위 밖")
        out[k] = int(v)
    for k in ("run", "use_defaults"):
        if raw.get(k) is True:
            out[k] = True
    return out


def _apply_defaults_answer(merged: dict, new: dict) -> dict:
    """답변이 '기본값으로'였으면 0으로 남은 도크, 통로를 기본값으로 (merge_answer 와 같은 규칙)."""
    if new.get("use_defaults"):
        for k in ("dock_in", "dock_out", "aisle_width"):
            if merged.get(k, 0) == 0:
                merged[k] = DEFAULTS[k]
    return merged          # use_defaults 는 남겨 둔다: 나머지 빠진 항목을 기본값으로 채울지 흐름에서 판단


class Interpreter:
    """llm=None 이면 정규식만 쓴다. 반환 source: "llm" | "regex" | "regex_fallback"."""

    def __init__(self, llm=None):
        self.llm = llm

    def _call(self, prompt: str, user: str) -> dict:
        return clean(self.llm.invoke(load(prompt), user, REQ_SCHEMA))

    def extract(self, text: str) -> tuple[dict[str, Any], str, str | None]:
        """문장 -> (요구사항, source, 실패 사유)."""
        if self.llm is None:
            return extract_requirements(text), "regex", None
        try:
            return self._call("interpret", text), "llm", None
        except Exception as e:
            log.warning("LLM 해석 실패 → 정규식: %s", e)
            return extract_requirements(text), "regex_fallback", f"{type(e).__name__}: {e}"[:200]

    def merge(self, prev: dict[str, Any], question: str | None, answer: str) -> tuple[dict[str, Any], str, str | None]:
        """수정 질문 답변 -> (기존 요구사항에 덮어쓴 요구사항, source, 실패 사유)."""
        if self.llm is not None:
            user = (f"[기존 요구사항] {json.dumps(prev, ensure_ascii=False)}\n"
                    f"[질문] {question or ''}\n[답변] {answer}")
            try:
                new = self._call("interpret_answer", user)
                if not new:
                    raise InterpretError("답변에서 항목을 찾지 못함")
                return _apply_defaults_answer({**prev, **new}, new), "llm", None
            except Exception as e:
                log.warning("LLM 답변 해석 실패 → 정규식: %s", e)
                return merge_answer(prev, answer), "regex_fallback", f"{type(e).__name__}: {e}"[:200]
        return merge_answer(prev, answer), "regex", None

    def extract_scenario(self, text: str) -> tuple[dict[str, Any], str, str | None]:
        """시나리오 문장 -> ({robots?, inbound?, outbound?, run?, use_defaults?}, source, 실패 사유)."""
        if self.llm is None:
            return extract_scenario(text), "regex", None
        try:
            return clean_scenario(self.llm.invoke(load("scenario"), text, SCN_SCHEMA)), "llm", None
        except Exception as e:
            log.warning("LLM 시나리오 해석 실패 → 정규식: %s", e)
            return extract_scenario(text), "regex_fallback", f"{type(e).__name__}: {e}"[:200]
