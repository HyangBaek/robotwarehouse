"""
service.py - 서버(Agent 도구)가 엔진을 부를 때 쓰는 얇은 연결 계층

  run_simulation(map_json, scenario)  : SC-04. 엔진 호출 + 충돌, 미완료 점검. 문제가 있으면 ok=False (VR로 보내지 않음)
  extract_orders(log)                 : 로그에서 주문 목록만 꺼냄 (SC-10: 같은 주문으로 재시뮬레이션)
  resimulate(map, scenario, base_log, change) : SC-10. 개선안 1개를 적용해 같은 주문으로 다시 실행하고 전, 후 비교

개선안(change) 형식 - 구현 시나리오 SC-09의 4개 유형 중 엔진이 직접 처리하는 3개
  {"type": "one_way",        "from": [x, y], "to": [x, y]}   일방통행 구간 추가 (rules.one_way)
  {"type": "storage_weight", "value": 5.0}                    보관 위치 점수의 '몰림' 벌점 가중치
  {"type": "robots",         "value": 6}                      로봇 수
  (도크 추가는 지도 자체가 바뀌므로 지도 수정, 검증을 거친 새 지도를 map_json으로 넘기면 됨)
"""
from __future__ import annotations

import copy

from .engine import DEFAULT_SPEC, simulate


# ----------------------------------------------------------------------------- 입력 정리
def normalize_scenario(raw: dict) -> dict:
    """POST /scenario 본문 {map_version, robots, inbound, outbound} -> 엔진 시나리오."""
    keys = ("scenario_id", "map_version", "robots", "inbound", "outbound", "orders", "events", "spec_mix")
    sc = {k: copy.deepcopy(raw[k]) for k in keys if k in raw}
    r = sc.get("robots", 4)
    if isinstance(r, dict):                       # {"count": 4, "starts": [[x, y], ...]} 형태도 허용
        r = r.get("starts") or r.get("count", 4)
    sc["robots"] = r
    if "orders" not in sc:
        for k in ("inbound", "outbound"):
            sc[k] = int(sc.get(k, 0))
            if sc[k] < 0:
                raise ValueError(f"{k}는 0 이상이어야 합니다")
    n = len(r) if isinstance(r, list) else int(r)
    if n < 1:
        raise ValueError("로봇은 1대 이상이어야 합니다")
    return sc


# ----------------------------------------------------------------------------- SC-04
def run_simulation(map_json: dict, scenario: dict, strategy: str = "optimized", seed: int = 42,
                   config: dict | None = None) -> dict:
    """반환: {"ok": bool, "log": dict | None, "error": None | {"code", "message"}}
    ok=False 이면 결과를 VR로 보내지 말고 서버 로그에 error를 남긴다 (SC-04 예외)."""
    try:
        sc = normalize_scenario(scenario)
        log = simulate(map_json, sc, strategy, seed, config)
    except Exception as e:                        # 입력 오류, 도크 없음 등
        return dict(ok=False, log=None, error=dict(code="ENGINE_ERROR", message=f"{type(e).__name__}: {e}"))
    return check_log(log)


def check_log(log: dict) -> dict:
    """충돌, 미완료 점검. 반환 형식은 run_simulation 과 같다 (서버의 롤링 재계획 결과에도 쓴다)."""
    if log["collisions"]:
        return dict(ok=False, log=log, error=dict(
            code="COLLISION", message=f"충돌 {len(log['collisions'])}건 (첫 충돌: {log['collisions'][0]})"))
    if not log["completed"]:
        rej = [o["order_id"] for o in log["orders"] if o["status"] != "done"]
        return dict(ok=False, log=log, error=dict(
            code="INCOMPLETE", message=f"미완료 주문 {len(rej)}건: {rej[:5]} (재고·용량 부족 또는 교착)"))
    return dict(ok=True, log=log, error=None)


# ----------------------------------------------------------------------------- SC-10
def extract_orders(log: dict) -> list[dict]:
    return [dict(order_id=o["order_id"], type=o["type"], arrival_t=o["arrival_t"],
                 spec=o.get("spec", DEFAULT_SPEC), qty=o.get("qty", 1)) for o in log["orders"]]


def apply_change(map_json: dict, scenario: dict, config: dict | None, change: dict):
    m, sc, cfg = copy.deepcopy(map_json), copy.deepcopy(scenario), dict(config or {})
    t = change["type"]
    if t == "one_way":
        m.setdefault("rules", {}).setdefault("one_way", []).append(
            {"from": list(change["from"]), "to": list(change["to"])})
    elif t == "storage_weight":
        cfg["storage_load_weight"] = float(change["value"])
    elif t == "robots":
        sc["robots"] = int(change["value"])
    else:
        raise ValueError(f"지원하지 않는 개선안 유형: {t}")
    return m, sc, cfg


def _summary(log):
    return dict(total_steps=log["total_steps"], avg_order_time=log["meta"]["avg_order_time"],
                total_waits=log["meta"]["total_waits"], collisions=len(log["collisions"]),
                completed=log["completed"])


def resimulate(map_json: dict, scenario: dict, base_log: dict, change: dict, seed: int = 42,
               config: dict | None = None) -> dict:
    """개선안 적용 전(base_log)과 후를 같은 주문(종류, 도착 스텝)으로 비교한다.
    반환: {"ok", "change", "before", "after", "improvement_pct", "log_after", "error"}"""
    m2, sc2, cfg2 = apply_change(map_json, normalize_scenario(scenario), config, change)
    sc2["orders"] = extract_orders(base_log)          # 같은 주문 고정
    for k in ("inbound", "outbound"):
        sc2.pop(k, None)
    res = run_simulation(m2, sc2, base_log["strategy"], seed, cfg2)
    if res["log"] is None:
        return dict(ok=False, change=change, before=_summary(base_log), after=None,
                    improvement_pct=None, log_after=None, error=res["error"])
    before, after = _summary(base_log), _summary(res["log"])
    imp = round((before["total_steps"] - after["total_steps"]) / before["total_steps"] * 100, 1) \
        if before["total_steps"] else None
    return dict(ok=res["ok"], change=change, before=before, after=after, improvement_pct=imp,
                log_after=res["log"], error=res["error"])
