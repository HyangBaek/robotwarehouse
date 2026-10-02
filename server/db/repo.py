"""SQLite 저장·조회 (B7). 다른 모듈은 SQL을 직접 쓰지 않고 이 모듈만 사용한다."""
import sqlite3
from pathlib import Path

SCHEMA = Path(__file__).with_name("schema.sql")


class Repo:
    def __init__(self, db_path: str):
        Path(db_path).parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(db_path, check_same_thread=False)
        self.conn.executescript(SCHEMA.read_text(encoding="utf-8"))

    # TODO(방유진): save_map / get_map / confirm_map
    # TODO(하재윤): save_sim / get_frames(sim_id, t_from, t_to) / get_stats(sim_id)
