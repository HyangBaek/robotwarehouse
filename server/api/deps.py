"""의존성 주입: 테스트(ENV-1)는 가짜, 실행(ENV-3/4)은 실제 구현."""
from dataclasses import dataclass
from typing import Any

from fastapi import Request
from fastapi.responses import JSONResponse


@dataclass
class Deps:
    repo: Any
    llm: Any
    stt: Any
    store: Any = None
    flow: Any = None


def get_deps(request: Request) -> Deps:
    return request.app.state.deps


def err(code: int, ecode: str, message: str):
    return JSONResponse(status_code=code, content={"error": {"code": ecode, "message": message}})


def spawn(request: Request, coro):
    """응답을 먼저 돌려주고 Agent 흐름은 뒤에서 실행한다 (결과는 WS 로)."""
    import asyncio
    tasks = request.app.state.tasks
    t = asyncio.create_task(coro)
    tasks.add(t)
    t.add_done_callback(tasks.discard)
    return t
