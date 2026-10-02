"""ITC-01~07, 17 (통합 테스트 케이스 7장). 라우트 구현 후 xfail 제거."""
import pytest
from fastapi.testclient import TestClient
from api.main import create_app


@pytest.mark.xfail(reason="/map/text 미구현")
def test_itc02_map_text(tmp_path):
    c = TestClient(create_app(db_path=tmp_path / "t.db"))
    with c.websocket_connect("/ws") as ws:
        sid = ws.receive_json()["session_id"]
        r = c.post("/map/text", json={"session_id": sid, "text": "랙 4줄"})
        assert r.status_code == 200
