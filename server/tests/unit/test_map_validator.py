"""TC-MAP-01~09 (개발자 테스트 시나리오 2.1). 구현 전에는 xfail."""
import pytest
from tests.conftest import load
from tools.map_validator import validate_map


@pytest.mark.xfail(raises=NotImplementedError, reason="validate_map 미구현")
def test_tc_map_01_valid(map_w1):
    r = validate_map(map_w1)
    assert r["valid"] and r["errors"] == []
