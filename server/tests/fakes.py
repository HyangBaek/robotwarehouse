"""ENV-1용 가짜 외부 서비스."""
import json
from pathlib import Path

FIX = Path(__file__).parent / "fixtures"


def load_llm(name: str) -> list:
    return json.loads((FIX / "llm" / name).read_text(encoding="utf-8"))


class FakeLLM:
    def __init__(self, replies: list):
        self.replies = list(replies)

    def invoke(self, *args, **kwargs):
        return self.replies.pop(0)


class FakeSTT:
    def __init__(self, table: dict[str, str]):
        self.table = table

    def transcribe(self, audio: bytes, filename: str = "audio.wav") -> str:
        return self.table.get(filename, "")
