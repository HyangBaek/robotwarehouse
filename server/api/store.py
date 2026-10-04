"""세션, 지도, 시뮬레이션 저장소. 진행 중인 세션은 메모리에 두고, 지도와 시뮬레이션 기록은 SQLite(db/repo.py)에도 남긴다.
앱(create_app)마다 하나씩 만든다. 메모리에 없는 시뮬레이션은 DB에서 다시 불러온다 (서버 재시작 뒤 기록 보기)."""
from __future__ import annotations

import itertools
import json
import logging
from dataclasses import dataclass, field
from typing import Any

from fastapi import WebSocket

log = logging.getLogger("api")


@dataclass
class MapEntry:
    version: str
    map: dict[str, Any]
    req: dict[str, Any]
    confirmed: bool = False


@dataclass
class SimEntry:
    sim_id: str
    session_id: str
    map_version: str
    scenario: dict[str, Any]
    strategy: str
    log: dict[str, Any]
    sim: Any = None                                   # planner.Sim (롤링 재계획용)
    events: list[dict[str, Any]] = field(default_factory=list)
    proposals: dict[str, dict[str, Any]] = field(default_factory=dict)
    baseline_log: dict[str, Any] | None = None       # 같은 지도, 주문, 이벤트의 기준 전략 결과 (비교, 리포트 공용)
    report: dict[str, Any] | None = None


@dataclass
class Session:
    session_id: str
    sockets: list[WebSocket] = field(default_factory=list)
    req: dict[str, Any] = field(default_factory=dict)
    question_id: str | None = None
    question_text: str | None = None
    question_count: int = 0


class Store:
    def __init__(self, repo=None):
        self.repo = repo
        seq, map_seq = repo.next_ids() if repo is not None else (1, 1)
        self._ids = itertools.count(seq)
        self._map_ids = itertools.count(map_seq)
        self.sessions: dict[str, Session] = {}
        self.maps: dict[str, MapEntry] = {}
        self.map_owner: dict[str, str] = {}
        self.sims: dict[str, SimEntry] = {}
        self.questions: dict[str, str] = {}
        self.proposals: dict[str, str] = {}

    def new_id(self, prefix: str) -> str:
        return f"{prefix}{next(self._ids)}"

    def session(self, sid: str | None) -> Session:
        sid = sid or "default"
        if sid not in self.sessions:
            self.sessions[sid] = Session(sid)
        return self.sessions[sid]

    def add_map(self, s: Session, m: dict, req: dict, confirmed: bool = False, summary: str = "") -> str:
        version = str(next(self._map_ids))
        self.maps[version] = MapEntry(version, m, req, confirmed)
        self.map_owner[version] = s.session_id
        if self.repo is not None:
            self.repo.save_map(version, s.session_id, m, req, summary, confirmed)
        return version

    def confirm_map(self, version: str):
        self.maps[version].confirmed = True
        if self.repo is not None:
            self.repo.confirm_map(version)

    def get_map(self, version: str) -> MapEntry | None:
        """메모리에 없으면 DB에서 불러온다."""
        if version not in self.maps and self.repo is not None:
            row = self.repo.get_map(version)
            if row is not None:
                self.maps[version] = MapEntry(version, row["map"], row["req"], row["confirmed"])
                self.map_owner[version] = row["session_id"]
        return self.maps.get(version)

    def get_sim(self, sim_id: str) -> SimEntry | None:
        """메모리에 없으면 DB 기록으로 SimEntry 를 만든다 (롤링 재계획용 엔진 상태는 필요할 때 다시 만든다)."""
        if sim_id in self.sims or self.repo is None or not sim_id:
            return self.sims.get(sim_id)
        row = self.repo.get_sim(sim_id)
        if row is None or self.get_map(row["map_version"]) is None:
            return None
        e = SimEntry(sim_id, row["session_id"], row["map_version"], row["scenario"], row["strategy"], row["log"],
                     None, row["events"])
        e.report = self.repo.get_report(sim_id)
        self.sims[sim_id] = e
        return e

    def new_sim(self, sim_id: str, session_id: str, map_version: str, scenario: dict, strategy: str,
                log_: dict, sim=None) -> SimEntry:
        e = SimEntry(sim_id, session_id, map_version, scenario, strategy, log_, sim)
        self.sims[sim_id] = e
        return e

    def save_sim(self, e: SimEntry, plan_ms: float | None = None):
        if self.repo is not None:
            self.repo.save_sim(e.sim_id, e.session_id, e.map_version, e.scenario, e.strategy, e.log, e.events, plan_ms)

    async def emit(self, s: Session, msg: dict[str, Any]):
        text = json.dumps(msg, ensure_ascii=False)
        log.info("WS -> %s %s", s.session_id, text[:160])
        for ws in list(s.sockets):
            try:
                await ws.send_text(text)
            except Exception:
                if ws in s.sockets:
                    s.sockets.remove(ws)
