"""시뮬레이션 기록 REST: 저장된 시뮬레이션 목록, 다시 불러오기, 로봇 동선 로그 (DR-03)."""
import csv
import io

from fastapi import APIRouter, Query, Request
from fastapi.responses import HTMLResponse, PlainTextResponse

from tools.map_generator import summarize
from .deps import get_deps, spawn
from .history_html import render_list, render_routes
from .routes_sim import get_sim

router = APIRouter(tags=["history"])


def robot_routes(log: dict) -> list[dict]:
    """로봇별 동선. 위치나 상태가 바뀐 스텝만 남긴다: {"id", "moves", "path": [[t, x, y, state, task_id], ...]}"""
    out: dict[str, dict] = {}
    for f in log["frames"]:
        for r in f["robots"]:
            e = out.setdefault(r["id"], {"id": r["id"], "moves": 0, "path": []})
            p = e["path"]
            if p and (p[-1][1], p[-1][2]) != (r["x"], r["y"]):
                e["moves"] += 1
            if not p or (p[-1][1], p[-1][2], p[-1][3], p[-1][4]) != (r["x"], r["y"], r["state"], r.get("task_id")):
                p.append([f["t"], r["x"], r["y"], r["state"], r.get("task_id")])
    return list(out.values())


@router.get("/sims")
async def list_sims(request: Request, session_id: str | None = None, limit: int = Query(30, ge=1, le=200)):
    """저장된 시뮬레이션 목록 (최신순). session_id 를 주면 그 세션 것만."""
    repo = get_deps(request).repo
    return {"sims": repo.list_sims(session_id, limit)}


@router.post("/sim/{sim_id}/load")
async def load_sim(sim_id: str, body: dict, request: Request):
    """기록을 세션에 다시 불러온다 -> WS map_ready(확정) -> sim_ready(history). 이후 비교, 리포트, 재계획도 이 세션으로."""
    d = get_deps(request)
    e = get_sim(request, sim_id)
    s = d.store.session(body.get("session_id") or e.session_id)
    e.session_id = s.session_id
    spawn(request, d.flow.load_sim(e, s))
    return {"status": "loading", "sim_id": sim_id}


@router.get("/sim/{sim_id}/routes")
async def routes(sim_id: str, request: Request):
    e = get_sim(request, sim_id)
    return {"sim_id": sim_id, "total_steps": e.log["total_steps"], "robots": robot_routes(e.log)}


@router.get("/sim/{sim_id}/routes.csv", response_class=PlainTextResponse)
async def routes_csv(sim_id: str, request: Request):
    """전체 동선 로그: 스텝마다 로봇 위치, 상태, 작업 주문."""
    e = get_sim(request, sim_id)
    buf = io.StringIO()
    w = csv.writer(buf)
    w.writerow(["t", "robot", "x", "y", "state", "task_id"])
    for f in e.log["frames"]:
        for r in f["robots"]:
            w.writerow([f["t"], r["id"], r["x"], r["y"], r["state"], r.get("task_id") or ""])
    return PlainTextResponse("﻿" + buf.getvalue(), media_type="text/csv; charset=utf-8",
                             headers={"Content-Disposition": f'attachment; filename="routes_{sim_id}.csv"'})


@router.get("/sim/{sim_id}/routes.html", response_class=HTMLResponse)
async def routes_html(sim_id: str, request: Request):
    e = get_sim(request, sim_id)
    grid = get_deps(request).store.get_map(e.map_version).map
    return render_routes(sim_id, grid, e.log, summarize(grid))


@router.get("/sims.html", response_class=HTMLResponse)
async def sims_html(request: Request):
    return render_list(get_deps(request).repo.list_sims(None, 100))
