"""시뮬레이션 로그 (DR-03). 기준: planner 출력과 VR 클라이언트(RobotPlayback.cs)."""
from typing import Literal, Optional
from pydantic import BaseModel, ConfigDict, Field


class RobotPos(BaseModel):
    id: str                       # "R1", "R2", ...
    x: int
    y: int
    state: str = "idle"           # move_empty / move_loaded / load / unload / wait / idle
    task_id: Optional[str] = None


class Frame(BaseModel):
    t: int
    robots: list[RobotPos]


class OrderRecord(BaseModel):
    order_id: str
    type: Literal["inbound", "outbound"]
    robot_id: Optional[str] = None
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
