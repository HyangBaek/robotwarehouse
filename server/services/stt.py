"""음성 → 텍스트 (FR-02). faster-whisper 로컬 실행. 모델은 서버 시작 시 1회만 로드한다."""
import io
import logging
import threading
import wave
from typing import Protocol

from config import settings

log = logging.getLogger("stt")

INITIAL_PROMPT = "랙, 통로 폭, 입하 도크, 출하 도크, 충전 구역, 일방통행"


class STT(Protocol):
    def transcribe(self, audio: bytes, filename: str = "audio.wav") -> str: ...


class WhisperSTT:
    """faster-whisper. GPU 여유 4GB 이상이면 large-v3-turbo/cuda/float16, 아니면 small·medium/cpu/int8."""

    def __init__(self, model: str = settings.stt_model, device: str = settings.stt_device,
                 compute_type: str = settings.stt_compute_type):
        self.model_name, self.device, self.compute_type = model, device, compute_type
        self._model = None
        self._lock = threading.Lock()

    def load(self):
        if self._model is None:
            from faster_whisper import WhisperModel
            log.info("STT 모델 로드: %s (%s, %s)", self.model_name, self.device, self.compute_type)
            self._model = WhisperModel(self.model_name, device=self.device, compute_type=self.compute_type)
        return self._model

    @staticmethod
    def _decode(audio: bytes):
        """16bit PCM WAV 는 numpy 로 직접 16kHz mono float32 로 바꾼다 (PyAV 버전 충돌 회피). 그 외는 원본 그대로."""
        import numpy as np
        try:
            with wave.open(io.BytesIO(audio)) as w:
                ch, rate, width = w.getnchannels(), w.getframerate(), w.getsampwidth()
                pcm = w.readframes(w.getnframes())
        except Exception:
            return io.BytesIO(audio)
        if width != 2:
            return io.BytesIO(audio)
        x = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768.0
        if ch > 1:
            x = x.reshape(-1, ch).mean(axis=1)
        if rate != 16000 and len(x):
            n = int(len(x) * 16000 / rate)
            x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x).astype(np.float32)
        return x

    def transcribe(self, audio: bytes, filename: str = "audio.wav") -> str:
        """빈 결과면 "" 반환 (EX-03). 블로킹 함수이므로 FastAPI 에서는 run_in_threadpool 로 호출."""
        model = self.load()
        with self._lock:                                 # 모델 1개를 여러 요청이 동시에 쓰지 않게
            segments, _ = model.transcribe(self._decode(audio), language="ko", vad_filter=True,
                                           beam_size=5, initial_prompt=INITIAL_PROMPT)
            text = " ".join(s.text.strip() for s in segments)   # 제너레이터: 순회해야 인식이 실행됨
        return text.strip()


def make_stt():
    return WhisperSTT() if settings.stt_provider == "faster-whisper" else None
