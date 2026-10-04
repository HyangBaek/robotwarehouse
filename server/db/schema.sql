-- 로봇웨어하우스 SQLite 스키마 (DR-03). 버전은 PRAGMA user_version 으로 관리한다 (db/repo.py SCHEMA_VERSION).
CREATE TABLE IF NOT EXISTS maps (
  map_version TEXT PRIMARY KEY,
  session_id  TEXT,
  map_json    TEXT NOT NULL,
  requirements_json TEXT,
  summary     TEXT,
  confirmed   INTEGER DEFAULT 0,
  created_at  TEXT DEFAULT (datetime('now', 'localtime'))
);
CREATE TABLE IF NOT EXISTS sims (
  sim_id      TEXT PRIMARY KEY,
  session_id  TEXT,
  map_version TEXT REFERENCES maps(map_version),
  scenario_json TEXT NOT NULL,
  strategy    TEXT,
  seed        INTEGER,
  total_steps INTEGER,
  orders_done INTEGER,
  orders_total INTEGER,
  collisions  INTEGER,
  completed   INTEGER,
  meta_json   TEXT,
  events_json TEXT,
  plan_ms     REAL,
  created_at  TEXT DEFAULT (datetime('now', 'localtime')),
  updated_at  TEXT DEFAULT (datetime('now', 'localtime'))
);
-- 로봇 동선: 스텝마다 전체 로봇 위치 {id, x, y, state, task_id}
CREATE TABLE IF NOT EXISTS frames (
  sim_id TEXT, t INTEGER, robots_json TEXT,
  PRIMARY KEY (sim_id, t)
);
CREATE TABLE IF NOT EXISTS orders (
  sim_id TEXT, order_id TEXT, type TEXT, spec TEXT, qty INTEGER, robot_id TEXT,
  arrival_t INTEGER, assigned_t INTEGER, done_t INTEGER, status TEXT,
  PRIMARY KEY (sim_id, order_id)
);
CREATE TABLE IF NOT EXISTS cell_stats (
  sim_id TEXT, x INTEGER, y INTEGER, pass INTEGER, wait INTEGER,
  PRIMARY KEY (sim_id, x, y)
);
CREATE TABLE IF NOT EXISTS reports (
  sim_id TEXT PRIMARY KEY, report_json TEXT NOT NULL,
  created_at TEXT DEFAULT (datetime('now', 'localtime'))
);
CREATE TABLE IF NOT EXISTS approvals (
  session_id TEXT, proposal_id TEXT, type TEXT, text TEXT,
  before_sim TEXT, after_sim TEXT, created_at TEXT DEFAULT (datetime('now', 'localtime'))
);
CREATE INDEX IF NOT EXISTS idx_sims_session ON sims(session_id, created_at);
