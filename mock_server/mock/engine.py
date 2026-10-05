"""경로 계산 엔진 모의 구현 (하재윤 담당 엔진의 입출력 계약만 맞춘 참고용).

simulate(map, scenario, strategy, seed, events) -> log (DR-03)
- optimized: 보관 위치 선정(도크에 가까운 칸) + 가장 가까운 유휴 로봇 + 가까운 도크 + 시공간 A* 우선순위 계획
- baseline : 무작위 보관 + 선착순 할당 + 첫 번째 도크 고정 + 같은 충돌 회피 계획(비교 공정성 위해 충돌 0 유지)
충돌 0건은 예약 테이블(칸, 맞교환, 정지 로봇 영구 예약)로 보장하고, check_collisions()로 다시 검사한다.
"""
from __future__ import annotations

import heapq
import random
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Any

from .validator import PASSABLE, grid_of, neighbors

WORK_STEPS = 2
MAX_STEPS = 6000
ARRIVAL_INTERVAL = 2


@dataclass
class Order:
    order_id: str
    kind: str  # 주문 유형: inbound(입하) 또는 outbound(출하)
    spec: str
    arrival: int
    robot_id: str | None = None
    assigned_t: int | None = None
    done_t: int | None = None
    queued: bool = False


@dataclass
class Robot:
    rid: str
    pos: tuple[int, int]
    free_at: int = 0                     # 이 시각부터 새 계획 가능
    legs: deque = field(default_factory=deque)  # 구간 정보: (목표 칸, 작업 상태, 적재 이동 여부, 주문)
    order: Order | None = None
    parked: bool = False
    fails: int = 0
    retry_at: int = 0


