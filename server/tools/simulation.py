"""run_simulation: 경로 엔진(planner) 호출 (B4). 충돌, 미완료면 ok=False -> 결과를 VR로 보내지 않는다."""
from planner import Sim, normalize_scenario
from planner import run_simulation as _run
from planner.service import check_log as _check


def run_simulation(grid: dict, scenario: dict, orders: list[dict] | None = None,
                   strategy: str = "optimized", event: dict | None = None, seed: int = 42) -> dict:
    """반환: {"ok", "log", "error"}. error = {"code": ENGINE_ERROR|COLLISION|INCOMPLETE, "message"}"""
    sc = dict(scenario)
    if orders:
        sc["orders"] = orders
    if event:
        sc["events"] = list(sc.get("events", [])) + [event]
    return _run(grid, sc, strategy, seed)


def check_log(log: dict) -> dict:
    """planner.service.check_log 로 점검. frames 는 복사해 둔다 (재계획이 Sim.frames 를 잘라내므로)."""
    return _check({**log, "frames": list(log["frames"])})


def start_simulation(grid: dict, scenario: dict, strategy: str = "optimized", seed: int = 42):
    """롤링 재계획을 위해 planner.Sim 객체를 함께 돌려준다. 반환: (sim | None, {"ok", "log", "error"})
    scenario["engine_config"] 가 있으면 엔진 설정으로 넘긴다 (개선안 storage_weight)."""
    try:
        sim = Sim(grid, normalize_scenario(scenario), strategy, seed, scenario.get("engine_config"))
        log = sim.run().result()
    except Exception as e:
        return None, dict(ok=False, log=None, error=dict(code="ENGINE_ERROR", message=f"{type(e).__name__}: {e}"))
    return sim, check_log(log)


def replan(sim, event: dict) -> dict:
    """event = {"t", "add_orders", "robots"}. 시점 t 이전 프레임은 유지, 이후만 다시 계산 (FR-19)."""
    try:
        log = sim.add_event(event)
    except Exception as e:
        return dict(ok=False, log=None, error=dict(code="ENGINE_ERROR", message=f"{type(e).__name__}: {e}"))
    return check_log(log)


def summary_ko(log: dict) -> dict:
    """VR 패널 표시용 요약 (mock_server 와 같은 키)."""
    m = log["meta"]
    return {"총 스텝": log["total_steps"], "완료 주문": m["orders_done"],
            "주문당 평균": m["avg_order_time"], "대기 합계": m["total_waits"]}
