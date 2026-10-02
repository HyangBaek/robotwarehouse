"""TC-AGT-11·12: 실제 LLM 지도 생성 성공률 (pytest -m llm)."""
import pytest

pytestmark = pytest.mark.llm


@pytest.mark.skip(reason="agent.nodes.composer.interpret 구현 후 활성화")
def test_extract_placeholder():
    pass
