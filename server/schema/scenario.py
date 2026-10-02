"""시나리오·주문 (DR-02)."""
from typing import Literal
from pydantic import BaseModel


class Order(BaseModel):
    order_id: str
    kind: Literal["inbound", "outbound"]
    spec: str
    qty: int = 1
    arrive_t: int = 0


class Scenario(BaseModel):
    scenario_id: str = "custom"
    map_version: str
    robots: int
    inbound: int
    outbound: int
    strategy: Literal["optimized", "baseline"] = "optimized"
    seed: int = 42
