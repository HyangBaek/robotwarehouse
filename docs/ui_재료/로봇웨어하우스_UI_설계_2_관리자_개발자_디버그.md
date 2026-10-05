# 로봇웨어하우스 UI 설계 ② 관리자 · 개발자 · 디버그 UI

> 이 문서는 **관리자(Admin) 모드**와 **개발자(Developer) 모드** 화면, 그리고 3D 디버그 도구를 다룹니다.
> 세 모드 공통 규칙(색상·글자 크기·버튼·모드 전환·데이터 연결·공통 상태)과 사용자 화면은 [UI 설계 ① 공통 · 사용자 UI](로봇웨어하우스_UI_설계_1_공통_사용자.md)를 봅니다.
>
> 통합 원본: `로봇웨어하우스_관리자_디버그_UI_설계.md`(관리자·개발자 부분), `로봇웨어하우스_로봇 창고 UI 관리자·개발자 UI 설계.md`(관리자·개발자 부분). 두 문서에 같은 화면이 있으면 하나로 합치고, 한쪽에만 있던 설명과 예시는 모두 살렸습니다.

---

## 1. 설계 방향

기존 사용자 조작 UI와 별도로 **관리자/디버그 패널**을 구성한다.

- **관리자 UI**: 현재 시스템에서 무슨 일이 일어나고 있는가
- **개발자 UI**: 왜 그렇게 되었는가

관리자/개발자 UI는 정보 밀도가 높아도 괜찮으며, 운영 상태 확인과 개발 중 문제 추적, 시연 제어를 담당한다.

---

## 2. 메뉴 구조

### 2.1 관리자 UI 메뉴

```text
ADMIN
│
├── Dashboard
│
├── Monitor
│   ├── Connection
│   ├── Agent
│   └── Simulation
│
├── Logs
│
├── Scenario
│
├── Test
│
└── Developer
    ├── Grid Debug
    ├── Raw JSON
    ├── Performance
    ├── API Debug
    └── Settings
```

### 2.2 권장 최종 구조

```text
ADMIN MODE
 ├── Dashboard
 ├── Connection
 ├── Agent
 ├── Simulation
 ├── Logs
 ├── Scenario
 └── Test

DEVELOPER MODE
 ├── Agent Debug
 ├── Grid Debug
 ├── Raw JSON
 ├── API Debug
 └── Performance
```

### 2.3 최종 관리자 UI 구조

```text
                  ADMIN MODE
                      │
       ┌──────────────┼──────────────┐
       │              │              │
   Dashboard       Monitor          Logs
                      │
               ┌──────┴──────┐
               │             │
             Agent       Simulation
               │             │
               └──────┬──────┘
                      │
                 Developer
                      │
          ┌───────────┼───────────┐
          │           │           │
       Grid        Raw JSON    Performance
```

---

## 3. 화면 배치

### 3.1 관리자 UI 전체 레이아웃

```text
┌──────────────────────────────────────────────────────────┐
│ 🤖 ROBOT WAREHOUSE       ADMIN / DEBUG   ● SYSTEM ONLINE │
├────────────┬─────────────────────────────────────────────┤
│            │                                             │
│ Dashboard  │              현재 시스템                    │
│            │                                             │
│ Connection │   Server ●   Agent ●   Engine ●   DB ●     │
│            │                                             │
│ Agent      │                                             │
│            │              3D Simulation                  │
│ Simulation │                                             │
│            │                                             │
│ Logs       │                                             │
│            │                                             │
│ Scenario   │                                             │
│            │                                             │
│ Test       │                                             │
│            │                                             │
│ Settings   │                                             │
└────────────┴─────────────────────────────────────────────┘
```

기본 구조는 **왼쪽 Navigation + 오른쪽 상세 콘텐츠**로 구성한다.

### 3.2 기본 관리자 패널 크기

```text
Width       1600
Height       900
Ratio       16:9
```

레이아웃:

```text
┌────────────────────────────────────────────────────┐
│ Header                                      80 px │
├──────────────┬─────────────────────────────────────┤
│              │                                     │
│ Navigation   │ Content                             │
│ 280 px       │                                     │
│              │                                     │
└──────────────┴─────────────────────────────────────┘
```

권장 시작값:

- Header: 80
- Navigation: 280
- Content: 1240

실제 Quest 2에서 가독성을 테스트하며 조정한다. 글자 크기·색상·버튼 규칙은 UI 설계 ①의 디자인 시스템을 따른다.

### 3.3 Quest 2 World Space 배치

관리자 패널은 얼굴 바로 앞에 붙이지 않는다.

권장 시작점:

