# 로봇웨어하우스 — 말로 짓고 로봇이 달리는 무인 물류창고

제4회 경남 AI·SW 경진대회 3-2 (MetaQuest2 VR 피지컬 AI Agent) 출품작입니다.

사용자가 VR 안에서 "랙 8줄, 통로 폭 3m, 입하 도크 2개…"라고 말하면 Agent가 요구사항을 해석해 격자 창고를 짓습니다. 빠진 정보가 있으면 되묻고, 사용자가 확인하면 여러 대의 로봇이 서로 부딪히지 않게 주문을 처리하는 모습을 Quest 2에서 재생합니다.

```
음성 → STT → LLM이 요구사항 JSON 추출 → 코드가 격자 지도 생성·검증 → (오류 시 되묻기) → 사용자 확인
     → 다중 로봇 경로 엔진 → VR 재생 · 히트맵 · 기준 전략 비교 · 롤링 재계획 · 병목 분석
```

**설계 원칙:** LLM은 해석·판단·설명만 맡고, 좌표와 경로는 코드가 계산합니다. LLM 결과는 검증과 사용자 승인을 거친 뒤에만 반영합니다.

| 팀원 | 담당 |
|---|---|
| 방유진 (대표) | Agent 서버 |
| 백향 | VR 클라이언트 · 통합 · 문서 |
| 하재윤 | 경로 알고리즘 · 실험 |

---

## 1. 시스템 구성

```
Quest 2 (APK)                        노트북                                    GPU 서버
┌──────────────┐  127.0.0.1:8000   ┌──────────────────────────────┐  SSH 터널  ┌──────────────────┐
│ Unity VR     │ ─USB adb reverse→ │ FastAPI Agent 서버 (server/)  │ ─────────→ │ Ollama           │
│ 클라이언트    │   REST + WebSocket │  · faster-whisper STT (CPU)   │  11434     │ gpt-oss:20b      │
└──────────────┘                   │  · 지도 생성·검증 도구          │            └──────────────────┘
                                   │  · planner 경로 엔진           │
                                   └──────────────────────────────┘
```

| 폴더 | 내용 |
|---|---|
| `unity/RobotWarehouse/` | Unity 6 VR 클라이언트 (6000.6.3f1, URP, XRI 3.6, OpenXR). 추가 코드는 전부 `Assets/RobotWarehouse/` |
| `server/` | Agent 서버 (FastAPI). 지시 해석, 지도 생성·검증, 경로 엔진 호출, 분석·개선 |
| `server/planner/` | 다중 로봇 경로 계산 엔진 (순수 Python). 자세한 내용은 [server/planner/README.md](server/planner/README.md) |
| `mock_server/` | VR 단독 검증용 모의 서버 (LLM·STT 없이 규칙 기반). 서버가 없을 때의 대체 경로 |
| `docs/` | `api.md`(서버 API), `vr_client_api.md`(VR 계약), `schema.md`(격자 지도 스키마·좌표 규칙) |
| `experiments/`, `scripts/` | 비교 실험, Unity 배치와 서버 지도 비교 도구 |

---

## 2. 빠른 시작

### 2-1. Agent 서버 (노트북)

```bash
cd server
cp .env.example .env
pip install -r requirements.txt

# GPU 서버 Ollama 터널 (창 유지). 팀 Ollama 포트는 서버의 OLLAMA_HOST 값
ssh -N -L 11434:localhost:<팀 Ollama 포트> -p <포트> <계정>@<GPU 서버>

uvicorn api.main:app --host 0.0.0.0 --port 8000
```

- 시작 로그에 `LLM gpt-oss:20b 연결됨`이 나오면 정상입니다. LLM에 닿지 않으면 정규식 해석으로 자동 대체되고, VR 패널에 `regex_fallback`이 표시됩니다.
- STT 모델은 서버 시작 시 한 번 로드합니다 (첫 실행 때만 모델을 내려받아 몇 분 걸림).
- 설정은 `server/.env`에서 바꿉니다.

