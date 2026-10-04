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
                    question_id: str | None = Form(None), stt_only: bool = Form(False)):
    """stt_only 이면 인식 결과만 보내고 멈춘다. VR 이 사용자 확인을 받은 뒤 /map/text 나 /map/answer 로 다시 보낸다."""
    d = get_deps(request)
    data = await audio.read()
    s = d.store.session(session_id)
    spawn(request, d.flow.handle_voice(s, data, question_id, stt_only=stt_only))
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
    if d.store.get_map(v) is None:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    d.store.confirm_map(v)
    return {"status": "confirmed", "map_version": v}


@router.post("/edit")
async def map_edit(body: dict, request: Request):
    d = get_deps(request)
    v = str(body.get("map_version"))
    if d.store.get_map(v) is None:
        return err(404, "UNKNOWN_MAP", f"map_version {v} 없음")
    s = d.store.session(body.get("session_id") or d.store.map_owner.get(v))
    spawn(request, d.flow.handle_edit(s, v, body.get("moves", [])))
    return {"request_id": d.store.new_id("REQ")}
