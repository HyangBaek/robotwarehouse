import json
from pathlib import Path
import pytest

FIX = Path(__file__).parent / "fixtures"


def load(rel: str):
    return json.loads((FIX / rel).read_text(encoding="utf-8"))


@pytest.fixture
def map_w1():
    return load("maps/w1_small.json")
