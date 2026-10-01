"""로봇웨어하우스 모의 서버 (VR 클라이언트 단독 검증용).

실제 서버(FastAPI + LangGraph + 경로 계산 엔진)와 같은 REST·WebSocket 계약을 흉내 낸다.
LLM·STT 대신 규칙 기반 해석(mock/parse_text.py)을 쓰고, Agent 노드 진행은 WebSocket `status`로 보낸다.

실행:  uvicorn server:app --host 0.0.0.0 --port 8000
"""
from __future__ import annotations

import asyncio
import copy
import io
import itertools
import json
import logging
import math
import os
import struct
import time
import wave
from dataclasses import dataclass, field
from typing import Any

from fastapi import FastAPI, File, Form, HTTPException, Query, UploadFile, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse

from mock.engine import simulate
from mock.mapgen import generate_map, summarize
from mock.parse_text import extract_requirements, fill_defaults, merge_answer
from mock.validator import validate_map, question_for

log = logging.getLogger("mock")
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(message)s")

MAX_QUESTIONS = 3
NODE_DELAY = float(os.environ.get("MOCK_NODE_DELAY", "0.3"))       # Agent 노드 사이 연출 지연(초)
MOCK_STT_TEXT = os.environ.get("MOCK_STT_TEXT", "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽 벽 쪽에")
MOCK_STT_ANSWER = os.environ.get("MOCK_STT_ANSWER", "입하 도크 1개, 출하 도크 1개로 해줘")

app = FastAPI(title="RobotWarehouse mock server")
_ids = itertools.count(1)


def new_id(prefix: str) -> str:
    return f"{prefix}{next(_ids)}"


@dataclass
class MapEntry:
    version: str
    map: dict[str, Any]
    req: dict[str, Any]
    confirmed: bool = False


@dataclass
class SimEntry:
    sim_id: str
    session_id: str
    map_version: str
    scenario: dict[str, Any]
    strategy: str
    log: dict[str, Any]
    events: list[dict[str, Any]] = field(default_factory=list)
    proposals: dict[str, dict[str, Any]] = field(default_factory=dict)


@dataclass
class Session:
    session_id: str
    sockets: list[WebSocket] = field(default_factory=list)
    req: dict[str, Any] = field(default_factory=dict)
    question_id: str | None = None
    question_count: int = 0


SESSIONS: dict[str, Session] = {}
MAPS: dict[str, MapEntry] = {}
MAP_OWNER: dict[str, str] = {}
SIMS: dict[str, SimEntry] = {}
QUESTIONS: dict[str, str] = {}
PROPOSALS: dict[str, str] = {}


def session(sid: str | None) -> Session:
    sid = sid or "default"
    if sid not in SESSIONS:
        SESSIONS[sid] = Session(sid)
    return SESSIONS[sid]


async def emit(s: Session, msg: dict[str, Any]):
    text = json.dumps(msg, ensure_ascii=False)
    log.info("WS -> %s %s", s.session_id, text[:160])
    for ws in list(s.sockets):
        try:
            await ws.send_text(text)
        except Exception:
            s.sockets.remove(ws)


async def status(s: Session, node: str, message: str):
    await emit(s, {"type": "status", "node": node, "message": message})
    await asyncio.sleep(NODE_DELAY)


def err(code: int, ecode: str, message: str):
    return JSONResponse(status_code=code, content={"error": {"code": ecode, "message": message}})


# ------------------------------------------------------------------ WebSocket

@app.websocket("/ws/{session_id}")
async def ws_endpoint(websocket: WebSocket, session_id: str):
    await websocket.accept()
    s = session(session_id)
    s.sockets.append(websocket)
    log.info("WS connected %s", session_id)
    try:
        while True:
            await websocket.receive_text()
    except WebSocketDisconnect:
        pass
    finally:
        if websocket in s.sockets:
            s.sockets.remove(websocket)


@app.get("/health")
async def health():
    return {"status": "ok", "mock": True}


# ------------------------------------------------------------------ 지도 (SC-01~03, 11, 12)

async def build_map(s: Session, req: dict[str, Any], defaults: list[str], from_answer: bool = False):
    await status(s, "지시 해석", f"요구사항 추출: {json.dumps(req, ensure_ascii=False)}")
    m = generate_map(req)
    await status(s, "격자 지도 생성", f"generate_map → {m['width']}×{m['height']}")
    result = validate_map(m)
    codes = [e["code"] for e in result["errors"]]
    await status(s, "지도 검증", "통과" if result["valid"] else f"validate=false {codes}")
    s.req = req
    if result["valid"]:
        s.question_id = None
        s.question_count = 0
        version = str(len(MAPS) + 1)
        MAPS[version] = MapEntry(version, m, req)
        MAP_OWNER[version] = s.session_id
        await emit(s, {"type": "map_ready", "map_version": version, "map": m, "summary": summarize(m),
                       "defaults_applied": defaults})
        return
    if s.question_count >= MAX_QUESTIONS:
        s.question_id = None
        s.question_count = 0
        await emit(s, {"type": "error", "code": "QUESTION_LIMIT", "message": "입력을 다시 해 주세요"})
        return
    text, cells = question_for(result["errors"])
    qid = new_id("Q")
    s.question_id = qid
    s.question_count += 1
    QUESTIONS[qid] = s.session_id
    await emit(s, {"type": "question", "question_id": qid, "text": text, "error_cells": cells,
                   "errors": codes, "map": m})


