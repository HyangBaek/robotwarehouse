"""Agent 처리 흐름 (SC-01~10). mock_server/server.py 의 흐름을 옮기고 해석·생성·검증·시뮬레이션만 실제 구현으로 바꿈.

각 단계마다 WS status {node, message} 를 보낸다. 시연 화면에서 Agent 판단 과정을 보여 주는 장치.
"""
from __future__ import annotations

import asyncio
import io
import json
import logging
import math
import struct
import time
import wave
from typing import Any

from agent.interpreter import Interpreter
from config import settings
from tools.aggregate import aggregate_logs
from tools.compare import compare_logs
from tools.map_generator import fill_defaults, generate_map, summarize
from tools.map_validator import question_for, validate_map
from tools.simulation import replan, start_simulation, summary_ko
from planner import simulate
from .store import Session, SimEntry, Store

log = logging.getLogger("api.flow")

SOURCE_LABEL = {"llm": "LLM", "regex": "정규식", "regex_fallback": "regex_fallback"}


class Flow:
    def __init__(self, store: Store, llm=None, stt=None, node_delay: float | None = None):
        self.store, self.stt = store, stt
        self.interpreter = Interpreter(llm)
        self.llm_name = getattr(llm, "model", None) or ("LLM" if llm else None)
        self.node_delay = settings.node_delay_sec if node_delay is None else node_delay

    async def status(self, s: Session, node: str, message: str):
        await self.store.emit(s, {"type": "status", "node": node, "message": message})
        if self.node_delay:
            await asyncio.sleep(self.node_delay)

    def _node_interpret(self) -> str:
        return f"지시 해석 ({self.llm_name})" if self.llm_name else "지시 해석"

    async def _report_source(self, s: Session, source: str, reason: str | None, ms: float):
        if source == "regex_fallback":
            await self.status(s, "regex_fallback", f"LLM 실패 → 정규식으로 해석 ({reason})")
        log.info("해석 source=%s %.0fms", source, ms)

    # ------------------------------------------------------------------ 지도 (SC-01~03, 11, 12)
    async def build_map(self, s: Session, req: dict[str, Any], defaults: list[str]):
        m = generate_map(req)
        await self.status(s, "격자 지도 생성", f"generate_map → {m['width']}×{m['height']}")
        result = validate_map(m)
        codes = [e["code"] for e in result["errors"]]
        await self.status(s, "지도 검증", "통과" if result["valid"] else f"validate=false {codes}")
        s.req = req
        if result["valid"]:
            s.question_id = s.question_text = None
            s.question_count = 0
            version = self.store.add_map(s, m, req)
            await self.store.emit(s, {"type": "map_ready", "map_version": version, "map": m,
                                      "summary": summarize(m), "defaults_applied": defaults})
            return
        if s.question_count >= settings.max_questions:
            s.question_id = s.question_text = None
            s.question_count = 0
            await self.store.emit(s, {"type": "error", "code": "QUESTION_LIMIT", "message": "입력을 다시 해 주세요"})
            return
        text, cells = question_for(result["errors"])
        await self.status(s, "수정 질문", f"오류 {codes} → 질문 생성")
        qid = self.store.new_id("Q")
        s.question_id, s.question_text = qid, text
        s.question_count += 1
        self.store.questions[qid] = s.session_id
        await self.store.emit(s, {"type": "question", "question_id": qid, "text": text, "error_cells": cells,
                                  "errors": codes, "map": m})

    async def handle_text(self, s: Session, text: str):
        try:
            t0 = time.perf_counter()
            await self.status(s, self._node_interpret(), f"\"{text}\" 해석 중")
            raw, source, reason = await asyncio.to_thread(self.interpreter.extract, text)
            ms = (time.perf_counter() - t0) * 1000
            await self._report_source(s, source, reason, ms)
            req, defaults = fill_defaults(raw)
            await self.status(s, self._node_interpret(),
                              f"요구사항 추출 ({SOURCE_LABEL[source]}, {ms / 1000:.1f}초): "
                              f"{json.dumps(raw, ensure_ascii=False)}")
            s.question_count = 0
            await self.build_map(s, req, defaults)
        except Exception as e:
            log.exception("handle_text 실패")
            await self.store.emit(s, {"type": "error", "code": "AGENT_ERROR", "message": f"지도 생성 실패: {e}"})

    async def handle_answer(self, s: Session, text: str):
        try:
            t0 = time.perf_counter()
            await self.status(s, self._node_interpret(), f"답변 반영: \"{text}\"")
            req, source, reason = await asyncio.to_thread(self.interpreter.merge, s.req, s.question_text, text)
            ms = (time.perf_counter() - t0) * 1000
            await self._report_source(s, source, reason, ms)
            await self.status(s, self._node_interpret(),
                              f"요구사항 수정 ({SOURCE_LABEL[source]}, {ms / 1000:.1f}초): "
                              f"{json.dumps(req, ensure_ascii=False)}")
            await self.build_map(s, req, [])
        except Exception as e:
            log.exception("handle_answer 실패")
            await self.store.emit(s, {"type": "error", "code": "AGENT_ERROR", "message": f"답변 반영 실패: {e}"})

    async def handle_voice(self, s: Session, data: bytes, question_id: str | None):
        await self.status(s, "STT", f"음성 {len(data) // 1024}KB 인식 중")
        if wav_rms(data) < 0.003:
            return await self._stt_empty(s)
        if self.stt is None:
            await self.store.emit(s, {"type": "error", "code": "STT_UNAVAILABLE", "message": "음성 인식기가 꺼져 있습니다. 글자로 입력해 주세요"})
            return
        t0 = time.perf_counter()
        try:
            text = await asyncio.to_thread(self.stt.transcribe, data, "audio.wav")
        except Exception as e:
            log.exception("STT 실패")
            await self.store.emit(s, {"type": "error", "code": "STT_ERROR", "message": f"음성 인식 실패: {e}"})
            return
        if not text:
            return await self._stt_empty(s)
        await self.status(s, "STT", f"인식 완료 ({time.perf_counter() - t0:.1f}초)")
        await self.store.emit(s, {"type": "transcript", "text": text})
        if question_id and s.question_id == question_id:
            await self.handle_answer(s, text)
        else:
            await self.handle_text(s, text)

    async def _stt_empty(self, s: Session):
        await self.store.emit(s, {"type": "transcript", "text": ""})
        await self.store.emit(s, {"type": "error", "code": "STT_EMPTY", "message": "잘 못 들었어요. 다시 말하거나 입력해 주세요"})

    async def handle_edit(self, s: Session, base_version: str, moves: list[dict]):
        import copy
        m = copy.deepcopy(self.store.maps[base_version].map)
        for mv in moves:
            fx, fy = mv["from"]
            tx, ty = mv["to"]
            m["cells"] = [c for c in m["cells"] if (c["x"], c["y"]) not in ((fx, fy), (tx, ty))]
            m["cells"].append({"x": tx, "y": ty, "type": "rack", "rack_id": mv.get("rack_id")})
        await self.status(s, "지도 검증", "VR 편집 지도 재검증")
        result = validate_map(m)
        if result["valid"]:
            version = self.store.add_map(s, m, self.store.maps[base_version].req)
            await self.store.emit(s, {"type": "map_ready", "map_version": version, "map": m,
                                      "summary": "편집 반영: " + summarize(m), "defaults_applied": []})
        else:
            text, cells = question_for(result["errors"])
            qid = self.store.new_id("Q")
            s.question_id, s.question_text = qid, text
            self.store.questions[qid] = s.session_id
            await self.store.emit(s, {"type": "question", "question_id": qid, "text": "옮긴 위치에서 문제가 생겼어요. " + text,
                                      "error_cells": cells, "map": m})

    # ------------------------------------------------------------------ 시뮬레이션 (SC-04, 07, 08)
    async def run_sim(self, s: Session, map_version: str, scenario: dict[str, Any], strategy: str, sim_id: str):
        m = self.store.maps[map_version].map
        await self.status(s, "시뮬레이션 실행", f"planner.simulate(map v{map_version}, 로봇 {scenario['robots']}, "
                                                f"주문 {scenario['inbound']}/{scenario['outbound']}, {strategy})")
        t0 = time.perf_counter()
        sim, res = await asyncio.to_thread(start_simulation, m, {**scenario, "map_version": map_version}, strategy)
        ms = round((time.perf_counter() - t0) * 1000, 1)
        if not res["ok"]:
            return await self._sim_failed(s, res)
        lg = res["log"]
        self.store.sims[sim_id] = SimEntry(sim_id, s.session_id, map_version, scenario, strategy, lg, sim)
        await self.status(s, "로그 분석", f"충돌 0건, 총 {lg['total_steps']} 스텝, 계산 {ms}ms")
        await self.store.emit(s, {"type": "sim_ready", "sim_id": sim_id, "strategy": strategy,
                                  "total_steps": lg["total_steps"], "summary": summary_ko(lg)})
        return sim_id

    async def _sim_failed(self, s: Session, res: dict):
        err = res["error"]
        log.error("시뮬레이션 실패 → VR로 보내지 않음: %s", err)
        await self.store.emit(s, {"type": "error", "code": err["code"], "message": err["message"]})

    async def run_event(self, e: SimEntry, event: dict):
        s = self.store.session(e.session_id)
        await self.status(s, "롤링 재계획", f"t={event['t']} 주문 +{event['add_orders']}, 로봇 {event['robots']}대 → 이후만 재계산")
        t0 = time.perf_counter()
        res = await asyncio.to_thread(replan, e.sim, event)
        ms = round((time.perf_counter() - t0) * 1000, 1)
        if not res["ok"]:
            return await self._sim_failed(s, res)
        e.log = res["log"]
        e.events.append(event)
        lg = e.log
        await self.status(s, "로그 분석", f"충돌 0건, 총 {lg['total_steps']} 스텝, 재계획 {ms}ms")
        await self.store.emit(s, {"type": "sim_ready", "sim_id": e.sim_id, "strategy": e.strategy,
                                  "total_steps": lg["total_steps"], "summary": summary_ko(lg),
                                  "replan_from": event["t"], "replan_ms": ms})

    async def run_compare(self, e: SimEntry):
        s = self.store.session(e.session_id)
        await self.status(s, "비교 리포트", "같은 지도·주문으로 기준 전략 실행")
        m = self.store.maps[e.map_version].map
        sc = {**e.scenario, "map_version": e.map_version, "events": e.events}
        base = await asyncio.to_thread(simulate, m, sc, "baseline", 42)
        await self.store.emit(s, {"type": "compare", **compare_logs(base, e.log)})

    # ------------------------------------------------------------------ 분석·개선 (SC-09, 10)
    async def run_analyze(self, e: SimEntry):
        s = self.store.session(e.session_id)
        await self.status(s, "로그 분석", "칸별 대기 집계 → 병목 상위 10칸")
        result = analyze_log(self.store, e)
        await self.status(s, "개선안 제안", f"개선안 {len(result['proposals'])}개 (엔진이 적용 가능한 유형만)")
        await self.store.emit(s, {"type": "analysis", **result})

    async def run_improve(self, e: SimEntry, p: dict, new_sim: str):
        s = self.store.session(e.session_id)
        version = e.map_version
        scenario = dict(e.scenario)
        if "robots" in p["apply"]:
            scenario["robots"] = p["apply"]["robots"]
        if "dock" in p["apply"]:
            req = dict(self.store.maps[version].req)
            key = "dock_in" if p["apply"]["dock"] == "dock_in" else "dock_out"
            req[key] = req.get(key, 1) + 1
            m = generate_map(req)
            if not validate_map(m)["valid"]:
                await self.store.emit(s, {"type": "error", "code": "IMPROVE_INVALID", "message": "개선안 적용 지도가 검증에 실패했습니다"})
                return
            version = self.store.add_map(s, m, req, confirmed=True)
            await self.store.emit(s, {"type": "map_ready", "map_version": version, "map": m, "confirmed": True,
                                      "summary": "개선안 적용: " + summarize(m), "defaults_applied": []})
        await self.run_sim(s, version, scenario, e.strategy, new_sim)


