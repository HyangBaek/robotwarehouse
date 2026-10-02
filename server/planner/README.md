# planner — 다중 로봇 경로 계산 엔진

로봇웨어하우스 팀 프로젝트의 **경로 계산 엔진**입니다. (담당: 하재윤)

> **한 줄 요약:** 지도 JSON과 시나리오를 넣으면, 로봇들이 서로 부딪히지 않고 주문을 처리하는 **스텝별 이동 기록(DR-03 로그)** 을 돌려주는 순수 Python 함수입니다.

- 외부 서버·DB·네트워크를 쓰지 않습니다. 같은 입력과 같은 시드면 항상 같은 결과가 나옵니다.
- 서버(FastAPI)에서는 함수 한 줄로 호출합니다. (IR-05)
- 출처와 AI 활용 내역은 [SOURCES.md](SOURCES.md)를 보세요.

---

## 1. 빠른 시작

**요구 사항:** Python 3.9 이상. `scipy`는 선택 사항입니다. 있으면 최적 할당(헝가리안)을 쓰고, 없으면 자동으로 그리디 할당으로 대체됩니다.

```bash
pip install scipy numpy        # 선택
```

**저장소에서의 위치:** `server/planner/` (이 폴더 전체를 그대로 둡니다.)

```python
# server/ 폴더에서 실행하거나, server/ 가 import 경로에 있어야 합니다
import json
from planner import simulate

m  = json.load(open("map_W2.json"))                      # 지도 JSON (DR-01)
sc = {"scenario_id": "S2", "map_version": "v1",
      "robots": 4, "inbound": 25, "outbound": 25}        # 시나리오 (DR-02)

log = simulate(m, sc, "optimized", seed=42)              # 최적화 전략
base = simulate(m, sc, "baseline", seed=42)              # 기준 전략

print(log["total_steps"], log["completed"], len(log["collisions"]))
```

**실행해 보기 (server/ 폴더에서)**

```bash
python planner/examples/run_demo.py        # 기준 대 최적화 비교 + 롤링 재계획 (약 10초)
python planner/tests/test_engine.py        # 엔진 테스트 (약 20초)
python planner/tests/test_cbs.py           # CBS 테스트
python planner/examples/benchmark_cbs.py   # CBS 대비 해 품질 비교표
# pytest를 쓰는 경우: pytest -q planner/tests
```

---

## 2. 파일 구성

```
planner/
├─ __init__.py          # 외부에서 쓰는 이름 모음 (simulate, compare, Sim, solve_cbs ...)
├─ engine.py            # 핵심 엔진 (지도 → 그래프, 경로 계획, 시뮬레이터, 로그)
├─ service.py           # 서버 연동 도우미 (SC-04 실행 점검, SC-10 개선안 재시뮬레이션)
├─ cbs.py               # CBS 비교 알고리즘 + 품질 벤치마크
├─ testmap.py           # 엔진 단독 테스트용 창고 지도 생성기 (임시)
├─ tests/
│   ├─ test_engine.py   # 충돌 0건, 로그 일관성, 일방통행, 롤링 재계획 등
│   ├─ test_service.py  # service.py 테스트
│   └─ test_cbs.py      # CBS 최적성(완전탐색과 비교), 충돌 0건
├─ examples/
│   ├─ run_demo.py      # 비교·재계획 데모, 샘플 로그 JSON 생성
│   └─ benchmark_cbs.py # CBS 비교표
├─ README.md
└─ SOURCES.md           # 출처·AI 활용 (별지2 작성용)
```

`testmap.py`는 **내가 만든 임시 지도**입니다. 실제 지도(`generate_grid_map` 출력)로 바꿔 끼우면 됩니다.

---

## 3. 사용 방법 (공개 함수)

| 함수 | 용도 | 비고 |
|---|---|---|
| `simulate(map, scenario, strategy, seed, config)` | 시뮬레이션 1회 실행, DR-03 로그 반환 | `strategy`: `"optimized"` / `"baseline"` |
| `compare(map, scenario, seed, config)` | 두 전략을 같은 조건으로 실행하고 개선율 계산 | FR-18, 28. 결과의 `summary`에 요약 |
| `Sim(...).run().add_event(event)` | 롤링 재계획 (시점 t 이전 유지, 이후 재계산) | FR-19. 아래 4.3 참고 |
| `find_collisions(frames)` | 로그의 점 충돌·맞교환 충돌 검사 | NFR-04 |
| `solve_cbs(grid, starts, goals, timeout)` | CBS 최적해 (소규모 비교용) | `Grid(map)`으로 격자를 만든 뒤 사용 |
| `benchmark(map, ...)` | CBS 대비 우선순위 계획의 해 품질 비교 | 보고서 실험표용 |