| 변수 | 기본값 | 설명 |
|---|---|---|
| `LLM_PROVIDER` | `ollama` | `none`이면 정규식 해석만 사용 |
| `LLM_MODEL` / `OLLAMA_BASE_URL` | `gpt-oss:20b` / `http://localhost:11434` | |
| `LLM_THINK` / `LLM_TIMEOUT_SEC` / `LLM_KEEP_ALIVE` | `low` / `8` / `60m` | 추론 강도, 시간 상한(넘으면 정규식 대체), GPU에 모델 유지 시간 |
| `STT_PROVIDER` / `STT_MODEL` | `faster-whisper` / `small` | `none`이면 음성 입력 끔 |
| `STT_DEVICE` / `STT_COMPUTE_TYPE` | `cpu` / `int8` | GPU 사용 시 `cuda` / `float16` |
| `NODE_DELAY_SEC` | `0.3` | Agent 단계 사이 연출 지연 (시연 화면에서 진행이 읽히도록) |

### 2-2. VR 클라이언트 (Quest 2, APK)

1. Unity에서 `unity/RobotWarehouse` 열기 → 상단 메뉴 **RobotWarehouse → 3. Quest 빌드 설정 적용** (HTTP 허용, 인터넷 권한, minSdk 32, IL2CPP ARM64)
2. 서버 주소는 기본값 `127.0.0.1:8000` 그대로 둡니다 (`adb reverse`로 노트북에 연결되므로 Wi-Fi IP를 넣지 않음)
3. File → Build Profiles → Android → Build
4. 시연 전 연결 (Windows PowerShell, 매번 Quest를 꽂은 뒤)
   ```powershell
   adb devices                        # 헤드셋 안에서 USB 디버깅 허용
   adb reverse tcp:8000 tcp:8000
   adb reverse --list
   ```
5. Quest에서 앱 실행 → 자동 연결. 처음 실행할 때 마이크 권한 허용

> 서버를 WSL에서 실행해도 Windows `localhost:8000`이 WSL로 전달되므로 그대로 동작합니다. adb는 Windows에서 실행해야 합니다 (WSL에서는 USB 장치가 보이지 않음).

### 2-3. 에디터에서 확인 · 서버 없이 보기

- 시연 씬은 `Assets/Scenes/MainScene.unity`. **RobotWarehouse → 1. 서버 연결**에서 `127.0.0.1` / `8000` 저장 → Play
- 사용자 패널 흐름: **직접 입력하기 → 생성하기 → √ 확인 → ▶ 시뮬레이션 실행**
- 에디터 조작: 마우스 왼쪽 = UI 클릭, 오른쪽 드래그 = 둘러보기, WASD/QE = 이동, **F1 = 관리자 패널** (예시 문장 W1~W6, 다시 연결, 오프라인 재생, 보기 전환)
- 서버 없이: 관리자 패널의 **오프라인 재생**, 또는 모의 서버 `cd mock_server && uvicorn server:app --host 0.0.0.0 --port 8000`
- 처음 한 번 **RobotWarehouse → 2. 창고 에셋 적용** (Unity Warehouse HDRP 에셋 → URP 사본)

---

## 3. 시연 흐름 (VR)

