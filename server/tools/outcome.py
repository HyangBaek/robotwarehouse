"""diagnose: 시뮬레이션이 주문을 다 처리하지 못했을 때 '무슨 일이 있었고, 누구 문제이며, 무엇을 바꿔 다시 돌릴지' 정리.

'처리 못 함'은 분석 실패가 아니다. 시뮬레이션은 끝까지 돌았고, 주문 일부를 처리하지 못한 채 끝난 상태다.

원인 판단 (엔진이 사유를 주면 그대로, 없으면 로그로 추정)
  거절된 입하 주문  -> no_capacity  같은 규격 랙에 빈칸이 없음 (지도 검증이 길 막힘을 먼저 거르므로)
  거절된 출하 주문  -> no_stock     같은 규격 재고가 없음
  orders[].reason 이 unreachable -> 길 막힘 (PIBT 엔진처럼 사유를 주는 엔진)
  log.stopped == deadlock        -> 로봇 정체 (PIBT 엔진)
  스텝 상한에 닿음                -> max_steps

결과는 세 군데에 쓴다.
  1. sim_ready.summary["처리 못 함"], sim_ready.issues : VR 패널
  2. 분석 Agent 입력(facts["처리_못_함"])과 설명 앞부분   : agent/nodes/analyst.py
  3. 개선안 후보 앞쪽 (재시뮬레이션 권고)                : tools.proposals 와 같은 형식 {type, text, apply, reason, effects}
"""
from __future__ import annotations

import math
from collections import Counter
from typing import Any

DEFAULT_SPEC = "pallet_1100x1100"
SPEC_LABEL = {"pallet_1100x1100": "1100x1100", "pallet_1200x1000": "1200x1000"}

ISSUES = {
    "no_capacity": dict(owner="창고 조건", what="입고할 랙 칸이 모두 차서 더 받을 수 없었습니다",
                        label="보관 공간 부족으로 입고 {n}건 처리 못 함"),
    "no_stock": dict(owner="창고 조건", what="출고할 재고가 랙에 없어 내보낼 수 없었습니다",
                     label="재고 부족으로 출고 {n}건 처리 못 함"),
    "unreachable": dict(owner="지도", what="로봇이 랙이나 도크까지 갈 길이 없었습니다 (지도 검증에서 걸렀어야 하는 경우)",
                        label="길이 막혀 {n}건 처리 못 함"),
    "deadlock": dict(owner="알고리즘 한계", what="로봇끼리 서로 길을 막아 한동안 아무 주문도 진행되지 않아 엔진이 멈췄습니다",
                     label="로봇 정체로 t={t}에서 중단, {n}건 처리 못 함"),
    "max_steps": dict(owner="처리량 부족", what="스텝 상한 안에 주문을 다 끝내지 못했습니다",
                      label="제한 시간 안에 {n}건 처리 못 함"),
}


def _reason(o: dict) -> str:
    if o.get("reason"):
        return o["reason"]
    return "no_capacity" if o.get("type") == "inbound" else "no_stock"


def _stopped(log: dict) -> str | None:
    if log.get("stopped"):
        return log["stopped"]
    cap = ((log.get("meta") or {}).get("config") or {}).get("max_steps", 3000)
    return "max_steps" if log["total_steps"] >= cap else None


def diagnose(log: dict, scenario: dict | None = None, req: dict | None = None,
             grid: dict | None = None) -> dict[str, Any]:
    """반환: {"completed", "unfinished", "stopped", "label", "issues": [...], "recommendations": [...]}"""
    m = log["meta"]
    unfinished = max(0, m["orders_total"] - m["orders_done"])
    out: dict[str, Any] = {"completed": bool(log["completed"]), "unfinished": unfinished, "stopped": None,
                           "label": None, "issues": [], "recommendations": []}
    if log["completed"]:
        return out
    stopped = _stopped(log)
    out["stopped"] = stopped
    rejected = [o for o in log["orders"] if o.get("status") == "rejected"]
    by = Counter(_reason(o) for o in rejected)
    specs = {code: Counter(o.get("spec", DEFAULT_SPEC) for o in rejected if _reason(o) == code) for code in by}
    issues = [_issue(code, by[code], log, specs.get(code)) for code in ("no_capacity", "no_stock", "unreachable") if by.get(code)]
    rest = unfinished - len(rejected)
    if rest > 0 and stopped in ("deadlock", "max_steps"):
        issues.append(_issue(stopped, rest, log))
    elif rest > 0 and not issues:
        issues.append({"code": "unfinished", "count": rest, "owner": "확인 필요",
                       "what": "끝나지 않은 주문이 남았습니다", "label": f"주문 {rest}건 처리 못 함"})
    out["issues"] = issues
    if issues:
        out["label"] = f"주문 {unfinished}건 처리 못 함: " + ", ".join(i["label"] for i in issues)
    out["recommendations"] = recommend(issues, scenario or {}, req or {}, grid)
    return out


