"""비교 실험 (TC-PF-06, 보고서 4.2): 기준 전략 대비 최적화 전략의 처리 시간 개선과 요소별 기여도.

실행 (저장소 루트에서):
    python experiments/run_compare.py                          # 지도 W1, W2, W3, W6, 권장 로봇 수, 시드 1~5
    python experiments/run_compare.py --maps W2 --robots 4 8   # 지정 조합
    python experiments/run_compare.py --ablation               # 요소별 기여도 (보관 위치, 할당, 경로 개선)

출력: experiments/results/summary.csv (조합별 평균), experiments/results/raw/runs.csv (실행별), 표 출력.
같은 지도, 시드면 두 전략의 주문(종류, 도착 스텝)이 같다 (planner.create_orders 시드 고정).
"""
from __future__ import annotations

import argparse
import csv
import statistics as st
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "server"))

from planner import simulate  # noqa: E402
from tools.invariants import check_invariants  # noqa: E402
from tools.map_generator import generate_grid_map  # noqa: E402
from tools.scenario import recommend_scenario  # noqa: E402

MAPS = {  # 시연 문장 W1~W6 중 유효한 지도 + 큰 지도
    "W1": {"racks": 4, "aisle_width": 3, "dock_in": 1, "dock_out": 1},
    "W2": {"racks": 8, "aisle_width": 3, "dock_in": 2, "dock_out": 1, "charge": "right"},
    "W3": {"racks": 6, "zones": 2, "one_way": "N", "use_defaults": True},
    "W6": {"use_defaults": True},
    "L12": {"racks": 12, "aisle_width": 3, "dock_in": 3, "dock_out": 2},
}

# 변형: (전략, 설정). baseline = 무작위 보관 + 선착순 할당 + 로봇별 최단 경로(막히면 대기)
VARIANTS = {
    "baseline": ("baseline", {}),
    "optimized": ("optimized", {}),
}
ABLATION = {
    "opt-무작위보관": ("optimized", {"random_storage": True}),
    "opt-그리디할당": ("optimized", {"use_hungarian": False}),
    "opt-LNS없음": ("optimized", {"lns_iters": 0}),
}


def run_one(job):
    name, robots, seed, variant, inbound, outbound = job
    strategy, cfg = {**VARIANTS, **ABLATION}[variant]
    m, _ = generate_grid_map(MAPS[name])
    t0 = time.perf_counter()
    log = simulate(m, {"scenario_id": name, "map_version": "exp", "robots": robots,
                       "inbound": inbound, "outbound": outbound}, strategy, seed, cfg)
    return dict(map=name, robots=robots, seed=seed, variant=variant, inbound=inbound, outbound=outbound,
                total_steps=log["total_steps"], completed=log["completed"],
                avg_order_time=log["meta"]["avg_order_time"], total_waits=log["meta"]["total_waits"],
                collisions=len(log["collisions"]), invariant_violations=len(check_invariants(log, m)),
                sec=round(time.perf_counter() - t0, 2))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--maps", nargs="+", default=["W1", "W2", "W3", "W6"], choices=list(MAPS))
    p.add_argument("--robots", type=int, nargs="+", default=None, help="생략하면 지도별 권장 로봇 수")
    p.add_argument("--seeds", type=int, nargs="+", default=[1, 2, 3, 4, 5])
    p.add_argument("--orders", type=int, nargs=2, default=[25, 25], metavar=("IN", "OUT"))
    p.add_argument("--ablation", action="store_true", help="요소별 기여도 변형도 실행")
    p.add_argument("--workers", type=int, default=8)
    a = p.parse_args()

    variants = list(VARIANTS) + (list(ABLATION) if a.ablation else [])
    jobs = []
    for name in a.maps:
        robots_list = a.robots or [recommend_scenario(generate_grid_map(MAPS[name])[0])["robots"]]
        for n in robots_list:
            for seed in a.seeds:
                for v in variants:
                    jobs.append((name, n, seed, v, *a.orders))
    print(f"{len(jobs)}회 실행 (지도 {a.maps}, 시드 {a.seeds}, 변형 {variants})")
    with ProcessPoolExecutor(a.workers) as ex:
        runs = list(ex.map(run_one, jobs))

    out = ROOT / "experiments" / "results"
    (out / "raw").mkdir(parents=True, exist_ok=True)
    with open(out / "raw" / "runs.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(runs[0]))
        w.writeheader()
        w.writerows(runs)

    rows = []
    keys = sorted({(r["map"], r["robots"]) for r in runs}, key=lambda k: (a.maps.index(k[0]), k[1]))
    for name, n in keys:
        base = [r for r in runs if (r["map"], r["robots"], r["variant"]) == (name, n, "baseline")]
        b_steps = st.mean(r["total_steps"] for r in base)
        for v in variants:
            rs = [r for r in runs if (r["map"], r["robots"], r["variant"]) == (name, n, v)]
            steps = [r["total_steps"] for r in rs]
            pct = [(b["total_steps"] - r["total_steps"]) / b["total_steps"] * 100
                   for b, r in zip(sorted(base, key=lambda x: x["seed"]), sorted(rs, key=lambda x: x["seed"]))]
            rows.append(dict(
                map=name, robots=n, variant=v, seeds=len(rs),
                steps_mean=round(st.mean(steps), 1), steps_sd=round(st.pstdev(steps), 1),
                improvement_pct_mean=round(st.mean(pct), 1), improvement_pct_min=round(min(pct), 1),
                avg_order_time=round(st.mean(r["avg_order_time"] or 0 for r in rs), 1),
                waits_mean=round(st.mean(r["total_waits"] for r in rs), 1),
                completed=f"{sum(r['completed'] for r in rs)}/{len(rs)}",
                collisions=sum(r["collisions"] for r in rs),
                invariant_violations=sum(r["invariant_violations"] for r in rs),
                sec_mean=round(st.mean(r["sec"] for r in rs), 2)))
        _ = b_steps
    with open(out / "summary.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)

    print(f"\n{'지도':4} {'로봇':>4} {'변형':14} {'처리 스텝(평균±SD)':>18} {'개선율 평균(최소)':>16} {'주문당':>7} {'대기':>7} {'완료':>5} {'충돌':>4} {'위반':>4}")
    for r in rows:
        print(f"{r['map']:4} {r['robots']:>4} {r['variant']:14} {r['steps_mean']:>10} ± {r['steps_sd']:<6} "
              f"{r['improvement_pct_mean']:>8}% ({r['improvement_pct_min']}%) {r['avg_order_time']:>7} "
              f"{r['waits_mean']:>7} {r['completed']:>5} {r['collisions']:>4} {r['invariant_violations']:>4}")
    print(f"\n저장: {out / 'summary.csv'}")


if __name__ == "__main__":
    main()