async def handle_text(s: Session, text: str):
    req, defaults = fill_defaults(extract_requirements(text))
    s.question_count = 0
    await build_map(s, req, defaults)


async def handle_answer(s: Session, text: str):
    req = merge_answer(s.req, text)
    await status(s, "지시 해석", f"답변 반영: {text}")
    await build_map(s, req, [], from_answer=True)


@app.post("/map/text")
async def map_text(body: dict[str, Any]):
    text = (body.get("text") or "").strip()
    if not text:
        return err(400, "EMPTY_TEXT", "창고 설명이 비어 있습니다")
    s = session(body.get("session_id"))
    asyncio.create_task(handle_text(s, text))
    return {"request_id": new_id("REQ")}


def wav_rms(data: bytes) -> float:
    try:
        with wave.open(io.BytesIO(data)) as w:
            frames = w.readframes(w.getnframes())
            n = len(frames) // 2
            if n == 0:
                return 0.0
            samples = struct.unpack(f"<{n}h", frames[: n * 2])
            return math.sqrt(sum(x * x for x in samples) / n) / 32768
    except Exception:
        return 0.0


@app.post("/map/voice")
async def map_voice(session_id: str = Form("default"), audio: UploadFile = File(...),
                    question_id: str | None = Form(None)):
    data = await audio.read()
    s = session(session_id)

    async def run():
        await status(s, "STT", f"음성 {len(data) // 1024}KB 인식 중 (모의 STT)")
        if wav_rms(data) < 0.003:
            await emit(s, {"type": "transcript", "text": ""})
            await emit(s, {"type": "error", "code": "STT_EMPTY", "message": "잘 못 들었어요. 다시 말하거나 입력해 주세요"})
            return
        text = MOCK_STT_ANSWER if question_id else MOCK_STT_TEXT
        await emit(s, {"type": "transcript", "text": text})
        if question_id and s.question_id == question_id:
            await handle_answer(s, text)
        else:
            await handle_text(s, text)

    asyncio.create_task(run())
    return {"request_id": new_id("REQ")}


@app.post("/map/answer")
async def map_answer(body: dict[str, Any]):
    qid = body.get("question_id")
    sid = QUESTIONS.get(qid)
    if sid is None:
        return err(404, "UNKNOWN_QUESTION", f"question_id {qid} 없음")
    s = session(sid)
    if s.question_id != qid:
        return err(409, "STALE_QUESTION", "이미 처리된 질문입니다")
    asyncio.create_task(handle_answer(s, body.get("text", "")))
    return {"request_id": new_id("REQ")}


@app.post("/map/confirm")
async def map_confirm(body: dict[str, Any]):
    v = str(body.get("map_version"))
    if v not in MAPS:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    MAPS[v].confirmed = True
    return {"status": "confirmed", "map_version": v}


@app.post("/map/edit")
async def map_edit(body: dict[str, Any]):
    v = str(body.get("map_version"))
    if v not in MAPS:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    s = session(body.get("session_id") or MAP_OWNER.get(v))
    m = copy.deepcopy(MAPS[v].map)
    for mv in body.get("moves", []):
        fx, fy = mv["from"]
        tx, ty = mv["to"]
        m["cells"] = [c for c in m["cells"] if (c["x"], c["y"]) not in ((fx, fy), (tx, ty))]
        m["cells"].append({"x": tx, "y": ty, "type": "rack", "rack_id": mv.get("rack_id")})

    async def run():
        await status(s, "지도 검증", "VR 편집 지도 재검증")
        result = validate_map(m)
        if result["valid"]:
            version = str(len(MAPS) + 1)
            MAPS[version] = MapEntry(version, m, MAPS[v].req)
            MAP_OWNER[version] = s.session_id
            await emit(s, {"type": "map_ready", "map_version": version, "map": m, "summary": "편집 반영: " + summarize(m),
                           "defaults_applied": []})
        else:
            text, cells = question_for(result["errors"])
            qid = new_id("Q")
            s.question_id = qid
            QUESTIONS[qid] = s.session_id
            await emit(s, {"type": "question", "question_id": qid, "text": "옮긴 위치에서 문제가 생겼어요. " + text,
                           "error_cells": cells, "map": m})

    asyncio.create_task(run())
    return {"request_id": new_id("REQ")}


