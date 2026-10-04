"""시나리오, 주문 (DR-02)."""
from typing import Literal
from pydantic import BaseModel


class Order(BaseModel):
    """planner.create_orders 출력과 같은 필드."""
    order_id: str
    type: Literal["inbound", "outbound"]
    arrival_t: int = 0
    spec: str = "pallet_1100x1100"    # 제품 규격: pallet_1100x1100 | pallet_1200x1000
    qty: int = 1                      # 파레트 수 (주문 1건 = 파레트 1개)


class Scenario(BaseModel):
    scenario_id: str = "custom"
    map_version: str
    robots: int
    inbound: int
    outbound: int
    spec_b_pct: int = 0               # 주문 중 1200x1000 규격 비율(%), 나머지는 1100x1100
    strategy: Literal["optimized", "baseline"] = "optimized"
    seed: int = 42
