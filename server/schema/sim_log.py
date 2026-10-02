"""시뮬레이션 로그 (DR-03)."""
from typing import Optional
from pydantic import BaseModel


class RobotPos(BaseModel):
    id: int
    x: int
    y: int
    state: str = "move"          # move / load / wait / idle
    task_id: Optional[str] = None


class Frame(BaseModel):
    t: int
    robots: list[RobotPos]


class OrderRecord(BaseModel):
    order_id: str
    robot_id: int
    assigned_t: int
    done_t: Optional[int] = None


class CellStat(BaseModel):
    x: int
    y: int
    pass_: int = 0
    wait: int = 0


class SimLog(BaseModel):
    frames: list[Frame]
    orders: list[OrderRecord]
    cell_stats: list[CellStat] = []
    collisions: list[dict] = []
