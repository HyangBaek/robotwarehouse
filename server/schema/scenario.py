"""시나리오·주문 (DR-02)."""
from typing import Literal
from pydantic import BaseModel


class Order(BaseModel):
    """planner.create_orders 출력과 같은 필드."""
    order_id: str
    type: Literal["inbound", "outbound"]
    arrival_t: int = 0


class Scenario(BaseModel):
    scenario_id: str = "custom"
    map_version: str
    robots: int
    inbound: int
    outbound: int
    strategy: Literal["optimized", "baseline"] = "optimized"
    seed: int = 42
