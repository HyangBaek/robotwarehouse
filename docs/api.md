# Unity ↔ 서버 API 명세

> 구현과 이 문서가 다르면 이 문서를 먼저 고치고 공지합니다.

## REST
| 메서드·경로 | 요청 | 응답 |
|---|---|---|
| POST /map/text | {session_id, text} | {request_id} |
| POST /map/voice | multipart {session_id, audio} | {request_id} |
| POST /map/answer | {session_id, question_id, text} | {request_id} |
| POST /map/confirm | {session_id, map_version} | {status} |
| POST /map/edit | {session_id, map_version, moves[]} | {request_id} |
| POST /scenario | {session_id, map_version, robots, inbound, outbound} | {sim_id} |
| GET /sim/{id}/frames | from, to | [{t, robots[]}] |
| GET /sim/{id}/stats | — | [{x, y, pass, wait}] |
| POST /sim/{id}/event | {t, add_orders, robots} | {status} |
| POST /analyze | {session_id, sim_id} | {request_id} |
| POST /improve/approve | {session_id, proposal_id} | {sim_id} |

## WebSocket `/ws`
접속 시 `{type: "hello", session_id}` 수신.
type: transcript · map_ready · question · sim_ready · compare · analysis · error
