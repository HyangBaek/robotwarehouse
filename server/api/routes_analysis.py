"""분석, 개선 REST (SC-09, SC-10)."""
from fastapi import APIRouter, Request
from fastapi.responses import HTMLResponse

from .deps import err, get_deps, spawn
from .report_html import render
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


@router.post("/report")
async def report(body: dict, request: Request):
    """최종 리포트 -> WS report (기준 전략 비교 포함, 첫 요청은 1~5초)."""
    d = get_deps(request)
    e = get_sim(request, body.get("sim_id"))
    spawn(request, d.flow.run_report(e))
    return {"request_id": d.store.new_id("REQ")}


@router.get("/sim/{sim_id}/report")
async def report_json(sim_id: str, request: Request):
    return await get_deps(request).flow.report_for(get_sim(request, sim_id))


@router.get("/sim/{sim_id}/report.html", response_class=HTMLResponse)
async def report_html(sim_id: str, request: Request):
    """노트북 브라우저용 리포트 (visionOS 스타일). 발표 화면, 보고서 캡처용."""
    return render(await get_deps(request).flow.report_for(get_sim(request, sim_id)))
