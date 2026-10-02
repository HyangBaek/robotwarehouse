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
    openai_api_key: str = os.getenv("OPENAI_API_KEY", "")
    llm_model: str = os.getenv("LLM_MODEL", "")
    db_path: str = os.getenv("DB_PATH", "data/warehouse.db")
    llm_timeout_sec: float = float(os.getenv("LLM_TIMEOUT_SEC", "20"))
    max_questions: int = 3
    max_llm_retry: int = 2


settings = Settings()
