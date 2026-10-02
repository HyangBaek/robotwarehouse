"""ITC-04: Unity 블록 좌표 덤프와 서버 지도 비교.
python scripts/compare_layout.py dump.json map.json"""
import json, sys
from collections import defaultdict


def by_type(cells):
    d = defaultdict(set)
    for c in cells:
        d[c["type"]].add((c["x"], c["y"]))
    return d


if __name__ == "__main__":
    dump, grid = (json.load(open(p, encoding="utf-8")) for p in sys.argv[1:3])
    a, b = by_type(dump["cells"]), by_type(grid["cells"])
    bad = {t: (a[t] ^ b[t]) for t in set(a) | set(b) if a[t] != b[t]}
    print("일치" if not bad else f"불일치: { {t: sorted(v)[:10] for t, v in bad.items()} }")
    sys.exit(1 if bad else 0)