# ------------------------------------------------------------------ 시뮬레이션 (SC-04, 07, 08)

async def run_sim(s: Session, map_version: str, scenario: dict[str, Any], strategy: str, sim_id: str | None = None,
                  events: list[dict[str, Any]] | None = None, replan_from: int | None = None):
    m = MAPS[map_version].map
    await status(s, "시뮬레이션 실행", f"simulate(map v{map_version}, 로봇 {scenario['robots']}, "
                                       f"주문 {scenario['inbound']}/{scenario['outbound']}, {strategy})")
    started = time.perf_counter()
    result = await asyncio.to_thread(simulate, m, scenario, strategy, 42, events)
    ms = round((time.perf_counter() - started) * 1000, 1)
    if result["collisions"]:
        log.error("충돌 %d건 → VR로 보내지 않음: %s", len(result["collisions"]), result["collisions"][:3])
        await emit(s, {"type": "error", "code": "COLLISION", "message": f"충돌 {len(result['collisions'])}건 (엔진 디버깅 필요)"})
        return None
    sim_id = sim_id or new_id("S")
    SIMS[sim_id] = SimEntry(sim_id, s.session_id, map_version, scenario, strategy, result, events or [])
    await status(s, "로그 분석", f"충돌 0건, 총 {result['total_steps']} 스텝, 계산 {ms}ms")
    msg = {"type": "sim_ready", "sim_id": sim_id, "strategy": strategy, "total_steps": result["total_steps"],
           "summary": {"총 스텝": result["summary"]["total_steps"], "완료 주문": result["summary"]["orders_done"],
                       "주문당 평균": result["summary"]["avg_order_steps"], "대기 합계": result["summary"]["wait_total"]}}
    if replan_from is not None:
        msg["replan_from"] = replan_from
        msg["replan_ms"] = ms
    await emit(s, msg)
    return sim_id


@app.post("/scenario")
async def post_scenario(body: dict[str, Any]):
    v = str(body.get("map_version"))
    if v not in MAPS:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    if not MAPS[v].confirmed:
        return err(409, "MAP_NOT_CONFIRMED", "지도를 먼저 확인해 주세요")
    s = session(body.get("session_id") or MAP_OWNER.get(v))
    scenario = {"robots": int(body.get("robots", 4)), "inbound": int(body.get("inbound", 25)),
                "outbound": int(body.get("outbound", 25))}
    sim_id = new_id("S")
    asyncio.create_task(run_sim(s, v, scenario, body.get("strategy", "optimized"), sim_id))
    return {"sim_id": sim_id}


def get_sim(sim_id: str) -> SimEntry:
    if sim_id not in SIMS:
        raise HTTPException(404, detail=f"sim_id {sim_id} 없음")
    return SIMS[sim_id]


@app.get("/sim/{sim_id}/frames")
async def sim_frames(sim_id: str, from_: int = Query(0, alias="from"), to: int = Query(200)):
    e = get_sim(sim_id)
    frames = e.log["frames"]
    return [f for f in frames[max(0, from_): to + 1]]


@app.get("/sim/{sim_id}/stats")
async def sim_stats(sim_id: str):
    return get_sim(sim_id).log["stats"]


@app.post("/sim/{sim_id}/event")
async def sim_event(sim_id: str, body: dict[str, Any]):
    e = get_sim(sim_id)
    t = int(body.get("t", 0))
    robots = int(body.get("robots", e.scenario["robots"]))
    if robots < max([e.scenario["robots"]] + [ev["robots"] for ev in e.events]):
        log.warning("모의 엔진은 로봇 수 감소를 지원하지 않습니다 (증가만)")
    events = e.events + [{"t": t, "add_orders": int(body.get("add_orders", 0)), "robots": robots}]
    s = session(e.session_id)
    asyncio.create_task(run_sim(s, e.map_version, e.scenario, e.strategy, sim_id, events, replan_from=t))
    return {"status": "replanning"}


@app.post("/compare")
async def compare(body: dict[str, Any]):
    e = get_sim(body.get("sim_id"))
    s = session(e.session_id)

    async def run():
        await status(s, "비교 리포트", "같은 지도·주문으로 기준 전략 실행")
        base = await asyncio.to_thread(simulate, MAPS[e.map_version].map, e.scenario, "baseline", 42, e.events)
        b, o = base["summary"], e.log["summary"]
        pct = 0.0 if b["total_steps"] == 0 else round((b["total_steps"] - o["total_steps"]) / b["total_steps"] * 100, 1)
        await emit(s, {"type": "compare",
                       "baseline": {"총 스텝": b["total_steps"], "주문당 평균": b["avg_order_steps"], "대기 합계": b["wait_total"]},
                       "optimized": {"총 스텝": o["total_steps"], "주문당 평균": o["avg_order_steps"], "대기 합계": o["wait_total"]},
                       "improvement_pct": pct})

    asyncio.create_task(run())
    return {"request_id": new_id("REQ")}


