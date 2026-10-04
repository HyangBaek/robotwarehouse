"""SQLite 저장소 (DR-03): 저장, 복원, 일련번호, 스키마 버전."""
import sqlite3

from db.repo import SCHEMA_VERSION, Repo
from planner import simulate
from tools.map_generator import generate_grid_map


def test_roundtrip_and_ids(tmp_path):
    db = tmp_path / "w.db"
    r = Repo(str(db))
    m, _ = generate_grid_map({"use_defaults": True})
    r.save_map("3", "s1", m, {"racks": 6}, "요약")
    log = simulate(m, {"map_version": "3", "robots": 3, "inbound": 4, "outbound": 4}, "optimized", 1)
    r.save_sim("S9", "s1", "3", {"robots": 3, "inbound": 4, "outbound": 4}, "optimized", log, [{"t": 5}], 12.5)
    r.save_report("S9", {"kpis": {"total_steps": log["total_steps"]}})
    got = Repo(str(db)).get_sim("S9")                          # 다시 열어도 그대로
    assert got["log"]["frames"] == log["frames"] and got["log"]["total_steps"] == log["total_steps"]
    assert [o["order_id"] for o in got["log"]["orders"]] == [o["order_id"] for o in log["orders"]]
    assert got["log"]["cell_stats"] == log["cell_stats"] and got["events"] == [{"t": 5}]
    assert r.get_report("S9")["kpis"]["total_steps"] == log["total_steps"]
    assert r.next_ids() == (10, 4)
    row = r.list_sims("s1")[0]
    assert row["sim_id"] == "S9" and row["map_summary"] == "요약" and row["completed"]


def test_old_schema_is_replaced(tmp_path):
    db = tmp_path / "old.db"
    c = sqlite3.connect(db)
    c.execute("CREATE TABLE sims (sim_id TEXT)")
    c.commit()
    c.close()
    Repo(str(db))
    assert sqlite3.connect(db).execute("PRAGMA user_version").fetchone()[0] == SCHEMA_VERSION
