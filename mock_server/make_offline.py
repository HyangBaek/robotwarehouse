"""Unity 오프라인 재생 데이터(W2 + S2) 생성.
실행: python make_offline.py [출력 폴더]  (기본: ../robotwarehouse/Assets/RobotWarehouse/Resources/Offline)
실제 서버가 완성되면 그 서버의 결과로 교체한다 (EX 시연 대비, E2E 체크리스트 "오프라인 재생 데이터 앱 내장").
"""
import json
import sys
from pathlib import Path

from mock.engine import simulate
from mock.mapgen import generate_map
from mock.parse_text import extract_requirements, fill_defaults

W2 = "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽 벽 쪽에"
out = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).parent.parent / "robotwarehouse/Assets/RobotWarehouse/Resources/Offline")
out.mkdir(parents=True, exist_ok=True)
m = generate_map(fill_defaults(extract_requirements(W2))[0])
log = simulate(m, {"robots": 4, "inbound": 25, "outbound": 25}, "optimized", 42)
assert not log["collisions"]
(out / "w2_map.json").write_text(json.dumps({"map_version": "offline", "map": m}, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
(out / "s2_frames.json").write_text(json.dumps(log["frames"], separators=(",", ":")), encoding="utf-8")
(out / "s2_stats.json").write_text(json.dumps(log["stats"], separators=(",", ":")), encoding="utf-8")
print(out, log["summary"])
