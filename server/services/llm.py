"""LLM 호출 래퍼. 테스트에서는 tests/fakes.py 의 FakeLLM 으로 교체한다."""
from typing import Protocol


class LLM(Protocol):
    def invoke(self, system: str, user: str, schema: dict) -> dict: ...


class OpenAILLM:
    def __init__(self, model: str, timeout: float):
        self.model, self.timeout = model, timeout

    def invoke(self, system: str, user: str, schema: dict) -> dict:
        # TODO(방유진): 구조화 출력(function calling)으로 호출, temperature=0
        raise NotImplementedError
