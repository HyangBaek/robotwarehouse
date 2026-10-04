"""창고 구성 노드: 지시 해석(LLM), 격자 지도 생성(도구), 지도 검증(도구), 수정 질문, 지도 전송."""
import asyncio
import json
import time

from langchain_core.runnables import RunnableConfig

from config import settings
from tools.map_generator import fill_defaults, generate_map as make_map, missing_items, missing_options, missing_question, summarize
from tools.map_validator import options_for, question_for, validate_map
from tools.scenario import recommend_scenario
from . import SOURCE_LABEL, flow_of, session_of, step
from ..state import AgentState


async def interpret(s: AgentState, config: RunnableConfig) -> dict:
    """문장(또는 답변) -> 요구사항. 필수 항목이 다 있거나 '기본값'이면 req_full, 빠졌으면 missing."""
    f = flow_of(config)
    se = session_of(f, s)
    t0 = time.perf_counter()
    if s["intent"] == "compose":
        await f.status(se, f.node_interpret(), f"\"{s['text']}\" 해석 중")
        raw, source, reason = await asyncio.to_thread(f.interpreter.extract, s["text"])
        qcount, label = 0, "요구사항 추출"
    else:
        await f.status(se, f.node_interpret(), f"답변 반영: \"{s['answer']}\"")
        raw, source, reason = await asyncio.to_thread(f.interpreter.merge, s.get("requirements") or {},
                                                      s.get("question_text"), s["answer"])
        qcount, label = s.get("question_count", 0), "요구사항 수정"
    ms = (time.perf_counter() - t0) * 1000
    await f.report_source(se, source, reason, ms)
    await f.status(se, f.node_interpret(), f"{label} ({SOURCE_LABEL[source]}, {ms / 1000:.1f}초): "
                                           f"{json.dumps(raw, ensure_ascii=False)}")
    raw = dict(raw)
    use_defaults = bool(raw.pop("use_defaults", False))
    missing = missing_items(raw)
    if missing and not use_defaults and qcount >= settings.max_questions:
        await f.status(se, "정보 확인", "질문 한도에 도달해 남은 항목은 기본값으로 채웁니다")
        use_defaults = True
    upd = {"requirements": raw, "question_count": qcount, "llm_source": source, "missing": [], "req_full": None,
           "defaults_applied": [], "map_note": "", "confirmed": False, "trace": step(s, "interpret")}
    if not missing or use_defaults:
        req, defaults = fill_defaults({**raw, "use_defaults": True} if use_defaults else raw)
        upd.update(req_full=req, defaults_applied=defaults)
    else:
        await f.status(se, "정보 확인", f"빠진 항목 {missing} -> 되묻기")
        upd["missing"] = missing
    return upd


async def generate_map(s: AgentState, config: RunnableConfig) -> dict:
    f = flow_of(config)
    m = make_map(s["req_full"])
    await f.status(session_of(f, s), "격자 지도 생성", f"generate_map -> {m['width']}x{m['height']}")
    return {"map": m, "requirements": s["req_full"], "trace": step(s, "generate_map")}


async def validate(s: AgentState, config: RunnableConfig) -> dict:
    f = flow_of(config)
    result = validate_map(s["map"])
    codes = [e["code"] for e in result["errors"]]
    what = {"edit": "VR 편집 지도 재검증: ", "approve": "개선안 적용 지도 재검증: "}.get(s.get("intent"), "")
    await f.status(session_of(f, s), "지도 검증", what + ("통과" if result["valid"] else f"validate=false {codes}"))
    return {"validation": result, "trace": step(s, "validate")}


async def ask_question(s: AgentState, config: RunnableConfig) -> dict:
    """빠진 정보 또는 검증 오류 -> 질문 하나 (최대 settings.max_questions 회)."""
    f = flow_of(config)
    se = session_of(f, s)
    if s.get("missing"):
        text = missing_question(s["missing"], s.get("requirements") or {})
        msg = {"error_cells": [], "errors": ["MISSING_INFO"], "missing": s["missing"], "options": missing_options(s["missing"])}
    else:
        errors = s["validation"]["errors"]
        text, cells = question_for(errors)
        codes = [e["code"] for e in errors]
        await f.status(se, "수정 질문", f"오류 {codes} -> 질문 생성")
        if s.get("intent") == "edit":
            text = "옮긴 위치에서 문제가 생겼어요. " + text
        msg = {"error_cells": cells, "errors": codes, "options": options_for(errors), "map": s["map"]}
    qid = f.store.new_id("Q")
    se.question_id, se.question_text = qid, text        # API 에서 지난 질문인지 확인하는 용도
    f.store.questions[qid] = se.session_id
    count = s.get("question_count", 0) + (0 if s.get("intent") == "edit" else 1)
    await f.store.emit(se, {"type": "question", "question_id": qid, "text": text, **msg})
    return {"question_text": text, "question_count": count, "trace": step(s, "ask_question")}


async def give_up(s: AgentState, config: RunnableConfig) -> dict:
    f = flow_of(config)
    se = session_of(f, s)
    se.question_id = se.question_text = None
    if s.get("intent") == "approve":
        msg = {"code": "IMPROVE_INVALID", "message": "개선안 적용 지도가 검증에 실패했습니다"}
    else:
        msg = {"code": "QUESTION_LIMIT", "message": "입력을 다시 해 주세요"}
    await f.store.emit(se, {"type": "error", **msg})
    return {"question_count": 0, "question_text": None, "error": msg["code"], "trace": step(s, "give_up")}


async def emit_map(s: AgentState, config: RunnableConfig) -> dict:
    """검증을 통과한 지도를 저장하고 VR 로 보낸다. 새로 만든 지도에는 권장 시나리오를 함께 보낸다."""
    f = flow_of(config)
    se = session_of(f, s)
    m, note = s["map"], s.get("map_note") or ""
    confirmed = bool(s.get("confirmed"))
    version = f.store.add_map(se, m, s.get("requirements") or {}, confirmed=confirmed, summary=note + summarize(m))
    se.question_id = se.question_text = None
    msg = {"type": "map_ready", "map_version": version, "map": m, "summary": note + summarize(m),
           "defaults_applied": s.get("defaults_applied") or [] if s.get("intent") in ("compose", "answer") else []}
    if s.get("intent") in ("compose", "answer"):
        msg["scenario_defaults"] = recommend_scenario(m)
    if confirmed:
        msg["confirmed"] = True
    await f.store.emit(se, msg)
    return {"map_version": version, "question_count": 0, "question_text": None, "trace": step(s, "emit_map")}
