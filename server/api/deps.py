"""의존성 주입: 테스트(ENV-1)는 가짜, 실행(ENV-3/4)은 실제 구현."""
from dataclasses import dataclass
from typing import Any


@dataclass
class Deps:
    repo: Any
    llm: Any
    stt: Any
    tts: Any = None
    graph: Any = None
