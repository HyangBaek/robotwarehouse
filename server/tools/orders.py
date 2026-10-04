"""create_orders: 시나리오 -> 주문 목록 (FR-12). 같은 seed면 같은 주문 (TC-ENG-02)."""
from planner import DEFAULT_CFG
from planner import create_orders as _create


def create_orders(scenario: dict) -> list[dict]:
    """반환: [{"order_id", "type", "arrival_t"}]"""
    return _create(int(scenario.get("inbound", 0)), int(scenario.get("outbound", 0)),
                   int(scenario.get("seed", 42)), int(scenario.get("arrival_span", DEFAULT_CFG["arrival_span"])))
