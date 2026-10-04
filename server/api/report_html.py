"""최종 리포트 HTML (GET /sim/{id}/report.html). Apple visionOS 창 스타일: 유리 패널, 세로 탭 오너먼트, 알약 칩.

외부 라이브러리 없이 SVG로 차트를 그린다 (오프라인 시연, 보고서 캡처용).
"""
from __future__ import annotations

from html import escape

C = {"loaded": "#FF9F0A", "empty": "#0A84FF", "handling": "#FFD60A", "wait": "#FF453A", "idle": "rgba(255,255,255,.22)",
     "opt": "#0A84FF", "base": "rgba(255,255,255,.38)", "good": "#30D158", "warn": "#FF9F0A", "info": "#64D2FF"}


def _n(v, suffix=""):
    if v is None:
        return "-"
    if isinstance(v, float):
        v = f"{v:,.1f}".rstrip("0").rstrip(".")
    elif isinstance(v, int):
        v = f"{v:,}"
    return f"{v}{suffix}"


def _tile(label, value, unit="", sub="", chip=None, good=True):
    chip_html = f'<span class="chip {"up" if good else "down"}">{escape(chip)}</span>' if chip else ""
    return (f'<div class="tile"><div class="tl">{escape(label)}</div>'
            f'<div class="tv">{escape(str(value))}<small>{escape(unit)}</small></div>'
            f'<div class="ts">{chip_html}{escape(sub)}</div></div>')


def _line_chart(series, w=760, h=330):
    """series: [(이름, 색, [(t, y)])]. y는 누적 완료 주문."""
    pts = [p for _, _, s in series for p in s]
    if not pts:
        return ""
    tmax = max(t for t, _ in pts) or 1
    ymax = max(y for _, y in pts) or 1
    L, R, T, B = 44, 16, 16, 34
    sx = lambda t: L + (w - L - R) * t / tmax
    sy = lambda y: T + (h - T - B) * (1 - y / ymax)
    grid = "".join(f'<line x1="{L}" x2="{w - R}" y1="{sy(ymax * i / 4):.1f}" y2="{sy(ymax * i / 4):.1f}" class="gl"/>'
                   f'<text x="{L - 8}" y="{sy(ymax * i / 4) + 4:.1f}" class="ax" text-anchor="end">{round(ymax * i / 4)}</text>'
                   for i in range(5))
    xt = "".join(f'<text x="{sx(tmax * i / 4):.1f}" y="{h - 10}" class="ax" text-anchor="middle">{round(tmax * i / 4)}</text>'
                 for i in range(5))
    body = ""
    for i, (_name, color, s) in enumerate(series):
        s = [(0, 0)] + list(s)
        d = " ".join(f"{'M' if j == 0 else 'L'}{sx(t):.1f},{sy(y):.1f}" for j, (t, y) in enumerate(s))
        if i == 0:
            area = d + f" L{sx(s[-1][0]):.1f},{sy(0):.1f} L{sx(0):.1f},{sy(0):.1f} Z"
            body += f'<path d="{area}" fill="url(#ga)"/>'
        body += f'<path d="{d}" fill="none" stroke="{color}" stroke-width="3" stroke-linejoin="round" stroke-linecap="round"/>'
    return (f'<svg viewBox="0 0 {w} {h}" class="chart"><defs><linearGradient id="ga" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="#0A84FF" stop-opacity=".35"/><stop offset="1" stop-color="#0A84FF" stop-opacity="0"/>'
            f'</linearGradient></defs>{grid}{xt}{body}'
            f'<text x="{w - R}" y="{T + 2}" class="ax" text-anchor="end">x축: 스텝 · y축: 완료 주문</text></svg>')


def _histogram(hist, w=1100, h=190):
    if not hist:
        return ""
    cmax = max(b["count"] for b in hist) or 1
    L, B, T = 12, 30, 24
    bw = (w - 2 * L) / len(hist)
    out = ""
    for i, b in enumerate(hist):
        bh = (h - B - T) * b["count"] / cmax
        x = L + i * bw + 6
        out += (f'<rect x="{x:.1f}" y="{h - B - bh:.1f}" width="{bw - 12:.1f}" height="{bh:.1f}" rx="10" fill="#0A84FF" opacity=".85"/>'
                f'<text x="{x + (bw - 12) / 2:.1f}" y="{h - B - bh - 6:.1f}" class="ax" text-anchor="middle">{b["count"]}</text>'
                f'<text x="{x + (bw - 12) / 2:.1f}" y="{h - 12}" class="ax" text-anchor="middle">{b["from"]}~{b["to"]}</text>')
    return f'<svg viewBox="0 0 {w} {h}" class="chart">{out}</svg>'


