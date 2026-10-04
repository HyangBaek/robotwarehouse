# VR 클라이언트 ↔ 서버 인터페이스 (VR 구현 기준)

> 구현 시나리오 5장 메시지 형식을 따랐고, 문서에 없어서 **VR 쪽에서 가정한 항목**은 표에 `가정`으로 표시했습니다.
> `docs/api.md` 확정본과 다르면 Unity는 `Assets/RobotWarehouse/Scripts/Runtime/Network/ApiRoutes.cs` 한 파일만 고치면 됩니다.
> 모의 서버(`mock_server/server.py`)가 이 계약을 그대로 구현하고 있어 서버 팀의 참고 구현으로 쓸 수 있습니다.

## 1. 연결

| 항목 | 값 | 비고 |
|---|---|---|
| REST 기본 주소 | `http://{서버IP}:{포트}` (기본 8000) | 에디터 메뉴 1. 서버 연결 → `Resources/ServerConfig.asset`에 저장, 빌드에 포함 |
| WebSocket | `ws://{서버IP}:{포트}/ws/{session_id}` | 세션별 이벤트 채널 (실제 서버도 이 경로로 통일) |
| `session_id` | 앱 실행마다 12자리 무작위 | 모든 POST 본문에 포함 |
| 상태 확인 | `GET /health` | **가정**. 앱 시작(자동 연결)·다시 연결 때 호출 |
| 재연결 | WebSocket이 끊기면 5초 간격 재시도 (EX-02) | 재연결 뒤 마지막 `sim_id` 프레임 이어 받기 |
| 요청 시간 상한 | 30초 (EX-04) | 초과 시 "응답이 늦어요" + 다시 시도 버튼 |

## 2. REST (VR → 서버)

| 메서드·경로 | VR이 보내는 본문 | VR이 기대하는 응답 | 시나리오 |
|---|---|---|---|
| `POST /map/text` | `{session_id, text}` | `{request_id}` / 빈 문장이면 400 | SC-01 |
| `POST /map/voice` | multipart `session_id`, `audio`(16kHz 16bit mono WAV), 선택 `question_id` | `{request_id}` | SC-02 · 음성 답변은 `question_id` 포함(**가정**) |
| `POST /map/answer` | `{session_id, question_id, text}` | `{request_id}` | SC-03 |
| `POST /map/confirm` | `{session_id, map_version}` | `{status: "confirmed"}` / 없는 버전 404 | SC-01 |
| `POST /map/edit` | `{session_id, map_version, moves:[{rack_id, from:[x,y], to:[x,y]}]}` | `{request_id}` → WS `map_ready` 또는 `question` | SC-11 |
| `POST /scenario` | `{session_id, map_version, robots, inbound, outbound, strategy:"optimized"}` | `{sim_id}` / 확인 전이면 409 | SC-04 |
| `GET /sim/{id}/frames?from=&to=` | — | `[{t, robots:[{id,x,y,state,task_id}]}]` (`to` 포함, 200개 단위로 요청) | SC-05 |
| `GET /sim/{id}/stats` | — | `[{x, y, pass, wait}]` | SC-06 |
| `POST /sim/{id}/event` | `{session_id, t, add_orders, robots}` | `{status}` → WS `sim_ready`(`replan_from`) | SC-08 |
| `POST /compare` | `{session_id, sim_id}` | `{request_id}` → WS `compare` | SC-07 (실제 서버 구현됨) |
| `POST /analyze` | `{session_id, sim_id}` | `{request_id}` → WS `analysis` | SC-09 |
| `POST /improve/approve` | `{session_id, proposal_id}` | `{sim_id}` → WS (`map_ready`) → `sim_ready` | SC-10 |

오류 응답은 `{"error": {"code", "message"}}` 또는 FastAPI 기본 `{"detail"}` 둘 다 읽습니다.

## 3. WebSocket (서버 → VR)

모든 메시지에 `type` 필드가 있어야 합니다. 나머지 필드는 최상위 또는 `data` 아래 둘 다 읽습니다.

| type | 필드 | VR 동작 |
|---|---|---|
| `status` | `{node, message}` | **가정**. Agent 노드 진행을 패널에 한 줄씩 표시 (시연 때 "판단 → 도구 실행"이 보이게) |
| `transcript` | `{text}` | 인식 문장 표시. 빈 문자열이면 "잘 못 들었어요" (EX-03) |
| `map_ready` | `{map_version, map, summary, defaults_applied[], confirmed?}` | 3D 창고 생성, 요약·기본값 안내, 확인 버튼 활성. `confirmed: true`면 확인 생략(**가정**, 개선안 승인 지도) |
| `question` | `{question_id, text, error_cells[[x,y]...], map?}` | 질문 표시, 답변 입력 활성, 문제 칸 빨간색. `map`이 같이 오면 실패 지도를 먼저 그림(**가정**) |
| `sim_ready` | `{sim_id, strategy, total_steps, summary, replan_from?, replan_ms?}` | 로봇 생성, 프레임 200개씩 받으며 재생, 통계 요청. `replan_from`이 있으면 그 스텝 이후만 교체 |
| `compare` | `{baseline, optimized, improvement_pct}` | 결과 패널에 두 값과 개선율 |
| `analysis` | `{bottlenecks[{x,y,wait}], explanation, proposals[{proposal_id,type,text}]}` | 병목 마커, 설명, 개선안 버튼, 히트맵 자동 켜기 |
| `error` | `{code, message}` | 빨간 글씨 표시 |
| (모든 메시지) | `audio_url` | 있으면 TTS로 재생 (FR-30, **가정**) |

`total_steps`는 **마지막 프레임의 `t`** 로 해석합니다(프레임은 `t = 0 … total_steps`). 서버가 프레임 수로 보내도 VR이 받은 마지막 `t`로 맞춥니다.

## 4. 로봇 `state` 값과 색

| state | 색 |
|---|---|
| `move`, `moving`, `move_empty` | 파랑 |
| `carry`, `loaded`, `move_loaded`, `load`, `unload`, `pick`, `drop` | 주황 |
| `wait`, `waiting`, `blocked` | 빨강 (히트맵 `wait` 집계와 같은 의미) |
| `charge`, `charging` | 초록 |
| 그 외 (`idle`) | 회색 |
