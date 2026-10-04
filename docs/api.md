# Unity ↔ 서버 API 명세

> 구현과 이 문서가 다르면 이 문서를 먼저 고치고 공지합니다. 구현: `server/api/`, VR 쪽 계약: `docs/vr_client_api.md`.
> REST는 요청을 받자마자 응답하고, 처리 결과는 WebSocket으로 보냅니다.

## REST
| 메서드·경로 | 요청 | 응답 | 결과 (WS) |
|---|---|---|---|
| GET /health | — | {status: "ok"} | |
| POST /map/text | {session_id, text} | {request_id} / 빈 문장 400 | map_ready 또는 question |
| POST /map/voice | multipart {session_id, audio(16kHz 16bit mono WAV), question_id?} | {request_id} | transcript → map_ready 또는 question. 무음 STT_EMPTY |
| POST /map/answer | {session_id, question_id, text} | {request_id} / 없는 질문 404, 지난 질문 409 | map_ready 또는 question |
| POST /map/confirm | {session_id, map_version} | {status: "confirmed", map_version} / 없는 버전 404 | |
| POST /map/edit | {session_id, map_version, moves[{rack_id, from, to}]} | {request_id} | map_ready 또는 question |
| POST /scenario | {session_id, map_version, robots(1~30), inbound, outbound, strategy} | {sim_id} / 확인 전 409 | sim_ready |
| GET /sim/{id}/frames | from, to (to 포함) | [{t, robots[{id, x, y, state, task_id}]}] | |
| GET /sim/{id}/stats | — | [{x, y, pass, wait}] | |
| POST /sim/{id}/event | {session_id, t, add_orders, robots} | {status: "replanning"} | sim_ready (replan_from, replan_ms) |
| POST /compare | {session_id, sim_id} | {request_id} | compare |
| POST /analyze | {session_id, sim_id} | {request_id} | analysis |
| POST /improve/approve | {session_id, proposal_id} | {sim_id} | (map_ready) → sim_ready |

오류 응답: `{"error": {"code", "message"}}` (없는 sim_id는 FastAPI 기본 `{"detail"}`).

## WebSocket `/ws/{session_id}`
`session_id`는 클라이언트가 만듭니다 (VR: 앱 실행마다 12자리). 서버가 ID를 발급하는 `/ws`(첫 메시지 `{type: "hello", session_id}`)는 테스트·디버그용입니다.

| type | 필드 |
|---|---|
| status | {node, message} — Agent 진행 단계 (STT, 지시 해석 (gpt-oss:20b), regex_fallback, 격자 지도 생성, 지도 검증, 수정 질문, 시뮬레이션 실행, 로그 분석, 비교 리포트, 롤링 재계획, 개선안 제안) |
| transcript | {text} |
| map_ready | {map_version, map, summary, defaults_applied[], confirmed?} |
| question | {question_id, text, error_cells[[x, y]], errors[], map} |
| sim_ready | {sim_id, strategy, total_steps, summary, replan_from?, replan_ms?} |
| compare | {baseline, optimized, improvement_pct} |
| analysis | {bottlenecks[{x, y, wait}], explanation, proposals[{proposal_id, type, text}]} |
| error | {code, message} — EMPTY_TEXT, STT_EMPTY, STT_UNAVAILABLE, STT_ERROR, QUESTION_LIMIT, AGENT_ERROR, COLLISION, INCOMPLETE, ENGINE_ERROR, IMPROVE_INVALID |

로봇 `state`: move_empty, move_loaded, load, unload, wait, idle.