def _robot_rows(robots):
    keys = [("move_loaded", "loaded"), ("move_empty", "empty"), ("handling", "handling"), ("wait", "wait"), ("idle", "idle")]
    rows = ""
    for r in robots:
        total = r["steps"] or 1
        seg = "".join(f'<i style="flex:{r[k]};background:{C[c]}" title="{k} {r[k]}"></i>' for k, c in keys if r[k])
        rows += (f'<div class="rrow"><b>{escape(r["id"])}</b><div class="sbar">{seg}</div>'
                 f'<span class="num">{r["utilization_pct"]:.0f}%</span><span class="num dim">{r["orders"]}건</span></div>')
        _ = total
    return rows


def _compare_rows(rows):
    out = ""
    for r in rows or []:
        b, o = r["baseline"], r["optimized"]
        mx = max(abs(b or 0), abs(o or 0)) or 1
        out += (f'<div class="crow"><div class="cl">{escape(r["label"])}</div>'
                f'<div class="cb"><div class="bar"><i style="width:{abs(b or 0) / mx * 100:.1f}%;background:{C["base"]}"></i>'
                f'<em>{_n(b)}</em></div><div class="bar"><i style="width:{abs(o or 0) / mx * 100:.1f}%;background:{C["opt"]}"></i>'
                f'<em>{_n(o)}</em></div></div>'
                f'<span class="chip {"up" if r["better"] else "down"}">{escape(r.get("change_text") or "-")}</span></div>')
    return out


CSS = """:root{--glass:rgba(48,48,56,.46);--card:rgba(255,255,255,.07);--line:rgba(255,255,255,.16);--t1:rgba(255,255,255,.96);--t2:rgba(255,255,255,.62);--t3:rgba(255,255,255,.40)}
*{box-sizing:border-box}
body{margin:0;min-height:100vh;color:var(--t1);font:15px/1.45 -apple-system,"SF Pro Text","Apple SD Gothic Neo","Pretendard","Noto Sans KR",system-ui,sans-serif;
background:#1b2230;background-image:radial-gradient(900px 600px at 15% 10%,#5b6f8e 0,transparent 60%),radial-gradient(800px 700px at 90% 20%,#8a6e5a 0,transparent 55%),radial-gradient(1000px 800px at 50% 110%,#2f4a5c 0,transparent 60%);background-attachment:fixed;padding:48px 24px 64px}
.wrap{max-width:1180px;margin:0 auto;display:flex;gap:20px;align-items:flex-start}
.orn{position:sticky;top:48px;display:flex;flex-direction:column;gap:8px;padding:10px;border-radius:40px;background:var(--glass);backdrop-filter:blur(40px) saturate(180%);-webkit-backdrop-filter:blur(40px) saturate(180%);border:1px solid var(--line);box-shadow:0 20px 50px rgba(0,0,0,.35)}
.orn a{width:44px;height:44px;border-radius:22px;display:grid;place-items:center;color:var(--t1);transition:background .2s}
.orn a:hover{background:rgba(255,255,255,.16)}
.orn svg{width:22px;height:22px;fill:none;stroke:currentColor;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round}
.win{flex:1;min-width:0;border-radius:44px;background:var(--glass);backdrop-filter:blur(50px) saturate(180%);-webkit-backdrop-filter:blur(50px) saturate(180%);border:1px solid var(--line);box-shadow:0 30px 80px rgba(0,0,0,.40),inset 0 1px 0 rgba(255,255,255,.18);padding:36px 36px 28px}
header{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;margin-bottom:22px}
h1{font:600 32px/1.15 -apple-system,"SF Pro Display","Apple SD Gothic Neo",system-ui,sans-serif;margin:0;letter-spacing:-.01em}
.sub{color:var(--t2);margin-top:6px}
.pills{display:flex;gap:8px;flex-wrap:wrap}
.pill{padding:8px 14px;border-radius:999px;background:var(--card);border:1px solid var(--line);color:var(--t2);font-size:13px;white-space:nowrap}
h2{font:600 19px/1.2 -apple-system,"SF Pro Display",system-ui,sans-serif;margin:30px 0 12px;scroll-margin-top:24px}
.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}
.tile{background:var(--card);border-radius:26px;padding:16px 18px;border:1px solid rgba(255,255,255,.06)}
.tl{color:var(--t2);font-size:13px}
.tv{font:600 30px/1.2 -apple-system,"SF Pro Display",system-ui,sans-serif;margin:6px 0 4px;font-variant-numeric:tabular-nums}
.tv small{font-size:14px;color:var(--t2);font-weight:500;margin-left:2px}
.ts{color:var(--t3);font-size:12px;display:flex;gap:6px;align-items:center;flex-wrap:wrap}
.chip{display:inline-block;padding:2px 9px;border-radius:999px;font-size:12px;font-weight:600}
.chip.up{background:rgba(48,209,88,.18);color:#30D158}.chip.down{background:rgba(255,69,58,.18);color:#FF6961}
.card{background:var(--card);border-radius:28px;padding:18px 20px;border:1px solid rgba(255,255,255,.06)}
ul.ins{list-style:none;margin:0;padding:0;display:grid;gap:10px}
ul.ins li{display:flex;gap:10px;align-items:baseline}
.dot{width:8px;height:8px;border-radius:4px;flex:none;transform:translateY(-1px)}
.two{display:grid;grid-template-columns:1.4fr 1fr;gap:12px}
.chart{width:100%;height:auto;display:block}
.gl{stroke:rgba(255,255,255,.10)}.ax{fill:var(--t3);font-size:11px}
.rrow{display:grid;grid-template-columns:52px 1fr 52px 48px;gap:12px;align-items:center;padding:6px 0}
.sbar{display:flex;height:14px;border-radius:7px;overflow:hidden;background:rgba(255,255,255,.06)}
.sbar i{display:block}
.num{text-align:right;font-variant-numeric:tabular-nums}.dim{color:var(--t2)}
.lgs{display:flex;gap:14px;flex-wrap:wrap;color:var(--t2);font-size:12px;margin-top:10px}
.lg i{display:inline-block;width:10px;height:10px;border-radius:5px;margin-right:6px;vertical-align:-1px}
.crow{display:grid;grid-template-columns:200px 1fr 86px;gap:14px;align-items:center;padding:10px 0;border-top:1px solid rgba(255,255,255,.06)}
.crow:first-child{border-top:0}
.cl{color:var(--t2)}
.bar{position:relative;height:16px;margin:3px 0;border-radius:8px;background:rgba(255,255,255,.05)}
.bar i{position:absolute;left:0;top:0;bottom:0;border-radius:8px}
.bar em{position:absolute;right:8px;top:-1px;font-style:normal;font-size:12px;color:var(--t1);font-variant-numeric:tabular-nums}
.stats{display:grid;grid-template-columns:repeat(2,1fr);gap:10px}
.foot{color:var(--t3);font-size:12px;margin-top:26px}
@media (max-width:860px){.grid{grid-template-columns:repeat(2,1fr)}.two{grid-template-columns:1fr}.orn{display:none}.crow{grid-template-columns:1fr 72px}.crow .cb{grid-column:1/-1}}
@media print{body{background:#fff;color:#111;padding:0}.orn{display:none}.win{backdrop-filter:none;background:#fff;border:0;box-shadow:none}:root{--t1:#111;--t2:#555;--t3:#888;--card:#f3f4f6;--line:#ddd}}
"""

