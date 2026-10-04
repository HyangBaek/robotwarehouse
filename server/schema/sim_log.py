"""시뮬레이션 로그 (DR-03). 기준: planner 출력과 VR 클라이언트(RobotPlayback.cs)."""
from typing import Literal, Optional
from pydantic import BaseModel, ConfigDict, Field


class RobotPos(BaseModel):
    id: str                       # "R1", "R2", ...
    x: int
    y: int
    state: str = "idle"           # 상태값: move_empty(빈 이동), move_loaded(적재 이동), load(적재), unload(하역), wait(대기), idle(유휴)
    task_id: Optional[str] = None


class Frame(BaseModel):
    t: int
    robots: list[RobotPos]


class OrderRecord(BaseModel):
    order_id: str
    type: Literal["inbound", "outbound"]
    robot_id: Optional[str] = None
    spec: str = "pallet_1100x1100"    # 제품 규격 (파레트)
    qty: int = 1                      # 파레트 수
    arrival_t: int = 0
    assigned_t: Optional[int] = None
    done_t: Optional[int] = None
    status: Literal["pending", "active", "done", "rejected"] = "pending"


class CellStat(BaseModel):
    model_config = ConfigDict(populate_by_name=True)
    x: int
    y: int
    pass_: int = Field(0, alias="pass")
    wait: int = 0


class SimLog(BaseModel):
    frames: list[Frame]
    orders: list[OrderRecord]
    cell_stats: list[CellStat] = []
    collisions: list[dict] = []
