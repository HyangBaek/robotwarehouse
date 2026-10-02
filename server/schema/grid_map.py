"""격자 지도 스키마 (DR-01). docs/schema.md 와 같은 내용이어야 한다."""
from typing import Literal, Optional
from pydantic import BaseModel

SCHEMA_VERSION = "1.0"
CellType = Literal["aisle", "rack", "wall", "dock_in", "dock_out", "charge"]


class Cell(BaseModel):
    x: int
    y: int
    type: CellType
    rack_id: Optional[str] = None


class Rack(BaseModel):
    rack_id: str
    levels: int
    slot_spec: str
    capacity: int


class Dock(BaseModel):
    dock_id: str
    type: Literal["dock_in", "dock_out"]
    x: int
    y: int


class OneWay(BaseModel):
    from_: tuple[int, int]
    to: tuple[int, int]
    dir: Literal["N", "S", "E", "W"]


class Rules(BaseModel):
    one_way: list[OneWay] = []
    passing_allowed: list[tuple[int, int]] = []


class GridMap(BaseModel):
    schema_version: str = SCHEMA_VERSION
    cell_size_m: float = 1.0
    width: int
    height: int
    cells: list[Cell]
    racks: list[Rack] = []
    docks: list[Dock] = []
    rules: Rules = Rules()