```text
Distance      약 1.5 ~ 2.0 m
Vertical      시선보다 약간 아래
Rotation      Head 방향 기준 초기 정렬
```

패널은 사용자의 머리를 계속 따라가는 방식보다, UI를 열었을 때 현재 시선 방향에 생성하고 독립적인 World Space 패널로 사용하는 방향을 권장한다.

```text
사용자 머리
     ●

        ┌──────────────────┐
        │   ADMIN PANEL    │
        └──────────────────┘
```

관리자 UI 역시 3D 공간을 완전히 가리지 않도록 한다.

```text
QUEST 2 USER VIEW

┌─────────────────────────────────────────┐
│                                         │
│               3D WAREHOUSE              │
│                                         │
│          🤖          🤖                 │
│                                         │
│                         ┌────────────┐  │
│                         │ Admin      │  │
│                         │ Panel      │  │
│                         │            │  │
│                         │ Dashboard  │  │
│                         │ Agent      │  │
│                         │ Simulation │  │
│                         │ Logs       │  │
│                         └────────────┘  │
│                                         │
└─────────────────────────────────────────┘
```

정보가 많은 Agent Graph나 Log Viewer는 Floating Window 형태로 크게 띄우는 방향을 권장한다.

---

## 4. Dashboard

### 4.1 목적

관리자가 처음 진입했을 때 다음 질문에 답할 수 있어야 한다.

> 지금 시스템 전체가 정상적으로 동작하고 있는가?

현재 시스템이 정상적으로 동작하고 있는지 빠르게 파악할 수 있도록, 상세 로그를 길게 보여주기보다 상태와 최근 이벤트 중심으로 요약한다.

### 4.2 화면 구성 (Navigation 포함)

```text
┌──────────────────────────────────────────────────────────┐
│ ROBOT WAREHOUSE                         ADMIN ● ONLINE    │
├──────────────┬───────────────────────────────────────────┤
│              │ SYSTEM STATUS                             │
│ Dashboard    │                                           │
│              │ ┌────────┐ ┌────────┐ ┌────────┐         │
│ Connection   │ │ SERVER │ │ AGENT  │ │ SIM    │         │
│              │ │   ●    │ │   ●    │ │   ●    │         │
│ Agent        │ │ ONLINE │ │ READY  │ │ READY  │         │
│              │ └────────┘ └────────┘ └────────┘         │
│ Simulation   │                                           │
│              │ ┌────────┐ ┌────────┐ ┌────────┐         │
│ Logs         │ │ DB     │ │ WS     │ │ QUEST  │         │
│              │ │   ●    │ │   ●    │ │   ●    │         │
│ Scenario     │ │CONNECTED││CONNECTED││CONNECTED│        │
│              │ └────────┘ └────────┘ └────────┘         │
│ Test         │                                           │
│              │ CURRENT SESSION                           │
│ Developer    │ Map W2 / S2 / 4 Robots                   │
│              │                                           │
│ [User Mode]  │ Recent Event                              │
└──────────────┴───────────────────────────────────────────┘
```

### 4.3 SYSTEM STATUS 상세

```text
┌──────────────────────────────────────────────────────────────┐
│ Dashboard                                                    │
├──────────────────────────────────────────────────────────────┤
│ SYSTEM STATUS                                                │
│                                                              │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐                     │
│ │ SERVER   │ │  AGENT   │ │ SIMULATOR│                     │
│ │    ●     │ │    ●     │ │    ●     │                     │
│ │ ONLINE   │ │  READY   │ │  READY   │                     │
│ └──────────┘ └──────────┘ └──────────┘                     │
│                                                              │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐                     │
│ │ DATABASE │ │ WEBSOCKET│ │  QUEST   │                     │
│ │    ●     │ │    ●     │ │    ●     │                     │
│ │ CONNECTED│ │ CONNECTED│ │ CONNECTED│                     │
│ └──────────┘ └──────────┘ └──────────┘                     │
│                                                              │
│ CURRENT SESSION                                              │
│ Map       W2                                                │
│ Scenario  S2                                                │
│ Robots    4                                                 │
│ Orders    25 / 25                                           │
└──────────────────────────────────────────────────────────────┘
```

요약 목록 형태 예:

```text
Server       ● ONLINE
Agent        ● READY
Simulator    ● RUNNING
Database     ● CONNECTED
WebSocket    ● CONNECTED
```

상태 표시:

```text
● GREEN   정상
● YELLOW  주의
● RED     오류
● GRAY    미연결 / 비활성
● BLUE    처리 중
```

### 4.4 Recent Events

Dashboard 하단에는 최근 이벤트를 일부만 표시한다.

