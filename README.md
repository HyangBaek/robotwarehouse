# 로봇웨어하우스 — VR 클라이언트 (Unity · Meta Quest 2)

아키텍처 ① 디바이스 계층(Unity VR 클라이언트)을 구현했습니다. 서버 없이도 **모의 서버** 또는 **오프라인 재생**으로 E2E 흐름(말/글 입력 → 격자 지도 → 확인 → 시나리오 → 다중 로봇 재생 → 히트맵·분석·비교)을 확인할 수 있습니다.

| 폴더 | 내용 |
|---|---|
| `robotwarehouse/` | Unity 6 VR 템플릿 프로젝트 (6000.6.3f1, URP, XRI 3.6, OpenXR). 추가 코드는 전부 `Assets/RobotWarehouse/` |
| `mock_server/` | VR 단독 검증용 FastAPI 모의 서버 (규칙 기반 지시 해석 + 시공간 A* 다중 로봇 계획). 서버 팀 참고 구현 |
| `docs/schema.md` | 격자 지도 스키마·좌표 규칙 (NFR-11) |
| `docs/vr_client_api.md` | VR이 보내고 기대하는 REST·WebSocket 계약, 문서에 없어 가정한 항목 표시 |

---

## 1. 빠른 시작 (에디터에서 5분)

1. **Unity 열기** → `robotwarehouse` 프로젝트. `Packages/manifest.json`에 Newtonsoft JSON 패키지를 추가해 두었으니 처음 열 때 자동 설치됩니다.
2. 메뉴 **RobotWarehouse → 1. 시연 씬 만들기** → `Assets/RobotWarehouse/Scenes/RobotWarehouse.unity` 생성, 빌드 목록 첫 번째로 등록.
3. **모의 서버 실행** (새 터미널)
   ```bash
   cd D:\hyang\robotwarehouse\mock_server
   pip install -r requirements.txt
   uvicorn server:app --host 0.0.0.0 --port 8000
   ```
4. Play → 왼쪽 패널에 서버 IP `127.0.0.1` 입력 → **연결** → 예시 버튼 **W2** → **창고 생성** → **지도 확인** → 오른쪽 패널 **실행**.
   - 에디터 조작: 마우스 왼쪽 = UI 클릭, 오른쪽 드래그 = 둘러보기, WASD/QE = 이동
   - 서버 없이 보기: **오프라인 재생** (W2 + S2 결과 내장)

## 2. Quest 2 빌드

1. 메뉴 **RobotWarehouse → 2. Quest 빌드 설정 적용** (HTTP 허용, 인터넷 권한, minSdk 32, IL2CPP ARM64)
2. Project Settings → XR Plug-in Management → **Android 탭 → OpenXR** 체크, OpenXR Feature Groups에서 **Meta Quest Support** 켬 (템플릿 기본값 확인만)
3. File → Build Profiles → Android → Switch Platform → Quest 2 연결 → **Build And Run**
4. 노트북과 Quest 2를 같은 Wi-Fi에 연결, 노트북 IP(`ipconfig`의 IPv4)를 VR 패널 숫자 패드로 입력 → 연결
   - Windows 방화벽에서 Python(uvicorn) 8000 포트 인바운드를 허용해야 Quest가 접속됩니다.

> minSdk 32는 Quest 2 설치 호환을 위해 템플릿 기본값(34)에서 낮춘 값입니다. 대여 기기 OS 버전에서 설치가 되는지 한 번 확인해 주세요 [검증 필요].

## 3. VR 조작

| 조작 | 동작 |
|---|---|
| 컨트롤러 레이 + 트리거 | 패널 버튼 누르기 (템플릿 XRI 레이) |
| **오른손 A 버튼 누르고 말하기** | 음성 입력 (질문 중이면 음성 답변). 패널의 "누르고 말하기" 버튼도 같음 |
| 스틱 | 템플릿 이동·텔레포트 |
| 보기: 미니어처 / 실물 1:1 | 테이블 위 축소 모형 ↔ 바닥 실물 크기 |
| 랙 편집: 켬 | 랙을 잡아 다른 통로 칸에 놓기 → 서버 재검증 (SC-11) |

## 4. 테스트

