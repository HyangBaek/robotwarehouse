"""세션·지도·시뮬레이션 메모리 저장소. 앱(create_app)마다 하나씩 만든다."""
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


@dataclass
class Session:
    session_id: str
    sockets: list[WebSocket] = field(default_factory=list)
    req: dict[str, Any] = field(default_factory=dict)
    question_id: str | None = None
    question_text: str | None = None
    question_count: int = 0


class Store:
    def __init__(self):
        self._ids = itertools.count(1)
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

    def add_map(self, s: Session, m: dict, req: dict, confirmed: bool = False) -> str:
        version = str(len(self.maps) + 1)
        self.maps[version] = MapEntry(version, m, req, confirmed)
        self.map_owner[version] = s.session_id
        return version

    async def emit(self, s: Session, msg: dict[str, Any]):
        text = json.dumps(msg, ensure_ascii=False)
        log.info("WS -> %s %s", s.session_id, text[:160])
        for ws in list(s.sockets):
            try:
                await ws.send_text(text)
            except Exception:
                if ws in s.sockets:
                    s.sockets.remove(ws)
