"""음성 -> 텍스트 (FR-02). faster-whisper 로컬 실행. 모델은 서버 시작 시 1회만 로드한다."""
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
    """faster-whisper. GPU 여유 4GB 이상이면 large-v3-turbo/cuda/float16, 아니면 small, medium/cpu/int8."""

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
            # 제너레이터: 순회해야 인식이 실행됨. 말소리가 아니라고 본 구간은 버린다
            parts = [s.text.strip() for s in segments if not is_non_speech(s)]
        return clean_transcript(" ".join(parts))


# Whisper 가 무음, 잡음에서 지어내는 자막식 문장 (한국어 학습 데이터 영향). 이것만 나오면 인식 실패로 본다
HALLUCINATIONS = ("시청해 주셔서 감사합니다", "시청해주셔서 감사합니다", "구독과 좋아요", "구독 좋아요", "감사합니다",
                  "고맙습니다", "MBC 뉴스", "자막 제공", "다음 영상에서 만나요")


def is_non_speech(seg) -> bool:
    """Whisper 기준(no_speech_prob > 0.6 이고 평균 log 확률 < -1)으로 말소리가 아닌 구간."""
    return getattr(seg, "no_speech_prob", 0.0) > 0.6 and getattr(seg, "avg_logprob", 0.0) < -1.0


def clean_transcript(text: str) -> str:
    """지어낸 문장만 남았으면 "" (VR 이 '인식하지 못했어요' 와 기본 창고 설정을 보여 줌)."""
    t = text.strip()
    core = t.strip(" .,!?~").replace(" ", "")
    if not core or any(core == h.replace(" ", "") for h in HALLUCINATIONS):
        return ""
    if INITIAL_PROMPT.replace(" ", "") in t.replace(" ", ""):   # 안내 단어 목록을 그대로 따라 읽은 경우
        return ""
    return t


def make_stt():
    return WhisperSTT() if settings.stt_provider == "faster-whisper" else None
