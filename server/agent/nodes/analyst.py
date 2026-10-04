"""분석, 개선 노드: 로그 분석(도구 + LLM 설명), 개선안 제안(규칙 후보 + LLM 선택), 개선안 적용(도구).

LLM 에는 원본 로그가 아니라 집계 수치만 넘긴다 (구현 시나리오 SC-09). LLM 설명에 집계에 없는 숫자가 나오거나
후보 밖의 개선안을 고르면 버리고 규칙 기반 결과를 쓴다 (수치를 지어내지 않게).
"""
import asyncio
import json
import logging
import re

from langchain_core.runnables import RunnableConfig

from tools.aggregate import aggregate_logs
from tools.proposals import apply_proposal, build_candidates
from tools.report import robot_stats
from . import SOURCE_LABEL, flow_of, session_of, step
from ..prompts import load
from ..state import AgentState

log = logging.getLogger("agent.analyst")
ANALYZE_SCHEMA = {"type": "object", "additionalProperties": False,
                  "properties": {"explanation": {"type": "string"}}, "required": ["explanation"]}


def facts_of(agg: dict, robots: list[dict], total_steps: int) -> dict:
    """LLM 에 넘길 집계 수치 (설명에 쓸 수 있는 숫자는 이것뿐)."""
    busy = max(robots, key=lambda r: r["utilization_pct"]) if robots else None
    lazy = min(robots, key=lambda r: r["utilization_pct"]) if robots else None
    return {
        "총_처리_스텝": total_steps, "전체_대기_스텝": agg["total_wait"], "상위5칸_대기_비중_퍼센트": agg["top5_share_pct"],
        "대기_상위_칸": [{"x": b["x"], "y": b["y"], "대기": b["wait"], "통과": b["pass"]} for b in agg["bottlenecks"][:5]],
        "대기1위에_가까운_도크": agg["nearest_dock"],
        "로봇_평균_유휴_퍼센트": round(sum(r["idle_pct"] for r in robots) / len(robots), 1) if robots else None,
        "로봇_평균_대기_퍼센트": round(sum(r["wait_pct"] for r in robots) / len(robots), 1) if robots else None,
        "가동률_최고_로봇": {"id": busy["id"], "가동률": busy["utilization_pct"]} if busy else None,
        "가동률_최저_로봇": {"id": lazy["id"], "가동률": lazy["utilization_pct"]} if lazy else None,
    }


def numbers_in(obj) -> set[str]:
    out: set[str] = set()
    if isinstance(obj, bool) or obj is None:
        return out
    if isinstance(obj, (int, float)):
        out |= {str(obj), f"{obj:g}", str(round(obj)), f"{obj:.1f}"}
    elif isinstance(obj, str):
        out |= set(re.findall(r"\d+(?:\.\d+)?", obj))
    elif isinstance(obj, dict):
        for v in obj.values():
            out |= numbers_in(v)
    elif isinstance(obj, (list, tuple)):
        for v in obj:
            out |= numbers_in(v)
    return out


def numbers_ok(text: str, allowed: set[str]) -> bool:
    """설명 속 숫자가 모두 집계에 있는지. 10 이하 정수(개수, 순위)는 허용."""
    return all(n in allowed or (n.isdigit() and int(n) <= 10) for n in re.findall(r"\d+(?:\.\d+)?", text))


def rule_explanation(facts: dict) -> str:
    top = facts["대기_상위_칸"]
    if not top:
        return "대기가 거의 없습니다. 현재 구성으로 충분합니다."
    t0 = top[0]
    lines = [f"대기가 가장 많은 칸은 ({t0['x']},{t0['y']})로 {t0['대기']}스텝 기다렸습니다.",
             f"상위 5칸이 전체 대기 {facts['전체_대기_스텝']}스텝 중 {facts['상위5칸_대기_비중_퍼센트']}%를 차지합니다."]
    near = facts["대기1위에_가까운_도크"]
    if near and near["dist"] <= 4:
        kind = "입하" if near["type"] == "dock_in" else "출하"
        lines.append(f"{kind} 도크 ({near['x']},{near['y']}) 앞에 로봇이 몰려 줄을 서고 있습니다.")
    if facts["로봇_평균_유휴_퍼센트"] is not None and facts["로봇_평균_유휴_퍼센트"] >= 35:
        lines.append(f"로봇 평균 유휴가 {facts['로봇_평균_유휴_퍼센트']}%로 로봇이 남습니다.")
    return " ".join(lines)


def llm_json(llm, prompt: str, payload: dict, schema: dict) -> dict:
    return llm.invoke(load(prompt), json.dumps(payload, ensure_ascii=False), schema)


