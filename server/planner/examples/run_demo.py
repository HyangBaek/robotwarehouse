"""python run_demo.py -> S2/S2-H 비교, 롤링 재계획 시간, 로그 JSON 저장"""
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))  # planner/ 의 상위 폴더

import json
from planner.engine import compare, Sim
from planner.testmap import make_warehouse, W2

m = make_warehouse(**W2)
for name, sc in [("S2", dict(robots=4, inbound=25, outbound=25)),
                 ("S2-H", dict(robots=8, inbound=25, outbound=25))]:
    c = compare(m, sc, seed=42)
    print(name, c["summary"])

# 롤링 재계획 (S2-R): 스텝 100에 주문 10건 추가 + 로봇 4 -> 6대
sim = Sim(m, dict(robots=4, inbound=25, outbound=25), "optimized", 42).run()
res = sim.add_event(dict(t=100, add_orders=10, robots=6))
ev = res["meta"]["events"][0]
print("S2-R", "steps", res["total_steps"], "done", res["meta"]["orders_done"], "/", res["meta"]["orders_total"],
      "collisions", len(res["collisions"]), "| 재계획 1회", ev["replan_s"], "s | 이후 전체 재계산", ev["recompute_total_s"], "s")

json.dump({k: res[k] for k in ("frames", "orders", "cell_stats", "collisions")}, open(pathlib.Path(__file__).with_name("sim_log_S2R.json"), "w"))
