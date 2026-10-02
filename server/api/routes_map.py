"""지도 관련 REST (SC-01~03, SC-11)."""
from fastapi import APIRouter, HTTPException

router = APIRouter(prefix="/map", tags=["map"])


def _todo():
    raise HTTPException(status_code=501, detail="not implemented")


@router.post("/text")
async def map_text(body: dict):
    _todo()


@router.post("/voice")
async def map_voice():
    _todo()


@router.post("/answer")
async def map_answer(body: dict):
    _todo()


@router.post("/confirm")
async def map_confirm(body: dict):
    _todo()


@router.post("/edit")
async def map_edit(body: dict):
    _todo()
