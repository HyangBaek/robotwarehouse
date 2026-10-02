"""시뮬레이션 노드: prepare_sim, simulate, compare (도구만 호출)."""
from ..state import AgentState


def prepare_sim(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(하재윤): tools.orders


def simulate(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(하재윤): tools.simulation (optimized + baseline)


def compare(s: AgentState) -> dict:
    raise NotImplementedError  # TODO(방유진): tools.compare, WS sim_ready·compare
