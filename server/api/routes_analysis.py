"""분석·개선 REST (SC-09, SC-10)."""
from fastapi import APIRouter, Request

from .deps import err, get_deps, spawn
from .routes_sim import get_sim

router = APIRouter(tags=["analysis"])


@router.post("/analyze")
async def analyze(body: dict, request: Request):
    d = get_deps(request)
    e = get_sim(request, body.get("sim_id"))
    spawn(request, d.flow.run_analyze(e))
    return {"request_id": d.store.new_id("REQ")}


@router.post("/improve/approve")
async def approve(body: dict, request: Request):
    d = get_deps(request)
    pid = body.get("proposal_id")
    if pid not in d.store.proposals:
        return err(404, "UNKNOWN_PROPOSAL", f"proposal_id {pid} 없음")
    e = d.store.sims[d.store.proposals[pid]]
    new_sim = d.store.new_id("S")
    spawn(request, d.flow.run_improve(e, e.proposals[pid], new_sim))
    return {"sim_id": new_sim}