def _issue(code: str, n: int, log: dict, specs: Counter | None = None) -> dict:
    spec = ISSUES[code]
    label = spec["label"].format(n=n, t=log["total_steps"])
    odd = [s for s in (specs or {}) if s != DEFAULT_SPEC]
    if odd:                                                   # 기본 규격이 아니면 규격을 밝힌다
        label += " (" + ", ".join(f"{SPEC_LABEL.get(s, s)} 규격 {specs[s]}건" for s in odd) + ")"
    it = {"code": code, "count": n, "owner": spec["owner"], "what": spec["what"], "label": label}
    if code in ("deadlock", "max_steps"):
        it["t"] = log["total_steps"]
    if specs:
        it["specs"] = dict(specs)
    return it


def _robots(sc: dict) -> int:
    r = sc.get("robots", 4)
    return len(r) if isinstance(r, list) else int(r)


def _rack_line_room(grid: dict | None, req: dict) -> int:
    """랙 한 줄이 처음에 받을 수 있는 입고 수 (줄 전체 용량 x (1 - 시작 재고 비율 0.5))."""
    racks = (grid or {}).get("racks") or []
    lines = int(req.get("racks") or 0)
    if not racks or not lines:
        return 15
    return max(1, int(sum(r.get("capacity", 6) for r in racks) / lines * 0.5))


def recommend(issues: list[dict], sc: dict, req: dict, grid: dict | None) -> list[dict]:
    """원인마다 재시뮬레이션할 변경 1~2개. 형식은 tools.proposals 후보와 같다 (LLM 은 이 안에서만 고른다)."""
    recs: list[dict] = []

    def add(ptype, apply, text, reason, effect):
        if all(r["apply"] != apply for r in recs):
            recs.append({"type": ptype, "apply": apply, "text": text, "reason": reason,
                         "effects": [effect, "같은 지도 조건에서 다시 실행해 처리 못 한 주문이 사라지는지 확인"]})

    inbound, outbound, robots = int(sc.get("inbound", 0)), int(sc.get("outbound", 0)), _robots(sc)
    for it in issues:
        n, code = it["count"], it["code"]
        why = it["label"]
        if code == "no_capacity":
            racks = int(req.get("racks") or 0)
            if racks:
                k = math.ceil(n / _rack_line_room(grid, req))
                add("map_resize", {"racks": racks + k}, f"랙 {racks}줄 -> {racks + k}줄로 늘려 다시 실행", why,
                    f"입고 {n}건을 받을 칸 확보 (지도가 바뀌므로 검증 후 실행)")
            if inbound > n:
                add("order_count", {"inbound": inbound - n}, f"입하 {inbound}건 -> {inbound - n}건으로 줄여 다시 실행", why,
                    "현재 창고가 감당하는 물량으로 결과 확인")
        elif code == "no_stock":
            if outbound > n:
                add("order_count", {"outbound": outbound - n}, f"출하 {outbound}건 -> {outbound - n}건으로 줄여 다시 실행", why,
                    "재고 범위 안에서 결과 확인")
            add("order_count", {"inbound": inbound + n}, f"입하 {inbound}건 -> {inbound + n}건으로 늘려 다시 실행", why,
                "재고 보충 (출하가 입하보다 먼저 오면 일부는 여전히 못 할 수 있음)")
        elif code == "deadlock":
            if robots > 2:
                add("robot_count", {"robots": robots - 2}, f"로봇 {robots}대 -> {robots - 2}대로 줄여 다시 실행", why,
                    "서로 막는 로봇 수를 줄여 정체 해소")
            aw = int(req.get("aisle_width") or 0)
            if aw:
                add("map_resize", {"aisle_width": aw + 1}, f"통로 폭 {aw}m -> {aw + 1}m로 넓혀 다시 실행", why,
                    "비켜 갈 공간 확보 (지도가 바뀌므로 검증 후 실행)")
        elif code == "max_steps":
            add("robot_count", {"robots": robots + 2}, f"로봇 {robots}대 -> {robots + 2}대로 늘려 다시 실행", why,
                "처리량을 늘려 제한 시간 안에 끝내기")
        # unreachable 은 지도를 직접 고쳐야 하므로 자동 재시뮬레이션 권고 없음 (설명으로만 안내)
    return recs[:3]


def summary_line(diag: dict) -> dict:
    """sim_ready.summary 에 붙일 항목 (VR 패널은 '키: 값'으로 보여 준다)."""
    return {"처리 못 함": diag["label"]} if diag["label"] else {}


def explanation_lines(diag: dict) -> list[str]:
    """분석 설명문 앞부분: 실제로 일어난 일 + 누구 문제."""
    lines = [f"{it['label']}. {it['what']} ({it['owner']} 문제)." for it in diag["issues"]]
    if any(it["code"] == "unreachable" for it in diag["issues"]):
        lines.append("길이 막힌 경우는 개선안 대신 지도를 직접 고쳐야 합니다.")
    return lines


def facts_of(diag: dict) -> dict | None:
    """분석 Agent(LLM) 입력에 붙일 '처리 못 함' 사실 (설명에 쓸 수 있는 숫자는 이 안의 값뿐)."""
    if diag["completed"]:
        return None
    return {"처리_못_한_주문": diag["unfinished"],
            "원인": [{"구분": it["code"], "건수": it["count"], "실제로_일어난_일": it["what"],
                     "누구_문제": it["owner"], "화면_표현": it["label"]} for it in diag["issues"]]}
