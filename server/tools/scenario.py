"""시나리오 도구: 지도 크기 기준 권장 시나리오 (FR-12)와 시나리오 문장 정규식 해석.

권장 로봇 수 = 도크 수 x 2 + 4 (2~16대, 랙이 적으면 랙 수 x 2 + 2 이하).
근거: experiments/run_compare.py 보정 실험(W1, W2, W3, W6, L12, 주문 25/25, 시드 1~3).
최적화 전략에서 로봇 2대를 더해도 처리 시간이 10% 미만으로 줄어드는 지점이 W1 8대, W2 10대, W3, W6 6대, L12 12대였고 (10/5 엔진 규칙 반영),
도크 앞 대기가 병목이라 도크 수에 비례한다.
"""
from __future__ import annotations

import re
from typing import Any

from .parse_text import NUM_WORDS, _num

ROBOTS_MIN, ROBOTS_MAX = 1, 16
ORDERS_MAX = 200
DEFAULT_ORDERS = 25           # 입하, 출하 각각

_EXTRA = {"열한": 11, "열하나": 11, "열두": 12, "열둘": 12, "열세": 13, "열셋": 13, "열네": 14, "열넷": 14,
          "열다섯": 15, "열여섯": 16, "스무": 20, "스물": 20, "서른": 30, "마흔": 40, "쉰": 50}
_NUM = r"(\d+|" + "|".join(sorted({**NUM_WORDS, **_EXTRA}, key=len, reverse=True)) + r")"


SPEC_A, SPEC_B = "pallet_1100x1100", "pallet_1200x1000"


def spec_counts(grid: dict) -> tuple[int, int]:
    """(1100x1100 랙 수, 1200x1000 랙 수)"""
    b = sum(1 for r in grid.get("racks", []) if r.get("slot_spec") == SPEC_B)
    return len(grid.get("racks", [])) - b, b


def spec_mix(spec_b_pct: int) -> dict[str, float] | None:
    """1200x1000 비율(%) -> planner spec_mix. 0 이면 None (모두 기본 규격, 이전과 같은 주문)."""
    p = max(0, min(100, int(spec_b_pct or 0)))
    return None if p == 0 else {SPEC_A: (100 - p) / 100, SPEC_B: p / 100}


def check_spec(grid: dict, spec_b_pct: int) -> str | None:
    """주문 규격을 받을 랙이 있는지. 없으면 오류 문장."""
    a, b = spec_counts(grid)
    p = int(spec_b_pct or 0)
    if p > 0 and b == 0:
        return "1200x1000 규격 랙이 없습니다. 지도에 1200 규격 랙을 추가하거나 비율을 0으로 해 주세요"
    if p < 100 and a == 0:
        return "1100x1100 규격 랙이 없습니다. 1200 규격 비율을 100으로 해 주세요"
    return None


def recommend_scenario(grid: dict) -> dict[str, Any]:
    """지도 -> {"robots", "inbound", "outbound", "spec_b_pct", "basis"}"""
    docks = len(grid.get("docks", []))
    racks = len(grid.get("racks", []))
    _, nb = spec_counts(grid)
    robots = docks * 2 + 4
    if racks:
        robots = min(robots, racks * 2 + 2)
    robots = max(2, min(ROBOTS_MAX, robots))
    return {"robots": robots, "inbound": DEFAULT_ORDERS, "outbound": DEFAULT_ORDERS,
            "spec_b_pct": round(nb / racks * 100) if racks else 0,    # 랙 규격 비율과 같게
            "basis": f"도크 {docks}개 × 2 + 4 = 권장 로봇 {robots}대"}


def _n(s: str) -> int:
    return _EXTRA[s] if s in _EXTRA else _num(s)


def extract_scenario(text: str) -> dict[str, Any]:
    """'로봇 6대, 입하 30건 출하 20건, 실행해줘' -> {"robots": 6, "inbound": 30, "outbound": 20, "run": True}.
    말하지 않은 항목은 키가 없다."""
    t = text.replace(" ", " ")
    out: dict[str, Any] = {}
    m = re.search(r"로봇(?:은|는|을|이)?\s*" + _NUM + r"\s*대", t) or re.search(_NUM + r"\s*대(?:로|의)?\s*로봇", t)
    if m:
        out["robots"] = _n(m.group(1))
    m = re.search(r"(?:입하|입고|들어오는)\s*(?:주문)?(?:은|는|을)?\s*" + _NUM + r"\s*(?:건|개)", t)
    if m:
        out["inbound"] = _n(m.group(1))
    m = re.search(r"(?:출하|출고|나가는)\s*(?:주문)?(?:은|는|을)?\s*" + _NUM + r"\s*(?:건|개)", t)
    if m:
        out["outbound"] = _n(m.group(1))
    if "inbound" not in out and "outbound" not in out:
        m = re.search(r"주문(?:은|는|을)?\s*(?:총|전체)?\s*" + _NUM + r"\s*(?:건|개)", t)
        if m:
            total = _n(m.group(1))
            out["inbound"], out["outbound"] = (total + 1) // 2, total // 2
    m = re.search(r"1200\s*(?:x\s*1000)?\s*(?:규격)?\s*(?:제품|주문)?\s*(?:비율)?(?:은|는|을|이|가)?\s*" + _NUM + r"\s*(?:%|퍼센트|프로)", t)
    if m:
        out["spec_b_pct"] = _n(m.group(1))
    elif re.search(r"1200\s*(?:x\s*1000)?\s*규격\s*만", t):
        out["spec_b_pct"] = 100
    if re.search(r"실행|돌려|시작|시뮬레이션\s*해", t):
        out["run"] = True
    if re.search(r"기본값|알아서|권장|추천", t):
        out["use_defaults"] = True
    return out


def merge_scenario(base: dict[str, Any], new: dict[str, Any]) -> dict[str, Any]:
    """권장값(또는 현재값) 위에 말한 항목만 덮어쓰고 범위를 맞춘다."""
    keys = ("robots", "inbound", "outbound", "spec_b_pct")
    out = {k: int(base.get(k, 0) or 0) for k in keys}
    for k in keys:
        if new.get(k) is not None:
            out[k] = int(new[k])
    out["spec_b_pct"] = max(0, min(100, out["spec_b_pct"]))
    out["robots"] = max(ROBOTS_MIN, min(ROBOTS_MAX, out["robots"]))
    for k in ("inbound", "outbound"):
        out[k] = max(0, min(ORDERS_MAX, out[k]))
    return out