| 단계 | 사용자 | 시스템 |
|---|---|---|
| 0 | 창고 만들기 화면의 **필요한 정보** 안내 확인 | 랙 줄 수, 통로 폭, 입하/출하 도크 수, 랙 단 수, 충전 구역, 구역 분할, 일방통행(두 구역일 때) |
| 1 | 오른손 A 버튼을 누르고 말하기 (또는 직접 입력, 관리자 패널의 예시 버튼 W1~W6) | STT -> 인식 결과 확인 화면 -> 생성하기 |
| 2 | - | LLM이 요구사항 추출. 진행 체크리스트가 한 칸씩 넘어감 |
| 3 | 빠진 정보를 물으면 선택 버튼이나 말로 답하기 ("3단, 구역은 하나", "나머지는 기본값") | 알아들은 내용과 빠진 항목을 한 번에 되묻기. 일부만 답하면 남은 것만 다시 물음 (최대 3회, 이후 기본값) |
| 4 | - | 격자 지도 생성 -> 검증. 오류(도크 0개, 통로 없음 등)가 있으면 문제 칸을 빨간색으로 표시하고 선택 버튼과 함께 되묻기 |
| 5 | **√ 확인** | 3D 창고 확정. 시뮬레이션 설정 화면에 지도 크기 기준 권장 로봇 수가 채워짐 |
| 6 | "말로 설정"(또는 A 버튼)으로 "로봇 6대, 입하 30건 출하 20건, 1200 규격 30%, 실행해줘", 또는 -/+ 조정 후 **▶ 시뮬레이션 실행** | 말한 항목만 바꾸고 나머지는 권장값 유지. "실행"이라고 하면 바로 실행 -> 로봇 재생, 배속, 경로 선, 히트맵 |
| 7 | **비교** (관리자 패널) | 같은 지도, 주문으로 기준 전략 실행 -> 개선율 |
| 8 | 재생 중 주문, 로봇 추가 (관리자 패널) | 그 시점 이후만 다시 계산 (롤링 재계획) |
| 9 | **병목 분석** -> 개선안 승인 | 병목 칸 표시, Agent 설명, 개선안(도크 추가, 로봇 수, 보관 분산, 일방통행)과 예상 효과 -> 적용 후 재실행, 적용 전/후 비교 |
| 10 | 재생이 끝나면 (또는 **리포트**) | 정면에 리포트 창: 요약 KPI 8개, 핵심 인사이트 / 로봇별 작업 구성, 가동률 / 누적 완료 곡선, 처리 시간 분포 / 기준 전략 비교. 노트북 브라우저에서 `http://서버:8000/sim/{sim_id}/report.html` 로 같은 리포트 |
| 11 | **이전 기록** (창고 만들기 화면) | 저장된 시뮬레이션 목록에서 골라 그때의 지도와 로봇 동선을 다시 재생. 노트북 브라우저 `http://서버:8000/sims.html` 에서 로봇별 동선 지도, CSV |

| VR 조작 | 동작 |
|---|---|
| 컨트롤러 레이 + 트리거 | 패널 버튼 누르기 (템플릿 XRI 레이) |
| **오른손 A 버튼 누르고 말하기** | 음성 입력. 질문 화면이면 답변, 시뮬레이션 설정 화면이면 시나리오. 패널의 "말하기" -> "녹음 종료" 버튼도 같음 |
| 오른손 B | 테이블 앞으로 다시 서기, 패널 다시 배치 |
| **왼손 Y** (에디터 F1) | 관리자, 디버그 패널 켜기/끄기 |
| 스틱 | 템플릿 이동, 텔레포트 |
| 보기: 미니어처 / 실물 1:1 (관리자 패널) | 테이블 위 축소 모형 <-> 발밑 아래 실물 크기 |
| 랙 옮기기: 켬 (창고 확인 화면) | 랙을 잡아 다른 통로 칸에 놓기 -> 서버 재검증 (SC-11) |

### 사용자 패널 화면 흐름 (ui_재료 사용자 UI 개선 설계)

| 단계 | 화면 | 주 행동 |
|---|---|---|
| ① 창고 만들기 | 필요한 정보 안내, 말하기 / 직접 입력 -> 녹음 -> 인식 결과 확인 -> Agent 처리(체크리스트), 이전 기록 | 말하기, 생성하기 |
| ② 창고 확인 | 요약(크기, 랙, 도크, 충전) 확인, 빠진 정보나 검증 실패면 질문 + 선택 버튼 | √ 확인, 질문에 응답 |
| ③ 시뮬레이션 | 로봇 대수, 입하/출하 건수, 1200 규격 비율(권장값으로 채워짐, 말로 설정 가능) -> 실행 중 체크리스트 -> 결과 재생(재생, 속도, 진행 바, 경로, 히트맵) | 시뮬레이션 실행, 재생 |
| ④ 결과 분석 | 병목 분석(위치, 대기, Agent 설명, 개선안) -> 개선안 승인 -> 적용 전/후 비교(수치, 히트맵), 최종 리포트 창 | 적용, 다시 시뮬레이션, 리포트 |

