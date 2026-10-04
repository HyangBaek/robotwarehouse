"""시나리오·시뮬레이션 REST (SC-04~08)."""
from fastapi import APIRouter, HTTPException, Query, Request

from .deps import err, get_deps, spawn

router = APIRouter(tags=["sim"])


def get_sim(request: Request, sim_id: str):
    sims = get_deps(request).store.sims
    if sim_id not in sims:
        raise HTTPException(404, detail=f"sim_id {sim_id} 없음")
    return sims[sim_id]


@router.post("/scenario")
async def scenario(body: dict, request: Request):
    d = get_deps(request)
    v = str(body.get("map_version"))
    if v not in d.store.maps:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    if not d.store.maps[v].confirmed:
        return err(409, "MAP_NOT_CONFIRMED", "지도를 먼저 확인해 주세요")
    try:
        sc = {"robots": int(body.get("robots", 4)), "inbound": int(body.get("inbound", 25)),
              "outbound": int(body.get("outbound", 25))}
    except (TypeError, ValueError):
        return err(400, "BAD_SCENARIO", "robots·inbound·outbound 는 정수여야 합니다")
    if not (1 <= sc["robots"] <= 30) or sc["inbound"] < 0 or sc["outbound"] < 0:
        return err(400, "BAD_SCENARIO", "로봇은 1~30대, 주문 수는 0 이상이어야 합니다")
    s = d.store.session(body.get("session_id") or d.store.map_owner.get(v))
    sim_id = d.store.new_id("S")
    spawn(request, d.flow.run_sim(s, v, sc, body.get("strategy", "optimized"), sim_id))
    return {"sim_id": sim_id}


@router.get("/sim/{sim_id}/frames")
async def frames(sim_id: str, request: Request, from_: int = Query(0, alias="from"), to: int = Query(200)):
    e = get_sim(request, sim_id)
    return e.log["frames"][max(0, from_): to + 1]


@router.get("/sim/{sim_id}/stats")
async def stats(sim_id: str, request: Request):
    return get_sim(request, sim_id).log["cell_stats"]


@router.post("/sim/{sim_id}/event")
async def event(sim_id: str, body: dict, request: Request):
    d = get_deps(request)
    e = get_sim(request, sim_id)
    if e.sim is None:
        return err(409, "NO_ENGINE_STATE", "재계획할 수 없는 시뮬레이션입니다")
    ev = {"t": max(0, int(body.get("t", 0))), "add_orders": max(0, int(body.get("add_orders", 0))),
          "robots": max(1, int(body.get("robots", e.scenario["robots"])))}
    spawn(request, d.flow.run_event(e, ev))
    return {"status": "replanning"}


@router.post("/compare")
async def compare(body: dict, request: Request):
    d = get_deps(request)
    e = get_sim(request, body.get("sim_id"))
    spawn(request, d.flow.run_compare(e))
    return {"request_id": d.store.new_id("REQ")}