```text
┌──────────────────────────────────────────────────────────────┐
│ RECENT EVENTS                                                 │
├──────────┬────────┬──────────────────────────────────────────┤
│ TIME     │ LEVEL  │ MESSAGE                                  │
├──────────┼────────┼──────────────────────────────────────────┤
│ 14:32:15 │ INFO   │ Simulation completed                     │
│ 14:32:10 │ INFO   │ Simulation started                       │
│ 14:32:03 │ WARN   │ Validation failed: UNREACHABLE           │
│ 14:32:02 │ INFO   │ Map validation started                   │
│ 14:32:01 │ INFO   │ Agent generation started                 │
└──────────┴────────┴──────────────────────────────────────────┘

                         [ View All Logs → ]
```

### 4.5 3D Preview

관리자 Dashboard 오른쪽에 작은 3D Preview를 둘 수 있다.

```text
┌───────────────────────┬─────────────────────┐
│ CURRENT SESSION       │ 3D PREVIEW          │
│                       │                     │
│ Map      W2           │      🤖 →           │
│ Robots   4            │                     │
│ Orders   50           │   ┌──────────┐     │
│                       │   │ WAREHOUSE│     │
│ Collision 0           │   └──────────┘     │
└───────────────────────┴─────────────────────┘
```

3D Preview는 Dashboard의 보조 정보이며, 실제 조작과 상세 관찰은 Simulation Debug에서 담당한다.

---

## 5. Connection Panel

사용자 화면에 노출되던 서버/연결 정보를 관리자 화면으로 이동한다.

```text
┌────────────────────────────────────────────┐
│ Connection                                 │
├────────────────────────────────────────────┤
│ SERVER                                     │
│ Host       192.168.0.12                    │
│ Port       8000                            │
│ Protocol   HTTP / WebSocket                │
│ Status     ● CONNECTED                     │
│                                            │
│ [ Ping ]        [ Reconnect ]              │
├────────────────────────────────────────────┤
│ WEBSOCKET                                  │
│ Status     ● CONNECTED                     │
│ Last RX    14:32:07                       │
│ Last TX    14:32:07                       │
│ Messages   1,245                           │
├────────────────────────────────────────────┤
│ QUEST 2                                    │
│ Device     Quest 2                         │
│ Status     ● CONNECTED                     │
│ FPS        72                              │
└────────────────────────────────────────────┘
```

사용자 UI에서는 서버 주소, Session ID 등의 개발용 정보를 최대한 숨긴다.

---

## 6. Agent Debug (Agent Monitor)

### 6.1 목적

이번 프로젝트에서 가장 중요한 디버그 화면이다. Agent의 실행 흐름을 한눈에 확인한다.

```text
사용자 입력
    ↓
Agent 판단
    ↓
Tool 실행
    ↓
Validation
    ↓
결과 (Response)
```

### 6.2 기본 화면

```text
┌──────────────────────────────────────────────────────────────┐
│ Agent Debug                                                  │
├───────────────┬──────────────────────────────────────────────┤
│ Agent Run     │ INPUT                                        │
│               │ ┌──────────────────────────────────────────┐ │
│ ● RUNNING     │ │ "랙 4줄짜리 창고를 만들어줘"             │ │
│               │ └──────────────────────────────────────────┘ │
│ Run ID        │                                              │
│ S2-001        │ AGENT GRAPH                                  │
│               │                                              │
│ Duration      │      ┌───────────┐                           │
│ 2.31 sec      │      │  Analyze  │                           │
│               │      └─────┬─────┘                           │
│               │            │                                 │
│               │      ┌─────▼─────┐                           │
│               │      │ Generate  │                           │
│               │      └─────┬─────┘                           │
│               │            │                                 │
│               │      ┌─────▼─────┐                           │
│               │      │ Validate  │                           │
│               │      └─────┬─────┘                           │
│               │            │                                 │
│               │      ┌─────▼─────┐                           │
│               │      │ Response  │                           │
│               │      └───────────┘                           │
└───────────────┴──────────────────────────────────────────────┘
```

### 6.3 Node 상태

각 Agent Node는 상태를 표시한다.

완료 (Completed)

```text
┌──────────────┐
│ ✓ Analyze    │
│   0.12 sec   │
└──────────────┘
```

실행 중 (Running)

```text
┌──────────────┐
│ ● Generate   │
│   RUNNING    │
└──────────────┘
```

대기 (Waiting)

```text
┌──────────────┐
│ ○ Validate   │
│   WAITING    │
└──────────────┘
```

오류 (Error)

```text
┌──────────────┐
│ ✕ Validate   │
│   ERROR      │
└──────────────┘
```

