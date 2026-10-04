"""FastAPI 진입점. 실행: uvicorn api.main:app --host 0.0.0.0 --port 8000"""
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI

from config import settings
from db.repo import Repo
from .deps import Deps
from .flow import Flow
from .store import Store
from . import ws, routes_map, routes_sim, routes_analysis, routes_history

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(name)s %(message)s")
log = logging.getLogger("api")
_DEFAULT = object()


def create_app(db_path: str | None = None, llm=_DEFAULT, stt=_DEFAULT,
               node_delay: float | None = None, preload_stt: bool = False) -> FastAPI:
    """llm, stt 를 넘기지 않으면 설정(.env)대로 만든다. None 을 넘기면 끈다 (정규식 해석, 음성 입력 불가)."""
    if llm is _DEFAULT:
        from services.llm import make_llm
        llm = make_llm()
    if stt is _DEFAULT:
        from services.stt import make_stt
        stt = make_stt()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        if llm is not None and hasattr(llm, "ping"):
            log.info("LLM %s %s", getattr(llm, "model", ""), "연결됨" if llm.ping() else "응답 없음 → 정규식 대체 경로 사용")
        if preload_stt and stt is not None and hasattr(stt, "load"):
            import asyncio
            await asyncio.to_thread(stt.load)              # 모델은 시작 시 1회만 로드
        yield

    app = FastAPI(title="RobotWarehouse Agent Server", lifespan=lifespan)
    repo = Repo(str(db_path or settings.db_path))
    store = Store(repo)
    app.state.tasks = set()
    app.state.deps = Deps(repo=repo, llm=llm, stt=stt,
                          store=store, flow=Flow(store, llm, stt, node_delay))
    for module in (ws, routes_map, routes_sim, routes_analysis, routes_history):
        app.include_router(module.router)

    @app.get("/health")
    def health():
        return {"status": "ok"}

    return app


app = create_app(preload_stt=True)
