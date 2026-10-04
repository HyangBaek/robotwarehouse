"""시나리오, 시뮬레이션 REST (SC-04~08)."""
from fastapi import APIRouter, File, Form, HTTPException, Query, Request, UploadFile

from tools.scenario import check_spec, spec_mix
from .deps import err, get_deps, spawn

router = APIRouter(tags=["sim"])


def get_sim(request: Request, sim_id: str):
    """메모리에 없으면 DB 기록에서 불러온다."""
    e = get_deps(request).store.get_sim(sim_id)
    if e is None:
        raise HTTPException(404, detail=f"sim_id {sim_id} 없음")
    return e


@router.post("/scenario")
async def scenario(body: dict, request: Request):
    """body: {session_id, map_version, robots, inbound, outbound, spec_b_pct?, strategy}.
    inbound, outbound = 입하, 출하 제품 수량(파레트), spec_b_pct = 그중 1200x1000 규격 비율(%) (FR-11)."""
    d = get_deps(request)
    v = str(body.get("map_version"))
    if (bad := _confirmed_map(d, v)) is not None:
        return bad
    try:
        sc = {"robots": int(body.get("robots", 4)), "inbound": int(body.get("inbound", 25)),
              "outbound": int(body.get("outbound", 25)), "spec_b_pct": int(body.get("spec_b_pct") or 0)}
    except (TypeError, ValueError):
        return err(400, "BAD_SCENARIO", "robots, inbound, outbound, spec_b_pct 는 정수여야 합니다")
    if not (1 <= sc["robots"] <= 30) or not (0 <= sc["inbound"] <= 200) or not (0 <= sc["outbound"] <= 200) \
            or not (0 <= sc["spec_b_pct"] <= 100):
        return err(400, "BAD_SCENARIO", "로봇은 1~30대, 주문 수는 0~200건, 1200 규격 비율은 0~100%여야 합니다")
    if (msg := check_spec(d.store.get_map(v).map, sc["spec_b_pct"])) is not None:
        return err(400, "SPEC_UNAVAILABLE", msg)
    if (mix := spec_mix(sc["spec_b_pct"])) is not None:
        sc["spec_mix"] = mix
    s = d.store.session(body.get("session_id") or d.store.map_owner.get(v))
    sim_id = d.store.new_id("S")
    spawn(request, d.flow.run_sim(s, v, sc, body.get("strategy", "optimized"), sim_id))
    return {"sim_id": sim_id}


def _confirmed_map(d, v: str):
    entry = d.store.get_map(v)
    if entry is None:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    if not entry.confirmed:
        return err(409, "MAP_NOT_CONFIRMED", "지도를 먼저 확인해 주세요")
    return None


def _current(robots, inbound, outbound, spec_b_pct=None) -> dict | None:
    """VR 패널의 현재 값. 로봇, 입하, 출하가 다 있을 때만 쓰고, 아니면 권장값에서 시작한다."""
    try:
        cur = {"robots": int(robots), "inbound": int(inbound), "outbound": int(outbound)}
    except (TypeError, ValueError):
        return None
    if spec_b_pct not in (None, ""):
        cur["spec_b_pct"] = int(spec_b_pct)
    return cur


@router.post("/scenario/text")
async def scenario_text(body: dict, request: Request):
    """시나리오 문장 -> WS scenario. body: {session_id, map_version, text, robots?, inbound?, outbound?}"""
    d = get_deps(request)
    v = str(body.get("map_version"))
    if (bad := _confirmed_map(d, v)) is not None:
        return bad
    text = (body.get("text") or "").strip()
    if not text:
        return err(400, "EMPTY_TEXT", "시나리오 설명이 비어 있습니다")
    s = d.store.session(body.get("session_id") or d.store.map_owner.get(v))
    cur = _current(body.get("robots"), body.get("inbound"), body.get("outbound"), body.get("spec_b_pct"))
    spawn(request, d.flow.handle_scenario_text(s, v, text, cur))
    return {"request_id": d.store.new_id("REQ")}


@router.post("/scenario/voice")
async def scenario_voice(request: Request, session_id: str = Form("default"), map_version: str = Form(...),
                         audio: UploadFile = File(...), robots: str | None = Form(None),
                         inbound: str | None = Form(None), outbound: str | None = Form(None),
                         spec_b_pct: str | None = Form(None)):
    """시나리오 음성 -> STT -> WS transcript -> WS scenario."""
    d = get_deps(request)
    if (bad := _confirmed_map(d, map_version)) is not None:
        return bad
    data = await audio.read()
    s = d.store.session(session_id)
    cur = _current(robots, inbound, outbound, spec_b_pct)

    async def on_text(text):
        await d.flow.handle_scenario_text(s, map_version, text, cur)
    spawn(request, d.flow.handle_voice(s, data, None, on_text=on_text))
    return {"request_id": d.store.new_id("REQ")}


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
