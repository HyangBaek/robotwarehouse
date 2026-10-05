"""모의 서버용 최종 리포트 (server/tools/report.py 를 모의 엔진 로그 형식에 맞춰 옮김).

build_report: 시뮬레이션 종료 후 최종 리포트 (FR-25~28). 숫자는 모두 로그에서 코드로 계산한다.

시간 단위는 스텝이다. 1스텝 = 로봇이 한 칸(1m) 움직이는 시간 (로봇 1m/s 가정 시 약 1초).
로봇 상태 (프레임 t 의 state = t-1 -> t 사이에 한 일)
  move_loaded 적재 이동, move_empty 빈 이동, load/unload 적재, 하역, wait 작업 중 대기, idle 유휴
"""
from __future__ import annotations

import statistics as st
from typing import Any


STATES = ("move_loaded", "move_empty", "handling", "wait", "idle")
STATE_LABEL = {"move_loaded": "적재 이동", "move_empty": "빈 이동", "handling": "적재·하역", "wait": "대기", "idle": "유휴"}
TIMELINE_BUCKETS = 24
HIST_BINS = 8


def _pct(a: float, b: float) -> float:
    return round(a / b * 100, 1) if b else 0.0


def _improve(base: float | None, opt: float | None, lower_is_better: bool = True) -> float | None:
    if base in (None, 0) or opt is None:
        return None
    d = (base - opt) / base * 100
    return round(d if lower_is_better else -d, 1)


# 모의 엔진 상태 → 실제 서버 상태 이름
_STATE_MAP = {"carry": "move_loaded", "move": "move_empty", "load": "load", "unload": "unload",
              "wait": "wait", "idle": "idle", "charge": "idle"}


def adapt_log(log: dict) -> dict:
    """모의 엔진 로그를 실제 서버 로그 형식으로 (상태 이름, 주문 status)."""
    frames = [{"t": f["t"], "robots": [{**r, "state": _STATE_MAP.get(r["state"], r["state"])} for r in f["robots"]]}
              for f in log["frames"]]
    orders = [{**o, "status": "done" if o.get("done_t") is not None else "pending"} for o in log["orders"]]
    return {**log, "frames": frames, "orders": orders}


def _bottlenecks(log: dict, top_k: int = 5) -> list[dict]:
    stats = sorted(log.get("stats", []), key=lambda c: (-c["wait"], c["x"], c["y"]))
    return [{"x": c["x"], "y": c["y"], "wait": c["wait"]} for c in stats[:top_k] if c["wait"] > 0]


def robot_stats(log: dict) -> list[dict[str, Any]]:
    frames = log["frames"]
    counts: dict[str, dict[str, int]] = {}
    for fr in frames[1:]:
        for r in fr["robots"]:
            c = counts.setdefault(r["id"], {k: 0 for k in STATES})
            s = r["state"]
            if s in ("load", "unload"):
                s = "handling"
            elif s not in c:
                s = "move_empty" if s.startswith("move") else "idle"
            c[s] += 1
    done_by: dict[str, int] = {}
    for o in log["orders"]:
        if o.get("status") == "done" and o.get("robot_id"):
            done_by[o["robot_id"]] = done_by.get(o["robot_id"], 0) + 1
    out = []
    for rid in sorted(counts, key=lambda x: (len(x), x)):
        c = counts[rid]
        total = sum(c.values()) or 1
        busy = c["move_loaded"] + c["move_empty"] + c["handling"]
        out.append({
            "id": rid, "orders": done_by.get(rid, 0), "steps": total,
            **c,
            "distance": c["move_loaded"] + c["move_empty"],
            "utilization_pct": _pct(busy, total),            # 가동률: 이동, 적재, 하역 비율
            "productive_pct": _pct(c["move_loaded"] + c["handling"], total),   # 짐을 다루는 비율
            "wait_pct": _pct(c["wait"], total),
            "idle_pct": _pct(c["idle"], total),
        })
    return out


