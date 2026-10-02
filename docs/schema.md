# 격자 지도 스키마 · 좌표 규칙 (DR-01, NFR-11)

> VR·경로 엔진·Agent가 함께 쓰는 단일 데이터 계약입니다. 스키마를 바꾸면 이 문서와 `schema_version`을 함께 올립니다.
> 현재 VR 클라이언트가 지원하는 버전: `1.0` (`GridMap.SupportedSchemaVersion`). 다르면 VR 패널에 경고가 뜹니다.

> 변경 시: 1) `schema_version` 올림 2) 이 문서 수정 3) 팀 채널에 공지 4) server/schema, unity Messages 동시 수정

- 칸 크기: 1m × 1m (로봇 1대)
- 좌표: 격자 (x, y) → Unity (x, 0, y), 원점은 창고 왼쪽 아래 모서리 칸의 중심
- 칸 유형: aisle / rack / wall / dock_in / dock_out / charge (지정하지 않은 칸은 aisle)
- 예시: `server/tests/fixtures/maps/w1_small.json`

## 1. 좌표 규칙 (VR 구현 기준, TC-VR-01)

| 항목 | 규칙 |
|---|---|
| 칸 크기 | `cell_size_m` (기본 1.0m). 칸 1개 = 로봇 1대 |
| 원점 | 격자 `(0, 0)` 칸의 **중심**이 창고 루트의 로컬 원점 |
| 축 | 격자 `+x` → Unity `+X`(동쪽), 격자 `+y` → Unity `+Z`(북쪽), 높이는 Unity `+Y` |
| 변환 | `Unity(x, 0, y) = (x × cell, 0, y × cell)` — `GridCoord.CellToLocal` |
| 역변환 | 가장 가까운 칸 중심으로 반올림 — `GridCoord.LocalToCell` |
| 방향 표기 | `N` = `+y`, `S` = `-y`, `E` = `+x`, `W` = `-x` (일방통행 `rules.one_way[].dir`) |

미니어처 보기에서는 창고 루트에 축척만 곱합니다. 좌표 규칙은 그대로입니다.

## 2. 필드

```json
{
  "schema_version": "1.0",
  "cell_size_m": 1.0,
  "width": 38, "height": 18,
  "cells": [ {"x": 0, "y": 0, "type": "wall"}, {"x": 4, "y": 4, "type": "rack", "rack_id": "R01"} ],
  "racks": [ {"rack_id": "R01", "levels": 3, "slot_spec": "pallet_1100x1100", "capacity": 30} ],
  "docks": [ {"dock_id": "IN1", "type": "dock_in", "x": 12, "y": 0} ],
  "rules": { "one_way": [ {"from": [15, 1], "to": [15, 16], "dir": "N"} ], "passing_allowed": [] }
}
```

| 필드 | 필수 | VR에서 쓰는 방식 |
|---|:-:|---|
| `width`, `height` | O | 바닥 텍스처 크기, 창고 중심 계산 |
| `cells[].type` | O | `rack` → 박스(높이 = 단 수 × 0.5m), `wall` → 1.2m 블록, `dock_in`·`dock_out`·`charge` → 바닥 색, 없는 칸 = `aisle` |
| `cells[].rack_id` | 랙만 | 랙 편집(SC-11) 시 `moves[].rack_id` |
| `racks[].levels` | O | 랙 박스 높이 |
| `docks[]` | O | `cells`에 없어도 도크 칸으로 칠함 |
| `rules` | - | VR은 표시만 하지 않음(엔진이 사용) |

## 3. 바닥 색

| 칸 | 색 |
|---|---|
| 통로 | 회색 체크 무늬 |
| 입하 도크 | 파랑 |
| 출하 도크 | 주황 |
| 충전·대기 | 초록 |
| 검증 오류 칸 | 빨강 반투명 (SC-03) |
