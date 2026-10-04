"""혼잡 실험: 로봇이 많을 때 교착이 생기는지, 대체 경로(PIBT)와 도크 배정 옵션이 푸는지.

실행 (저장소 루트에서):  python experiments/run_congestion.py
출력: experiments/results/congestion.csv, 표 출력.

변형 (모두 최적화 전략)
  wait            계획이 모두 실패하면 전원 대기 (이전 방식)
  pibt            계획이 모두 실패하면 PIBT 한 스텝
  pibt+상한        + 도크 하나로 동시에 향하는 로봇 최대 3대 (넘는 주문은 보류)
  pibt+상한+분산   + 같은 종류 도크 중 향하는 로봇이 가장 적은 도크 선택
"""
from __future__ import annotations

import csv
import statistics as st
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "server"))
sys.path.insert(0, str(ROOT / "experiments"))

from planner import simulate  # noqa: E402
from planner.testmap import make_warehouse  # noqa: E402
from tools.map_generator import generate_grid_map  # noqa: E402
from run_compare import MAPS  # noqa: E402

VARIANTS = {
    "wait": {"fallback": "wait"},
    "pibt": {"fallback": "pibt"},
    "pibt+상한": {"fallback": "pibt", "dock_limit": 3},
    "pibt+상한+분산": {"fallback": "pibt", "dock_limit": 3, "dock_select": "least_loaded"},
}
MAX_STEPS = 2000
# (지도, 로봇 수 목록, 입하, 출하)
CASES = [(name, (16, 24, 30), 50, 50) for name in ("W1", "W2", "W3", "W6", "L12")] + [("TEST", (20, 30, 35), 140, 140)]


def get_map(name):
    if name == "TEST":   # 인계서 3장 #8 교착 재현 지도: 도크 6개가 모두 서쪽 벽, 앞 통로 폭 2칸
        return make_warehouse(n_lines=12, line_len=30, n_in=4, n_out=2, n_charge=30)
    return generate_grid_map(MAPS[name])[0]


def run_one(job):
    name, robots, seed, variant, n_in, n_out = job
    m = get_map(name)
    t0 = time.perf_counter()
    log = simulate(m, {"scenario_id": name, "map_version": "exp", "robots": robots, "inbound": n_in,
                       "outbound": n_out}, "optimized", seed, {**VARIANTS[variant], "max_steps": MAX_STEPS})
    done = log["meta"]["orders_done"]
    return dict(map=name, robots=robots, seed=seed, variant=variant, orders=n_in + n_out, completed=log["completed"],
                orders_done=done, total_steps=log["total_steps"], fallbacks=log["meta"]["fallbacks"],
                collisions=len(log["collisions"]), sec=round(time.perf_counter() - t0, 1))


def main():
    jobs = [(name, n, seed, v, i, o) for name, ns, i, o in CASES for n in ns for seed in (1, 2) for v in VARIANTS]
    jobs.sort(key=lambda j: -(j[1] * (3 if j[0] == "TEST" else 1)))     # 오래 걸리는 것부터
    print(f"{len(jobs)}회 실행, 상한 {MAX_STEPS}스텝")
    with ProcessPoolExecutor(int(sys.argv[1]) if len(sys.argv) > 1 else 12) as ex:
        runs = list(ex.map(run_one, jobs))
    out = ROOT / "experiments" / "results" / "congestion.csv"
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(runs[0]))
        w.writeheader()
        w.writerows(sorted(runs, key=lambda r: (r["map"], r["robots"], r["variant"], r["seed"])))
    print(f"\n{'지도':5} {'로봇':>4} {'변형':14} {'완료':>5} {'처리 주문':>9} {'스텝(완료분)':>12} {'대체경로':>8} {'충돌':>4} {'초':>6}")
    order = [c[0] for c in CASES]
    for key in sorted({(r["map"], r["robots"]) for r in runs}, key=lambda k: (order.index(k[0]), k[1])):
        for v in VARIANTS:
            rs = [r for r in runs if (r["map"], r["robots"], r["variant"]) == (*key, v)]
            ok = [r["total_steps"] for r in rs if r["completed"]]
            print(f"{key[0]:5} {key[1]:>4} {v:14} {sum(r['completed'] for r in rs)}/{len(rs):<3} "
                  f"{st.mean(r['orders_done'] for r in rs):>6.0f}/{rs[0]['orders']:<3} "
                  f"{(f'{st.mean(ok):.0f}' if ok else '-'):>12} {st.mean(r['fallbacks'] for r in rs):>8.0f} "
                  f"{sum(r['collisions'] for r in rs):>4} {st.mean(r['sec'] for r in rs):>6.0f}")
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
