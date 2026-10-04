# Unity ↔ 서버 API 명세

> 구현과 이 문서가 다르면 이 문서를 먼저 고치고 공지합니다. 구현: `server/api/`, VR 쪽 계약: `docs/vr_client_api.md`.
> REST는 요청을 받자마자 응답하고, 처리 결과는 WebSocket으로 보냅니다.

## REST
| 메서드·경로 | 요청 | 응답 | 결과 (WS) |
|---|---|---|---|
| GET /health | — | {status: "ok"} | |
| POST /map/text | {session_id, text} | {request_id} / 빈 문장 400 | map_ready 또는 question (필수 정보가 빠지면 MISSING_INFO 질문) |
| POST /map/voice | multipart {session_id, audio(16kHz 16bit mono WAV), question_id?} | {request_id} | transcript → map_ready 또는 question. 무음 STT_EMPTY |
| POST /map/answer | {session_id, question_id, text} | {request_id} / 없는 질문 404, 지난 질문 409 | map_ready 또는 question |
| POST /map/confirm | {session_id, map_version} | {status: "confirmed", map_version} / 없는 버전 404 | |
| POST /map/edit | {session_id, map_version, moves[{rack_id, from, to}]} | {request_id} | map_ready 또는 question |
| POST /scenario | {session_id, map_version, robots(1~30), inbound(0~200), outbound(0~200), spec_b_pct(0~100), strategy} | {sim_id} / 확인 전 409 / 규격 랙 없음 400 SPEC_UNAVAILABLE | sim_ready |
| POST /scenario/text | {session_id, map_version, text, robots?, inbound?, outbound?} | {request_id} / 확인 전 409 | scenario |
| POST /scenario/voice | multipart {session_id, map_version, audio, robots?, inbound?, outbound?} | {request_id} / 확인 전 409 | transcript → scenario |
| GET /sim/{id}/frames | from, to (to 포함) | [{t, robots[{id, x, y, state, task_id}]}] | |
| GET /sim/{id}/stats | — | [{x, y, pass, wait}] | |
| POST /sim/{id}/event | {session_id, t, add_orders, robots} | {status: "replanning"} | sim_ready (replan_from, replan_ms) |
| POST /compare | {session_id, sim_id} | {request_id} | compare |
| POST /analyze | {session_id, sim_id} | {request_id} | analysis |
| POST /improve/approve | {session_id, proposal_id} | {sim_id} | (map_ready) → sim_ready |
| POST /report | {session_id, sim_id} | {request_id} | report (기준 전략 비교 포함, 첫 요청 1~5초) |
| GET /sim/{id}/report | — | 리포트 JSON (WS report 와 같은 내용) | |
| GET /sim/{id}/report.html | — | 노트북 브라우저용 리포트 페이지 (visionOS 스타일) | |
| GET /sims | session_id?, limit? | {sims: [{sim_id, created_at, map_summary, robots, inbound, outbound, spec_b_pct, total_steps, orders_done, orders_total, completed, events}]} (SQLite, 최신순) | |
| POST /sim/{id}/load | {session_id} | {status: "loading"} | map_ready(confirmed) -> sim_ready(history, scenario) |
| GET /sim/{id}/routes | — | {robots: [{id, moves, path: [[t, x, y, state, task_id], ...]}]} 위치나 상태가 바뀐 스텝만 | |
| GET /sim/{id}/routes.csv | — | 전체 동선 로그 CSV (t, robot, x, y, state, task_id) | |
| GET /sim/{id}/routes.html, GET /sims.html | — | 로봇 동선 지도(로봇별 선 켜고 끄기, 시간 슬라이더), 기록 목록 페이지 | |

오류 응답: `{"error": {"code", "message"}}` (없는 sim_id는 FastAPI 기본 `{"detail"}`).

## WebSocket `/ws/{session_id}`
`session_id`는 클라이언트가 만듭니다 (VR: 앱 실행마다 12자리). 서버가 ID를 발급하는 `/ws`(첫 메시지 `{type: "hello", session_id}`)는 테스트·디버그용입니다.