UI 톤은 Apple visionOS 창을 모티브로 합니다 (유리 패널, 얇은 밝은 테두리, 알약 버튼, Apple 시스템 색). 색의 역할(파랑 실행, 초록 확인, 주황 주의, 빨강 오류, 유리 회색 보조)은 설계서 기준이고, 공통 스타일은 `UI/UIFactory.cs` 한 곳에 있습니다.

---

## 4. Agent 서버 구조 (`server/`)

```
server/
├─ api/            FastAPI. main.py(앱), flow.py(Agent 처리 흐름), store.py(세션·지도·시뮬레이션 저장), routes_*.py, ws.py
├─ agent/          graph.py(LangGraph 오케스트레이터), nodes/(노드), state.py(세션 상태), routing.py(분기),
│                  interpreter.py(LLM 해석 + 정규식 대체), prompts/(해석, 답변, 시나리오, 분석, 개선안 프롬프트)
├─ services/       llm.py(Ollama), stt.py(faster-whisper)
├─ tools/          결정적 도구: map_generator, map_validator, parse_text(정규식), simulation, compare, orders, aggregate, invariants
├─ planner/        경로 엔진 (하재윤)
├─ schema/         격자 지도·시나리오·시뮬레이션 로그 스키마 (pydantic)
└─ tests/          unit, integration, llm(-m llm), fixtures(문장 세트, 지도, 시험 음성)
```

| 단계 | 구현 | 비고 |
|---|---|---|
| 음성 인식 | `services/stt.py` | faster-whisper `small` int8, `language="ko"`, VAD, 도메인 단어 initial prompt. 무음이면 `STT_EMPTY` |
| 지시 해석 | `agent/interpreter.py` | gpt-oss:20b 구조화 출력(JSON 스키마, temperature 0). 응답을 범위·형식 검증하고, 실패·시간 초과면 정규식(`tools/parse_text.py`)으로 대체 |
| 필수 정보 확인 | `tools/map_generator.missing_items`, `api/flow.complete_or_ask` | 필수 8항목 중 빠진 것을 한 질문으로 되묻기. "기본값"이라고 하면 `fill_defaults`로 채움 |
| 지도 생성 | `tools/map_generator.generate_map` | 요구사항 → 격자 지도 JSON. 같은 입력이면 같은 결과 |
| 권장 시나리오 · 시나리오 해석 | `tools/scenario.py`, `agent/interpreter.extract_scenario` | 권장 로봇 수 = 도크 수 × 2 + 4 (2~16대). 시나리오 문장·음성에서 로봇 수·주문 수·실행 여부 추출 (LLM, 실패 시 정규식) |
| 지도 검증 | `tools/map_validator.py` | 형식, 크기, 도크 유무, 도달 가능성. 일방통행이 있으면 엔진과 같은 방향 그래프로 "들어갔다 나올 수 있는지" 검사. 오류를 모두 모아 질문으로 변환 |
| 경로 계산 | `planner` | 시공간 A* 기반 구간 단위 우선순위 계획 + LNS 개선 + 헝가리안 작업 할당, 롤링 재계획. 기준 전략: 개별 최단 경로 + 충돌 시 대기 |
| 결과 점검 | `tools/simulation.check_log`, `tools/outcome.py`, `tools/invariants.py` | 충돌이면 VR로 보내지 않고 `error` 전송. 주문을 다 처리하지 못하면 결과는 보내고 원인(공간·재고 부족, 길 막힘, 로봇 정체, 시간 초과)과 재시뮬레이션 권고를 붙임. 엔진과 독립된 검사기(금지 칸·순간이동·주문 누락)는 테스트에서 사용 |
| 분석·개선 | `agent/nodes/analyst.py`, `tools/aggregate.py`, `tools/proposals.py` | 칸별 대기, 로봇별 유휴 집계 → LLM이 집계 수치만 근거로 원인 설명 → 규칙이 만든 후보(도크 추가, 로봇 수, 보관 분산, 일방통행, 처리 못 한 주문이 있으면 랙·통로·주문 수 재시뮬레이션 권고를 앞에) 중 LLM이 1~3개 선택. 집계에 없는 숫자나 후보 밖 선택은 버리고 규칙 결과 사용 |
| 제품 규격 · 운영 규칙 | `planner/engine.py`, `tools/scenario.py`, `tools/invariants.py` | 랙 규격(1100x1100 표준, 1200x1000)에 맞는 칸에만 보관, 주문 규격 비율 입력. 일방통행과 엇갈림 가능 구간(`passing_allowed`), 독립 검사기로 규칙 위반 0건 확인 |
| 저장 · 기록 | `db/repo.py`, `api/routes_history.py`, `api/history_html.py`, VR `UI/HistoryPanel.cs` | SQLite에 지도, 시나리오, 로봇 동선(스텝별 위치), 주문 기록, 칸 통계, 리포트, 개선안 승인 저장. 서버 재시작 뒤에도 조회, 다시 재생, 재계획 |
| 최종 리포트 | `tools/report.py`, `api/report_html.py`, VR `UI/ReportPanel.cs` | 로봇별 가동률(이동·적재·하역 비율)·대기·유휴, 주문 처리 시간(평균·중앙값·P90·최대, 배정 대기·수행 시간), 시간대별 누적 완료, 기준 전략 비교(같은 주문), 규칙 기반 인사이트. 숫자는 모두 코드가 로그에서 계산 |

