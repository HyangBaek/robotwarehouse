"""
engine.py - 다중 로봇 경로 계산 엔진 (로봇웨어하우스, 하재윤 담당)

핵심 구조
  1. 시공간 A*            : 예약 표(점/맞교환)를 피하는 단일 로봇 경로
  2. 윈도우 우선순위 계획  : 로봇 전체를 W스텝 앞까지 계획 (충돌 0 보장)
  3. LNS 개선             : 일부 로봇 경로를 지우고 다시 계획해 비용을 줄임 (경로 최적화)
  4. 이벤트 기반 롤링 재계획: 작업 단계가 바뀌거나 h스텝이 지나면 전체를 다시 계획
  5. 체크포인트 재계획     : 주문 추가, 로봇 수 변경 시 시점 t 이전 결과는 유지하고 이후만 재계산

공개 함수
  simulate(map_json, scenario, strategy="optimized", seed=42, config=None) -> dict   (DR-03 로그)
  compare(map_json, scenario, seed=42) -> dict                                       (기준 vs 최적화)
  find_collisions(frames) -> list                                                    (충돌 검사기)
  Sim.add_event(event) -> dict                                                       (롤링 재계획)

좌표: 격자 (x, y), 인덱스 = y * width + x. 방향은 규칙의 from->to 벡터로 정한다.
"""
from __future__ import annotations

import copy
import heapq
import random
import time
import zlib
from collections import deque

try:
    from scipy.optimize import linear_sum_assignment
except ImportError:  # scipy 없으면 그리디 할당으로 대체
    linear_sum_assignment = None

INF = 10 ** 6
PASSABLE = {"aisle", "dock_in", "dock_out", "charge"}
DEFAULT_SPEC = "pallet_1100x1100"            # 한국 표준 파레트 (KS T-11), 랙 slot_spec 이 없으면 이 규격

DEFAULT_CFG = dict(
    window=24,                # 계획 구간 W (스텝)
    horizon=12,               # 재계획 없이 실행하는 최대 스텝 h (<= window)
    lns_iters=8,              # 계획 1회당 LNS 반복 수 (고정 횟수 -> 재현 가능)
    service=1,                # 적재, 하역에 걸리는 스텝
    max_steps=3000,           # 안전 상한
    storage_load_weight=3.0,  # 보관 위치 점수에서 '같은 랙으로 몰림' 벌점 가중치
    fill_ratio=0.5,           # 시작 시 랙 재고 비율 (출하 주문용)
    arrival_span=60,          # 자동 생성 주문의 도착 스텝 범위
    use_hungarian=True,       # 일괄 최적 할당 (없으면 그리디)
    order_mode="far_first",   # 우선순위: far_first | near_first
    stall_patience=3,         # (baseline) 이 스텝 이상 막히면 옆걸음
    random_storage=False,     # 실험용: 최적화 전략에서도 무작위 보관 (기여도 분해)
    fallback="pibt",          # 윈도우 계획이 모두 실패할 때: pibt(한 스텝 PIBT) | wait(전원 대기, 이전 방식)
    dock_limit=0,             # 실험용: 도크 하나로 동시에 향하는 로봇 상한 (0 = 제한 없음). 넘으면 주문을 다음 스텝까지 보류
    dock_select="hash",       # 실험용: hash(주문 번호로 고정, 이전 방식) | least_loaded(향하는 로봇이 가장 적은 도크)
)


# --------------------------------------------------------------------------- 격자
def passing_cells(m: dict) -> set[int]:
    """rules.passing_allowed 를 칸 번호 집합으로. 두 점씩 묶어 직선 구간으로 본다
    (요구사항 명세 예시 [[10, 1], [10, 18]] 은 x=10 의 y=1~18 구간). 홀수 개면 마지막 점은 칸 하나."""
    W = m["width"]
    pts = [tuple(p) for p in m.get("rules", {}).get("passing_allowed", []) or []]
    out = set()
    for k in range(0, len(pts), 2):
        (x1, y1), (x2, y2) = pts[k], pts[min(k + 1, len(pts) - 1)]
        if x1 != x2 and y1 != y2:                      # 직선이 아니면 두 점만
            out |= {y1 * W + x1, y2 * W + x2}
            continue
        for y in range(min(y1, y2), max(y1, y2) + 1):
            for x in range(min(x1, x2), max(x1, x2) + 1):
                out.add(y * W + x)
    return out


