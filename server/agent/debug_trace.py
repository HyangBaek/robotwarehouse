"""노드 실행 순서 출력. 예: python -m agent.debug_trace --text '랙 6줄, 통로 폭 3미터'"""
import argparse

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--text", required=True)
    args = p.parse_args()
    # TODO(방유진): build_graph().stream(...) 으로 노드 이름을 순서대로 출력
    raise SystemExit("TODO: 구현 필요")