**Agent 오케스트레이터 (LangGraph, `agent/graph.py`):** 판단이 들어가는 요청은 그래프로 진행하고, 세션마다 상태(요구사항, 질문 횟수, 지도 버전)를 체크포인터에 유지해 수정 질문 이력을 이어 갑니다.

```
창고 설명/답변 -> 지시 해석 -> (빠진 정보) 수정 질문
                         -> 격자 지도 생성 -> 지도 검증 -> (오류) 수정 질문 / (통과) 지도 전송
랙 편집        -> 지도 검증 -> ...
시나리오 실행  -> 시뮬레이션 실행
분석 요청      -> 로그 분석 -> 개선안 제안
개선안 승인    -> 개선안 적용 -> (지도 변경) 지도 검증 -> 지도 전송 -> 시뮬레이션 실행 / (시나리오 변경) 시뮬레이션 실행
```

노드 순서 확인: `cd server && python -m agent.debug_trace --text "랙 6줄, 통로 폭 3미터" --answer "나머지는 기본값"`. 판단이 없는 요청(음성 인식, 시나리오 문장 해석, 롤링 재계획, 비교, 리포트, 기록)은 `api/flow.py`가 도구를 바로 부릅니다.

API 명세는 [docs/api.md](docs/api.md), VR 쪽 계약은 [docs/vr_client_api.md](docs/vr_client_api.md)를 보세요.

---

## 5. 측정값

### 5-1. 기준 전략 대비 처리 시간 (`python experiments/run_compare.py --maps W1 W2 W3 W6 L12 --ablation`)

권장 로봇 수, 주문 입하·출하 각 25건, 시드 1~5 평균. 두 전략은 같은 지도·같은 주문(종류·도착 스텝)으로 실행합니다. 결과 파일: `experiments/results/summary.csv`.

| 지도 | 로봇 | 기준 전략 (스텝) | 최적화 (스텝) | 개선율 평균 (최소) | 주문당 평균 시간 | 충돌 · 불변 조건 위반 | 주문 전부 완료 (두 전략 합계) |
|---|---|---|---|---|---|---|---|
| W1 (랙 4, 도크 2) | 8 | 531 ± 92 | 162 ± 7 | **68.4%** (58.2%) | 250.6 → 61.2 | 0 · 0 | 10/10 |
| W2 (랙 8, 도크 3) | 10 | 494 ± 168 | 158 ± 8 | **64.8%** (53.4%) | 212.7 → 61.7 | 0 · 0 | 10/10 |
| W3 (두 구역 + 일방통행) | 8 | 556 ± 77 | 169 ± 4 | **69.0%** (61.5%) | 277.5 → 65.6 | 0 · 0 | 10/10 |
| W6 (기본값) | 8 | 737 ± 179 | 170 ± 8 | **75.4%** (67.3%) | 366.1 → 66.6 | 0 · 0 | 10/10 |
| L12 (랙 12, 도크 5) | 14 | 290 ± 28 | 146 ± 4 | **49.1%** (42.3%) | 137.7 → 60.2 | 0 · 0 | 10/10 |

