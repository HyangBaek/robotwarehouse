"""WebSocket 세션 관리: 세션별 연결, 서버 → VR 메시지 전송."""
import uuid
from fastapi import APIRouter, WebSocket, WebSocketDisconnect

router = APIRouter()
CONNECTIONS: dict[str, WebSocket] = {}


@router.websocket("/ws")
async def ws_endpoint(ws: WebSocket):
    await ws.accept()
    session_id = uuid.uuid4().hex[:12]
    CONNECTIONS[session_id] = ws
    await ws.send_json({"type": "hello", "session_id": session_id})
    try:
        while True:
            await ws.receive_text()   # 클라이언트 ping 등
    except WebSocketDisconnect:
        CONNECTIONS.pop(session_id, None)


async def send(session_id: str, message: dict) -> None:
    ws = CONNECTIONS.get(session_id)
    if ws is not None:
        await ws.send_json(message)
