"""분석·개선 REST (SC-09, SC-10)."""
from fastapi import APIRouter, HTTPException

router = APIRouter(tags=["analysis"])


@router.post("/analyze")
async def analyze(body: dict):
    raise HTTPException(status_code=501, detail="not implemented")


@router.post("/improve/approve")
async def approve(body: dict):
    raise HTTPException(status_code=501, detail="not implemented")