async def analyze(s: AgentState, config: RunnableConfig) -> dict:
    f = flow_of(config)
    se = session_of(f, s)
    e = f.store.get_sim(s["sim_id"])
    grid = f.store.get_map(e.map_version).map
    await f.status(se, "로그 분석", "칸별 대기 집계 -> 병목 상위 10칸, 로봇별 유휴")
    agg = aggregate_logs(e.log, grid, top_k=10)
    robots = robot_stats(e.log)
    facts = facts_of(agg, robots, e.log["total_steps"])
    text, source = rule_explanation(facts), "rule"
    llm = f.interpreter.llm
    if llm is not None and agg["bottlenecks"]:
        try:
            out = await asyncio.to_thread(llm_json, llm, "analyze", facts, ANALYZE_SCHEMA)
            cand = (out.get("explanation") or "").strip()
            if cand and numbers_ok(cand, numbers_in(facts)):
                text, source = cand, "llm"
            else:
                log.warning("LLM 분석 설명에 집계에 없는 숫자 -> 규칙 설명: %s", cand[:120])
        except Exception as ex:
            log.warning("LLM 분석 실패 -> 규칙 설명: %s", ex)
    await f.status(se, "로그 분석", f"원인 설명 ({SOURCE_LABEL[source]})")
    return {"analysis": {"agg": agg, "robots": robots, "facts": facts, "explanation": text, "source": source},
            "trace": step(s, "analyze")}


async def propose(s: AgentState, config: RunnableConfig) -> dict:
    f = flow_of(config)
    se = session_of(f, s)
    e = f.store.get_sim(s["sim_id"])
    grid = f.store.get_map(e.map_version).map
    a = s["analysis"]
    cands = build_candidates(a["agg"], grid, e.scenario, a["robots"])
    for i, c in enumerate(cands):
        c["key"] = f"C{i + 1}"
    chosen, reason, source = cands[:3], "", "rule"
    llm = f.interpreter.llm
    if llm is not None and len(cands) > 1:
        keys = [c["key"] for c in cands]
        schema = {"type": "object", "additionalProperties": False, "required": ["selected", "reason"],
                  "properties": {"selected": {"type": "array", "items": {"type": "string", "enum": keys}},
                                 "reason": {"type": "string"}}}
        payload = {"집계": a["facts"], "후보": [{k: c[k] for k in ("key", "type", "text", "reason")} for c in cands]}
        try:
            out = await asyncio.to_thread(llm_json, llm, "propose", payload, schema)
            sel = [k for k in dict.fromkeys(out.get("selected") or []) if k in keys][:3]
            why = (out.get("reason") or "").strip()
            if sel and numbers_ok(why, numbers_in(payload)):
                chosen, reason, source = [c for c in cands if c["key"] in sel], why, "llm"
            else:
                log.warning("LLM 개선안 선택이 후보 밖이거나 숫자 불일치 -> 규칙: %s", out)
        except Exception as ex:
            log.warning("LLM 개선안 선택 실패 -> 규칙: %s", ex)
    for c in chosen:
        pid = f.store.new_id("P")
        c["proposal_id"] = pid
        e.proposals[pid] = c
        f.store.proposals[pid] = e.sim_id
    await f.status(se, "개선안 제안", f"후보 {len(cands)}개 중 {len(chosen)}개 선택 ({SOURCE_LABEL[source]}, 엔진이 적용 가능한 유형만)")
    explanation = a["explanation"] + (f" 개선 방향: {reason}" if reason else "")
    await f.store.emit(se, {"type": "analysis",
                            "bottlenecks": [{"x": b["x"], "y": b["y"], "wait": b["wait"]} for b in a["agg"]["bottlenecks"]],
                            "explanation": explanation,
                            "proposals": [{"proposal_id": c["proposal_id"], "type": c["type"], "text": c["text"],
                                           "reason": c["reason"], "effects": c.get("effects", [])} for c in chosen]})
    return {"candidates": chosen, "trace": step(s, "propose")}


async def apply(s: AgentState, config: RunnableConfig) -> dict:
    """승인한 개선안을 지도나 시나리오에 적용한다. 지도가 바뀌면 검증부터 다시 (SC-10)."""
    f = flow_of(config)
    se = session_of(f, s)
    e = f.store.get_sim(s["sim_id"])
    p = e.proposals[s["proposal_id"]]
    entry = f.store.get_map(e.map_version)
    await f.status(se, "개선안 적용", p["text"])
    res = apply_proposal(p, entry.map, e.scenario, entry.req)
    upd = {"changed": res["changed"], "scenario": res["scenario"], "strategy": e.strategy, "proposal": p,
           "error": None, "trace": step(s, "apply")}
    if res["changed"] == "map":
        upd.update(map=res["grid"], requirements=res["req"], map_note="개선안 적용: ", confirmed=True, missing=[])
    else:
        upd["map_version"] = e.map_version
    return upd