class Sim:
    def __init__(self, m: dict[str, Any], scenario: dict[str, Any], strategy: str, seed: int):
        self.m = m
        self.g = grid_of(m)
        self.strategy = strategy
        self.rng = random.Random(seed)
        self.W, self.H = m["width"], m["height"]
        self.passable = {p for p, t in self.g.items() if t in PASSABLE}
        self.dock_in = sorted(p for p, t in self.g.items() if t == "dock_in")
        self.dock_out = sorted(p for p, t in self.g.items() if t == "dock_out")
        self.one_way: dict[tuple[int, int], str] = {}
        for seg in (m.get("rules") or {}).get("one_way", []):
            (fx, fy), (tx, ty) = seg["from"], seg["to"]
            for x in range(min(fx, tx), max(fx, tx) + 1):
                for y in range(min(fy, ty), max(fy, ty) + 1):
                    self.one_way[(x, y)] = seg.get("dir", "N")
        levels = {r["rack_id"]: r.get("levels", 1) for r in m.get("racks", [])}
        self.rack_cells: dict[tuple[int, int], int] = {}
        self.access: dict[tuple[int, int], tuple[int, int]] = {}
        rack_ids = {(c["x"], c["y"]): c.get("rack_id") for c in m["cells"] if c["type"] == "rack"}
        for p, t in self.g.items():
            if t != "rack":
                continue
            acc = [n for n in neighbors(*p) if self.g.get(n) == "aisle"] or [n for n in neighbors(*p) if n in self.passable]
            if acc:
                self.rack_cells[p] = levels.get(rack_ids.get(p), 1)
                self.access[p] = sorted(acc)[0]
        self.stock: dict[tuple[int, int], int] = {p: 0 for p in self.rack_cells}
        self.reserved_cap: dict[tuple[int, int], int] = {p: 0 for p in self.rack_cells}
        work_cells = set(self.access.values()) | set(self.dock_in) | set(self.dock_out)
        docks = self.dock_in + self.dock_out
        near_dock = {p for p in self.passable if any(abs(p[0] - d[0]) + abs(p[1] - d[1]) <= 3 for d in docks)}
        charge = sorted(p for p, t in self.g.items() if t == "charge")
        others = sorted(p for p in self.passable if self.g[p] == "aisle" and p not in work_cells and p not in near_dock
                        and p not in self.one_way)
        self.parking = charge + others
        self.charge_set = set(charge)
        self.parking_set = set(self.parking)
        self.dist_cache: dict[tuple[int, int], dict] = {}

        # 예약 테이블
        self.vres: dict[tuple[tuple[int, int], int], str] = {}
        self.eres: dict[tuple[tuple[int, int], tuple[int, int], int], str] = {}
        self.perm: dict[tuple[int, int], tuple[str, int]] = {}
        self.cell_last: dict[tuple[int, int], dict[str, int]] = {}
        self.traj: dict[str, dict[int, tuple[tuple[int, int], str, str | None]]] = {}
        self.spawn_info: dict[str, tuple[int, tuple[int, int]]] = {}

        spec = scenario.get("spec", "pallet_1100x1100")
        self.orders: list[Order] = []
        self.pending: deque[Order] = deque()
        self.robots: list[Robot] = []
        self.next_order = 0
        self._add_orders(int(scenario.get("inbound", 0)), int(scenario.get("outbound", 0)), 0, spec)
        self._spawn_robots(int(scenario.get("robots", 1)), 0)
        self.spec = spec
        self.stats_pass: dict[tuple[int, int], int] = {p: 0 for p in self.passable}
        self.stats_wait: dict[tuple[int, int], int] = {p: 0 for p in self.passable}

    # ---------------------------------------------------------------- 준비
    def _add_orders(self, n_in: int, n_out: int, t0: int, spec: str):
        kinds = []
        i = o = 0
        while i < n_in or o < n_out:
            if i < n_in:
                kinds.append("inbound"); i += 1
            if o < n_out:
                kinds.append("outbound"); o += 1
        stocked_slots = [p for p in sorted(self.rack_cells)]
        for k, kind in enumerate(kinds):
            self.next_order += 1
            oid = f"O{self.next_order:03d}"
            self.orders.append(Order(oid, kind, spec, t0 + k * ARRIVAL_INTERVAL))
            if kind == "outbound":  # 출하할 재고를 미리 채워 둔다
                cand = [p for p in stocked_slots if self.stock[p] + self.reserved_cap[p] < self.rack_cells[p]]
                if cand:
                    self.stock[self.rng.choice(cand)] += 1

    def _spawn_robots(self, count: int, t: int):
        while len(self.robots) < count:
            rid = f"R{len(self.robots) + 1}"
            spot = next((p for p in self.parking if self._free_forever(p, t, rid)), None)
            if spot is None:
                break
            r = Robot(rid, spot, free_at=t, parked=True)
            self.robots.append(r)
            self.traj[rid] = {}
            self.spawn_info[rid] = (t, spot)
            self._set_perm(spot, rid, t)

    # ---------------------------------------------------------------- 예약
    def _set_perm(self, cell, rid, t):
        self.perm[cell] = (rid, t)

    def _clear_perm(self, rid):
        for c, (who, _) in list(self.perm.items()):
            if who == rid:
                del self.perm[c]

    def _blocked(self, cell, t, rid) -> bool:
        who = self.vres.get((cell, t))
        if who is not None and who != rid:
            return True
        p = self.perm.get(cell)
        return p is not None and p[0] != rid and p[1] <= t

    def _free_forever(self, cell, t, rid) -> bool:
        p = self.perm.get(cell)
        if p is not None and p[0] != rid:
            return False
        for who, last in self.cell_last.get(cell, {}).items():
            if who != rid and last >= t:
                return False
        return True

    def _move_ok(self, a, b):
        d = self.one_way.get(a)
        if d and b in self.one_way:
            dy = b[1] - a[1]
            dx = b[0] - a[0]
            if d == "N" and dy < 0: return False
            if d == "S" and dy > 0: return False
            if d == "E" and dx < 0: return False
            if d == "W" and dx > 0: return False
        return True

    def dist(self, goal) -> dict:
        if goal in self.dist_cache:
            return self.dist_cache[goal]
        d = {goal: 0}
        q = deque([goal])
        while q:
            c = q.popleft()
            for n in neighbors(*c):
                if n in self.passable and n not in d and not (c in self.charge_set and n in self.charge_set):
                    d[n] = d[c] + 1
                    if n not in self.charge_set:   # 충전 칸은 출발, 도착만 가능 (지나가는 길로 쓰지 않음)
                        q.append(n)
        self.dist_cache[goal] = d
        return d

    # ---------------------------------------------------------------- 시공간 A*
    def plan(self, rid, start, t0, goal, work):
        """충전 칸은 통로에서 목표로만 들어가고 통로로 나온다 (지나가는 길로 쓰지 않고, 충전 칸끼리 옮겨 가지 않음)."""
        h = self.dist(goal)
        if start not in h:
            return None
        p = self.perm.get(goal)
        if p is not None and p[0] != rid:
            return None  # 목표 칸에 다른 로봇이 서 있음 -> 다음 스텝에 다시 시도
        busy_until = max([v for who, v in self.cell_last.get(goal, {}).items() if who != rid] + [t0])
        horizon = max(t0 + 2 * h[start] + 40, busy_until + h[start] + 20)
        openq = [(h[start], 0, start, t0)]
        came: dict = {}
        seen = set()
        expansions = 0
        while openq:
            f, g_, c, t = heapq.heappop(openq)
            if (c, t) in seen:
                continue
            seen.add((c, t))
            expansions += 1
            if expansions > 12000:
                return None
            if c == goal and all(not self._blocked(goal, t + k, rid) for k in range(1, work + 1)) \
                    and self._free_forever(goal, t, rid):
                path = [(c, t)]
                while (c, t) in came:
                    c, t = came[(c, t)]
                    path.append((c, t))
                return [p for p, _ in reversed(path)]
            if t >= horizon:
                continue
            for n in list(neighbors(*c)) + [c]:
                if n != c and (n not in self.passable or not self._move_ok(c, n)):
                    continue
                if n != c and n in self.charge_set and (n != goal or c in self.charge_set):
                    continue
                if n not in h or self._blocked(n, t + 1, rid):
                    continue
                if n != c:
                    who = self.eres.get((n, c, t))
                    if who is not None and who != rid:
                        continue
                if (n, t + 1) in seen:
                    continue
                if (n, t + 1) not in came:
                    came[(n, t + 1)] = (c, t)
                heapq.heappush(openq, (g_ + 1 + h[n], g_ + 1, n, t + 1))
        return None

    def _touch(self, cell, rid, t):
        d = self.cell_last.setdefault(cell, {})
        if d.get(rid, -1) < t:
            d[rid] = t

    def commit(self, r: Robot, path, t0, work_state, loaded, order_id, parked_goal=False):
        """경로 + 작업 대기를 예약하고 궤적에 기록한다."""
        self._clear_perm(r.rid)
        tr = self.traj[r.rid]
        goal = path[-1]
        for k, c in enumerate(path):
            t = t0 + k
            self.vres[(c, t)] = r.rid
            self._touch(c, r.rid, t)
            if k > 0:
                prev = path[k - 1]
                self.eres[(prev, c, t - 1)] = r.rid
                if prev == c:
                    state = "wait"
                else:
                    state = "carry" if loaded else "move"
            else:
                # 경로 첫 칸: 앞서 기록된 상태를 쓰고, 없으면 주문이 있는 로봇은 '대기'(작업 중 정지), 없으면 '유휴'
                state = tr.get(t, (c, "wait" if order_id else "idle", None))[1]
            tr[t] = (c, state, order_id)
        t_end = t0 + len(path) - 1
        for k in range(1, (0 if parked_goal else WORK_STEPS) + 1):
            t = t_end + k
            self.vres[(goal, t)] = r.rid
            self._touch(goal, r.rid, t)
            tr[t] = (goal, work_state, order_id)
        end = t_end + (0 if parked_goal else WORK_STEPS)
        self._set_perm(goal, r.rid, end)
        r.pos = goal
        r.free_at = end

    # ---------------------------------------------------------------- 할당
    def _choose_slot(self, order: Order, near):
        if order.kind == "inbound":
            cand = [p for p in self.rack_cells if self.stock[p] + self.reserved_cap[p] < self.rack_cells[p]]
        else:
            cand = [p for p in self.rack_cells if self.stock[p] > 0]
        if not cand:
            return None
        if self.strategy == "baseline":
            return self.rng.choice(sorted(cand))
        d = self.dist(near)
        return min(cand, key=lambda p: (d.get(self.access[p], 10 ** 6), p))

    def _choose_dock(self, docks, near):
        if not docks:
            return None
        if self.strategy == "baseline":
            return docks[0]
        d = self.dist(near)
        return min(docks, key=lambda p: (d.get(p, 10 ** 6), p))

    def _active_goals(self):
        return {leg[0] for r in self.robots for leg in r.legs}

    def assign(self, t):
        idle = [r for r in self.robots if r.order is None and not r.legs and r.free_at <= t]
        while self.pending and idle:
            order = self.pending[0]
            busy = self._active_goals()
            if self.strategy == "baseline":
                # 선착순: 번호가 가장 앞선 유휴 로봇, 첫 번째 도크, 무작위 칸
                robot = sorted(idle, key=lambda r: int(r.rid[1:]))[0]
                if order.kind == "inbound":
                    dock, slot = (self.dock_in[0] if self.dock_in else None), self._choose_slot(order, None)
                else:
                    slot, dock = self._choose_slot(order, None), (self.dock_out[0] if self.dock_out else None)
            elif order.kind == "inbound":
                if not self.dock_in:
                    break
                robot, dock = min(((r, d) for r in idle for d in self.dock_in),
                                  key=lambda rd: (self.dist(rd[1]).get(rd[0].pos, 10 ** 6), int(rd[0].rid[1:])))
                dd = self.dist(dock)
                cand = [p for p in self.rack_cells if self.stock[p] + self.reserved_cap[p] < self.rack_cells[p]]
                slot = min(cand, key=lambda p: (dd.get(self.access[p], 10 ** 6) + (12 if self.access[p] in busy else 0), p)) if cand else None
            else:
                cand = [p for p in self.rack_cells if self.stock[p] > 0]
                if not cand or not self.dock_out:
                    break
                def cost(rs):
                    r, p = rs
                    acc = self.access[p]
                    return (self.dist(acc).get(r.pos, 10 ** 6)
                            + min(self.dist(d).get(acc, 10 ** 6) for d in self.dock_out)
                            + (12 if acc in busy else 0), int(r.rid[1:]), p)
                robot, slot = min(((r, p) for r in idle for p in cand), key=cost)
                acc = self.access[slot]
                dock = min(self.dock_out, key=lambda d: (self.dist(d).get(acc, 10 ** 6), d))
            if dock is None or slot is None:
                break
            if order.kind == "inbound":
                self.reserved_cap[slot] += 1
                robot.legs.extend([(dock, "load", False, order), (self.access[slot], "unload", True, order, slot)])
            else:
                self.stock[slot] -= 1
                robot.legs.extend([(self.access[slot], "load", False, order), (dock, "unload", True, order)])
            self.pending.popleft()
            idle.remove(robot)
            robot.order = order
            robot.parked = False
            order.robot_id = robot.rid
            order.assigned_t = t

    # ---------------------------------------------------------------- 실행
    def run(self, events: list[dict[str, Any]] | None = None):
        events = sorted(events or [], key=lambda e: e["t"])
        t = 0
        while t < MAX_STEPS:
            for e in [e for e in events if e["t"] == t]:
                n = int(e.get("add_orders", 0))
                self._add_orders((n + 1) // 2, n // 2, t, self.spec)
                self._spawn_robots(int(e.get("robots", len(self.robots))), t)
            for o in self.orders:
                if not o.queued and o.arrival <= t:
                    o.queued = True
                    self.pending.append(o)
            self.assign(t)
            for r in sorted(self.robots, key=lambda r: int(r.rid[1:])):
                if r.free_at > t or r.retry_at > t:
                    continue
                if r.legs:
                    self._do_leg(r, t)
                elif not r.parked and r.order is None:
                    spot = self._parking_for(r, t)
                    if spot is None:
                        self._hold(r, t)
                    elif spot == r.pos:
                        r.parked = True
                    else:
                        path = self.plan(r.rid, r.pos, t, spot, 0)
                        if path is None:
                            self._hold(r, t)
                        else:
                            self.commit(r, path, t, "idle", False, None, parked_goal=True)
                            r.parked = True
                else:
                    self._hold(r, t)
            for r in self.robots:  # 경로를 못 찾아 제자리에서 기다리는 로봇은 '대기'로 기록
                if r.legs and t not in self.traj[r.rid]:
                    self.traj[r.rid][t] = (r.pos, "wait", r.legs[0][3].order_id)
            if all(o.done_t is not None for o in self.orders) and not [e for e in events if e["t"] > t]:
                break
            t += 1
        self.t_end = max([o.done_t for o in self.orders if o.done_t is not None] + [t, 0])
        return self

    def _do_leg(self, r: Robot, t):
        leg = r.legs[0]
        goal, work, loaded, order = leg[0], leg[1], leg[2], leg[3]
        path = self.plan(r.rid, r.pos, t, goal, WORK_STEPS)
        if path is None:
            r.fails += 1
            # 교착 회피: 여러 번 실패하면 잠시 주차 칸으로 비켜 섰다가 다시 시도
            # 작업 중에는 충전 칸으로 비켜 서지 않는다 (충전 칸은 일이 없는 로봇의 주차, 충전 자리)
            if r.fails >= 6:
                spot = self._parking_for(r, t, exclude_current=True, allow_charge=False)
                detour = self.plan(r.rid, r.pos, t, spot, 0) if spot else None
                if detour is not None and len(detour) > 1:
                    self.commit(r, detour, t, "wait", loaded, order.order_id, parked_goal=True)
                    r.fails = 0
                    return
            self._hold(r, t)
            r.retry_at = t + 1 + min(r.fails, 3)
            return
        r.fails = 0
        self.commit(r, path, t, work, loaded, order.order_id)
        r.legs.popleft()
        if work == "unload":
            if order.kind == "inbound":
                slot = leg[4]
                self.stock[slot] += 1
                self.reserved_cap[slot] -= 1
            order.done_t = r.free_at
            r.order = None

    def _parking_for(self, r: Robot, t, exclude_current: bool = False, allow_charge: bool = True):
        if r.pos in self.parking_set and not exclude_current:
            return r.pos
        d = self.dist(r.pos)
        for p in sorted(self.parking, key=lambda p: (p not in self.charge_set, d.get(p, 10 ** 6), p)):
            if exclude_current and p == r.pos:
                continue
            if not allow_charge and p in self.charge_set:
                continue
            if d.get(p) is not None and self._free_forever(p, t, r.rid):
                return p
        return None

    def _hold(self, r: Robot, t):
        p = self.perm.get(r.pos)
        if p is None or p[0] != r.rid:
            self._set_perm(r.pos, r.rid, t)

    # ---------------------------------------------------------------- 로그
    def frames(self, t_from: int = 0):
        out = []
        last: dict[str, tuple[int, int]] = {}
        for t in range(0, self.t_end + 1):
            robots = []
            for r in self.robots:
                spawn_t, spawn_pos = self.spawn_info[r.rid]
                if t < spawn_t:
                    continue
                tr = self.traj[r.rid]
                if t in tr:
                    c, state, oid = tr[t]
                else:
                    c = last.get(r.rid, spawn_pos)
                    state = "charge" if self.g.get(c) == "charge" else "idle"
                    oid = None
                last[r.rid] = c
                robots.append({"id": r.rid, "x": c[0], "y": c[1], "state": state, "task_id": oid})
            if t >= t_from:
                out.append({"t": t, "robots": robots})
        return out


def simulate(m, scenario, strategy="optimized", seed=42, events=None):
    """순수 함수 형태의 엔진 진입점 (SC-04 구현 포인트)."""
    started = time.perf_counter()
    sim = Sim(m, scenario, strategy, seed)
    sim.run(events)
    elapsed_ms = (time.perf_counter() - started) * 1000
    frames = sim.frames()
    stats = []
    for i, f in enumerate(frames):
        if i == 0:
            continue
        prev = {r["id"]: (r["x"], r["y"]) for r in frames[i - 1]["robots"]}
        for r in f["robots"]:
            c = (r["x"], r["y"])
            if r["id"] in prev and prev[r["id"]] != c:
                sim.stats_pass[c] = sim.stats_pass.get(c, 0) + 1
            if r["state"] == "wait":
                sim.stats_wait[c] = sim.stats_wait.get(c, 0) + 1
    stats = [{"x": x, "y": y, "pass": sim.stats_pass[(x, y)], "wait": sim.stats_wait[(x, y)]}
             for (x, y) in sorted(sim.passable, key=lambda p: (p[1], p[0]))]
    orders = [{"order_id": o.order_id, "type": o.kind, "robot_id": o.robot_id,
               "assigned_t": o.assigned_t, "done_t": o.done_t, "arrival_t": o.arrival} for o in sim.orders]
    done = [o for o in sim.orders if o.done_t is not None and o.assigned_t is not None]
    log = {
        "strategy": strategy,
        "frames": frames,
        "orders": orders,
        "stats": stats,
        "collisions": check_collisions(frames),
        "total_steps": frames[-1]["t"] if frames else 0,
        "compute_ms": round(elapsed_ms, 1),
    }
    log["summary"] = {
        "total_steps": log["total_steps"],
        "orders_done": len(done),
        "orders_total": len(orders),
        "avg_order_steps": round(sum(o.done_t - o.assigned_t for o in done) / len(done), 1) if done else 0,
        "wait_total": sum(s["wait"] for s in stats),
        "robots": len(sim.robots),
        "collisions": len(log["collisions"]),
        "compute_ms": log["compute_ms"],
    }
    return log


def check_collisions(frames) -> list[dict[str, Any]]:
    """같은 칸, 같은 스텝, 맞교환 충돌 검사 (NFR-04)."""
    out = []
    for f in frames:
        seen: dict = {}
        for r in f["robots"]:
            c = (r["x"], r["y"])
            if c in seen:
                out.append({"t": f["t"], "robot_a": seen[c], "robot_b": r["id"], "x": c[0], "y": c[1], "type": "vertex"})
            seen[c] = r["id"]
    for a, b in zip(frames, frames[1:]):
        pa = {r["id"]: (r["x"], r["y"]) for r in a["robots"]}
        pb = {r["id"]: (r["x"], r["y"]) for r in b["robots"]}
        ids = sorted(set(pa) & set(pb))
        for i, r1 in enumerate(ids):
            for r2 in ids[i + 1:]:
                if pa[r1] == pb[r2] and pa[r2] == pb[r1] and pa[r1] != pa[r2]:
                    out.append({"t": b["t"], "robot_a": r1, "robot_b": r2, "x": pb[r1][0], "y": pb[r1][1], "type": "swap"})
    return out