# ------------------------------------------------------------------ 분석·개선 (SC-09, 10)

def analyze_log(e: SimEntry) -> dict[str, Any]:
    stats = e.log["stats"]
    m = MAPS[e.map_version].map
    top = sorted(stats, key=lambda c: (-c["wait"], c["x"], c["y"]))[:10]
    top = [c for c in top if c["wait"] > 0]
    total = sum(c["wait"] for c in stats) or 1
    docks = [(d["x"], d["y"], d["type"]) for d in m["docks"]]
    proposals = []
    if not top:
        return {"bottlenecks": [], "explanation": "대기가 거의 없습니다. 현재 구성으로 충분합니다.", "proposals": []}
    t0 = top[0]
    share = round(sum(c["wait"] for c in top[:5]) / total * 100, 1)
    near = min(docks, key=lambda d: abs(d[0] - t0["x"]) + abs(d[1] - t0["y"])) if docks else None
    near_dist = abs(near[0] - t0["x"]) + abs(near[1] - t0["y"]) if near else 99
    lines = [f"대기가 가장 많은 칸은 ({t0['x']},{t0['y']})로 {t0['wait']}스텝 기다렸습니다.",
             f"상위 5칸이 전체 대기 {total}스텝 중 {share}%를 차지합니다."]
    if near and near_dist <= 4:
        kind = "입하" if near[2] == "dock_in" else "출하"
        lines.append(f"{kind} 도크 ({near[0]},{near[1]}) 앞에 로봇이 몰려 줄을 서고 있습니다.")
        proposals.append({"proposal_id": new_id("P"), "type": "dock_add", "text": f"{kind} 도크 1개 추가",
                          "apply": {"dock": near[2]}})
    robots = e.scenario["robots"]
    if robots > 4:
        proposals.append({"proposal_id": new_id("P"), "type": "robot_count", "text": f"로봇 {robots}대 → {robots - 2}대",
                          "apply": {"robots": robots - 2}})
    if not proposals:
        proposals.append({"proposal_id": new_id("P"), "type": "dock_add", "text": "출하 도크 1개 추가",
                          "apply": {"dock": "dock_out"}})
    for p in proposals:
        e.proposals[p["proposal_id"]] = p
        PROPOSALS[p["proposal_id"]] = e.sim_id
    return {"bottlenecks": [{"x": c["x"], "y": c["y"], "wait": c["wait"]} for c in top],
            "explanation": " ".join(lines),
            "proposals": [{k: v for k, v in p.items() if k != "apply"} for p in proposals]}


@app.post("/analyze")
async def analyze(body: dict[str, Any]):
    e = get_sim(body.get("sim_id"))
    s = session(e.session_id)

    async def run():
        await status(s, "로그 분석", "칸별 대기 집계 → 병목 상위 10칸")
        result = analyze_log(e)
        await status(s, "개선안 제안", f"개선안 {len(result['proposals'])}개 (엔진이 적용 가능한 유형만)")
        await emit(s, {"type": "analysis", **result})

    asyncio.create_task(run())
    return {"request_id": new_id("REQ")}


@app.post("/improve/approve")
async def improve(body: dict[str, Any]):
    pid = body.get("proposal_id")
    if pid not in PROPOSALS:
        return err(404, "UNKNOWN_PROPOSAL", f"proposal_id {pid} 없음")
    e = SIMS[PROPOSALS[pid]]
    p = e.proposals[pid]
    s = session(e.session_id)
    new_sim = new_id("S")

    async def run():
        version = e.map_version
        scenario = dict(e.scenario)
        if "robots" in p["apply"]:
            scenario["robots"] = p["apply"]["robots"]
        if "dock" in p["apply"]:
            req = dict(MAPS[version].req)
            key = "dock_in" if p["apply"]["dock"] == "dock_in" else "dock_out"
            req[key] = req.get(key, 1) + 1
            m = generate_map(req)
            result = validate_map(m)
            if not result["valid"]:
                await emit(s, {"type": "error", "code": "IMPROVE_INVALID", "message": "개선안 적용 지도가 검증에 실패했습니다"})
                return
            version = str(len(MAPS) + 1)
            MAPS[version] = MapEntry(version, m, req, confirmed=True)
            MAP_OWNER[version] = s.session_id
            await emit(s, {"type": "map_ready", "map_version": version, "map": m, "confirmed": True,
                           "summary": "개선안 적용: " + summarize(m), "defaults_applied": []})
        await run_sim(s, version, scenario, e.strategy, new_sim)

    asyncio.create_task(run())
    return {"sim_id": new_sim}
