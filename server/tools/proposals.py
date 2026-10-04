"""개선안 후보 생성(규칙)과 적용 (FR-29, 구현 시나리오 SC-09, SC-10).

후보는 엔진이 적용할 수 있는 4가지 유형만 만든다. LLM 은 이 후보 안에서만 고른다 (agent/nodes/analyst.py).
  dock_add        도크 추가                지도 수정 후 재검증
  robot_count     로봇 수 조정             시나리오 값 변경
  storage_weight  보관 위치 몰림 벌점 조정  엔진 설정 storage_load_weight 변경
  one_way         병목 통로 일방통행 지정   rules.one_way 추가 후 재검증
처리 못 한 주문이 있을 때 tools.outcome 이 앞쪽에 넣는 재시뮬레이션 권고
  map_resize      랙 줄 수, 통로 폭 변경    요구사항 변경 -> 지도 재생성 -> 재검증
  order_count     입하, 출하 건수 변경      시나리오 값 변경
"""
from __future__ import annotations

import copy
from typing import Any

from .map_generator import RACK_LENGTH, generate_map
from .map_validator import validate_map
from .scenario import recommend_scenario

PROPOSAL_TYPES = ("dock_add", "robot_count", "storage_weight", "one_way", "map_resize", "order_count")
DEFAULT_STORAGE_WEIGHT = 3.0          # 엔진 기본값 (planner DEFAULT_CFG["storage_load_weight"] 와 같음)


def _one_way_lane(grid: dict, x: int) -> dict | None:
    """x 열이 랙 사이 세로 통로이면 랙 구간 전체를 북쪽 일방통행으로 만든 규칙. 아니면 None."""
    types = {(c["x"], c["y"]): c["type"] for c in grid["cells"]}
    rack_y = sorted(c["y"] for c in grid["cells"] if c["type"] == "rack")
    if not rack_y:
        return None
    y0, y1 = rack_y[0], rack_y[0] + RACK_LENGTH - 1
    if any(types.get((x, y), "aisle") != "aisle" for y in range(y0, y1 + 1)):
        return None
    if not any(types.get((x + dx, y)) == "rack" for dx in (-1, 1) for y in range(y0, y1 + 1)):
        return None
    return {"from": [x, y0], "to": [x, y1], "dir": "N"}


