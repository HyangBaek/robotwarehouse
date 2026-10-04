"""시뮬레이션 실행 노드: 경로 계산 엔진 호출, 충돌 점검, SQLite 저장, VR 로 sim_ready.
주문을 다 처리하지 못해도 결과를 보내고 원인을 붙인다 (tools.outcome)."""
import asyncio
import time

from langchain_core.runnables import RunnableConfig

from tools.simulation import start_simulation
from . import flow_of, session_of, step
from ..state import AgentState


async def simulate(s: AgentState, config: RunnableConfig) -> dict:
    f = flow_of(config)
    se = session_of(f, s)
    approve = s.get("intent") == "approve"
    sim_id = s["new_sim_id"] if approve else s["sim_id"]
    version, scenario, strategy = s["map_version"], s["scenario"], s.get("strategy") or "optimized"
    grid = f.store.get_map(version).map
    await f.status(se, "시뮬레이션 실행", f"planner.simulate(map v{version}, 로봇 {scenario['robots']}, "
                                         f"주문 {scenario.get('inbound')}/{scenario.get('outbound')}, {strategy})")
    t0 = time.perf_counter()
    sim, res = await asyncio.to_thread(start_simulation, grid, {**scenario, "map_version": version}, strategy)
    ms = round((time.perf_counter() - t0) * 1000, 1)
    if not res["ok"]:
        await f.sim_failed(se, res)
        return {"error": res["error"]["code"], "trace": step(s, "simulate")}
    entry = f.store.new_sim(sim_id, se.session_id, version, scenario, strategy, res["log"], sim)
    await asyncio.to_thread(f.store.save_sim, entry, ms)                 # 동선 로그 SQLite 저장 (DR-03)
    if approve and f.store.repo is not None:
        await asyncio.to_thread(f.store.repo.save_approval, se.session_id, s["proposal"], s["sim_id"], sim_id)
    lg = res["log"]
    await f.status(se, "로그 분석", f"충돌 0건, 총 {lg['total_steps']} 스텝, 계산 {ms}ms")
    await f.store.emit(se, {"type": "sim_ready", "sim_id": sim_id, "strategy": strategy, "total_steps": lg["total_steps"],
                            **await f.outcome_fields(se, lg, scenario, version)})
    return {"error": None, "trace": step(s, "simulate")}