### 6.4 Agent Node Detail

Agent Node를 선택하면 상세 패널을 표시한다.

```text
┌──────────────────────────────────────────┐
│ NODE DETAIL                              │
├──────────────────────────────────────────┤
│ Node                                     │
│ Validate                                 │
│                                          │
│ Status                                   │
│ ✓ COMPLETED                              │
│                                          │
│ Duration                                 │
│ 1.24 sec                                 │
│                                          │
│ TOOL                                     │
│ validate_map                             │
│                                          │
│ INPUT                                    │
│ map_version: W2-v3                       │
│                                          │
│ RESULT                                   │
│ valid: false                             │
│ error: UNREACHABLE                       │
│                                          │
│ [ View Raw JSON ]                        │
└──────────────────────────────────────────┘
```

### 6.5 Tool Call Timeline

Agent Graph와 별도로 실제 Tool 호출 내역을 시간 순서로 보여준다.

```text
┌──────────────────────────────────────────────────────────────┐
│ TOOL CALL TIMELINE                                            │
├────────┬────────────────────┬──────────┬─────────────────────┤
│ TIME   │ TOOL               │ STATUS   │ DURATION            │
├────────┼────────────────────┼──────────┼─────────────────────┤
│14:32:01│ generate_map       │ ✓        │ 1.82s               │
│14:32:02│ validate_map       │ ✓        │ 0.24s               │
│14:32:03│ ask_user           │ ✓        │ 0.03s               │
│14:32:10│ simulate           │ ✓        │ 4.21s               │
│14:32:15│ analyze            │ ●        │ running             │
└────────┴────────────────────┴──────────┴─────────────────────┘
```

### 6.6 Agent Debug 데이터 흐름

현재 시스템 구조에 맞춰 다음 흐름을 보여준다.

```text
LLM
 ↓
Requirements JSON
 ↓
generate_map()
 ↓
validate_map()
 ↓
Result
```

Agent의 내부 사고 과정을 그대로 노출하기보다 실제 시스템 이벤트와 Tool Call을 중심으로 표시한다.

---

## 7. Simulation Debug

### 7.1 기본 화면

실제 시뮬레이션 상태를 확인하고 제어하는 화면이다.

```text
┌──────────────────────────────────────────────────────────────┐
│ Simulation Debug                                             │
├───────────────┬──────────────────────────────────────────────┤
│ SESSION       │                                              │
│               │                3D SIMULATION                 │
│ Scenario S2   │                                              │
│ Map W2        │             🤖 R1 → → →                     │
│               │                                              │
│ Robots 4      │        ┌─────────────────────┐               │
│ Orders 50     │        │                     │               │
│               │        │    3D WAREHOUSE     │               │
│ Strategy      │        │                     │               │
│ optimized     │        └─────────────────────┘               │
│               │                                              │
├───────────────┴──────────────────────────────────────────────┤
│ SIMULATION CONTROL                                           │
│                                                              │
│ [ ▶ ] [ ❚❚ ] [ ■ ]       Speed [1x] [2x] [4x]               │
│                                                              │
│ ━━━━━━━━━━━━━━━━━━━━━●━━━━━━━━━━━━━━━ 76%                    │
└──────────────────────────────────────────────────────────────┘
```

### 7.2 Simulation Status

```text
┌───────────────────────┐
│ SIMULATION STATUS     │
├───────────────────────┤
│ Progress      76%     │
│ Step          842     │
│                       │
│ Completed     38/50   │
│ Waiting       17      │
│ Collision     0       │
│                       │
│ Runtime       42.1s   │
└───────────────────────┘
```

### 7.3 Simulation Timeline

재생바는 Timeline으로 설계한다.

```text
0                                      1102
│────────────────────●──────────────────│
                    842
```

```text
Step      842 / 1102
Runtime   42.1 sec
```

재생 컨트롤:

```text
[ Play ] [ Pause ]

Speed
[1x] [2x] [4x]
```

---

## 8. Robot · Overlay · Grid 디버그

### 8.1 Robot Debug

```text
┌──────────────────────────────────────────────────────────┐
│ ROBOTS                                                    │
├────┬─────────┬─────────┬────────┬──────────┬────────────┤
│ ID │ STATE   │ TASK    │ POS    │ WAIT     │ PATH       │
├────┼─────────┼─────────┼────────┼──────────┼────────────┤
│ R1 │ MOVE    │ O-021   │ 12,8   │ 0        │ ● ON       │
│ R2 │ WAIT    │ O-018   │ 11,8   │ 12       │ ● ON       │
│ R3 │ LOAD    │ O-019   │ 5,3    │ 0        │ OFF        │
│ R4 │ MOVE    │ O-022   │ 8,11   │ 3        │ ● ON       │
└────┴─────────┴─────────┴────────┴──────────┴────────────┘
```