ICONS = {  # 단순 SF Symbols 느낌의 선 아이콘
    "sum": '<path d="M5 12h4l2-6 3 12 2-6h3" />',
    "robot": '<rect x="5" y="8" width="14" height="10" rx="3"/><path d="M12 4v4M9 13h.01M15 13h.01"/>',
    "order": '<rect x="5" y="4" width="14" height="16" rx="3"/><path d="M9 9h6M9 13h6M9 17h3"/>',
    "cmp": '<path d="M7 20V10M12 20V4M17 20v-7"/>',
}


def render(rep: dict) -> str:
    k, o = rep["kpis"], rep["orders"]
    cmp = {r["key"]: r for r in (rep.get("comparison") or [])}

    def chip(key):
        r = cmp.get(key)
        return (r["change_text"], r["better"]) if r else (None, True)

    ct, cg = chip("total_steps")
    lt, lg = chip("lead_avg")
    ut, ug = chip("utilization_pct")
    tt, tg = chip("throughput_per_100")
    tiles = "".join([
        _tile("총 처리 시간", _n(k["total_steps"]), " 스텝", "기준 전략 대비", ct, cg),
        _tile("처리량", _n(k["throughput_per_100"]), " 건/100스텝", "", tt, tg),
        _tile("주문 완료", f'{o["done"]}/{o["total"]}', "", f'{o["completion_pct"]}%'),
        _tile("주문당 평균 시간", _n(o["lead_avg"]), " 스텝", f'P90 {_n(o["lead_p90"])}', lt, lg),
        _tile("로봇 가동률", _n(k["utilization_pct"]), "%", f'적재 작업 {_n(k["productive_pct"])}%', ut, ug),
        _tile("대기 · 유휴", f'{_n(k["wait_pct"])} · {_n(k["idle_pct"])}', "%", "로봇 평균"),
        _tile("총 이동 거리", _n(k["distance"]), " 칸", f'주문당 {_n(k["distance_per_order"])}칸'),
        _tile("충돌", _n(k["collisions"]), "건", f'규칙 위반 {_n(k.get("rule_violations", 0))} · 재계획 {_n(k["replan_avg_ms"])}ms',
              "안전" if k["collisions"] == 0 and not k.get("rule_violations") else "확인",
              k["collisions"] == 0 and not k.get("rule_violations")),
    ])
    ins = "".join(f'<li><span class="dot" style="background:{C[i["level"]]}"></span>{escape(i["text"])}</li>'
                  for i in rep["insights"])
    series = [("최적화", C["opt"], [(p["t"], p["done"]) for p in rep["timeline"]])]
    if rep.get("baseline"):
        series.append(("기준 전략", C["base"], [(p["t"], p["done"]) for p in rep["baseline"]["timeline"]]))
    legend = "".join(f'<span class="lg"><i style="background:{C[c]}"></i>{escape(n)}</span>' for c, n in
                     [("loaded", "적재 이동"), ("empty", "빈 이동"), ("handling", "적재·하역"), ("wait", "대기"), ("idle", "유휴")])
    sc = rep["scenario"]
    bn = " · ".join(f'({b["x"]},{b["y"]}) {b["wait"]}' for b in rep.get("bottlenecks", [])[:5]) or "없음"
    nav = "".join(f'<a href="#{i}" title="{t}"><svg viewBox="0 0 24 24">{ICONS[i]}</svg></a>'
                  for i, t in [("sum", "요약"), ("robot", "로봇 효율"), ("order", "주문"), ("cmp", "비교")])
    return f"""<!doctype html><html lang="ko"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>시뮬레이션 리포트 {escape(rep.get("sim_id") or "")}</title>
<style>
{CSS}</style></head><body><div class="wrap"><nav class="orn">{nav}</nav><main class="win">
<header><div><h1>시뮬레이션 리포트</h1>
<div class="sub">{escape(rep.get("map_summary") or "")}</div></div>
<div class="pills"><span class="pill">로봇 {sc.get("robots")}대</span><span class="pill">입하 {sc.get("inbound")} · 출하 {sc.get("outbound")}</span>
<span class="pill">{escape(str(rep.get("strategy")))} · 시드 {rep.get("seed")}</span><span class="pill">{escape(rep.get("sim_id") or "")}</span></div></header>
<h2 id="sum">요약</h2><div class="grid">{tiles}</div>
<h2>핵심 인사이트</h2><div class="card"><ul class="ins">{ins}</ul></div>
<h2 id="robot">로봇 효율</h2><div class="card">{_robot_rows(rep["robots"])}<div class="lgs">{legend}<span style="margin-left:auto">가동률 · 처리 주문</span></div></div>
<h2 id="order">주문 처리</h2><div class="two"><div class="card"><div class="tl">누적 완료 주문</div>{_line_chart(series)}
<div class="lgs"><span class="lg"><i style="background:{C["opt"]}"></i>최적화</span>{'<span class="lg"><i style="background:' + C["base"] + '"></i>기준 전략</span>' if rep.get("baseline") else ""}</div></div>
<div class="stats">{_tile("평균", _n(o["lead_avg"]), " 스텝")}{_tile("중앙값", _n(o["lead_median"]), " 스텝")}{_tile("P90", _n(o["lead_p90"]), " 스텝")}{_tile("최대", _n(o["lead_max"]), " 스텝")}
{_tile("배정 대기", _n(o["queue_avg"]), " 스텝", "도착 → 배정")}{_tile("수행 시간", _n(o["service_avg"]), " 스텝", "배정 → 완료")}</div></div>
<div class="card" style="margin-top:12px"><div class="tl">주문 처리 시간 분포 (도착 → 완료, 스텝)</div>{_histogram(o["histogram"])}</div>
<h2 id="cmp">기준 전략 비교</h2><div class="card">{_compare_rows(rep.get("comparison")) or '<div class="dim">비교 결과 없음</div>'}
<div class="lgs"><span class="lg"><i style="background:{C["base"]}"></i>기준 전략 (무작위 보관 · 선착순 · 개별 최단 경로)</span><span class="lg"><i style="background:{C["opt"]}"></i>최적화 (점수 보관 · 헝가리안 · 협력 경로 + LNS)</span></div></div>
<div class="foot">병목 칸 (x,y) 대기: {escape(bn)} · 권장 로봇 수 {rep.get("recommended_robots")}대 · {escape(rep.get("time_note") or "")}</div>
</main></div></body></html>"""
