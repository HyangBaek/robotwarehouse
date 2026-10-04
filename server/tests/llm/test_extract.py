"""TC-AGT-11·12: 실제 LLM 지도 생성 성공률 (pytest -m llm). Ollama 가 응답하지 않으면 건너뛴다."""
import pytest

from agent.interpreter import REQ_SCHEMA, clean
from agent.prompts import load
from tests.eval_sentences import check, load_rows

pytestmark = pytest.mark.llm
ROWS = load_rows()


@pytest.fixture(scope="module")
def llm():
    from services.llm import OllamaLLM
    m = OllamaLLM(timeout=60)
    if not m.ping():
        pytest.skip("Ollama 응답 없음 (SSH 터널·모델 확인)")
    return m


@pytest.mark.parametrize("row", [r for r in ROWS if r["id"] in ("W1-a", "W2-a", "W5-a", "W3-a", "W6-a")],
                         ids=lambda r: r["id"])
def test_core_sentences(llm, row):
    got = clean(llm.invoke(load("interpret"), row["text"], REQ_SCHEMA))
    assert check(got, row) == []


def test_w5_docks_null(llm):
    got = clean(llm.invoke(load("interpret"), "랙 6줄, 통로 폭 3미터", REQ_SCHEMA))
    assert "dock_in" not in got and "dock_out" not in got      # M02 되묻기가 일어나야 함


def test_success_rate(llm):
    ok = sum(not check(clean(llm.invoke(load("interpret"), r["text"], REQ_SCHEMA)), r) for r in ROWS)
    assert ok / len(ROWS) >= 0.9, f"{ok}/{len(ROWS)}"