def order_stats(log: dict) -> dict[str, Any]:
    done = [o for o in log["orders"] if o.get("status") == "done"]
    lead = sorted(o["done_t"] - o["arrival_t"] for o in done)
    queue = [o["assigned_t"] - o["arrival_t"] for o in done if o.get("assigned_t") is not None]
    service = [o["done_t"] - o["assigned_t"] for o in done if o.get("assigned_t") is not None]

    def q(p):
        return lead[min(len(lead) - 1, int(round(p * (len(lead) - 1))))] if lead else None

    by_type = {}
    for ty in ("inbound", "outbound"):
        lt = [o["done_t"] - o["arrival_t"] for o in done if o["type"] == ty]
        by_type[ty] = {"count": len(lt), "avg": round(st.mean(lt), 1) if lt else None}
    by_spec = {}                                          # 제품 규격별 (FR-11)
    for o in log["orders"]:
        b = by_spec.setdefault(o.get("spec") or "pallet_1100x1100", {"total": 0, "done": 0, "qty": 0, "lead": []})
        b["total"] += 1
        b["qty"] += o.get("qty", 1) or 1
        if o.get("status") == "done":
            b["done"] += 1
            b["lead"].append(o["done_t"] - o["arrival_t"])
    for b in by_spec.values():
        lt = b.pop("lead")
        b["avg"] = round(st.mean(lt), 1) if lt else None
    hist = []
    if lead:
        lo, hi = lead[0], lead[-1]
        width = max(1, -(-(hi - lo + 1) // HIST_BINS))
        for i in range(HIST_BINS):
            a = lo + i * width
            hist.append({"from": a, "to": a + width - 1, "count": sum(1 for x in lead if a <= x < a + width)})
    return {
        "total": len(log["orders"]), "done": len(done),
        "rejected": sum(1 for o in log["orders"] if o.get("status") == "rejected"),
        "completion_pct": _pct(len(done), len(log["orders"])),
        "lead_avg": round(st.mean(lead), 1) if lead else None,
        "lead_median": q(0.5), "lead_p90": q(0.9), "lead_max": lead[-1] if lead else None,
        "lead_min": lead[0] if lead else None,
        "queue_avg": round(st.mean(queue), 1) if queue else None,       # 도착 -> 배정
        "service_avg": round(st.mean(service), 1) if service else None,  # 배정 -> 완료
        "last_done_t": max((o["done_t"] for o in done), default=None),
        "by_type": by_type, "by_spec": by_spec, "histogram": hist,
    }


def timeline(log: dict) -> list[dict[str, Any]]:
    """구간 끝 시점마다 누적 완료 주문, 진행 중 주문, 일하는 로봇 수."""
    T = log["total_steps"]
    if T <= 0:
        return []
    ends = sorted({max(1, round(T * (i + 1) / TIMELINE_BUCKETS)) for i in range(TIMELINE_BUCKETS)})
    frames = log["frames"]
    out = []
    for t in ends:
        done = sum(1 for o in log["orders"] if o.get("done_t") is not None and o["done_t"] <= t)
        arrived = sum(1 for o in log["orders"] if o["arrival_t"] <= t)
        fr = frames[min(t, len(frames) - 1)]
        busy = sum(1 for r in fr["robots"] if r["state"] not in ("idle", "wait"))
        out.append({"t": t, "done": done, "in_progress": max(0, arrived - done), "busy_robots": busy,
                    "robots": len(fr["robots"])})
    return out


def kpis(log: dict, robots: list[dict], orders: dict, grid: dict | None = None) -> dict[str, Any]:
    T = log["total_steps"]
    n = len(robots) or 1
    return {
        "total_steps": T,
        "throughput_per_100": round(orders["done"] / T * 100, 2) if T else 0.0,   # 100스텝당 완료 주문
        "orders_done": orders["done"], "orders_total": orders["total"],
        "completion_pct": orders["completion_pct"],
        "lead_avg": orders["lead_avg"], "lead_p90": orders["lead_p90"],
        "utilization_pct": round(sum(r["utilization_pct"] for r in robots) / n, 1),
        "productive_pct": round(sum(r["productive_pct"] for r in robots) / n, 1),
        "wait_pct": round(sum(r["wait_pct"] for r in robots) / n, 1),
        "idle_pct": round(sum(r["idle_pct"] for r in robots) / n, 1),
        "distance": sum(r["distance"] for r in robots),
        "distance_per_order": round(sum(r["distance"] for r in robots) / orders["done"], 1) if orders["done"] else None,
        "collisions": len(log.get("collisions", [])),
        "rule_violations": 0,                                                  # 모의 엔진은 규칙 검사 없음
        "replan_avg_ms": round(log.get("meta", {}).get("replan_avg_s", 0) * 1000, 1),
        "robots": len(robots),
    }


COMPARE_ROWS = [  # (키, 이름, 단위, 낮을수록 좋음)
    ("total_steps", "총 처리 시간", "스텝", True),
    ("lead_avg", "주문당 평균 처리 시간", "스텝", True),
    ("lead_p90", "주문 처리 시간 P90", "스텝", True),
    ("throughput_per_100", "처리량 (100스텝당 주문)", "건", False),
    ("utilization_pct", "로봇 가동률", "%", False),
    ("wait_pct", "대기 비율", "%", True),
    ("distance", "총 이동 거리", "칸", True),
]


def comparison(base_k: dict, opt_k: dict) -> list[dict[str, Any]]:
    rows = []
    for key, label, unit, lower in COMPARE_ROWS:
        b, o = base_k.get(key), opt_k.get(key)
        imp = _improve(b, o, lower)
        if unit == "%" and b is not None and o is not None:       # 비율 지표는 %p 차이로 표시
            change = f"{o - b:+.1f}%p"
            better = (o < b) if lower else (o > b)
        else:
            change = f"{'-' if lower else '+'}{abs(imp)}%" if imp is not None and imp > 0 else (
                f"{'+' if lower else '-'}{abs(imp)}%" if imp is not None else "-")
            better = imp is not None and imp > 0
        rows.append({"key": key, "label": label, "unit": unit, "baseline": b, "optimized": o,
                     "improvement_pct": imp, "change_text": change, "better": better})
    return rows


def insights(k: dict, robots: list[dict], orders: dict, agg: dict, rec: dict | None,
             cmp_rows: list[dict] | None) -> list[dict[str, str]]:
    """규칙 기반 요약 문장. 숫자는 위에서 계산한 값만 쓴다. level: good | info | warn"""
    out = []
    if cmp_rows:
        r = cmp_rows[0]
        if r["improvement_pct"] is not None:
            out.append({"level": "good" if r["better"] else "warn",
                        "text": f"기준 전략 대비 총 처리 시간 {abs(r['improvement_pct'])}% "
                                f"{'단축' if r['better'] else '증가'} ({r['baseline']} → {r['optimized']}스텝)"})
    if orders["done"] < orders["total"]:
        out.append({"level": "warn", "text": f"주문 {orders['total'] - orders['done']}건 처리 못 함 "
                                             f"(거부 {orders['rejected']}건): 분석 화면에서 원인과 재시뮬레이션 권고 확인"})
    else:
        out.append({"level": "good", "text": f"주문 {orders['total']}건 모두 완료, 충돌 {k['collisions']}건, "
                                             f"운영 규칙 위반 {k['rule_violations']}건"})
    if len(orders.get("by_spec", {})) > 1:
        parts = [f"{s.split('_')[-1]} {b['done']}/{b['total']}건 평균 {b['avg']}스텝" for s, b in sorted(orders["by_spec"].items())]
        out.append({"level": "info", "text": "규격별 처리: " + ", ".join(parts)})
    if robots:
        busiest = max(robots, key=lambda r: (r["orders"], r["utilization_pct"]))
        least = min(robots, key=lambda r: (r["orders"], r["utilization_pct"]))
        if busiest["id"] != least["id"]:
            out.append({"level": "info", "text": f"가장 많이 일한 로봇 {busiest['id']} (주문 {busiest['orders']}건, "
                                                 f"가동률 {busiest['utilization_pct']}%), 가장 적은 로봇 {least['id']} "
                                                 f"(주문 {least['orders']}건)"})
    if k["idle_pct"] >= 35 and rec and k["robots"] > rec["robots"]:
        out.append({"level": "info", "text": f"로봇 평균 유휴 {k['idle_pct']}%: 로봇이 남습니다. "
                                             f"이 지도의 권장 로봇 수는 {rec['robots']}대"})
    elif k["idle_pct"] >= 35:
        out.append({"level": "info", "text": f"로봇 평균 유휴 {k['idle_pct']}%: 주문 도착보다 처리 능력이 큽니다"})
    if k["wait_pct"] >= 12:
        b = (agg.get("bottlenecks") or [{}])[0]
        where = f" 대기가 가장 많은 칸 ({b.get('x')},{b.get('y')})" if b else ""
        out.append({"level": "warn", "text": f"작업 중 대기 비율 {k['wait_pct']}%: 혼잡이 있습니다.{where}"})
    if orders["lead_avg"] and orders["lead_p90"] and orders["lead_p90"] > orders["lead_avg"] * 1.8:
        out.append({"level": "info", "text": f"주문 처리 시간 편차가 큼: 평균 {orders['lead_avg']} / P90 {orders['lead_p90']}스텝"})
    return out


def build_report(log: dict, grid: dict, scenario: dict, baseline_log: dict | None = None,
                 map_summary: str = "", sim_id: str = "") -> dict[str, Any]:
    log = adapt_log(log)
    robots = robot_stats(log)
    orders = order_stats(log)
    k = kpis(log, robots, orders, grid)
    agg = {"bottlenecks": _bottlenecks(log)}
    rec = None
    base = None
    cmp_rows = None
    if baseline_log is not None:
        baseline_log = adapt_log(baseline_log)
        b_robots = robot_stats(baseline_log)
        b_orders = order_stats(baseline_log)
        bk = kpis(baseline_log, b_robots, b_orders, grid)
        cmp_rows = comparison(bk, k)
        base = {"kpis": bk, "timeline": timeline(baseline_log)}
    return {
        "sim_id": sim_id, "strategy": log.get("strategy"), "seed": log.get("seed"),
        "map_summary": map_summary, "map_size": [grid.get("width"), grid.get("height")],
        "scenario": {k2: scenario.get(k2) for k2 in ("robots", "inbound", "outbound")},
        "recommended_robots": rec["robots"] if rec else None,
        "kpis": k, "robots": robots, "orders": orders, "timeline": timeline(log),
        "bottlenecks": agg["bottlenecks"], "baseline": base, "comparison": cmp_rows,
        "insights": insights(k, robots, orders, agg, rec, cmp_rows),
        "state_labels": STATE_LABEL,
        "time_note": "1스텝 = 로봇이 한 칸(1m) 이동하는 시간 (1m/s 가정 시 약 1초)",
    }
