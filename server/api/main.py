"""FastAPI 진입점. 실행: uvicorn api.main:app --host 0.0.0.0 --port 8000"""
from fastapi import FastAPI

from config import settings
from db.repo import Repo
from .deps import Deps
from . import ws, routes_map, routes_sim, routes_analysis


def create_app(db_path: str | None = None, llm=None, stt=None, tts=None) -> FastAPI:
    app = FastAPI(title="RobotWarehouse Agent Server")
    app.state.deps = Deps(repo=Repo(str(db_path or settings.db_path)), llm=llm, stt=stt, tts=tts)
    for module in (ws, routes_map, routes_sim, routes_analysis):
        app.include_router(module.router)

    @app.get("/health")
    def health():
        return {"status": "ok"}

    return app


app = create_app()