로봇을 선택하면:

```text
Robot R2

State
WAIT

Task
O-018

Position
(11, 8)

Wait Count
12

Current Target
Outbound Dock

[ Focus Robot ]
```

`Focus Robot`을 선택하면 3D 창고에서 해당 로봇을 중심으로 카메라를 이동하고, 이후 그 로봇을 따라가도록 한다.

### 8.2 3D Debug Overlay

관리자/개발자 모드에서만 다음 정보를 표시한다.

```text
DEBUG OVERLAY

☑ Robot ID
☑ Robot Path
☑ Task ID
☑ Grid
☑ Collision
☑ Waiting
☑ Bottleneck
☐ Coordinate
```

3D 공간에서는 다음과 같이 표시한다.

```text
       R1
       ↓
───────→────────

       R2
       ↓
      WAIT 12

          🔴
       BOTTLENECK
        (12,8)
```

### 8.3 Grid Debug

Grid Cell을 선택하면 해당 Cell의 상세 정보를 표시한다.

```text
┌──────────────────────────┐
│ CELL DETAIL              │
├──────────────────────────┤
│ X             12         │
│ Y              8         │
│                          │
│ Type          aisle      │
│ Occupied      false      │
│ Robot         R2         │
│ Wait Count    12         │
│                          │
│ [ Focus ]                │
└──────────────────────────┘
```

검증 실패나 병목 문제를 찾을 때 활용한다.

---

## 9. 로그

### 9.1 Log Viewer

```text
┌──────────────────────────────────────────────────────────────┐
│ LOG VIEWER                                  [Export] [Clear] │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│ [ ALL ] [ ERROR ] [ WARN ] [ AGENT ] [ API ] [ SIM ] [ VR ]│
│                                                              │
├──────────┬────────┬──────────┬───────────────────────────────┤
│ TIME     │ LEVEL  │ MODULE   │ MESSAGE                       │
├──────────┼────────┼──────────┼───────────────────────────────┤
│14:32:15  │ INFO   │ SIM      │ simulation completed          │
│14:32:10  │ INFO   │ SIM      │ simulation started            │
│14:32:03  │ WARN   │ VALIDATE │ UNREACHABLE (12,3)            │
│14:32:03  │ AGENT  │ AGENT    │ ask_user invoked              │
│14:32:02  │ INFO   │ AGENT    │ validation started            │
│14:32:01  │ INFO   │ AGENT    │ map generation started        │
└──────────┴────────┴──────────┴───────────────────────────────┘
```

`[Clear]`는 확인을 거치는 위험 버튼이다(UI 설계 ① 6.5).

### 9.2 Log Filter

Level 필터:

```text
LEVEL

☑ ERROR
☑ WARN
☑ INFO
☐ DEBUG
```

Module 필터:

```text
MODULE

☑ Agent
☑ Simulation
☑ API
☑ WebSocket
☑ Unity
☑ Validator
```

검색:

```text
[ 🔍 Search logs... ]
```

예를 들어 `UNREACHABLE`을 검색하면 관련 로그만 표시한다.

### 9.3 Log Detail

```text
┌────────────────────────────────────────┐
│ LOG DETAIL                             │
├────────────────────────────────────────┤
│ Time                                   │
│ 14:32:03.442                           │
│                                        │
│ Level                                  │
│ WARNING                                │
│                                        │
│ Module                                 │
│ Validator                              │
│                                        │
│ Code                                   │
│ UNREACHABLE                            │
│                                        │
│ Position                               │
│ (12, 3)                                │
│                                        │
│ Session                                │
│ S2-001                                 │
│                                        │
│ [ Raw JSON ]    [ Copy ]               │
└────────────────────────────────────────┘
```

---

## 10. 오류 표시

### 10.1 Error → 3D 공간 연동

관리자/개발자 UI에서 중요한 기능이다. 디버깅 효율을 높이기 위해 로그와 실제 3D 공간을 연결한다.

```text
Log
 ↓
Error Detail
 ↓
[ Focus in 3D ]
 ↓
문제 위치로 카메라 이동
```

예:

```text
UNREACHABLE
     ↓
Position (12,3)
     ↓
3D Camera 이동
     ↓
해당 위치 강조
```

3D 공간에서의 모습:

```text
             3D Warehouse

                 🔴
              (12, 3)
             UNREACHABLE
```

이 방식으로 **로그 ↔ 실제 3D 공간**을 연결한다.

### 10.2 Validation Error UI