class Grid:
    """격자 지도 JSON(DR-01) -> 이동 그래프."""

    def __init__(self, m: dict):
        self.W, self.H = m["width"], m["height"]
        W, H = self.W, self.H
        self.N = N = W * H
        self.type = ["aisle"] * N
        rack_cell = {}
        for c in m.get("cells", []):
            i = c["y"] * W + c["x"]
            self.type[i] = c["type"]
            if c["type"] == "rack":
                rack_cell[i] = c.get("rack_id", f"R{i}")
        for d in m.get("docks", []):
            self.type[d["y"] * W + d["x"]] = d["type"]
        self.ok = [t in PASSABLE for t in self.type]
        self.dock_in = [i for i in range(N) if self.type[i] == "dock_in"]
        self.dock_out = [i for i in range(N) if self.type[i] == "dock_out"]
        self.charge = [i for i in range(N) if self.type[i] == "charge"]

        self.rack_cap = {r["rack_id"]: r.get("capacity", 6) for r in m.get("racks", [])}
        self.rack_spec = {r["rack_id"]: r.get("slot_spec", DEFAULT_SPEC) for r in m.get("racks", [])}
        self.racks: dict[str, list[int]] = {}
        for i, rid in rack_cell.items():
            self.racks.setdefault(rid, []).append(i)
            self.rack_cap.setdefault(rid, 6)
            self.rack_spec.setdefault(rid, DEFAULT_SPEC)

        # 일방통행: from -> to 벡터 방향만 허용
        self.ow = {}
        for r in m.get("rules", {}).get("one_way", []):
            (x1, y1), (x2, y2) = r["from"], r["to"]
            d = ((x2 > x1) - (x2 < x1), (y2 > y1) - (y2 < y1))
            for y in range(min(y1, y2), max(y1, y2) + 1):
                for x in range(min(x1, x2), max(x1, x2) + 1):
                    self.ow[y * W + x] = d
        # 엇갈림 가능 구간(FR-17): 일방통행 안에 있어도 양방향 통행을 허용한다
        self.passing = passing_cells(m)
        for i in self.passing:
            self.ow.pop(i, None)

        self.adj = [[] for _ in range(N)]
        self.radj = [[] for _ in range(N)]
        for i in range(N):
            if not self.ok[i]:
                continue
            x, y = i % W, i // W
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < W and 0 <= ny < H):
                    continue
                j = ny * W + nx
                if not self.ok[j]:
                    continue
                if i in self.ow and j in self.ow and self.ow[i] != (dx, dy):
                    continue
                self.adj[i].append(j)
                self.radj[j].append(i)
        self.adjw = [a + [i] for i, a in enumerate(self.adj)]  # 이동 + 대기
        self.zero = [0] * N
        self._d: dict[int, list[int]] = {}

        # 랙 칸에 접근 가능한 통로 칸
        self.access = {}
        for i in rack_cell:
            x, y = i % W, i // W
            acc = []
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < W and 0 <= ny < H and self.ok[ny * W + nx]:
                    acc.append(ny * W + nx)
            self.access[i] = acc

    def dist(self, goal: int) -> list[int]:
        """모든 칸에서 goal까지의 최단 이동 수 (일방통행 반영, 역방향 BFS, 캐시)."""
        d = self._d.get(goal)
        if d is None:
            d = [INF] * self.N
            d[goal] = 0
            q = deque([goal])
            while q:
                i = q.popleft()
                for j in self.radj[i]:
                    if d[j] == INF:
                        d[j] = d[i] + 1
                        q.append(j)
            self._d[goal] = d
        return d


# --------------------------------------------------------------------------- 예약 표
class Resv:
    """점 예약 (칸, t)와 간선 예약(맞교환 방지)."""

    def __init__(self, N: int):
        self.N = N
        self.v: set[int] = set()
        self.e: set[int] = set()
        self.last: dict[int, int] = {}

    def add(self, path: list[int]):
        N = self.N
        v, e, last = self.v, self.e, self.last
        for t, c in enumerate(path):
            v.add(t * N + c)
            if last.get(c, -1) < t:
                last[c] = t
            if t + 1 < len(path):
                n = path[t + 1]
                if n != c:
                    e.add((t * N + n) * N + c)  # 'n -> c' 이동을 막는다


# --------------------------------------------------------------------------- 시공간 A*
def plan_one(g: Grid, start: int, goal: int, res: Resv, W: int):
    """
    시공간 A*. 반환: (경로[0..W], 비용) 또는 None.
      goal >= 0 : 이동, 대기 모두 비용 1, h = BFS 거리.
                  목표에 도착해 이후 예약이 없으면 종료, 아니면 t == W에서 종료(f = g + h).
      goal <  0 : 유휴. 대기 비용 0, 이동 비용 1 (비켜 주기만 한다).
    """
    N, adjw = g.N, g.adjw
    if goal >= 0:
        h = g.dist(goal)
        if h[start] >= INF:
            return None
        wc = 1
    else:
        h = g.zero
        wc = 0
    v, e, last = res.v, res.e, res.last
    best = {start: 0}
    par = {start: -1}
    heap = [(h[start], 0, start)]
    pop, push = heapq.heappop, heapq.heappush
    while heap:
        f, nt, i = pop(heap)
        t = -nt
        key = t * N + i
        gc = best[key]
        if f != gc + h[i]:
            continue
        if t == W or (i == goal and last.get(i, -1) < t):
            path = []
            k = key
            while k != -1:
                path.append(k % N)
                k = par[k]
            path.reverse()
            path += [path[-1]] * (W + 1 - len(path))
            return path, f
        t1 = t + 1
        base = t * N + i
        for j in adjw[i]:
            k1 = t1 * N + j
            if k1 in v:
                continue
            if j != i and base * N + j in e:
                continue
            hj = h[j]
            if hj >= INF:
                continue
            gn = gc + (1 if (j != i or wc) else 0)
            if gn < best.get(k1, INF):
                best[k1] = gn
                par[k1] = key
                push(heap, (gn + hj, -t1, j))
    return None


