"""음성 → 텍스트 (FR-02)."""
from typing import Protocol


class STT(Protocol):
    def transcribe(self, audio: bytes, filename: str = "audio.wav") -> str: ...


class WhisperSTT:
    def transcribe(self, audio: bytes, filename: str = "audio.wav") -> str:
        # TODO(방유진): Whisper 호출, 빈 결과면 "" 반환 (EX-03)
        raise NotImplementedError