---

## 4. 입력과 출력 (연동 계약)

> 필드 이름이 팀 스키마(`docs/schema.md`)와 다르면 **엔진이 아니라 서버 쪽에서 변환**해 넘기는 것을 권장합니다.

### 4.1 입력

**지도 JSON (DR-01)** — 엔진이 읽는 필드만 적었습니다.

| 필드 | 사용 방식 |
|---|---|
| `width`, `height` | 격자 크기. 좌표는 `(x, y)`, 칸 번호는 `y * width + x` |
| `cells[].type` | `aisle` `rack` `wall` `dock_in` `dock_out` `charge`. **지정하지 않은 칸은 `aisle`** |
| `cells[].rack_id` | 랙 칸의 랙 ID (같은 ID의 칸은 한 랙으로 묶임) |
| `racks[].rack_id`, `racks[].capacity` | 랙 ID별 수용량. 없으면 6 |
| `docks[]` (`type`, `x`, `y`) | 도크 위치. `cells`의 도크와 중복되어도 됨 |
| `rules.one_way[]` | `{"from":[x,y], "to":[x,y]}` 직선 구간. **`from→to` 방향 벡터로 방향을 정함** (`dir` 문자열은 읽지 않음) |

**시나리오 (DR-02)**

| 필드 | 설명 |
|---|---|
| `scenario_id`, `map_version` | 로그에 그대로 기록됨 |
| `robots` | 정수(대수) 또는 `[[x,y], ...]` (시작 위치). 정수면 충전 칸부터 채움 |
| `inbound`, `outbound` | 입하·출하 주문 개수. 도착 스텝은 시드로 무작위 생성 |
| `orders[]` | 직접 지정: `{"order_id":"O001","type":"inbound"/"outbound","arrival_t":0}`. 있으면 위 개수보다 우선 |
| `events[]` | `{"t":100,"add_orders":10,"robots":6}` 미리 넣어 둘 변경 이벤트 |

### 4.2 출력 (DR-03)

```python
{
  "scenario_id", "strategy", "map_version", "seed",
  "total_steps": int,                 # 총 처리 스텝 (프레임 수 - 1)
  "completed": bool,                  # 모든 주문 완료 여부
  "frames": [ {"t": 0, "robots": [ {"id":"R1","x":34,"y":2,"state":"idle","task_id":None}, ... ]}, ... ],
  "orders": [ {"order_id","type","robot_id","arrival_t","assigned_t","done_t","status"}, ... ],
  "cell_stats": [ {"x","y","pass","wait"}, ... ],     # 값이 0이 아닌 칸만 포함
  "collisions": [ {"t","robot_a","robot_b","x","y","type"} ],   # 정상이면 빈 리스트
  "meta": { "orders_done","orders_total","avg_order_time","total_waits","total_moves",
            "replans","replan_avg_s","replan_max_s","events","config" }
}
```

- **`frames[t]`**: `t=0`은 시작 상태입니다. `state`는 **그 프레임에 도착하기까지의 스텝에서 한 일**입니다.

  | state | 뜻 |
  |---|---|
  | `move_empty` | 짐 없이 이동 (픽업 장소로 가는 중, 또는 유휴 중 비켜 주기) |
  | `move_loaded` | 짐을 싣고 이동 |
  | `wait` | 작업 중인데 제자리 대기 (히트맵의 대기 집계 대상) |
  | `load` / `unload` | 싣는 중 / 내리는 중 (각 1스텝) |
  | `idle` | 작업 없음, 정지 |

- **`orders[].status`**: `done` / `rejected`(재고·용량이 없어 배정 불가) / `active`(미완료).
- **`cell_stats`**: `pass`는 그 칸으로 **들어온** 횟수(합계 = 전체 이동 횟수), `wait`는 `wait` 상태로 머문 로봇-스텝 수.
- **로봇 `id`**: `"R1"`, `"R2"` … 문자열. 이벤트로 로봇이 늘면 그 시점부터 프레임의 `robots`가 길어집니다.
- **충돌의 정의**: 같은 스텝에 같은 칸에 있음(`vertex`), 서로 칸을 맞바꿈(`swap`). 앞 로봇이 비우는 칸에 뒤 로봇이 들어가는 것(줄 서서 이동)은 허용합니다.

