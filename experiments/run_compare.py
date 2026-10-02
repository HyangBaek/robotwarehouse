"""비교 실험: python experiments/run_compare.py --scenario S2 --robots 4"""
import argparse

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--scenario", default="S2")
    p.add_argument("--robots", type=int, nargs="+", default=[2, 4, 6, 8])
    p.add_argument("--orders", type=int, nargs="+", default=[20, 50])
    args = p.parse_args()
    # TODO(하재윤): 조합별 baseline/optimized 실행 → results/summary.csv (TC-PF-06, 보고서 4.2)
    raise SystemExit("TODO: 구현 필요")