- 기준 전략 = 무작위 보관 위치 + 선착순 할당 + 로봇별 최단 경로(막히면 대기). 최적화 = 도크 거리·몰림 점수로 보관 위치 선정 + 헝가리안 할당 + 시공간 협력 경로 계획 + LNS 개선
- **해석 주의:** 로봇이 많을수록 기준 전략이 혼잡으로 크게 느려져 개선율이 커집니다. 로봇 4대처럼 혼잡이 적을 때도 개선율은 44~54%였습니다 (보정 실험, 시드 1~3)

**요소별 기여도** (최적화에서 한 요소만 기준 방식으로 바꿨을 때 늘어난 스텝, 위 조건)

| 지도 | 보관 위치 점수 → 무작위 | LNS 개선 끔 | 헝가리안 → 그리디 할당 |
|---|---|---|---|
| W1 | +55 | +43 | +7 |
| W2 | +77 | +41 | +3 |
| W3 | +87 | +45 | 0 |
| W6 | +70 | +38 | 0 |
| L12 | +93 | +16 | +6 |

보관 위치 선정이 가장 크게, 그다음 LNS 경로 개선이 기여합니다. 할당 방식의 차이는 작습니다.

### 5-2. 권장 로봇 수 (보정 실험, 로봇 2~16대 × 시드 1~3)

최적화 전략에서 로봇 2대를 더해도 처리 시간이 10% 미만으로 줄어드는 지점: W1·W3·W6 8대, W2 10대, L12 12~16대. 도크 앞 대기가 병목이라 도크 수에 비례하므로 **권장 로봇 수 = 도크 수 × 2 + 4**로 정했습니다 (각 8·8·8·10·14대).

### 5-3. Agent · 음성

| 항목 | 결과 | 조건 |
|---|---|---|
| 요구사항 추출 (LLM) | 24/24, 평균 2.7초, 최대 3.6초 | `tests/fixtures/sentences.csv`, gpt-oss:20b, think=low |
| 요구사항 추출 (정규식) | 24/24 | 같은 문장 세트로 규칙을 보강했으므로 과대 평가된 값 |
| 빠진 정보 답변·시나리오 문장 (LLM) | 10/10, 1.6~4.2초 | "3단으로 하고 구역은 하나", "나머지는 기본값", "로봇 6대, 입하 30건 출하 20건으로 실행해줘" 등 |
| 음성 인식 (small, CPU) | 3/3 정답, 문장당 1.9~2.6초 | TTS로 만든 시험 음성 (Ryzen 9 5900HS). 사람 목소리로 재측정 필요 |
| 연속 흐름 (실제 서버 + LLM) | 문장 → 빠진 정보 되묻기 → 답변 → 지도 → 시나리오 문장 → 실행 → 비교, 15.0초 | W2, 권장 10대, 입하 30·출하 20 → 158스텝, 기준 304스텝 대비 −48% |

## 6. 테스트

| 수준 | 실행 | 대상 |
|---|---|---|
| 서버 단위·통합 | `cd server && pytest -q` | 지도 생성·검증, 해석 검증·대체 경로, 엔진 연결, API·WebSocket 전체 흐름(문장·음성·되묻기·비교·재계획·분석), planner 테스트 |
| LLM 품질 | `cd server && pytest -m llm -q` | 실제 Ollama 호출. 응답이 없으면 건너뜀 |
| 해석 정답률·지연 | `cd server && python -m tests.eval_sentences` | 정규식 / LLM / LLM+대체 경로 비교 |
| Unity EditMode | Window → General → Test Runner → EditMode → Run All | 좌표 변환, 메시지·프레임 파싱, WAV |
| 비교 실험 | `python experiments/run_compare.py [--ablation]` | 기준 전략 대비 개선율, 요소별 기여도 → `experiments/results/summary.csv` |
| 모의 서버 | `cd mock_server && pytest -q` | 모의 엔진·API |

