CREATE TABLE IF NOT EXISTS maps (
  map_version TEXT PRIMARY KEY,
  session_id  TEXT,
  map_json    TEXT NOT NULL,
  requirements_json TEXT,
  valid       INTEGER,
  confirmed   INTEGER DEFAULT 0,
  created_at  TEXT DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sims (
  sim_id      TEXT PRIMARY KEY,
  map_version TEXT REFERENCES maps(map_version),
  scenario_json TEXT NOT NULL,
  strategy    TEXT,
  total_steps INTEGER,
  collisions  INTEGER,
  plan_ms     REAL,
  created_at  TEXT DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS frames (
  sim_id TEXT, t INTEGER, robots_json TEXT,
  PRIMARY KEY (sim_id, t)
);
CREATE TABLE IF NOT EXISTS orders (
  sim_id TEXT, order_id TEXT, robot_id INTEGER, assigned_t INTEGER, done_t INTEGER,
  PRIMARY KEY (sim_id, order_id)
);
CREATE TABLE IF NOT EXISTS cell_stats (
  sim_id TEXT, x INTEGER, y INTEGER, pass INTEGER, wait INTEGER,
  PRIMARY KEY (sim_id, x, y)
);
CREATE TABLE IF NOT EXISTS approvals (
  session_id TEXT, proposal_id TEXT, type TEXT, approved INTEGER,
  before_sim TEXT, after_sim TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP
);