| 수준 | 실행 | 대상 |
|---|---|---|
| Unity EditMode (UN) | Window → General → Test Runner → EditMode → Run All | TC-VR-01~06, 메시지·프레임 파싱, WAV |
| 모의 서버 (UT/IT) | `cd mock_server && pytest -q` (27개) | 지도 생성·검증, 엔진 불변 조건(충돌 0·순간이동·금지 칸·일방통행), 롤링 재계획, API 흐름 |
| 실기기 (DV) | 개발자 테스트 시나리오 2.6 체크리스트 | TC-VR-11~20 |

모의 서버 기준 측정값 (W2, 주문 25/25, 시드 42): 로봇 4대 optimized 688스텝 / baseline 747스텝, 로봇 8대 719 / 795스텝, 충돌 0건. 모의 엔진 값이라 보고서 수치로 쓰지 말고 실제 엔진 값으로 바꿉니다.

## 5. 코드 구조 (`Assets/RobotWarehouse/Scripts/Runtime`)

| 모듈 (아키텍처 ①) | 파일 | 요구사항 |
|---|---|---|
| API 클라이언트 | `Network/ApiClient.cs`, `WsClient.cs`, `ApiRoutes.cs` | IR-01·02, EX-02·04 |
| 입력 모듈 | `Input/VoiceRecorder.cs`, `WavEncoder.cs`, `UI/MainPanels.cs` | FR-01·02·11, SC-02 |
| 창고 렌더러 | `Warehouse/WarehouseRenderer.cs` | FR-20, SC-03 문제 칸 강조, SC-09 병목 마커 |
| 로봇 재생기 | `Playback/FrameTimeline.cs`, `RobotPlayback.cs`, `PathLineView.cs` | FR-21·22·23, SC-08 |
| 히트맵 레이어 | `Heatmap/HeatmapLayer.cs`, `HeatmapMath.cs` | FR-24 (텍스처 1장) |
| 결과 패널 | `UI/MainPanels.cs`, `UIFactory.cs` | FR-05·09·27·28·29 |
| 흐름 제어 | `App/AppController.cs` | SC-01~12, EX-01~04 |
| XR 연동 | `XR/XRSupport.cs`, `RackGrabEditor.cs`, `DesktopRigController.cs` | IR-06, FR-10 |
| 데이터 계약 | `Data/GridMap.cs`, `SimData.cs`, `ServerMessage.cs`, `Core/GridCoord.cs` | DR-01~03 |

VR 클라이언트에는 경로 계산 코드가 없습니다(NFR-02). 모든 이동은 서버 프레임을 보간해 재생합니다.

## 6. 남은 일·제약

- 실제 서버(`docs/api.md`)가 확정되면 `docs/vr_client_api.md`의 **가정** 항목을 맞춰야 합니다 (WebSocket 경로, `/compare`, 음성 답변의 `question_id`, `status`·`confirmed`·`replan_from` 필드).
- Quest 2 시스템 키보드가 뜨지 않으면 한글 입력은 음성·예시 버튼(W1~W6)으로 합니다. IP는 숫자 패드로 입력합니다.
- 실기기 FPS(NFR-01)는 아직 측정 전입니다. 로봇·블록은 색별 공유 머티리얼(SRP Batcher), 히트맵은 텍스처 1장, 그림자 끔.
- 오프라인 재생 데이터(`Resources/Offline/*.json`)는 모의 엔진 결과입니다. 실제 엔진 결과로 바꾸려면 같은 형식으로 덮어쓰거나 `python mock_server/make_offline.py`를 참고합니다.

## 7. 출처 (별지2 기재용)

| 항목 | 출처·라이선스 |
|---|---|
| 한글 UI 폰트 | NanumGothic (Google Fonts, SIL Open Font License 1.1) — `Assets/RobotWarehouse/Resources/Fonts/` |
| Unity VR 템플릿, XR Interaction Toolkit, OpenXR, Newtonsoft JSON(com.unity.nuget.newtonsoft-json) | Unity Technologies 패키지 |
| 모의 서버 | FastAPI, Uvicorn (MIT/BSD) |
| 코드 작성 | AI 도구(Claude) 활용 — 별지2 AI 활용 신고 대상 |
