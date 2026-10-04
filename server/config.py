"""환경 변수 로드. 키 값은 코드에 쓰지 않는다 (NFR-10)."""
import os
from dataclasses import dataclass

try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    pass


@dataclass(frozen=True)
class Settings:
    llm_provider: str = os.getenv("LLM_PROVIDER", "ollama")          # ollama | none(정규식만)
    llm_model: str = os.getenv("LLM_MODEL", "gpt-oss:20b")
    ollama_base_url: str = os.getenv("OLLAMA_BASE_URL", "http://localhost:11434")
    llm_think: str = os.getenv("LLM_THINK", "low")                     # gpt-oss 추론 강도 low|medium|high, 빈 값이면 생략
    llm_timeout_sec: float = float(os.getenv("LLM_TIMEOUT_SEC", "8"))
    llm_keep_alive: str = os.getenv("LLM_KEEP_ALIVE", "60m")             # 요청마다 모델을 GPU에 유지할 시간 (-1 = 계속)
    stt_provider: str = os.getenv("STT_PROVIDER", "faster-whisper")   # 음성 인식: faster-whisper 사용, none 이면 끔
    stt_model: str = os.getenv("STT_MODEL", "small")                  # 모델 크기: large-v3-turbo, medium, small 중 선택
    stt_device: str = os.getenv("STT_DEVICE", "cpu")                  # cuda | cpu
    stt_compute_type: str = os.getenv("STT_COMPUTE_TYPE", "int8")     # 연산 정밀도: GPU(cuda)는 float16, CPU는 int8
    db_path: str = os.getenv("DB_PATH", "data/warehouse.db")
    node_delay_sec: float = float(os.getenv("NODE_DELAY_SEC", "0.3"))  # Agent 단계 사이 연출 지연
    max_questions: int = 3
    max_llm_retry: int = 2


settings = Settings()
