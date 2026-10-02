"""TC-ENG-01~17 (개발자 테스트 시나리오 2.4). 구현 전에는 xfail."""
import pytest
from engine import simulate
from engine.collision import check_invariants


@pytest.mark.xfail(raises=NotImplementedError, reason="simulate 미구현")
def test_tc_eng_08_no_collision(map_w1):
    log = simulate(map_w1, {"robots": 2, "inbound": 10, "outbound": 10}, seed=42)
    assert check_invariants(log, map_w1) == []