### 4.3 롤링 재계획 (FR-19)

```python
from planner import Sim

sim = Sim(m, sc, "optimized", seed=42).run()            # 먼저 한 번 실행
res = sim.add_event({"t": 100, "add_orders": 10, "robots": 6})
# res["frames"][:101] 은 이벤트 전과 동일, 이후만 새로 계산됨
print(res["meta"]["events"][0])   # replan_s: 첫 재계획 시간, recompute_total_s: 이후 전체 재계산 시간
```

- 시점 t 이전의 체크포인트로 돌아가 이벤트를 넣고 이어서 계산합니다. 그래서 스텝 0~t 프레임이 바뀌지 않습니다.
- **`Sim` 객체는 메모리에 있어야 합니다.** 서버가 객체를 보관하기 어려우면 `scenario["events"]`에 이벤트를 포함해 `simulate`를 다시 호출하는 방식으로 대체할 수 있습니다. 결과는 같고 계산 시간만 더 듭니다.
- 이벤트 종류는 현재 **주문 추가**와 **로봇 수 변경**입니다. 로봇 수를 줄이면 해당 로봇은 진행 중인 작업을 마치고 시작 위치로 돌아가 정지합니다.

### 4.4 설정 (`config`)

`simulate(..., config={"window": 30})`처럼 일부만 덮어쓸 수 있습니다.

| 키 | 기본값 | 의미 | 올리면 |
|---|:-:|---|---|
| `window` | 24 | 한 번에 앞을 내다보는 스텝 수 | 품질↑, 느려짐 |
| `horizon` | 12 | 재계획 없이 실행하는 최대 스텝 | 속도↑, 반응성↓ |
| `lns_iters` | 8 | 경로 개선 반복 횟수 (0이면 끔) | 품질↑, 느려짐 |
| `service` | 1 | 싣기·내리기 소요 스텝 | — |
| `storage_load_weight` | 3.0 | 보관 위치 점수에서 "한 랙에 작업이 몰림" 벌점 | 랙 선택이 분산됨 |
| `fill_ratio` | 0.5 | 시작 시 랙 재고 비율 (출하 주문용) | — |
| `arrival_span` | 60 | 자동 생성 주문의 도착 스텝 범위 | — |
| `use_hungarian` | True | 일괄 최적 할당 (False면 그리디 = FR-14 원문) | — |
| `max_steps` | 3000 | 안전 상한 (넘으면 `completed=False`) | — |
| `random_storage` | False | 최적화 전략에서도 무작위 보관 (기여도 분해 실험용) | — |

### 4.5 서버 연동 도우미 (`service.py`)

서버(Agent 도구)가 엔진을 부를 때 쓰는 얇은 계층입니다. 엔진 내부를 몰라도 아래 세 함수만 쓰면 됩니다.

| 함수 | 시나리오 | 하는 일 |
|---|---|---|
| `run_simulation(map, scenario)` | SC-04 | 엔진 호출 후 충돌·미완료를 점검. `{"ok", "log", "error"}` 반환. **`ok=False`면 결과를 VR로 보내지 않고 서버 로그에 `error`를 남김** |
| `resimulate(map, scenario, base_log, change)` | SC-10 | 개선안 1개를 적용하고 **같은 주문**(종류·도착 스텝)으로 다시 실행. 전·후 총 스텝, 평균 처리 시간, 대기 합계, 개선율 반환 |
| `extract_orders(log)` | SC-10 | 로그에서 주문 목록만 추출 |

`POST /scenario` 본문 `{map_version, robots, inbound, outbound}`은 그대로 `run_simulation`에 넘길 수 있습니다.

**`change` 형식** — 구현 시나리오 SC-09의 개선안 4개 유형 중 엔진이 직접 처리하는 3개입니다. Agent는 이 형식 안에서만 개선안을 제안해야 합니다.

```python
{"type": "one_way",        "from": [x, y], "to": [x, y]}   # 일방통행 구간 추가
{"type": "storage_weight", "value": 5.0}                    # 보관 위치 '몰림' 벌점 가중치 (기본 3.0)
{"type": "robots",         "value": 6}                      # 로봇 수
# 도크 추가: 지도가 바뀌므로 방유진 님의 지도 수정·검증을 거친 새 지도를 map_json으로 넘김
```