```text
┌──────────────────────────────────────────────┐
│ VALIDATION ERROR                             │
├──────────────────────────────────────────────┤
│                                              │
│ ✕ NO_DOCK_IN                                 │
│                                              │
│ Input does not contain an inbound dock.      │
│                                              │
│ Error Cell                                   │
│ (12, 4)                                      │
│                                              │
│ Agent Action                                 │
│ → Question generated                         │
│                                              │
│ [ Focus Error ]                              │
└──────────────────────────────────────────────┘
```

3D 공간에서도 해당 위치를 강조한다.

### 10.3 Error Code Panel

현재 시나리오에서 사용하는 검증 오류 코드는 다음과 같이 관리한다.

```text
SCHEMA_INVALID
UNREACHABLE
NO_DOCK_IN
NO_DOCK_OUT
SIZE_EXCEEDED
```

관리자 화면에서는 사람이 이해할 수 있는 설명과 함께 표시한다.

```text
┌────────────────────────────────────────┐
│ Validation Errors                      │
├───────────────┬────────────────────────┤
│ CODE          │ DESCRIPTION            │
├───────────────┼────────────────────────┤
│ SCHEMA_INVALID│ 필수 필드/값 오류      │
│ UNREACHABLE   │ 접근 불가능한 영역     │
│ NO_DOCK_IN    │ 입하 도크 없음         │
│ NO_DOCK_OUT   │ 출하 도크 없음         │
│ SIZE_EXCEEDED │ 격자 크기 초과         │
└───────────────┴────────────────────────┘
```

---

## 11. 시나리오 · 테스트 · 시연 데이터

### 11.1 Scenario Manager

미리 정의된 테스트 시나리오를 선택하고 실행한다.

```text
┌────────────────────────────────────────────────────────────┐
│ SCENARIO MANAGER                                           │
├────────────────────────────────────────────────────────────┤
│                                                            │
│ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐             │
│ │   S1   │ │   S2   │ │  S2-H  │ │   S3   │             │
│ │        │ │        │ │        │ │        │             │
│ │ W1     │ │ W2     │ │ W2     │ │ W3     │             │
│ │ 2 Rob. │ │ 4 Rob. │ │ 8 Rob. │ │ 4 Rob. │             │
│ │ 10+10  │ │ 25+25  │ │ 25+25  │ │ 25+25  │             │
│ └────────┘ └────────┘ └────────┘ └────────┘             │
│                                                            │
├────────────────────────────────────────────────────────────┤
│ Selected: S2                                               │
│                                                            │
│ [ Load ]                         [ Run Scenario ]           │
└────────────────────────────────────────────────────────────┘
```

### 11.2 Baseline / Optimized 비교

관리자 화면에서 기준 전략과 최적화 전략을 비교 실행할 수 있도록 한다.

```text
┌─────────────────────────────────────────────┐
│ Strategy Comparison                         │
├─────────────────────────────────────────────┤
│                                             │
│ Scenario : S2                               │
│                                             │
│             Baseline       Optimized        │
│ Time        1420 steps     1080 steps       │
│ Orders      50 / 50        50 / 50          │
│ Collision   0              0                │
│                                             │
│ Improvement                                 │
│             23.9%                           │
│                                             │
│ [ Run Baseline ] [ Run Optimized ]          │
└─────────────────────────────────────────────┘
```

화면 안의 수치는 레이아웃 예시다. 실제 화면에는 엔진 측정값만 표시한다.

### 11.3 Test Runner

```text
┌────────────────────────────────────────────────────────────┐
│ TEST RUNNER                                                 │
├────────────────────────────────────────────────────────────┤
│                                                            │
│ M01  음성 → 창고 생성                     ✓ PASS           │
│ M02  검증 실패 → Agent 질문              ✓ PASS           │
│ M03  다중 로봇 재생                      ✓ PASS           │
│ M04  E2E                                ✓ PASS           │
│ M05  구조 변경 창고                      ○ NOT RUN         │
│                                                            │
├────────────────────────────────────────────────────────────┤
│ Last Run                                                   │
│ Passed     4                                               │
│ Failed     0                                               │
│                                                            │
│ [ Run Selected ]                [ Run All ]                 │
└────────────────────────────────────────────────────────────┘
```

### 11.4 Fixture / Demo Data

시연용 데이터를 관리자 화면에서 선택할 수 있도록 한다. 사용자 패널에서 제거한 W1~W6 버튼과 오프라인 재생은 이 화면으로 옮긴다.

