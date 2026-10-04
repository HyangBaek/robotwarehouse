"""python examples/benchmark_cbs.py -> CBS 대비 PP, PP+LNS 해 품질 비교표 (소규모 인스턴스)"""
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))  # planner/ 의 상위 폴더

from planner import benchmark, print_table
from planner.testmap import W1, W2, make_warehouse

# 실제 지도가 오면: m = json.load(open("map_W2.json"))
for title, cfg, kw in [("W1 (소형)", W1, {}),
                       ("W2, 혼잡 구역", W2, dict(region=(2, 12, 1, 16)))]:
    print(f"\n== {title} ==")
    print_table(benchmark(make_warehouse(**cfg), instances=20, timeout=10, **kw))
