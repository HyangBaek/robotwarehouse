"""WebSocket 세션 관리: 세션별 연결, 서버 -> VR 메시지 전송. VR 계약: /ws/{session_id} (클라이언트가 ID 생성)."""
import uuid

from fastapi import APIRouter, WebSocket, WebSocketDisconnect

router = APIRouter()


async def _serve(ws: WebSocket, session_id: str, hello: bool):
    await ws.accept()
    store = ws.app.state.deps.store
    s = store.session(session_id)
    s.sockets.append(ws)
    if hello:
        await ws.send_json({"type": "hello", "session_id": session_id})
    await store.flush(s, ws)                          # 끊긴 동안 보낸 결과 메시지 (EX-02)
    try:
        while True:
            await ws.receive_text()   # 클라이언트 ping 등
    except WebSocketDisconnect:
        pass
    finally:
        if ws in s.sockets:
            s.sockets.remove(ws)


@router.websocket("/ws/{session_id}")
async def ws_session(ws: WebSocket, session_id: str):
    await _serve(ws, session_id, hello=False)


@router.websocket("/ws")
async def ws_endpoint(ws: WebSocket):
    """ID 를 서버가 발급하는 방식 (테스트, 디버그용). 첫 메시지 {"type": "hello", "session_id"}."""
    await _serve(ws, uuid.uuid4().hex[:12], hello=True)