```text
┌──────────────────────────────────┐
│ Demo Fixtures                    │
├──────────────────────────────────┤
│ Warehouse                        │
│ [ W1 ] [ W2 ] [ W3 ]             │
│                                  │
│ Scenario                         │
│ [ S1 ] [ S2 ] [ S2-H ] [ S3 ]   │
│                                  │
│ Playback                         │
│ [ Load Offline Data ]             │
│                                  │
│ [ Reset Session ]                 │
└──────────────────────────────────┘
```

---

## 12. Performance · Settings

### 12.1 Performance Monitor

개발자 모드에서만 노출한다.

```text
┌──────────────────────────────────────────────┐
│ PERFORMANCE                                  │
├──────────────────────────────────────────────┤
│                                              │
│ QUEST 2                                      │
│ FPS             --                           │
│ Frame Time      --                           │
│ CPU             --                           │
│ GPU             --                           │
│ Memory          --                           │
│                                              │
│ NETWORK                                      │
│ Ping            --                           │
│ RX              --                           │
│ TX              --                           │
│                                              │
│ SIMULATION                                   │
│ Step Time      --                            │
└──────────────────────────────────────────────┘
```

실제 측정 데이터가 연결되기 전에는 임의의 값을 표시하지 않고 `--`를 사용한다.

### 12.2 Settings

```text
┌────────────────────────────────────┐
│ Settings                           │
├────────────────────────────────────┤
│ Network                            │
│ Server IP      [ 192.168.0.12 ]   │
│ Port           [ 8000 ]            │
│                                    │
│ Simulation                         │
│ Default Robots [ 4 ]               │
│ Default Orders [ 25 / 25 ]         │
│                                    │
│ Debug                              │
│ ☑ Show Agent Graph                 │
│ ☑ Show Grid                        │
│ ☑ Show Robot ID                    │
│ ☑ Show Collision                   │
│ ☑ Verbose Log                      │
│                                    │
│ Performance                        │
│ ☐ FPS Overlay                      │
│ ☐ Network Overlay                  │
└────────────────────────────────────┘
```

---

## 13. 관리자에게 허용할 제어 기능

### 13.1 읽기 중심

다음은 기본적으로 읽기 전용으로 둔다.

```text
Agent 내부 판단 결과
Simulation log
Robot state
Analysis result
Validation result
```

### 13.2 제어 가능

```text
서버 재연결
시나리오 선택
테스트 실행
로그 필터
Debug Overlay
시뮬레이션 중지 / 재시작
Fixture 선택
오프라인 데이터 재생
```

관리자 화면이라고 해서 내부 상태를 직접 수정할 수 있게 만들지는 않는다. 중지·재시작·초기화 같은 명령은 UI 설계 ① 6.5의 확인 절차를 거친다.

---

## 14. 정보 간 연결 (UX)

관리자/개발자 UI에서 가장 중요한 것은 정보량이 아니라 **정보 간 연결**이다.

```text
Agent Node
    ↓
Tool Call
    ↓
Log
    ↓
Simulation
    ↓
3D Object
```

예:

```text
validate_map
     ↓
UNREACHABLE
     ↓
Log
     ↓
Cell (4,2)
     ↓
[Focus]
     ↓
3D에서 해당 Cell 강조
```

또는:

```text
bottleneck (12,8)
     ↓
Analysis
     ↓
Log
     ↓
Heatmap
     ↓
3D에서 해당 위치 강조
```

---

## 15. Unity 구조

### 15.1 Unity Hierarchy (관리자 UI 상세)

```text
UIRoot
│
├── UserUI
│   ├── Header
│   ├── Progress
│   ├── Panels
│   └── Feedback
│
├── AdminUI
│   │
│   ├── AdminHeader
│   │
│   ├── Navigation
│   │   ├── Dashboard
│   │   ├── Connection
│   │   ├── Agent
│   │   ├── Simulation
│   │   ├── Logs
│   │   ├── Scenario
│   │   ├── Test
│   │   └── Settings
│   │
│   ├── Content
│   │   ├── DashboardPanel
│   │   ├── ConnectionPanel
│   │   ├── AgentPanel
│   │   ├── SimulationPanel
│   │   ├── LogPanel
│   │   ├── ScenarioPanel
│   │   ├── TestPanel
│   │   └── SettingsPanel
│   │
│   └── DebugOverlay
│       ├── GridOverlay
│       ├── RobotOverlay
│       ├── PathOverlay
│       ├── CollisionOverlay
│       └── BottleneckOverlay
│
└── SystemFeedback
    ├── Toast
    └── Modal
```

세 모드 전체의 Canvas 구조는 UI 설계 ①의 "Unity Canvas 전체 구조"를 본다.

### 15.2 구현 대상 Prefab

