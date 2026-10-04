"""SQLite 저장, 조회 (DR-03, B7). 다른 모듈은 SQL을 직접 쓰지 않고 이 모듈만 사용한다.

저장 대상: 지도 버전, 시나리오, 시뮬레이션 로그(로봇 동선 프레임, 주문 처리 기록, 칸별 통과와 대기), 리포트, 개선안 승인.
여러 스레드(asyncio.to_thread)에서 부르므로 연결 하나를 잠금으로 보호한다.
"""
import json
import re
import sqlite3
import threading
from pathlib import Path

SCHEMA = Path(__file__).with_name("schema.sql")
SCHEMA_VERSION = 2


class Repo:
    def __init__(self, db_path: str):
        Path(db_path).parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(db_path, check_same_thread=False)
        self.conn.row_factory = sqlite3.Row
        self.lock = threading.Lock()
        with self.lock:
            ver = self.conn.execute("PRAGMA user_version").fetchone()[0]
            if ver != SCHEMA_VERSION:                  # 이전 스키마(개발용 데이터)는 지우고 새로 만든다
                for (name,) in self.conn.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall():
                    if not name.startswith("sqlite_"):
                        self.conn.execute(f"DROP TABLE IF EXISTS {name}")
            self.conn.executescript(SCHEMA.read_text(encoding="utf-8"))
            self.conn.execute(f"PRAGMA user_version = {SCHEMA_VERSION}")
            self.conn.commit()

    # ------------------------------------------------------------------ 식별자
    def next_ids(self) -> tuple[int, int]:
        """(다음 일련번호, 다음 지도 버전). 서버를 다시 켜도 이전 기록과 겹치지 않게 한다."""
        with self.lock:
            sims = [r[0] for r in self.conn.execute("SELECT sim_id FROM sims")]
            maps = [r[0] for r in self.conn.execute("SELECT map_version FROM maps")]
        num = lambda s: int(m.group()) if (m := re.search(r"\d+", s or "")) else 0
        return max(map(num, sims), default=0) + 1, max(map(num, maps), default=0) + 1

    # ------------------------------------------------------------------ 지도
    def save_map(self, version: str, session_id: str, grid: dict, req: dict, summary: str, confirmed: bool = False):
        with self.lock:
            self.conn.execute(
                "INSERT OR REPLACE INTO maps (map_version, session_id, map_json, requirements_json, summary, confirmed) "
                "VALUES (?, ?, ?, ?, ?, ?)",
                (version, session_id, json.dumps(grid), json.dumps(req, ensure_ascii=False), summary, int(confirmed)))
            self.conn.commit()

    def confirm_map(self, version: str):
        with self.lock:
            self.conn.execute("UPDATE maps SET confirmed = 1 WHERE map_version = ?", (version,))
            self.conn.commit()

    def get_map(self, version: str) -> dict | None:
        with self.lock:
            r = self.conn.execute("SELECT * FROM maps WHERE map_version = ?", (version,)).fetchone()
        if r is None:
            return None
        return {"map_version": r["map_version"], "session_id": r["session_id"], "map": json.loads(r["map_json"]),
                "req": json.loads(r["requirements_json"] or "{}"), "summary": r["summary"],
                "confirmed": bool(r["confirmed"])}

    # ------------------------------------------------------------------ 시뮬레이션
    def save_sim(self, sim_id: str, session_id: str, map_version: str, scenario: dict, strategy: str,
                 log: dict, events: list | None = None, plan_ms: float | None = None):
        """같은 sim_id 가 있으면 덮어쓴다 (롤링 재계획 뒤 다시 저장)."""
        meta = {k: log.get(k) for k in ("seed", "completed", "collisions")}
        meta["meta"] = {k: v for k, v in log.get("meta", {}).items() if k != "config"}
        rows_f = [(sim_id, f["t"], json.dumps(f["robots"])) for f in log["frames"]]
        rows_o = [(sim_id, o["order_id"], o.get("type"), o.get("spec"), o.get("qty", 1), o.get("robot_id"),
                   o.get("arrival_t"), o.get("assigned_t"), o.get("done_t"), o.get("status")) for o in log["orders"]]
        rows_c = [(sim_id, c["x"], c["y"], c.get("pass", 0), c.get("wait", 0)) for c in log.get("cell_stats", [])]
        with self.lock:
            cur = self.conn.cursor()
            for table in ("frames", "orders", "cell_stats", "reports"):
                cur.execute(f"DELETE FROM {table} WHERE sim_id = ?", (sim_id,))
            cur.execute(
                "INSERT OR REPLACE INTO sims (sim_id, session_id, map_version, scenario_json, strategy, seed, total_steps, "
                "orders_done, orders_total, collisions, completed, meta_json, events_json, plan_ms, updated_at) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now', 'localtime'))",
                (sim_id, session_id, map_version, json.dumps(scenario, ensure_ascii=False), strategy, log.get("seed"),
                 log["total_steps"], log.get("meta", {}).get("orders_done"), len(log["orders"]),
                 len(log.get("collisions", [])), int(bool(log.get("completed"))), json.dumps(meta),
                 json.dumps(events or []), plan_ms))
            cur.executemany("INSERT INTO frames VALUES (?, ?, ?)", rows_f)
            cur.executemany("INSERT INTO orders VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", rows_o)
            cur.executemany("INSERT INTO cell_stats VALUES (?, ?, ?, ?, ?)", rows_c)
            self.conn.commit()

    def list_sims(self, session_id: str | None = None, limit: int = 30) -> list[dict]:
        q = ("SELECT s.sim_id, s.session_id, s.map_version, s.scenario_json, s.strategy, s.total_steps, s.orders_done, "
             "s.orders_total, s.collisions, s.completed, s.events_json, s.created_at, s.updated_at, m.summary "
             "FROM sims s LEFT JOIN maps m ON m.map_version = s.map_version")
        args: tuple = ()
        if session_id:
            q += " WHERE s.session_id = ?"
            args = (session_id,)
        q += " ORDER BY s.created_at DESC, s.rowid DESC LIMIT ?"
        with self.lock:
            rows = self.conn.execute(q, args + (limit,)).fetchall()
        out = []
        for r in rows:
            sc = json.loads(r["scenario_json"])
            out.append({"sim_id": r["sim_id"], "session_id": r["session_id"], "map_version": r["map_version"],
                        "map_summary": r["summary"], "strategy": r["strategy"], "robots": sc.get("robots"),
                        "inbound": sc.get("inbound"), "outbound": sc.get("outbound"),
                        "spec_b_pct": sc.get("spec_b_pct", 0), "total_steps": r["total_steps"],
                        "orders_done": r["orders_done"], "orders_total": r["orders_total"],
                        "collisions": r["collisions"], "completed": bool(r["completed"]),
                        "events": len(json.loads(r["events_json"] or "[]")),
                        "created_at": r["created_at"], "updated_at": r["updated_at"]})
        return out

    def get_sim(self, sim_id: str) -> dict | None:
        """{"sim_id", "session_id", "map_version", "scenario", "strategy", "events", "log"} (log 은 DR-03 형식)."""
        with self.lock:
            s = self.conn.execute("SELECT * FROM sims WHERE sim_id = ?", (sim_id,)).fetchone()
            if s is None:
                return None
            frames = self.conn.execute("SELECT t, robots_json FROM frames WHERE sim_id = ? ORDER BY t", (sim_id,)).fetchall()
            orders = self.conn.execute("SELECT * FROM orders WHERE sim_id = ? ORDER BY order_id", (sim_id,)).fetchall()
            cells = self.conn.execute("SELECT x, y, pass, wait FROM cell_stats WHERE sim_id = ? ORDER BY x, y",
                                      (sim_id,)).fetchall()
        meta = json.loads(s["meta_json"] or "{}")
        log = {"strategy": s["strategy"], "map_version": s["map_version"], "seed": meta.get("seed"),
               "total_steps": s["total_steps"], "completed": bool(meta.get("completed")),
               "frames": [{"t": f["t"], "robots": json.loads(f["robots_json"])} for f in frames],
               "orders": [{k: o[k] for k in ("order_id", "type", "spec", "qty", "robot_id", "arrival_t", "assigned_t",
                                             "done_t", "status")} for o in orders],
               "cell_stats": [{"x": c["x"], "y": c["y"], "pass": c["pass"], "wait": c["wait"]} for c in cells],
               "collisions": meta.get("collisions") or [], "meta": meta.get("meta") or {}}
        return {"sim_id": s["sim_id"], "session_id": s["session_id"], "map_version": s["map_version"],
                "scenario": json.loads(s["scenario_json"]), "strategy": s["strategy"],
                "events": json.loads(s["events_json"] or "[]"), "log": log}

    # ------------------------------------------------------------------ 리포트, 개선안 승인
    def save_report(self, sim_id: str, report: dict):
        with self.lock:
            self.conn.execute("INSERT OR REPLACE INTO reports (sim_id, report_json) VALUES (?, ?)",
                              (sim_id, json.dumps(report, ensure_ascii=False)))
            self.conn.commit()

    def get_report(self, sim_id: str) -> dict | None:
        with self.lock:
            r = self.conn.execute("SELECT report_json FROM reports WHERE sim_id = ?", (sim_id,)).fetchone()
        return json.loads(r[0]) if r else None

    def save_approval(self, session_id: str, proposal: dict, before_sim: str, after_sim: str):
        with self.lock:
            self.conn.execute("INSERT INTO approvals (session_id, proposal_id, type, text, before_sim, after_sim) "
                              "VALUES (?, ?, ?, ?, ?, ?)",
                              (session_id, proposal.get("proposal_id"), proposal.get("type"), proposal.get("text"),
                               before_sim, after_sim))
            self.conn.commit()
