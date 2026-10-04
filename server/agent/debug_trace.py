"""Agent 그래프의 노드 실행 순서와 보낸 메시지를 출력한다 (서버, VR 없이).

예: python -m agent.debug_trace --text "랙 6줄, 통로 폭 3미터" --answer "입하 출하 하나씩, 나머지는 기본값"
LLM 은 쓰지 않는다 (정규식 해석). 실제 LLM 으로 보려면 --llm 을 붙인다 (.env 설정 사용).
"""
import argparse
import asyncio

from api.flow import Flow
from api.store import Store


class _PrintStore(Store):
    async def emit(self, s, msg):
        t = msg["type"]
        info = msg.get("message") or msg.get("text") or msg.get("summary") or msg.get("map_version") or ""
        print(f"    WS {t:10} {msg.get('node', '')} {str(info)[:90]}")


async def main(text: str, answers: list[str], use_llm: bool):
    llm = None
    if use_llm:
        from services.llm import make_llm
        llm = make_llm()
    flow = Flow(_PrintStore(), llm=llm, stt=None, node_delay=0)
    s = flow.store.session("debug")
    turns = [("compose", {"text": text})] + [("answer", {"answer": a}) for a in answers]
    for intent, inp in turns:
        print(f"[{intent}] {list(inp.values())[0]}")
        out = await flow.run_graph(s, intent=intent, **inp)
        print("  노드 순서:", " -> ".join(out.get("trace", [])))


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--text", required=True)
    p.add_argument("--answer", action="append", default=[])
    p.add_argument("--llm", action="store_true")
    a = p.parse_args()
    asyncio.run(main(a.text, a.answer, a.llm))
