# SOURCES — 출처 및 AI 활용 (경로 계산 엔진)

별지2 출처·AI 활용 신고서의 **경로 알고리즘 담당분**입니다. 팀 공통 별지2의 6.1(AI 도구), 6.2(공개 코드·논문·알고리즘), 6.4(라이브러리)에 옮겨 적습니다.

---

## 1. AI 도구 활용 (6.1)

| 도구 | 용도 | 활용 범위 | 사용자 |
|---|---|---|---|
| Claude (Anthropic) | 코드 작성·디버깅 보조, 알고리즘 후보 비교·자문, 문서 초안 | `engine.py`, `cbs.py`, `testmap.py`, `tests/`, `examples/`, `README.md`, `SOURCES.md` | 하재윤 |

**활용 내용**
- 알고리즘 후보(우선순위 계획, CBS, PBS, LNS, PIBT 등)를 비교하고 구성을 정했습니다.
- 시공간 A\*, 윈도우 우선순위 계획, LNS 개선, 롤링 재계획(체크포인트), 기준 전략, CBS의 코드 초안과 테스트 코드를 AI가 작성했습니다.
- 이 코드가 낸 수치(개선율, 충돌 0건 등)는 직접 실행해 확인한 값만 보고서에 사용합니다.

---

## 2. 참고한 논문·알고리즘 (6.2)

| 이 코드에서의 사용 | 참고 문헌 | 사용 방식 | 코드 위치 |
|---|---|---|---|
| 시공간 A\*, 예약 표 기반 협력 경로 계획(우선순위 계획), 윈도우 계획 | Silver, D. (2005). *Cooperative Pathfinding.* Proceedings of the AIIDE 2005 | 방법 참고, 직접 구현 | `engine.plan_one`, `plan_window` |
| A\* 탐색 | Hart, P. E., Nilsson, N. J., Raphael, B. (1968). *A Formal Basis for the Heuristic Determination of Minimum Cost Paths.* IEEE Trans. Systems Science and Cybernetics, 4(2) | 방법 참고, 직접 구현 | `engine.plan_one`, `cbs.low_level` |
| LNS를 이용한 경로 개선 | Li, J., Gange, G., Harabor, D., Stuckey, P. J., Ma, H., Koenig, S. (2021). *Anytime Multi-Agent Path Finding via Large Neighborhood Search.* IJCAI 2021 | 개념 참고. 지연 큰 로봇과 주변 로봇을 다시 계획하는 **단순화한 버전** | `engine.lns_improve` |
| 롤링 재계획(윈도우 단위 계획과 반복 재계획) | Li, J., Tinka, A., Kiesel, S., Durham, J. W., Kumar, T. K. S., Koenig, S. (2021). *Lifelong Multi-Agent Path Finding in Large-Scale Warehouses.* AAAI 2021 | 개념 참고. 논문의 계획 풀이기를 쓰지 않고 우선순위 계획으로 대체 | `Sim.run`, `Sim._plan` |
| CBS (비교용 최적 알고리즘) | Sharon, G., Stern, R., Felner, A., Sturtevant, N. R. (2015). *Conflict-Based Search for Optimal Multi-Agent Pathfinding.* Artificial Intelligence, 219 | 방법 참고, 기본형을 직접 구현 | `cbs.solve_cbs` |
| 작업 할당(헝가리안 방법) | Kuhn, H. W. (1955). *The Hungarian Method for the Assignment Problem.* Naval Research Logistics Quarterly, 2(1–2) | 방법 참고. 계산은 SciPy 함수 호출 | `Sim._assign` |

**구현에서 논문과 달라진 점** (심사 질문 대비)
- LNS: 논문의 이웃 선택 휴리스틱과 적응적 선택을 쓰지 않고, 지연 큰 로봇 + 가까운 로봇(또는 무작위) 그룹을 고정 횟수 반복합니다.
- 롤링 재계획: 논문의 PBS 풀이기를 쓰지 않았습니다.
- CBS: 충돌 선택 최적화, 대칭 제거, bypass 등 개선 기법이 없는 기본형이라 로봇이 많으면 느립니다.
- 충돌 정의: 점 충돌과 맞교환 충돌만 검사합니다 (줄 서서 이동하는 것은 허용).

---

## 3. 공개 코드·라이브러리 (6.4)

| 자산 | 출처 | 라이선스 | 사용 방식 |
|---|---|---|---|
| Python 표준 라이브러리 | python.org | PSF License | 전체 코드 |
| SciPy (`scipy.optimize.linear_sum_assignment`) | scipy.org · Virtanen et al. (2020), *SciPy 1.0*, Nature Methods 17 | BSD-3-Clause | 선택 사항. 헝가리안 할당에 호출만 함. 없으면 그리디로 대체 |
| NumPy | numpy.org | BSD-3-Clause | SciPy 의존 |
| pytest | pytest.org | MIT | 선택 사항. 테스트 실행 (없어도 `python tests/test_*.py`로 실행 가능) |

---

## 4. 데이터

- 이 폴더의 `testmap.py`가 만드는 창고 지도와 `examples/`의 시나리오는 **엔진 테스트를 위해 직접 만든 가상 데이터**입니다. 실제 창고 데이터나 공개 데이터셋을 쓰지 않았습니다.
- 파레트 규격, 상자 치수, 물동량 같은 공개 수치는 이 엔진에서 사용하지 않습니다.

---

## 5. 팀 별지2에 붙일 요약

> **경로 계산 엔진** (하재윤): 다중 로봇 경로 계획은 협력 경로 계획(Silver, 2005)의 시공간 A\* 기반 우선순위 계획을 구현하고, LNS(Li et al., IJCAI 2021) 방식의 경로 개선과 윈도우 기반 롤링 재계획(Li et al., AAAI 2021의 개념)을 적용했다. 최적성 비교 기준선으로 CBS(Sharon et al., 2015)를 구현했다. 작업 할당은 헝가리안 방법(Kuhn, 1955)을 SciPy로 호출했다. 참고한 외부 소스 코드는 없으며 방법만 참고해 직접 구현했다. 구현 과정에서 AI 코딩 도구(Claude, Anthropic)를 활용했다.