def analyze_log(store: Store, e: SimEntry) -> dict[str, Any]:
    """규칙 기반 병목 분석 (mock_server 와 같은 출력). LLM 설명은 동결 이후 과제."""
    m = store.maps[e.map_version].map
    agg = aggregate_logs(e.log, m, top_k=10)
    top = agg["bottlenecks"]
    proposals = []
    if not top:
        return {"bottlenecks": [], "explanation": "대기가 거의 없습니다. 현재 구성으로 충분합니다.", "proposals": []}
    t0 = top[0]
    lines = [f"대기가 가장 많은 칸은 ({t0['x']},{t0['y']})로 {t0['wait']}스텝 기다렸습니다.",
             f"상위 5칸이 전체 대기 {agg['total_wait']}스텝 중 {agg['top5_share_pct']}%를 차지합니다."]
    near = agg["nearest_dock"]
    if near and near["dist"] <= 4:
        kind = "입하" if near["type"] == "dock_in" else "출하"
        lines.append(f"{kind} 도크 ({near['x']},{near['y']}) 앞에 로봇이 몰려 줄을 서고 있습니다.")
        proposals.append({"proposal_id": store.new_id("P"), "type": "dock_add", "text": f"{kind} 도크 1개 추가",
                          "apply": {"dock": near["type"]}})
    robots = e.scenario["robots"]
    if robots > 4:
        proposals.append({"proposal_id": store.new_id("P"), "type": "robot_count",
                          "text": f"로봇 {robots}대 → {robots - 2}대", "apply": {"robots": robots - 2}})
    if not proposals:
        proposals.append({"proposal_id": store.new_id("P"), "type": "dock_add", "text": "출하 도크 1개 추가",
                          "apply": {"dock": "dock_out"}})
    for p in proposals:
        e.proposals[p["proposal_id"]] = p
        store.proposals[p["proposal_id"]] = e.sim_id
    return {"bottlenecks": [{"x": c["x"], "y": c["y"], "wait": c["wait"]} for c in top],
            "explanation": " ".join(lines),
            "proposals": [{k: v for k, v in p.items() if k != "apply"} for p in proposals]}


def wav_rms(data: bytes) -> float:
    """16bit WAV 의 RMS (0~1). WAV 가 아니면 1.0 을 돌려 STT 에 맡긴다."""
    try:
        with wave.open(io.BytesIO(data)) as w:
            frames = w.readframes(w.getnframes())
    except Exception:
        return 1.0
    n = len(frames) // 2
    if n == 0:
        return 0.0
    samples = struct.unpack(f"<{n}h", frames[: n * 2])
    return math.sqrt(sum(x * x for x in samples) / n) / 32768
