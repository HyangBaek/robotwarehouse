"""Agent 처리 흐름 (SC-01~12).

판단이 들어가는 요청(창고 설명, 답변, 랙 편집, 시뮬레이션 실행, 분석, 개선안 승인)은 LangGraph 그래프(agent/graph.py)로
진행하고, 판단이 없는 요청(음성 인식, 시나리오 문장 해석, 롤링 재계획, 비교, 리포트, 기록 불러오기)은 여기서 도구를 바로 부른다.
각 단계마다 WS status {node, message} 를 보낸다. 시연 화면에서 Agent 판단 과정을 보여 주는 장치.
"""
from __future__ import annotations

import asyncio
import copy
import io
import logging
import math
import struct
import time
import wave
from typing import Any

from agent.graph import build_graph
from agent.interpreter import Interpreter
from agent.nodes import SOURCE_LABEL
from config import settings
from planner import simulate
from tools.compare import compare_logs
from tools.map_generator import summarize
from tools.report import build_report
from tools.scenario import merge_scenario, recommend_scenario
from tools.simulation import replan, start_simulation, summary_ko
from .store import Session, SimEntry, Store

log = logging.getLogger("api.flow")


class Flow:
    def __init__(self, store: Store, llm=None, stt=None, node_delay: float | None = None):
        self.store, self.stt = store, stt
        self.interpreter = Interpreter(llm)
        self.llm_name = getattr(llm, "model", None) or ("LLM" if llm else None)
        self.node_delay = settings.node_delay_sec if node_delay is None else node_delay
        self.graph = build_graph()
        self._locks: dict[str, asyncio.Lock] = {}

    async def status(self, s: Session, node: str, message: str):
        await self.store.emit(s, {"type": "status", "node": node, "message": message})
        if self.node_delay:
            await asyncio.sleep(self.node_delay)

    def node_interpret(self) -> str:
        return f"지시 해석 ({self.llm_name})" if self.llm_name else "지시 해석"

    async def report_source(self, s: Session, source: str, reason: str | None, ms: float):
        if source == "regex_fallback":
            await self.status(s, "regex_fallback", f"LLM 실패 → 정규식으로 해석 ({reason})")
        log.info("해석 source=%s %.0fms", source, ms)

    # ------------------------------------------------------------------ LangGraph Agent 실행
    async def run_graph(self, s: Session, **inp):
        """한 요청(턴)을 Agent 그래프로 진행한다. 세션마다 한 번에 한 턴만 (체크포인트 충돌 방지)."""
        lock = self._locks.setdefault(s.session_id, asyncio.Lock())
        async with lock:
            try:
                return await self.graph.ainvoke(
                    {**inp, "session_id": s.session_id, "trace": []},
                    {"configurable": {"thread_id": s.session_id, "flow": self}})
            except Exception as e:
                log.exception("Agent 그래프 실패: %s", inp.get("intent"))
                await self.store.emit(s, {"type": "error", "code": "AGENT_ERROR", "message": f"처리 실패: {e}"})

    async def handle_text(self, s: Session, text: str):
        """창고 설명 -> 지시 해석 -> (빠진 정보 질문) -> 지도 생성 -> 검증 -> (수정 질문) -> 지도 전송"""
        return await self.run_graph(s, intent="compose", text=text)

    async def handle_answer(self, s: Session, text: str):
        return await self.run_graph(s, intent="answer", answer=text)

    async def handle_voice(self, s: Session, data: bytes, question_id: str | None, on_text=None, stt_only: bool = False):
        """on_text 가 있으면 인식 문장을 그쪽으로 넘긴다 (시나리오 음성). stt_only 면 인식 결과만 보낸다."""
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
        await self.store.emit(s, {"type": "transcript", "text": text, "stt_only": stt_only})
        if stt_only:
            return
        if on_text is not None:
            await on_text(text)
        elif question_id and s.question_id == question_id:
            await self.handle_answer(s, text)
        else:
            await self.handle_text(s, text)

    async def _stt_empty(self, s: Session):
        await self.store.emit(s, {"type": "transcript", "text": ""})
        await self.store.emit(s, {"type": "error", "code": "STT_EMPTY", "message": "잘 못 들었어요. 다시 말하거나 입력해 주세요"})

    async def handle_edit(self, s: Session, base_version: str, moves: list[dict]):
        """VR 에서 랙을 옮긴 지도 -> 지도 검증 -> 지도 전송 또는 수정 질문 (SC-11)."""
        base = self.store.get_map(base_version)
        m = copy.deepcopy(base.map)
        for mv in moves:
            fx, fy = mv["from"]
            tx, ty = mv["to"]
            m["cells"] = [c for c in m["cells"] if (c["x"], c["y"]) not in ((fx, fy), (tx, ty))]
            m["cells"].append({"x": tx, "y": ty, "type": "rack", "rack_id": mv.get("rack_id")})
        return await self.run_graph(s, intent="edit", map=m, requirements=base.req, map_note="편집 반영: ",
                                    confirmed=False, missing=[], defaults_applied=[])

    # ------------------------------------------------------------------ 시나리오 음성, 문장 (SC-04)
    async def handle_scenario_text(self, s: Session, map_version: str, text: str, current: dict | None = None):
        """'로봇 6대, 입하 30건 실행해줘' -> WS scenario {robots, inbound, outbound, run}. VR 이 값을 반영하고 run 이면 실행."""
        try:
            m = self.store.get_map(map_version).map
            rec = recommend_scenario(m)
            t0 = time.perf_counter()
            await self.status(s, "시나리오 해석", f"\"{text}\" 해석 중")
            got, source, reason = await asyncio.to_thread(self.interpreter.extract_scenario, text)
            ms = (time.perf_counter() - t0) * 1000
            await self.report_source(s, source, reason, ms)
            base = rec if (got.get("use_defaults") or not current) else {**rec, **current}
            sc = merge_scenario(base, got)
            changed = [k for k in ("robots", "inbound", "outbound", "spec_b_pct") if k in got]
            await self.status(s, "시나리오 해석", f"({SOURCE_LABEL[source]}, {ms / 1000:.1f}초) 로봇 {sc['robots']}대, "
                                                  f"입하 {sc['inbound']}건, 출하 {sc['outbound']}건"
                                                  + (f", 1200 규격 {sc['spec_b_pct']}%" if sc["spec_b_pct"] else "")
                                                  + (" → 실행" if got.get("run") else ""))
            await self.store.emit(s, {"type": "scenario", "map_version": map_version, **sc,
                                      "run": bool(got.get("run")), "changed": changed, "basis": rec["basis"]})
        except Exception as e:
            log.exception("handle_scenario_text 실패")
            await self.store.emit(s, {"type": "error", "code": "AGENT_ERROR", "message": f"시나리오 해석 실패: {e}"})

    # ------------------------------------------------------------------ 시뮬레이션 (SC-04, 07, 08)
    async def run_sim(self, s: Session, map_version: str, scenario: dict[str, Any], strategy: str, sim_id: str):
        return await self.run_graph(s, intent="simulate", map_version=map_version, scenario=scenario,
                                    strategy=strategy, sim_id=sim_id)

    async def sim_failed(self, s: Session, res: dict):
        err = res["error"]
        log.error("시뮬레이션 실패 → VR로 보내지 않음: %s", err)
        await self.store.emit(s, {"type": "error", "code": err["code"], "message": err["message"]})

    async def run_event(self, e: SimEntry, event: dict):
        s = self.store.session(e.session_id)
        await self.status(s, "롤링 재계획", f"t={event['t']} 주문 +{event['add_orders']}, 로봇 {event['robots']}대 → 이후만 재계산")
        if e.sim is None:                                 # DB 에서 불러온 기록: 같은 입력으로 엔진 상태를 다시 만든다
            m = self.store.get_map(e.map_version).map
            sc = {**e.scenario, "map_version": e.map_version, "events": e.events}
            e.sim, _ = await asyncio.to_thread(start_simulation, m, sc, e.strategy)
        t0 = time.perf_counter()
        res = await asyncio.to_thread(replan, e.sim, event)
        ms = round((time.perf_counter() - t0) * 1000, 1)
        if not res["ok"]:
            return await self.sim_failed(s, res)
        e.log = res["log"]
        e.events.append(event)
        e.baseline_log = e.report = None                  # 이벤트가 바뀌었으니 비교, 리포트 다시 계산
        await asyncio.to_thread(self.store.save_sim, e, ms)
        lg = e.log
        await self.status(s, "로그 분석", f"충돌 0건, 총 {lg['total_steps']} 스텝, 재계획 {ms}ms")
        await self.store.emit(s, {"type": "sim_ready", "sim_id": e.sim_id, "strategy": e.strategy,
                                  "total_steps": lg["total_steps"], "summary": summary_ko(lg),
                                  "replan_from": event["t"], "replan_ms": ms})

    async def baseline_for(self, e: SimEntry) -> dict:
        if e.baseline_log is None:
            m = self.store.get_map(e.map_version).map
            sc = {**e.scenario, "map_version": e.map_version, "events": e.events}
            e.baseline_log = await asyncio.to_thread(simulate, m, sc, "baseline", 42, e.scenario.get("engine_config"))
        return e.baseline_log

    async def run_compare(self, e: SimEntry):
        s = self.store.session(e.session_id)
        await self.status(s, "비교 리포트", "같은 지도·주문으로 기준 전략 실행")
        base = await self.baseline_for(e)
        await self.store.emit(s, {"type": "compare", **compare_logs(base, e.log)})

    async def report_for(self, e: SimEntry) -> dict:
        """최종 리포트 (기준 전략 비교 포함). 한 번 계산하면 이벤트 전까지 재사용."""
        if e.report is None:
            base = await self.baseline_for(e)
            entry = self.store.get_map(e.map_version)
            e.report = await asyncio.to_thread(build_report, e.log, entry.map, e.scenario, base,
                                               summarize(entry.map), e.sim_id)
            if self.store.repo is not None:
                await asyncio.to_thread(self.store.repo.save_report, e.sim_id, e.report)
        return e.report

    async def load_sim(self, e: SimEntry, s: Session):
        """저장된 시뮬레이션을 세션에 다시 불러온다: 지도(확정 상태) -> 재생 (기록 보기)."""
        entry = self.store.get_map(e.map_version)
        await self.status(s, "기록 불러오기", f"{e.sim_id} (지도 v{e.map_version})")
        await self.store.emit(s, {"type": "map_ready", "map_version": e.map_version, "map": entry.map, "confirmed": True,
                                  "summary": "기록: " + summarize(entry.map), "defaults_applied": []})
        await self.store.emit(s, {"type": "sim_ready", "sim_id": e.sim_id, "strategy": e.strategy,
                                  "total_steps": e.log["total_steps"], "summary": summary_ko(e.log),
                                  "scenario": {k: e.scenario.get(k) for k in ("robots", "inbound", "outbound", "spec_b_pct")},
                                  "history": True})

    async def run_report(self, e: SimEntry):
        s = self.store.session(e.session_id)
        try:
            await self.status(s, "최종 리포트", "로봇 효율·주문 처리 시간·기준 전략 비교 집계")
            rep = await self.report_for(e)
            await self.store.emit(s, {"type": "report", **rep, "html_url": f"/sim/{e.sim_id}/report.html"})
        except Exception as ex:
            log.exception("리포트 실패")
            await self.store.emit(s, {"type": "error", "code": "REPORT_ERROR", "message": f"리포트 생성 실패: {ex}"})

    # ------------------------------------------------------------------ 분석, 개선 (SC-09, 10)
    async def run_analyze(self, e: SimEntry):
        """로그 분석(집계 + LLM 설명) -> 개선안 제안(규칙 후보 + LLM 선택) -> WS analysis"""
        return await self.run_graph(self.store.session(e.session_id), intent="analyze", sim_id=e.sim_id)

    async def run_improve(self, e: SimEntry, p: dict, new_sim: str):
        """개선안 적용 -> (지도 변경 시 검증, 전송) -> 같은 주문으로 재시뮬레이션"""
        return await self.run_graph(self.store.session(e.session_id), intent="approve", sim_id=e.sim_id,
                                    proposal_id=p["proposal_id"], new_sim_id=new_sim)


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
