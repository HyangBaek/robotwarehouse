"""지도 관련 REST (SC-01~03, SC-11)."""
from fastapi import APIRouter, File, Form, Request, UploadFile

from .deps import err, get_deps, spawn

router = APIRouter(prefix="/map", tags=["map"])


@router.post("/text")
async def map_text(body: dict, request: Request):
    d = get_deps(request)
    text = (body.get("text") or "").strip()
    if not text:
        return err(400, "EMPTY_TEXT", "창고 설명이 비어 있습니다")
    s = d.store.session(body.get("session_id"))
    spawn(request, d.flow.handle_text(s, text))
    return {"request_id": d.store.new_id("REQ")}


@router.post("/voice")
async def map_voice(request: Request, session_id: str = Form("default"), audio: UploadFile = File(...),
                    question_id: str | None = Form(None)):
    d = get_deps(request)
    data = await audio.read()
    s = d.store.session(session_id)
    spawn(request, d.flow.handle_voice(s, data, question_id))
    return {"request_id": d.store.new_id("REQ")}


@router.post("/answer")
async def map_answer(body: dict, request: Request):
    d = get_deps(request)
    qid = body.get("question_id")
    sid = d.store.questions.get(qid)
    if sid is None:
        return err(404, "UNKNOWN_QUESTION", f"question_id {qid} 없음")
    s = d.store.session(sid)
    if s.question_id != qid:
        return err(409, "STALE_QUESTION", "이미 처리된 질문입니다")
    spawn(request, d.flow.handle_answer(s, body.get("text", "")))
    return {"request_id": d.store.new_id("REQ")}


@router.post("/confirm")
async def map_confirm(body: dict, request: Request):
    d = get_deps(request)
    v = str(body.get("map_version"))
    if v not in d.store.maps:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    d.store.maps[v].confirmed = True
    return {"status": "confirmed", "map_version": v}


@router.post("/edit")
async def map_edit(body: dict, request: Request):
    d = get_deps(request)
    v = str(body.get("map_version"))
    if v not in d.store.maps:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    s = d.store.session(body.get("session_id") or d.store.map_owner.get(v))
    spawn(request, d.flow.handle_edit(s, v, body.get("moves", [])))
    return {"request_id": d.store.new_id("REQ")}