**주의:** 개선안을 적용해도 처리 시간이 줄어든다는 보장은 없습니다. `resimulate`는 실제로 다시 돌린 전·후 값을 그대로 돌려주므로, Agent는 악화된 경우에도 그 수치를 그대로 보여 줘야 합니다.

---

## 5. 알고리즘 개요

| 단계 | 방법 | 위치 |
|---|---|---|
| 지도 → 그래프 | 4방향 이동 + 대기. 일방통행은 방향 간선으로 표현. 칸마다 BFS 거리표 캐시 | `engine.Grid` |
| 보관 위치 선정 | 점수 = 도크까지 거리 + 가중치 × 이미 몰린 작업 수 (기준 전략은 무작위) | `Sim._choose_storage` |
| 작업 할당 | 대기 주문과 유휴 로봇의 거리 합을 최소로 하는 일괄 할당 (헝가리안) | `Sim._assign` |
| 단일 로봇 경로 | 시공간 A\* (상태 = 칸, 시각). 예약 표를 피함 | `engine.plan_one` |
| 다중 로봇 조율 | 우선순위 계획. 확정한 경로를 예약 표에 올려 다음 로봇이 피함 → **충돌 0건** | `engine.plan_window` |
| 경로 개선 | LNS: 지연이 큰 로봇과 주변 로봇의 경로를 지우고 다시 계획, 비용이 늘면 폐기 | `engine.lns_improve` |
| 롤링 재계획 | 작업 단계가 바뀌면(도착·적재 완료 등) 또는 12스텝마다 전체를 다시 계획 | `Sim.run`, `Sim._plan` |
| 재계획 이벤트 | 체크포인트에서 이어서 계산 | `Sim.add_event` |
| 기준 전략 | 무작위 보관 + 선착순 할당 + 로봇별 개별 최단 경로, 충돌 시 대기 | `Sim._baseline_positions` |
| 비교 알고리즘 | CBS (제약 트리 탐색, 최적해) | `cbs.solve_cbs` |

쉬운 비유: **공유 달력**입니다. 로봇마다 "몇 스텝에 이 칸을 쓴다"를 예약 표에 적고, 다음 로봇은 비어 있는 시간에 맞춰 길을 짭니다.

---

## 6. 요구사항 대응 현황

| 요구사항 | 내용 | 상태 | 메모 |
|---|---|:-:|---|
| FR-12 | 주문 목록 생성 | △ | 종류·도착 스텝만 생성. **제품 규격·수량 필드 미구현** |
| FR-14 | 가까운 유휴 로봇 할당 | ○ | 기본은 헝가리안(일괄 최적). `use_hungarian=False`면 그리디 |
| FR-15 | 보관 위치 선정 | △ | 거리·용량·몰림 고려. **수용 규격(`slot_spec`) 비교 미구현** |
| FR-16 | 충돌 없는 다중 경로 | ○ | 우선순위 계획. CBS 비교 구현 |
| FR-17 | 운영 규칙 | △ | **일방통행만**. 엇갈림 가능 통로(`passing_allowed`) 미구현 |
| FR-18 | 기준 전략 시뮬레이션 | ○ | `strategy="baseline"` |
| FR-19 | 롤링 재계획 | ○ | `Sim.add_event` (서버 연동 방식 미정) |
| FR-28·29 | 비교·재시뮬레이션 (공동) | △ | `compare()` 제공. 개선안을 설정으로 바꿔 재실행하는 연동은 협의 필요 |

○ 구현 · △ 부분 구현

---

## 7. 검증 결과

> ⚠️ **아래 수치는 `testmap.py`의 임시 지도 기준입니다.** 실제 지도(W1·W2·W3)로 다시 측정해 보고서에 쓸 값을 확정해야 합니다.

**테스트:** 엔진 8개 + CBS 4개 통과. 충돌 0건(S1·S2·S2-H, 시드 1·7·42), 일방통행 위반 0건, 같은 시드에서 동일 결과, 롤링 재계획 시 스텝 0~100 프레임 불변.

**기준 전략 대비 (시드 42)**

| 시나리오 | 기준 | 최적화 | 개선율 |
|---|:-:|:-:|:-:|
| S2 (로봇 4, 주문 50) | 763스텝 | 189스텝 | 75.2% |
| S2-H (로봇 8, 주문 50) | 471스텝 | 159스텝 | 66.2% |