| type | 필드 |
|---|---|
| status | {node, message} — Agent 진행 단계 (STT, 지시 해석 (gpt-oss:20b), regex_fallback, 격자 지도 생성, 지도 검증, 수정 질문, 시뮬레이션 실행, 로그 분석, 비교 리포트, 롤링 재계획, 개선안 제안) |
| transcript | {text} |
| map_ready | {map_version, map, summary, defaults_applied[], confirmed?, scenario_defaults?{robots, inbound, outbound, basis}} |
| question | {question_id, text, error_cells[[x, y]], errors[], map?, missing?[]} — 필수 정보 누락이면 errors=["MISSING_INFO"], missing=빠진 키, map 없음 |
| scenario | {map_version, robots, inbound, outbound, run, changed[], basis} — 시나리오 문장·음성 해석 결과. VR은 패널 값에 반영하고 run이면 실행 |
| sim_ready | {sim_id, strategy, total_steps, summary, completed, issues[], replan_from?, replan_ms?} — 주문을 다 처리하지 못해도 보낸다. 그때 `summary["처리 못 함"]`에 원인별 문장 |
| compare | {baseline, optimized, improvement_pct} |
| analysis | {bottlenecks[{x, y, wait}], explanation, issues[], proposals[{proposal_id, type(dock_add, robot_count, storage_weight, one_way, map_resize, order_count), text, reason, effects}]} — 처리 못 한 주문이 있으면 설명 맨 앞에 원인, 개선안 앞쪽에 재시뮬레이션 권고 |
| report | {sim_id, map_summary, scenario, kpis, robots[], orders, timeline[], baseline?, comparison[], insights[], bottlenecks[], html_url} — 최종 리포트 (`tools/report.py`) |
| error | {code, message} — EMPTY_TEXT, STT_EMPTY, STT_UNAVAILABLE, STT_ERROR, QUESTION_LIMIT, AGENT_ERROR, COLLISION, ENGINE_ERROR, IMPROVE_INVALID |

로봇 `state`: move_empty, move_loaded, load, unload, wait, idle.

## 지도 필수 정보와 되묻기
필수 항목 (VR 첫 화면 안내와 같은 순서, `tools/map_generator.REQUIRED_ITEMS`): 랙 줄 수, 통로 폭, 입하 도크 수, 출하 도크 수, 랙 단 수, 충전 구역(왼쪽/오른쪽), 구역 분할(하나/둘), 일방통행(두 구역일 때, 없음/북/남).
- 빠진 항목은 한 질문으로 모아서 묻는다. 일부만 답하면 남은 항목만 다시 묻는다.
- "기본값", "알아서", "나머지는 기본값"이면 빠진 항목을 기본값으로 채우고 `defaults_applied`에 적는다.
- 질문 한도(3회)에 닿으면 남은 항목은 기본값으로 채운다.
- 정보가 다 모인 뒤 지도 검증 오류(도크 0개, 통로 없음, 일방통행 막다른 길)가 있으면 기존처럼 문제 칸과 함께 되묻는다.

## 권장 시나리오
`scenario_defaults.robots` = 도크 수 × 2 + 4 (2~16대, 랙 수 × 2 + 2 이하), 주문은 입하·출하 각 25건. 근거는 `experiments/run_compare.py` 보정 실험 (README 5장).

## 제품 규격과 수량 (FR-11, 12, 15)
- 랙마다 `racks[].slot_spec` 을 둔다: `pallet_1100x1100`(한국 표준 파레트, 기본) 또는 `pallet_1200x1000`. 지도 요구사항 `racks_b` 줄만큼 오른쪽 랙이 1200x1000 규격 ("랙 8줄, 그중 1200 규격 랙 2줄")
- 주문 1건 = 파레트 1개 (`qty=1`). 입하, 출하 제품 수량은 `inbound`, `outbound` 개수. `spec_b_pct` 는 그중 1200x1000 규격 비율이고, 입하와 출하 각각 비율대로 정확한 개수를 나눈다
- 엔진은 주문 규격과 같은 규격의 랙에만 보관하고, 남은 용량을 넘지 않는다. `spec_b_pct=0` 이면 이전과 같은 주문이 나온다
- 리포트 `orders.by_spec`: 규격별 주문 수, 완료 수, 평균 처리 시간

