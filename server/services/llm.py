"""LLM 호출 래퍼. 테스트에서는 tests/fakes.py 의 FakeLLM 으로 교체한다."""
import json
from typing import Protocol

import httpx

from config import settings


class LLM(Protocol):
    def invoke(self, system: str, user: str, schema: dict) -> dict: ...


class OllamaLLM:
    """GPU 서버 Ollama (gpt-oss:20b). format=JSON 스키마로 구조화 출력, temperature=0."""

    def __init__(self, model: str = settings.llm_model, base: str = settings.ollama_base_url,
                 timeout: float = settings.llm_timeout_sec, think: str = settings.llm_think,
                 keep_alive: str = settings.llm_keep_alive):
        self.model, self.base, self.timeout, self.think = model, base.rstrip("/"), timeout, think
        self.keep_alive = keep_alive

    def invoke(self, system: str, user: str, schema: dict) -> dict:
        body = {"model": self.model, "stream": False, "format": schema, "keep_alive": self.keep_alive,
                "options": {"temperature": 0},
                "messages": [{"role": "system",
                              "content": system + "\n\n출력 JSON 스키마:\n" + json.dumps(schema, ensure_ascii=False)},
                             {"role": "user", "content": user}]}
        if self.think:
            body["think"] = self.think
        r = httpx.post(f"{self.base}/api/chat", timeout=self.timeout, json=body)
        r.raise_for_status()
        return json.loads(r.json()["message"]["content"])

    def ping(self) -> bool:
        try:
            r = httpx.get(f"{self.base}/api/tags", timeout=3)
            return any(m.get("name", "").startswith(self.model) for m in r.json().get("models", []))
        except Exception:
            return False


def make_llm():
    return OllamaLLM() if settings.llm_provider == "ollama" else None
