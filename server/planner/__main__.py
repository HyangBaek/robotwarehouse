"""python -m planner  (server/ 폴더에서 실행) -> 엔진이 정상 동작하는지 10초 안에 확인하는 간단 실행"""
from .engine import compare
from .testmap import W1, make_warehouse

m = make_warehouse(**W1)
sc = {"scenario_id": "S1", "robots": 2, "inbound": 10, "outbound": 10}
c = compare(m, sc, seed=42)
s = c["summary"]
o = c["optimized"]
print("== planner 동작 확인 (임시 지도 W1, 로봇 2대, 주문 20건) ==")
print(f"기준 전략   : {s['baseline_steps']} 스텝")
print(f"최적화 전략 : {s['optimized_steps']} 스텝  (개선율 {s['improvement_pct']}%)")
print(f"충돌 횟수   : 기준 {s['collisions'][0]}건, 최적화 {s['collisions'][1]}건")
print(f"주문 완료   : {o['meta']['orders_done']}/{o['meta']['orders_total']}")
print("정상" if o["completed"] and not o["collisions"] else "문제 있음 - 출력을 알려 주세요")