다음 단계의 Unity 구현을 고려하면 다음 Prefab 단위로 분리한다. 사용자 UI Prefab은 UI 설계 ①을 본다.

```text
Admin
├── AdminDashboard.prefab
├── ConnectionPanel.prefab
├── AgentMonitorPanel.prefab
├── SimulationMonitorPanel.prefab
├── LogViewerPanel.prefab
├── ScenarioManagerPanel.prefab
└── TestRunnerPanel.prefab

Developer
├── AgentDebugPanel.prefab
├── GridDebugPanel.prefab
├── RawJsonPanel.prefab
├── ApiDebugPanel.prefab
└── PerformancePanel.prefab

Debug
└── DebugOverlay.prefab
```

### 15.3 관리자 UI State 구조

```text
ADMIN
│
├── DASHBOARD
│
├── CONNECTION
│
├── AGENT_MONITOR
│   ├── RUNNING
│   ├── COMPLETED
│   └── ERROR
│
├── SIMULATION
│   ├── IDLE
│   ├── RUNNING
│   ├── PAUSED
│   ├── COMPLETED
│   └── ERROR
│
├── LOGS
│
├── SCENARIO
│
└── TEST
```

---

## 16. 구현 우선순위 (관리자 · 개발자)

### 1차 구현 — 필수

```text
Dashboard
Agent
Simulation
Logs
```

이 네 화면을 먼저 구현한다.

### 2차 구현 — 개발 편의

```text
Scenario
Test
Grid Debug
Robot Debug
```

### 3차 구현 — 상세 개발 도구

```text
Raw JSON
API Debug
Performance
Settings
Fixture Manager
```

---

## 17. 다음 구현 단계

현재 설계에서 우선 구현할 화면은 다음 네 개다.

1. **Admin Dashboard**
   - 전체 시스템 상태
   - 현재 Session
   - 최근 이벤트
   - 3D Preview

2. **Agent Debug**
   - 입력
   - Agent Graph
   - Node 상태
   - Node Detail
   - Tool Call Timeline

3. **Simulation Debug**
   - 3D Simulation
   - 진행률
   - Robot 상태
   - Simulation Control
   - Debug Overlay

4. **Log Viewer**
   - 로그 테이블
   - Level/Module 필터
   - 검색
   - Log Detail
   - 3D 위치 Focus

그 이후 Scenario / Test / Grid / Performance 등을 추가한다.

---

## 18. 설계 원칙

최종적으로 관리자/개발자 UI는 다음 원칙을 따른다.

> **사용자 UI는 작업을 단순하게 만든다.**
>
> **관리자 UI는 시스템 상태를 한눈에 보여준다.**
>
> **개발자 UI는 Agent → Tool → Simulation → Log의 원인을 추적할 수 있게 한다.**

특히 Agent Debug에서는

```text
사용자 입력
   ↓
Agent 판단
   ↓
Tool 호출
   ↓
검증
   ↓
Simulation
   ↓
Analysis
```

의 흐름을 하나의 실행 기록으로 연결하는 것을 기본 구조로 삼는다.

---

## 정리 메모 (통합하면서 발견한 차이)

원본 두 문서끼리 표현이 달랐던 부분입니다. 구현 전에 하나로 정합니다.

| 항목 | 차이 | 제안 |
|---|---|---|
| 메뉴 구성 | 2.1 메뉴는 Settings가 Developer 아래, 3.1 레이아웃과 15.1 Hierarchy는 Settings가 관리자 Navigation에 있음. Agent Debug도 Admin(Agent Monitor)과 Developer(Agent Debug) 양쪽에 등장 | 2.2 권장 최종 구조를 기준으로 하되 Settings 위치를 한 곳으로 결정 |
| 주문 수 표기 | 4.3은 `Orders 25 / 25`(입하/출하), 4.5·7.1은 `Orders 50`(합계) | 화면마다 같은 표기로 통일 (예: `50 (25+25)`) |
| 우선순위 이름 | 16장 1차 4개(Dashboard·Agent·Simulation·Logs)와 UI 설계 ① 11장 Phase 1 Admin 5개(Connection 포함)가 다름 | Connection을 1차에 넣을지 결정 |
| 상태 색상 | 4.3은 YELLOW=주의, GRAY=미연결/비활성. UI 설계 ①은 YELLOW=주의/대기 | ① 6.2 상태 색상 기준으로 통일 |
| Log Viewer 레벨 | 9.1 예시에 `AGENT` 레벨이 있으나 9.2 Level 필터에는 ERROR·WARN·INFO·DEBUG만 있음 | `AGENT`는 Module로 옮기고 레벨은 4단계로 통일 |
