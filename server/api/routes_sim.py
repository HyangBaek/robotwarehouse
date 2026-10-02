"""시나리오·시뮬레이션 REST (SC-04~08)."""
from fastapi import APIRouter, HTTPException

router = APIRouter(tags=["sim"])


def _todo():
    raise HTTPException(status_code=501, detail="not implemented")


@router.post("/scenario")
async def scenario(body: dict):
    _todo()


@router.get("/sim/{sim_id}/frames")
async def frames(sim_id: str, t_from: int = 0, t_to: int = 199):
    _todo()


@router.get("/sim/{sim_id}/stats")
async def stats(sim_id: str):
    _todo()


@router.post("/sim/{sim_id}/event")
async def event(sim_id: str, body: dict):
    _todo()