def plan_window(g: Grid, rs: dict, order: list, W: int):
    """고정 로봇을 먼저 예약하고, order 순서대로 우선순위 계획. 실패 시 (None, 실패 id)."""
    res = Resv(g.N)
    paths, costs = {}, {}
    for rid, (pos, goal, fixed) in rs.items():
        if fixed:
            p = [pos] * (W + 1)
            paths[rid], costs[rid] = p, 0
            res.add(p)
    for rid in order:
        pos, goal, _ = rs[rid]
        out = plan_one(g, pos, goal, res, W)
        if out is None:
            return None, rid
        paths[rid], costs[rid] = out
        res.add(out[0])
    return (paths, costs), None


def lns_improve(g: Grid, rs: dict, order: list, paths: dict, costs: dict, W: int,
                iters: int, rng: random.Random):
    """
    LNS: 지연이 큰 로봇과 그 주변 로봇의 경로를 지우고, 나머지를 고정한 채 다시 계획한다.
    새 경로도 예약 표를 지키므로 충돌 0은 유지되고, 비용이 늘면 버리므로 나빠지지 않는다.
    """
    if len(order) < 2:
        return paths, costs
    total = sum(costs.values())
    delay = {}
    for r in order:
        pos, goal, _ = rs[r]
        delay[r] = costs[r] - (g.dist(goal)[pos] if goal >= 0 else 0)
    for _ in range(iters):
        k = min(len(order), rng.choice((2, 3, 4)))
        if rng.random() < 0.3:
            group = rng.sample(order, k)
        else:
            seed = rng.choices(order, weights=[max(delay[r], 0) + 0.1 for r in order])[0]
            sx, sy = rs[seed][0] % g.W, rs[seed][0] // g.W
            near = sorted(order, key=lambda r: abs(rs[r][0] % g.W - sx) + abs(rs[r][0] // g.W - sy))
            group = near[:k]
        gset = set(group)
        res = Resv(g.N)
        for r, p in paths.items():
            if r not in gset:
                res.add(p)
        newp, newc = {}, {}
        ok = True
        for r in rng.sample(group, len(group)):
            pos, goal, _ = rs[r]
            out = plan_one(g, pos, goal, res, W)
            if out is None:
                ok = False
                break
            newp[r], newc[r] = out
            res.add(out[0])
        if not ok:
            continue
        nt = total - sum(costs[r] for r in group) + sum(newc.values())
        if nt <= total:
            paths.update(newp)
            costs.update(newc)
            total = nt
            for r in group:
                delay[r] = costs[r] - (g.dist(rs[r][1])[rs[r][0]] if rs[r][1] >= 0 else 0)
    return paths, costs


# --------------------------------------------------------------------------- PIBT 한 스텝 (대체 경로)
def pibt_step(g: Grid, rs: dict, prio: dict, rng: random.Random) -> dict:
    """
    PIBT(우선순위 상속 + 백트래킹)로 모든 로봇의 다음 한 칸을 정한다. 반환: {rid: 다음 칸}.
    rs = {rid: (pos, goal, fixed)}. fixed(적재, 하역 중) 로봇은 제자리. prio 가 큰 로봇이 먼저 고른다.
    높은 우선순위 로봇이 원하는 칸에 있는 로봇은 우선순위를 물려받아 먼저 비켜 주고,
    비킬 곳이 없으면 부모가 다음 후보를 고른다. 점 충돌, 맞교환 충돌이 없는 이동만 만든다.
    """
    pos = {r: v[0] for r, v in rs.items()}
    occ = {c: r for r, c in pos.items()}
    nxt, res = {}, {}
    for r, (p, _, fixed) in rs.items():
        if fixed:
            nxt[r], res[p] = p, r
    tie = {r: rng.random() for r in rs}

    def cands(a):
        p, goal, _ = rs[a]
        opts = list(g.adjw[p])
        if goal >= 0:
            h = g.dist(goal)
            opts.sort(key=lambda c: (h[c], tie[a] if c == p else rng.random()))
        else:                                  # 유휴: 제자리 우선, 비켜야 하면 아무 데나
            opts.sort(key=lambda c: (c != p, rng.random()))
        return opts

    def go(a, parent):
        for c in cands(a):
            if c in res:
                continue
            if parent is not None and c == pos[parent]:
                continue                       # 부모와 맞교환 금지
            b = occ.get(c)
            if b is not None and b != a and b in nxt and nxt[b] == pos[a]:
                continue                       # 이미 정해진 로봇과 맞교환 금지
            res[c], nxt[a] = a, c
            if b is not None and b != a and b not in nxt:
                if not go(b, a):               # 자식이 못 비키면 자식이 c에 남음 -> 다음 후보
                    continue
            return True
        nxt[a] = pos[a]
        res[pos[a]] = a
        return False

    for a in sorted((r for r in rs if r not in nxt), key=lambda r: (-prio[r], tie[r])):
        if a not in nxt:
            go(a, None)
    return nxt


# --------------------------------------------------------------------------- 충돌 검사기
def find_collisions(frames: list[dict]) -> list[dict]:
    """같은 칸, 같은 스텝, 맞교환 충돌을 전 구간에서 검사한다 (NFR-04)."""
    out = []
    prev = {}
    for fr in frames:
        t = fr["t"]
        cur = {r["id"]: (r["x"], r["y"]) for r in fr["robots"]}
        seen = {}
        for rid, p in cur.items():
            if p in seen:
                out.append(dict(t=t, robot_a=seen[p], robot_b=rid, x=p[0], y=p[1], type="vertex"))
            seen[p] = rid
        if prev:
            ids = [i for i in cur if i in prev]
            for a in range(len(ids)):
                for b in range(a + 1, len(ids)):
                    ra, rb = ids[a], ids[b]
                    if cur[ra] == prev[rb] and cur[rb] == prev[ra] and cur[ra] != cur[rb]:
                        out.append(dict(t=t, robot_a=ra, robot_b=rb, x=cur[ra][0], y=cur[ra][1], type="swap"))
        prev = cur
    return out


# --------------------------------------------------------------------------- 주문 생성기
def create_orders(n_in: int, n_out: int, seed: int, span: int, t0: int = 0, start_id: int = 0,
                  spec_mix: dict | None = None):
    """주문 1건 = 파레트 1개 (qty=1). spec_mix = {규격: 비율} 이면 입하, 출하 각각 비율대로 규격을 나눠 붙인다.
    spec_mix 가 없으면 모두 기본 규격이고, 난수 순서가 바뀌지 않아 이전과 같은 주문이 나온다."""
    rng = random.Random(seed)
    types = ["inbound"] * n_in + ["outbound"] * n_out
    rng.shuffle(types)
    arr = sorted(t0 + rng.randint(0, span) for _ in types)
    orders = [dict(order_id=f"O{start_id + i + 1:03d}", type=ty, arrival_t=a, spec=DEFAULT_SPEC, qty=1)
              for i, (ty, a) in enumerate(zip(types, arr))]
    mix = {k: float(v) for k, v in (spec_mix or {}).items() if float(v) > 0}
    if mix and set(mix) != {DEFAULT_SPEC}:
        srng = random.Random(f"{seed}:{t0}:spec")
        total = sum(mix.values())
        for ty in ("inbound", "outbound"):
            idx = [i for i, o in enumerate(orders) if o["type"] == ty]
            raw = {k: len(idx) * v / total for k, v in sorted(mix.items())}
            cnt = {k: int(v) for k, v in raw.items()}
            for k in sorted(raw, key=lambda k: -(raw[k] - cnt[k]))[:len(idx) - sum(cnt.values())]:
                cnt[k] += 1                            # 최대 나머지 방식으로 개수를 정확히 맞춤
            labels = [k for k in sorted(cnt) for _ in range(cnt[k])]
            srng.shuffle(labels)
            for i, lab in zip(idx, labels):
                orders[i]["spec"] = lab
    return orders


# --------------------------------------------------------------------------- 시뮬레이터
class Sim:
    STATE = ("t", "robots", "pending", "future", "stock", "busy", "olog", "events", "order_seq", "_elapsed")

    def __init__(self, map_json: dict, scenario: dict, strategy: str = "optimized",
                 seed: int = 42, config: dict | None = None):
        self.cfg = {**DEFAULT_CFG, **(config or {})}
        self.cfg["horizon"] = min(self.cfg["horizon"], self.cfg["window"])
        self.g = Grid(map_json)
        self.strategy, self.seed = strategy, seed
        self.scenario_id = scenario.get("scenario_id")
        self.map_version = scenario.get("map_version")
        g = self.g
        if not g.dock_in or not g.dock_out:
            raise ValueError("입하·출하 도크가 필요합니다")
        self.spec_mix = scenario.get("spec_mix")
        orders = scenario.get("orders") or create_orders(
            scenario.get("inbound", 0), scenario.get("outbound", 0), seed, self.cfg["arrival_span"],
            spec_mix=self.spec_mix)
        self.t = 0
        self.future = sorted(copy.deepcopy(orders), key=lambda o: (o["arrival_t"], o["order_id"]))
        self.pending: list[dict] = []
        self.order_seq = len(orders)
        self.stock = {rid: int(g.rack_cap[rid] * self.cfg["fill_ratio"]) for rid in g.racks}
        self.busy = {rid: 0 for rid in g.racks}
        self.olog: dict[str, dict] = {}
        self.events = sorted(copy.deepcopy(scenario.get("events", [])), key=lambda e: e["t"])
        self.robots: list[dict] = []
        self._spawn(scenario.get("robots", 4))
        self.frames = [self._frame({r["id"]: "idle" for r in self.robots}, {})]
        self.ckpts: list[dict] = []
        self.replan_log: list[dict] = []
        self.event_log: list[dict] = []
        self._pending_event: dict | None = None
        self._acc_cache: dict = {}
        self._short = False                                    # 대체 경로(PIBT)를 쓴 계획이면 한 스텝만 실행
        self._elapsed: dict = {}                               # PIBT 우선순위: 목표에 못 간 채 지난 계획 횟수
        self._load_cache: dict = {}

    # ----- 상태 저장, 복원 (롤링 재계획용)
    def _save(self):
        s = {k: copy.deepcopy(getattr(self, k)) for k in self.STATE}
        s["nframes"] = len(self.frames)
        return s

    def _load(self, s):
        for k in self.STATE:
            setattr(self, k, copy.deepcopy(s[k]))
        del self.frames[s["nframes"]:]

    # ----- 로봇 생성
    def _free_cells(self, k):
        g = self.g
        occ = {r["pos"] for r in self.robots}
        seeds = list(g.charge) or list(g.dock_in) + list(g.dock_out)
        seen, q, out = set(seeds), deque(seeds), []
        while q and len(out) < k:
            c = q.popleft()
            if c not in occ and g.type[c] not in ("dock_in", "dock_out"):
                out.append(c)
            for j in g.adj[c]:
                if j not in seen:
                    seen.add(j)
                    q.append(j)
        return out

    def _spawn(self, spec):
        cells = [y * self.g.W + x for x, y in spec] if isinstance(spec, list) else self._free_cells(spec)
        for c in cells:
            rid = f"R{len(self.robots) + 1}"
            self.robots.append(dict(id=rid, pos=c, home=c, phase="idle", order=None, rack=None,
                                    pickup=-1, drop=-1, timer=0, stall=0, active=True))

    def _frame(self, labels, tids):
        W = self.g.W
        return dict(t=self.t, robots=[
            dict(id=r["id"], x=r["pos"] % W, y=r["pos"] // W,
                 state=labels.get(r["id"], "idle"), task_id=tids.get(r["id"]))
            for r in self.robots])

    # ----- 주문 흐름
    def _release(self):
        n = 0
        while self.future and self.future[0]["arrival_t"] <= self.t:
            self.pending.append(self.future.pop(0))
            n += 1
        return n > 0

    def _apply_events(self):
        while self.events and self.events[0]["t"] <= self.t:
            ev = self.events.pop(0)
            n = ev.get("add_orders", 0)
            if n:
                new = create_orders((n + 1) // 2, n // 2, self.seed + ev["t"], 0,
                                    t0=ev["t"], start_id=self.order_seq, spec_mix=self.spec_mix)
                self.order_seq += n
                self.future = sorted(self.future + new, key=lambda o: (o["arrival_t"], o["order_id"]))
            tgt = ev.get("robots")
            if tgt:
                act = [r for r in self.robots if r["active"]]
                if tgt > len(act):
                    need = tgt - len(act)
                    for r in self.robots:  # 비활성 로봇 먼저 복귀
                        if not r["active"] and need:
                            r["active"] = True
                            need -= 1
                    if need:
                        self._spawn_cells(need)
                else:
                    for r in reversed(act[tgt:]):
                        r["active"] = False  # 진행 중 작업을 마치고 집으로 돌아가 정지

    def _spawn_cells(self, k):
        cells = self._free_cells(k)
        self._spawn([(c % self.g.W, c // self.g.W) for c in cells])

    def _best_access(self, rid, dock, inbound):
        key = (rid, dock, inbound)
        if key not in self._acc_cache:
            g, best = self.g, (INF, -1)
            for cell in g.racks[rid]:
                for a in g.access[cell]:
                    d = g.dist(a)[dock] if inbound else g.dist(dock)[a]
                    if (d, a) < best:
                        best = (d, a)
            self._acc_cache[key] = best
        return self._acc_cache[key]

    def _dock_load(self):
        """도크별로 지금 그 도크를 향하는(또는 그 위에서 작업 중인) 로봇 수."""
        load = {}
        for r in self.robots:
            if r["phase"] in ("to_pickup", "load"):
                c = r["pickup"]
            elif r["phase"] in ("to_drop", "unload"):
                c = r["drop"]
            else:
                continue
            if self.g.type[c] in ("dock_in", "dock_out"):
                load[c] = load.get(c, 0) + 1
        return load

    def _dock_for(self, o):
        docks = self.g.dock_in if o["type"] == "inbound" else self.g.dock_out
        if self.cfg["dock_select"] == "least_loaded" and len(docks) > 1:
            load = self._load_cache
            return min(docks, key=lambda d: (load.get(d, 0), zlib.crc32(f"{o['order_id']}:{d}".encode())))
        return docks[zlib.crc32(o["order_id"].encode()) % len(docks)]

    def _choose_storage(self, o):
        g, cfg = self.g, self.cfg
        inbound = o["type"] == "inbound"
        dock = self._dock_for(o)
        rng = random.Random(f"{self.seed}:{o['order_id']}")
        spec = o.get("spec", DEFAULT_SPEC)
        cand = [rid for rid in g.racks if g.rack_spec[rid] == spec           # 수용 규격이 맞는 랙만 (FR-15)
                and (self.stock[rid] < g.rack_cap[rid] if inbound else self.stock[rid] > 0)]
        cand = [rid for rid in cand if self._best_access(rid, dock, inbound)[0] < INF]
        if not cand:
            return None
        if self.strategy == "baseline" or cfg.get("random_storage"):  # 무작위 보관 위치
            rid = rng.choice(cand)
            accs = [a for c in g.racks[rid] for a in g.access[c]]
            acc = rng.choice(accs)
        else:                                                  # 점수: 도크 거리 + 몰림 벌점
            w = cfg["storage_load_weight"]
            rid = min(cand, key=lambda r: (self._best_access(r, dock, inbound)[0] + w * self.busy[r], r))
            acc = self._best_access(rid, dock, inbound)[1]
        self.stock[rid] += 1 if inbound else -1
        self.busy[rid] += 1
        return (dock, acc, rid) if inbound else (acc, dock, rid)

    def _assign(self):
        idle = [r for r in self.robots if r["phase"] == "idle" and r["active"]]
        if not idle or not self.pending:
            return
        prepared = []
        limit = self.cfg["dock_limit"]
        if limit or self.cfg["dock_select"] == "least_loaded":   # 도크 옵션을 켰을 때만 부하 계산
            self._load_cache = self._dock_load()
        cands = self.pending if limit else self.pending[:len(idle)]
        for o in list(cands):
            if len(prepared) >= len(idle):
                break
            if limit:
                d = self._dock_for(o)
                if self._load_cache.get(d, 0) >= limit:
                    continue                                   # 도크가 꽉 참: 주문 보류
            self.pending.remove(o)
            plan = self._choose_storage(o)
            if plan is not None and limit:
                dk = plan[0] if o["type"] == "inbound" else plan[1]
                self._load_cache[dk] = self._load_cache.get(dk, 0) + 1
            if plan is None:
                self.olog[o["order_id"]] = dict(order_id=o["order_id"], type=o["type"], robot_id=None,
                                                spec=o.get("spec", DEFAULT_SPEC), qty=o.get("qty", 1),
                                                arrival_t=o["arrival_t"], assigned_t=None, done_t=None,
                                                status="rejected")
                continue
            prepared.append((o, plan))
        if not prepared:
            return
        g = self.g
        if self.strategy == "baseline" or not (self.cfg["use_hungarian"] and linear_sum_assignment):
            pairs, free = [], list(idle)
            for j, (o, plan) in enumerate(prepared):          # baseline: 선착순 / optimized 대체: 그리디
                if self.strategy == "baseline":
                    r = free.pop(0)
                else:
                    r = min(free, key=lambda r: g.dist(plan[0])[r["pos"]])
                    free.remove(r)
                pairs.append((r, o, plan))
        else:
            cost = [[min(g.dist(plan[0])[r["pos"]], 10 ** 4) for (o, plan) in prepared] for r in idle]
            ri, ci = linear_sum_assignment(cost)
            pairs = [(idle[i], *prepared[j]) for i, j in zip(ri, ci)]
        for r, o, (pick, drop, rid) in pairs:
            r.update(phase="to_pickup", order=o["order_id"], rack=rid, pickup=pick, drop=drop)
            self.olog[o["order_id"]] = dict(order_id=o["order_id"], type=o["type"], robot_id=r["id"],
                                            spec=o.get("spec", DEFAULT_SPEC), qty=o.get("qty", 1),
                                            arrival_t=o["arrival_t"], assigned_t=self.t, done_t=None,
                                            status="active")

    def _arrivals(self):
        changed = False
        again = True
        while again:
            again = False
            for r in self.robots:
                if r["phase"] == "to_pickup" and r["pos"] == r["pickup"]:
                    r["phase"], r["timer"] = "load", self.cfg["service"]
                    again = changed = True
                elif r["phase"] == "to_drop" and r["pos"] == r["drop"]:
                    r["phase"], r["timer"] = "unload", self.cfg["service"]
                    again = changed = True
        return changed

    def _finish(self, r):
        self.olog[r["order"]].update(done_t=self.t, status="done")
        self.busy[r["rack"]] -= 1
        r.update(phase="idle", order=None, rack=None, pickup=-1, drop=-1)

    def _goal(self, r):
        if r["phase"] == "to_pickup":
            return r["pickup"]
        if r["phase"] == "to_drop":
            return r["drop"]
        if r["phase"] == "idle" and r["pos"] != r["home"]:
            if not r["active"] or self.strategy == "baseline" or self.g.type[r["pos"]] in ("dock_in", "dock_out"):
                return r["home"]                               # 비활성, 도크 위 유휴 로봇은 집으로
        return -1

    # ----- 한 스텝 실행
    def _step(self, newpos: dict) -> bool:
        labels, tids = {}, {}
        for r in self.robots:
            rid, np_ = r["id"], newpos[r["id"]]
            moved, ph = np_ != r["pos"], r["phase"]
            if ph == "load":
                lab = "load"
            elif ph == "unload":
                lab = "unload"
            elif ph == "to_pickup":
                lab = "move_empty" if moved else "wait"
            elif ph == "to_drop":
                lab = "move_loaded" if moved else "wait"
            else:
                lab = "move_empty" if moved else "idle"
            labels[rid], tids[rid] = lab, r["order"]
            r["stall"] = r["stall"] + 1 if lab == "wait" else 0
            r["pos"] = np_
        self.t += 1
        changed = False
        for r in self.robots:
            if r["phase"] in ("load", "unload"):
                r["timer"] -= 1
                if r["timer"] <= 0:
                    if r["phase"] == "load":
                        r["phase"] = "to_drop"
                    else:
                        self._finish(r)
                    changed = True
        if self._arrivals():
            changed = True
        if self._release() and any(r["phase"] == "idle" and r["active"] for r in self.robots):
            changed = True
        self.frames.append(self._frame(labels, tids))
        return changed

    # ----- optimized: 윈도우 계획
    def _plan(self):
        g, cfg, W = self.g, self.cfg, self.cfg["window"]
        t0 = time.perf_counter()
        rs = {}
        for r in self.robots:
            goal = self._goal(r)
            if goal >= 0 and g.dist(goal)[r["pos"]] >= INF:
                goal = -1
            rs[r["id"]] = (r["pos"], goal, r["phase"] in ("load", "unload"))
        stall = {r["id"]: r["stall"] for r in self.robots}
        for rid, (pos, goal, fixed) in rs.items():
            self._elapsed[rid] = self._elapsed.get(rid, 0) + 1 if (goal >= 0 and pos != goal and not fixed) else 0

        def key(rid):
            pos, goal, _ = rs[rid]
            d = g.dist(goal)[pos] if goal >= 0 else 0
            return (goal < 0, -stall[rid], -d if cfg["order_mode"] == "far_first" else d)

        order = sorted((rid for rid, v in rs.items() if not v[2]), key=key)
        rng = random.Random(self.seed * 1000003 + self.t)
        attempts, out, all_wait = 0, None, False
        while True:
            out, failed = plan_window(g, rs, order, W)
            if out:
                break
            attempts += 1
            if attempts <= len(order):                         # 실패한 로봇을 최우선으로
                order.remove(failed)
                order.insert(0, failed)
            elif attempts <= len(order) + 3:
                rng.shuffle(order)
            else:                                              # 안전 장치
                if cfg["fallback"] == "pibt":                  # PIBT 한 스텝 이동 후 바로 다시 계획
                    # 목표에 못 간 시간이 길수록 우선 (유휴 로봇의 귀가 포함). 같으면 먼 로봇 우선
                    prio = {rid: self._elapsed.get(rid, 0) + (g.dist(v[1])[v[0]] / g.N if v[1] >= 0 else -1)
                            for rid, v in rs.items()}
                    step = pibt_step(g, rs, prio, rng)
                    paths = {rid: [v[0]] + [step[rid]] * W for rid, v in rs.items()}
                    self._short = True
                else:                                          # 전원 대기 (현재 상태는 충돌 없음)
                    paths = {rid: [v[0]] * (W + 1) for rid, v in rs.items()}
                out = (paths, {rid: 0 for rid in rs})
                all_wait = True
                break
        paths, costs = out
        if cfg["lns_iters"] and len(order) > 1 and not all_wait:   # 전원 대기면 개선할 경로가 없음
            paths, costs = lns_improve(g, rs, order, paths, costs, W, cfg["lns_iters"], rng)
        dt = time.perf_counter() - t0
        self.replan_log.append(dict(t=self.t, sec=round(dt, 4), robots=len(self.robots),
                                    retries=attempts, fallback=all_wait))
        if self._pending_event is not None:
            self._pending_event["replan_s"] = round(dt, 4)
            self._pending_event = None
        return paths

    # ----- baseline: 로봇별 개별 최단 경로, 충돌 시 대기
    def _baseline_positions(self):
        g = self.g
        rng = random.Random(f"{self.seed}:b{self.t}")
        pos = {r["id"]: r["pos"] for r in self.robots}
        want = {}
        for r in self.robots:
            goal = self._goal(r)
            p = r["pos"]
            if goal < 0 or p == goal or r["phase"] in ("load", "unload") or g.dist(goal)[p] >= INF:
                want[r["id"]] = p
                continue
            h = g.dist(goal)
            if r["stall"] >= self.cfg["stall_patience"] and rng.random() < 0.5:
                want[r["id"]] = rng.choice(g.adj[p])
            else:
                want[r["id"]] = next(j for j in g.adj[p] if h[j] < h[p])
        n = len(self.robots)
        ids = [r["id"] for r in self.robots]
        ids = ids[self.t % n:] + ids[:self.t % n]              # 우선권 회전
        move = {i for i in ids if want[i] != pos[i]}
        changed = True
        while changed:
            changed = False
            stay_cells = {pos[i] for i in ids if i not in move}
            claimed = {}
            for i in ids:
                if i not in move:
                    continue
                c = want[i]
                swap = any(j in move and want[j] == pos[i] and pos[j] == c for j in ids if j != i)
                if c in stay_cells or c in claimed or swap:
                    move.discard(i)
                    changed = True
                    break
                claimed[c] = i
        return {i: (want[i] if i in move else pos[i]) for i in ids}

    # ----- 메인 루프
    def _done(self):
        return (not self.pending and not self.future and not self.events
                and all(r["phase"] == "idle" for r in self.robots))

    def run(self):
        cfg = self.cfg
        while not self._done() and self.t < cfg["max_steps"]:
            if not self.ckpts or self.ckpts[-1]["t"] != self.t:
                self.ckpts.append(self._save())
            self._apply_events()
            self._release()
            self._assign()
            self._arrivals()
            nxt_ev = self.events[0]["t"] if self.events else None
            if self.strategy == "baseline":
                self._step(self._baseline_positions())
                continue
            self._short = False
            paths = self._plan()
            for k in range(1, (1 if self._short else cfg["horizon"]) + 1):
                changed = self._step({rid: p[k] for rid, p in paths.items()})
                if changed or (nxt_ev is not None and self.t >= nxt_ev):
                    break
        return self

    # ----- 롤링 재계획: 시점 t 이전 결과는 유지, 이후만 다시 계산
    def add_event(self, event: dict) -> dict:
        """event = {"t": 100, "add_orders": 10, "robots": 6}. 반환: result()"""
        t = event["t"]
        snap = max((s for s in self.ckpts if s["t"] <= t), key=lambda s: s["t"])
        self._load(snap)
        self.ckpts = [s for s in self.ckpts if s["t"] <= snap["t"]]
        self.events = sorted(self.events + [dict(event)], key=lambda e: e["t"])
        rec = dict(event)
        self.event_log.append(rec)
        self._pending_event = rec
        t0 = time.perf_counter()
        self.run()
        rec["recompute_total_s"] = round(time.perf_counter() - t0, 3)
        rec["resumed_from_t"] = snap["t"]
        return self.result()

    # ----- 결과 (DR-03)
    def result(self) -> dict:
        passes, waits = {}, {}
        prev = {}
        for fr in self.frames:
            cur = {r["id"]: (r["x"], r["y"]) for r in fr["robots"]}
            for r in fr["robots"]:
                p = (r["x"], r["y"])
                if r["id"] in prev and prev[r["id"]] != p:
                    passes[p] = passes.get(p, 0) + 1
                if r["state"] == "wait":
                    waits[p] = waits.get(p, 0) + 1
            prev = cur
        cells = sorted(set(passes) | set(waits))
        stats = [dict(x=x, y=y, **{"pass": passes.get((x, y), 0), "wait": waits.get((x, y), 0)}) for x, y in cells]
        orders = sorted(self.olog.values(), key=lambda o: o["order_id"])
        done = [o for o in orders if o["status"] == "done"]
        coll = find_collisions(self.frames)
        return dict(
            scenario_id=self.scenario_id, strategy=self.strategy, map_version=self.map_version,
            seed=self.seed, total_steps=len(self.frames) - 1,
            completed=self._done() and len(done) == len(orders),
            frames=self.frames, orders=orders, cell_stats=stats, collisions=coll,
            meta=dict(
                orders_done=len(done), orders_total=len(orders),
                avg_order_time=round(sum(o["done_t"] - o["arrival_t"] for o in done) / len(done), 2) if done else None,
                total_waits=sum(waits.values()), total_moves=sum(passes.values()),
                replans=len(self.replan_log),
                replan_avg_s=round(sum(x["sec"] for x in self.replan_log) / max(1, len(self.replan_log)), 4),
                replan_max_s=max((x["sec"] for x in self.replan_log), default=0),
                fallbacks=sum(1 for x in self.replan_log if x.get("fallback")),
                events=self.event_log, config=self.cfg))


# --------------------------------------------------------------------------- 공개 함수
def simulate(map_json: dict, scenario: dict, strategy: str = "optimized", seed: int = 42,
             config: dict | None = None) -> dict:
    """엔진 진입점: simulate(map, scenario, strategy) -> log (순수 함수, 같은 입력이면 같은 결과)."""
    return Sim(map_json, scenario, strategy, seed, config).run().result()


def compare(map_json: dict, scenario: dict, seed: int = 42, config: dict | None = None) -> dict:
    """기준 전략과 최적화 전략을 같은 시드, 주문으로 실행하고 개선율을 계산한다 (FR-18, FR-28)."""
    b = simulate(map_json, scenario, "baseline", seed, config)
    o = simulate(map_json, scenario, "optimized", seed, config)
    imp = lambda a, c: round((a - c) / a * 100, 1) if a else None
    return dict(
        baseline=b, optimized=o,
        summary=dict(
            baseline_steps=b["total_steps"], optimized_steps=o["total_steps"],
            improvement_pct=imp(b["total_steps"], o["total_steps"]),
            baseline_avg_order=b["meta"]["avg_order_time"], optimized_avg_order=o["meta"]["avg_order_time"],
            avg_order_improvement_pct=imp(b["meta"]["avg_order_time"] or 0, o["meta"]["avg_order_time"] or 0),
            baseline_waits=b["meta"]["total_waits"], optimized_waits=o["meta"]["total_waits"],
            collisions=(len(b["collisions"]), len(o["collisions"]))))