def build_candidates(agg: dict, grid: dict, scenario: dict, robots: list[dict] | None = None) -> list[dict]:
    """집계(tools.aggregate) + 로봇 효율(tools.report.robot_stats) -> 후보 [{type, text, apply, reason}].
    숫자는 모두 여기서 정한다. 같은 입력이면 같은 후보."""
    out: list[dict[str, Any]] = []
    top = agg.get("bottlenecks") or []
    n = int(scenario.get("robots", 4))
    rec = recommend_scenario(grid)["robots"] if grid.get("docks") else n
    idle = round(sum(r["idle_pct"] for r in robots) / len(robots), 1) if robots else None

    near = agg.get("nearest_dock")
    if near and near["dist"] <= 4:
        kind = "입하" if near["type"] == "dock_in" else "출하"
        out.append({"type": "dock_add", "text": f"{kind} 도크 1개 추가", "apply": {"dock": near["type"]},
                    "reason": f"대기 1위 칸이 {kind} 도크 ({near['x']},{near['y']})에서 {near['dist']}칸",
                    "effects": [f"{kind} 도크 앞 줄 서기가 줄어듦", "지도가 바뀌므로 검증 후 같은 주문으로 다시 실행"]})
    if n > rec:
        v = max(rec, n - 2)
        out.append({"type": "robot_count", "text": f"로봇 {n}대 -> {v}대", "apply": {"robots": v},
                    "reason": f"권장 {rec}대보다 많음" + (f", 평균 유휴 {idle}%" if idle is not None else ""),
                    "effects": ["유휴 로봇과 통로 혼잡이 줄어듦", "처리 시간은 조금 늘 수 있음 (비교 화면에서 확인)"]})
    elif n < rec:
        v = min(rec, n + 2)
        out.append({"type": "robot_count", "text": f"로봇 {n}대 -> {v}대", "apply": {"robots": v},
                    "reason": f"권장 {rec}대보다 적음",
                    "effects": ["동시에 처리하는 주문이 늘어 처리 시간이 줄어듦", "로봇 대수만큼 비용이 늘어남"]})
    if top and agg.get("top5_share_pct", 0) >= 30:
        w = DEFAULT_STORAGE_WEIGHT * 2
        out.append({"type": "storage_weight", "text": f"보관 위치 분산 강화 (몰림 벌점 {DEFAULT_STORAGE_WEIGHT:g} -> {w:g})",
                    "apply": {"storage_load_weight": w},
                    "reason": f"대기 상위 5칸이 전체 대기의 {agg['top5_share_pct']}%",
                    "effects": ["한 랙에 작업이 몰리지 않아 병목 칸 대기가 줄어듦", "이동 거리는 조금 늘 수 있음"]})
    for b in top[:3]:                                      # 병목 칸이 있는 세로 통로를 일방통행으로
        lane = _one_way_lane(grid, b["x"])
        if lane is None:
            continue
        trial = copy.deepcopy(grid)
        trial.setdefault("rules", {}).setdefault("one_way", []).append(lane)
        if validate_map(trial)["valid"]:                   # 막다른 길이 생기면 후보에서 뺌
            out.append({"type": "one_way", "text": f"{b['x']}열 통로 북쪽 일방통행", "apply": {"one_way": lane},
                        "reason": f"병목 칸 ({b['x']},{b['y']}) 대기 {b['wait']}스텝",
                        "effects": ["마주 오는 로봇끼리 비켜 주는 대기가 줄어듦", "돌아가는 경로가 생길 수 있음"]})
            break
    if not out:
        out.append({"type": "dock_add", "text": "출하 도크 1개 추가", "apply": {"dock": "dock_out"},
                    "reason": "뚜렷한 병목이 없어 출고 여유를 늘리는 기본 후보",
                    "effects": ["출하가 몰릴 때의 여유가 생김"]})
    return out


def apply_proposal(proposal: dict, grid: dict, scenario: dict, req: dict) -> dict:
    """반환: {"changed": "map" | "scenario", "grid", "scenario", "req"}. 원본은 바꾸지 않는다."""
    t = proposal.get("type")
    if t not in PROPOSAL_TYPES:
        raise ValueError(f"허용되지 않은 개선안 유형: {t}")
    a = proposal["apply"]
    g, sc, rq = copy.deepcopy(grid), copy.deepcopy(scenario), dict(req)
    if t == "dock_add":
        key = "dock_in" if a["dock"] == "dock_in" else "dock_out"
        rq[key] = int(rq.get(key, 1)) + 1
        return {"changed": "map", "grid": generate_map(rq), "scenario": sc, "req": rq}
    if t == "one_way":
        g.setdefault("rules", {}).setdefault("one_way", []).append(a["one_way"])
        return {"changed": "map", "grid": g, "scenario": sc, "req": rq}
    if t == "map_resize":
        for k in ("racks", "aisle_width"):
            if k in a:
                rq[k] = int(a[k])
        return {"changed": "map", "grid": generate_map(rq), "scenario": sc, "req": rq}
    if t == "order_count":
        for k in ("inbound", "outbound"):
            if k in a:
                sc[k] = int(a[k])
        return {"changed": "scenario", "grid": g, "scenario": sc, "req": rq}
    if t == "robot_count":
        sc["robots"] = int(a["robots"])
    else:
        sc["engine_config"] = {**sc.get("engine_config", {}), "storage_load_weight": float(a["storage_load_weight"])}
    return {"changed": "scenario", "grid": g, "scenario": sc, "req": rq}