---

## 7. VR 클라이언트 코드 구조 (`Assets/RobotWarehouse/Scripts/Runtime`)

| 모듈 | 파일 | 요구사항 |
|---|---|---|
| API 클라이언트 | `Network/ApiClient.cs`, `WsClient.cs`, `ApiRoutes.cs` | IR-01·02, EX-02·04 |
| 입력 모듈 | `Input/VoiceRecorder.cs`, `WavEncoder.cs`, `UI/MainPanels.cs` | FR-01·02·11, SC-02 |
| 창고 렌더러 | `Warehouse/WarehouseRenderer.cs` | FR-20, SC-03 문제 칸 강조, SC-09 병목 마커 |
| 로봇 재생기 | `Playback/FrameTimeline.cs`, `RobotPlayback.cs`, `PathLineView.cs` | FR-21·22·23, SC-08 |
| 히트맵 레이어 | `Heatmap/HeatmapLayer.cs`, `HeatmapMath.cs` | FR-24 |
| 사용자·관리자 패널 | `UI/UserPanel.cs`, `UI/DevPanel.cs`, `UI/MainPanels.cs`, `UIFactory.cs` | FR-05·09·27·28·29 |
| 최종 리포트 · 기록 창 | `UI/ReportPanel.cs`, `UI/HistoryPanel.cs` | FR-25~28, DR-03 |
| 흐름 제어 | `App/AppController.cs` | SC-01~12, EX-01~04 |
| XR 연동 | `XR/XRSupport.cs`, `RackGrabEditor.cs`, `DesktopRigController.cs` | IR-06, FR-10 |
| 데이터 계약 | `Data/GridMap.cs`, `SimData.cs`, `ServerMessage.cs`, `Core/GridCoord.cs` | DR-01~03 |

VR 클라이언트에는 경로 계산 코드가 없습니다(NFR-02). 모든 이동은 서버 프레임을 보간해 재생합니다.

**UI 스타일:** Apple visionOS 창을 모티브로 합니다. 둥근 유리 패널과 얇은 밝은 테두리, 알약 모양 버튼, 흰 글자와 반투명 보조 글자, 시스템 색상(파랑·초록·주황·빨강). 공통 스타일은 `UI/UIFactory.cs` 한 곳에 있습니다.

**3D 에셋:** Unity Warehouse(HDRP)의 Shelf·Palletrobot·Cardboard·Pallet을 URP 사본(`AssetSkin/Prefabs/RW_*`)으로 변환해 씁니다. 원본은 수정하지 않고, 설정은 `Assets/RobotWarehouse/Resources/WarehouseSkin.asset`(`useSkin`, `boxFill`, `robotYawOffset`)에서 바꿉니다.

---

## 8. 남은 일 · 제약

- 실기기 확인 전: VR 앱 ↔ 실제 서버 연결, 사람 목소리 STT, Quest 마이크 녹음, 실기기 FPS(NFR-01)
- VR의 필요한 정보 안내·시나리오 음성 버튼은 Unity에서 컴파일·화면 배치 확인 전 (왼쪽 패널에 안내 3줄이 늘어 넘치면 `AgentLog` 높이를 줄임)
- TTS 음성 안내(FR-30, P2)는 범위에서 제외했습니다 (안내는 패널 글자로 표시)
- 로봇은 1~30대로 제한합니다 (planner가 큰 지도에서 30대 이상일 때 교착)
- minSdk 32는 Quest 2 설치 호환을 위해 낮춘 값입니다. 대여 기기에서 설치 확인 필요
- 오프라인 재생 데이터(`Resources/Offline/*.json`)는 모의 엔진 결과입니다
- Quest 2 시스템 키보드가 뜨지 않으면 한글 입력은 음성 또는 관리자 패널의 예시 버튼(W1~W6)으로 합니다

---

## 9. 출처

[SOURCES.md](SOURCES.md) (별지2), 경로 엔진 출처는 [server/planner/SOURCES.md](server/planner/SOURCES.md)를 보세요.
