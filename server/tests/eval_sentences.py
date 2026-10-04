"""T9: 문장 세트로 요구사항 추출 정답률·지연 측정 (NFR-05). 정규식과 LLM 을 같은 기준으로 비교.

실행 (server/ 에서):  python -m tests.eval_sentences            # 정규식 + LLM(설정대로)
                      python -m tests.eval_sentences --regex-only
정답 기준: CSV 에 값이 있는 항목은 같아야 하고, 비어 있는 항목은 '말하지 않음'(키 없음)이어야 한다.
"""
import csv
import sys
import time
from pathlib import Path

from agent.interpreter import Interpreter, clean
from tools.parse_text import extract_requirements

CSV = Path(__file__).parent / "fixtures" / "sentences.csv"
KEYS = ["racks", "aisle_width", "dock_in", "dock_out", "zones", "one_way", "use_defaults"]


def load_rows():
    return list(csv.DictReader(CSV.read_text(encoding="utf-8").splitlines()))


def expected(row):
    out = {}
    for k in KEYS:
        v = row.get(k, "")
        if v == "":
            continue
        out[k] = True if v == "true" else (v if k == "one_way" else int(v))
    return out


def check(got: dict, row) -> list[str]:
    exp = expected(row)
    got = {k: v for k, v in got.items() if k in KEYS}
    return [f"{k}: 기대 {exp.get(k)} / 결과 {got.get(k)}" for k in KEYS if exp.get(k) != got.get(k)]


def run(name, fn, rows):
    ok, times = 0, []
    print(f"\n== {name}")
    for row in rows:
        t0 = time.perf_counter()
        try:
            got = fn(row["text"])
        except Exception as e:
            got = {"_error": str(e)}
        times.append(time.perf_counter() - t0)
        diff = check(got, row)
        ok += not diff
        if diff:
            print(f"  ✗ {row['id']} {row['text']}\n      {'; '.join(diff)}")
    n = len(rows)
    times.sort()
    print(f"  정답률 {ok}/{n} = {ok / n * 100:.1f}%   지연 평균 {sum(times) / n:.2f}s  "
          f"p50 {times[n // 2]:.2f}s  최대 {times[-1]:.2f}s")
    return ok / n


def main():
    rows = load_rows()
    run("정규식", extract_requirements, rows)
    if "--regex-only" in sys.argv:
        return
    from services.llm import make_llm
    llm = make_llm()
    if llm is None or not llm.ping():
        print("\nLLM 응답 없음 (터널·Ollama 확인) → LLM 측정 생략")
        return
    from agent.prompts import load
    from agent.interpreter import REQ_SCHEMA
    run(f"LLM {llm.model} (대체 경로 없이)", lambda t: clean(llm.invoke(load("interpret"), t, REQ_SCHEMA)), rows)
    it = Interpreter(llm)
    run("LLM + 정규식 대체 경로 (실제 서버 동작)", lambda t: it.extract(t)[0], rows)


if __name__ == "__main__":
    main()