## 운영 규칙 (FR-17)
- `rules.one_way[]`: `from -> to` 벡터 방향으로만 이동
- `rules.passing_allowed[]`: 점 두 개씩 묶은 직선 구간 (명세 예시 `[[10, 1], [10, 18]]`). 일방통행 안에 있어도 양방향 통행 허용
- 엔진과 독립된 검사기(`tools/invariants.rule_violations`)가 일방통행 역주행을 검사하고, 리포트 `kpis.rule_violations` 로 보여 준다

## 저장 (DR-03)
SQLite(`DB_PATH`, 기본 `server/data/warehouse.db`)에 지도, 시나리오, 시뮬레이션(스텝별 로봇 위치, 주문 처리 기록, 칸별 통과와 대기), 리포트, 개선안 승인을 저장한다. 서버를 다시 켜도 기록 조회, 리포트, 다시 불러오기, 롤링 재계획이 된다 (엔진 상태는 같은 입력으로 다시 계산).


## 처리 못 한 주문 (팀 방침)
시뮬레이션은 끝까지 돌았지만 주문 일부를 처리하지 못한 경우입니다. 분석 실패가 아니므로 오류로 막지 않고 결과를 보내며 원인을 나눠 표시합니다 (`server/tools/outcome.py`). 충돌만 엔진 버그로 보고 `COLLISION` 오류로 막습니다. "미완료"라는 말은 쓰지 않습니다.

| code | 실제로 일어난 일 | 누구 문제 | 화면 표현 | 재시뮬레이션 권고 (개선안 type, apply) |
|---|---|---|---|---|
| no_capacity | 같은 규격 랙에 입고할 빈칸이 없음 | 창고 조건 | 보관 공간 부족으로 입고 n건 처리 못 함 | 랙 줄 수 늘리기 (`map_resize` racks), 입하 줄이기 (`order_count` inbound) |
| no_stock | 같은 규격 재고가 없음 | 창고 조건 | 재고 부족으로 출고 n건 처리 못 함 | 출하 줄이기, 입하 늘리기 (`order_count`) |
| unreachable | 로봇이 랙·도크에 갈 길이 없음 | 지도 | 길이 막혀 n건 처리 못 함 | 자동 권고 없음. 지도 직접 수정 |
| deadlock | 로봇끼리 막혀 엔진이 멈춤 (PIBT 엔진이 알려 주는 경우) | 알고리즘 한계 | 로봇 정체로 t=○에서 중단, n건 처리 못 함 | 로봇 줄이기 (`robot_count`), 통로 넓히기 (`map_resize` aisle_width) |
| max_steps | 스텝 상한(3000) 안에 못 끝냄 | 처리량 부족 | 제한 시간 안에 n건 처리 못 함 | 로봇 늘리기 (`robot_count`) |

기본 규격(1100x1100)이 아닌 주문이면 화면 표현 끝에 규격과 건수를 붙입니다. `issues[]` 항목: `{code, count, owner, what, label, t?, specs?}`.
분석 Agent(LLM)는 집계 수치에 더해 `처리_못_함`(건수, 원인별 실제로 일어난 일·누구 문제·화면 표현)을 받고, 설명 첫 문장을 원인으로 시작하며 원인을 고치는 권고를 먼저 고릅니다.
비교 화면은 한쪽이라도 주문을 다 처리하지 못하면 `improvement_pct: null`, 이유는 `note`.
WebSocket이 끊긴 동안 보낸 결과 메시지(status 제외)는 세션별 최대 20개를 보관했다가 다시 연결하면 보냅니다 (EX-02).