**개선이 어디서 나왔나 (S2-H)**

| 구성 | 총 스텝 |
|---|:-:|
| 기준 전략 | 471 |
| 무작위 보관 + 우선순위 계획 | 398 |
| 무작위 보관 + LNS | 340 |
| 점수 보관 + 우선순위 계획 | 201 |
| 점수 보관 + LNS (최종) | 159 |

기준 전략이 단순해 개선율이 크게 나옵니다. **개선의 큰 부분은 보관 위치 선정**에서 나오고, 경로 계획(LNS 포함)의 기여는 그보다 작습니다. 보고서에는 이 분해표를 함께 싣는 것을 권장합니다.

**CBS 대비 해 품질 (최적 = 1.00)**: 로봇 2~6대에서 우선순위 계획 1.01~1.04, +LNS 1.00~1.002. 혼잡 구역 로봇 8~14대에서 우선순위 계획 1.04~1.13, +LNS 1.02~1.03. CBS는 14대에서 절반이 5초 안에 풀리지 않았습니다.

---

## 8. 알려진 한계와 가정

**가정**
- 로봇은 한 스텝에 상하좌우 한 칸 이동 또는 대기. 시간은 격자 스텝 단위입니다.
- 랙 칸은 통과할 수 없고, 로봇은 **인접한 통로 칸**에서 싣고 내립니다.
- 싣기·내리기는 각 1스텝(`service`). 시작 재고는 랙 용량의 50%(`fill_ratio`).
- 일방통행 방향은 `from→to` 벡터로 정합니다. 스키마의 `dir`("N" 등)과 y축 방향이 어긋날 수 있어 실제 지도로 확인이 필요합니다.
- 도크가 여러 개면 주문 ID의 해시값으로 분산해 고릅니다.

**한계**
- 임시 지도에서만 검증했습니다. 실제 지도, 로봇 8대 초과, 통로 폭 1의 좁은 지도에서는 교착이 날 수 있습니다. 이 경우 `completed=False`가 되고 `max_steps`에서 멈춥니다. (안전 장치로 계획이 실패하면 전원 대기)
- CBS는 기본형이며 일회성 인스턴스(시작·목표 고정) 비교용입니다. 주문이 계속 들어오는 `simulate` 흐름에는 쓰지 않습니다.
- 우선순위 계획은 이론상 완전하지 않습니다.
- 처리 시간은 스텝 단위 값이며 실제 시간과 다릅니다. (신청서 한계 항목과 같음)

---

## 9. 팀원별 연동 확인 목록

**방유진 (서버·지도·분석)**
- [ ] 지도 JSON 필드가 4.1의 표와 맞는가 (`cells`, `racks`, `docks`, `rules.one_way`)
- [ ] 시나리오 형식 (`robots`, 주문 목록, `map_version`)과 `simulate()` 입력의 변환 위치
- [ ] 서버의 시뮬레이션 실행 도구에서 `simulate(...)` 호출 (IR-05), `Sim` 객체 보관 방식(4.3)
- [ ] 분석 Agent가 `cell_stats`, `orders`를 읽는 형식 / 개선안 재시뮬레이션에서 바꿀 설정 항목 (`storage_load_weight`, 로봇 수, `one_way`)
- [ ] 랙 `slot_spec`과 주문 규격 필드 형식 (FR-12, 15 보완에 필요)

**백향 (VR·통합)**
- [ ] `frames[t].robots[]`의 `{id, x, y, state, task_id}`로 재생에 충분한가 (4.2)
- [ ] 로봇 수가 중간에 늘어도(이벤트) 재생이 깨지지 않는가
- [ ] 샘플 로그: `python planner/examples/run_demo.py`가 `examples/sim_log_S2R.json`을 만듦
- [ ] 보고서 정량 결과표 값 (7장, 실제 지도 측정 후 확정)

---

## 10. 문의·기여

- 담당: 하재윤. 입력 형식 변경이나 버그는 이슈로 남기고, 엔진 수정 후에는 `tests/`를 다시 실행해 주세요.
- 이 엔진의 초안은 AI 코딩 도구(Claude, Anthropic)의 도움을 받아 작성했습니다. 자세한 내용과 참고 논문은 [SOURCES.md](SOURCES.md)에 있습니다.
