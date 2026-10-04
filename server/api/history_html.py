"""시뮬레이션 기록 페이지 (GET /sims.html)와 로봇 동선 페이지 (GET /sim/{id}/routes.html). 리포트와 같은 visionOS 스타일."""
from __future__ import annotations

import json
from html import escape

from .report_html import CSS

# 로봇별 선 색 (visionOS 시스템 색 계열)
PALETTE = ["#0A84FF", "#FF9F0A", "#30D158", "#FF453A", "#BF5AF2", "#64D2FF", "#FFD60A", "#FF375F",
           "#5E5CE6", "#AC8E68", "#66D4CF", "#FF6961", "#A2845E", "#32ADE6", "#FFCC00", "#8E8E93"]
CELL_COLOR = {"wall": "rgba(255,255,255,.30)", "rack": "rgba(255,255,255,.16)", "dock_in": "#0A84FF",
              "dock_out": "#FF9F0A", "charge": "rgba(48,209,88,.55)"}
EXTRA_CSS = """
table.list{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums}
table.list th{color:var(--t3);font-weight:500;font-size:12px;text-align:left;padding:8px 10px}
table.list td{padding:12px 10px;border-top:1px solid rgba(255,255,255,.06)}
table.list td.nw{white-space:nowrap}
table.list tr:hover td{background:rgba(255,255,255,.04)}
a.btn{display:inline-block;padding:6px 14px;border-radius:999px;background:rgba(255,255,255,.12);color:var(--t1);text-decoration:none;font-size:13px;margin-right:6px}
a.btn:hover{background:rgba(255,255,255,.22)}
a.btn.pri{background:#0A84FF}
.map{width:100%;height:auto;display:block;border-radius:22px;background:rgba(0,0,0,.18)}
.robots{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}
.robots label{display:flex;align-items:center;gap:6px;padding:6px 12px;border-radius:999px;background:rgba(255,255,255,.07);cursor:pointer;font-size:13px}
.robots input{accent-color:#0A84FF}
.ctrl{display:flex;gap:12px;align-items:center;margin-top:14px}
.ctrl input[type=range]{flex:1;accent-color:#fff}
button.pill{border:0;border-radius:999px;padding:8px 18px;background:rgba(255,255,255,.14);color:#fff;font:inherit;cursor:pointer}
button.pill:hover{background:rgba(255,255,255,.24)}
"""


def _page(title: str, body: str, script: str = "") -> str:
    return (f'<!doctype html><html lang="ko"><head><meta charset="utf-8">'
            f'<meta name="viewport" content="width=device-width,initial-scale=1"><title>{escape(title)}</title>'
            f'<style>{CSS}{EXTRA_CSS}</style></head><body><div class="wrap"><main class="win">{body}</main></div>'
            f'{script}</body></html>')


def render_list(sims: list[dict]) -> str:
    rows = ""
    for s in sims:
        spec = f" · 1200 규격 {s['spec_b_pct']}%" if s.get("spec_b_pct") else ""
        state = "완료" if s["completed"] else "미완료"
        rows += (f'<tr><td><b>{escape(s["sim_id"])}</b><div class="dim" style="font-size:12px">{escape(s["created_at"] or "")}</div></td>'
                 f'<td>{escape(s.get("map_summary") or "")}</td>'
                 f'<td>로봇 {s["robots"]}대 · 입하 {s["inbound"]} · 출하 {s["outbound"]}{escape(spec)}</td>'
                 f'<td class="nw">{s["total_steps"]} 스텝</td><td class="nw">{s["orders_done"]}/{s["orders_total"]} {state}'
                 f'{" · 재계획 " + str(s["events"]) + "회" if s["events"] else ""}</td>'
                 f'<td style="white-space:nowrap"><a class="btn pri" href="/sim/{escape(s["sim_id"])}/routes.html">동선</a>'
                 f'<a class="btn" href="/sim/{escape(s["sim_id"])}/report.html">리포트</a>'
                 f'<a class="btn" href="/sim/{escape(s["sim_id"])}/routes.csv">CSV</a></td></tr>')
    body = (f'<header><div><h1>시뮬레이션 기록</h1><div class="sub">SQLite에 저장된 시뮬레이션 {len(sims)}건 · 최신순</div></div></header>'
            f'<div class="card"><table class="list"><tr><th>ID · 시각</th><th>지도</th><th>시나리오</th><th>처리 시간</th>'
            f'<th>주문</th><th></th></tr>{rows or "<tr><td colspan=6 class=dim>기록이 없습니다</td></tr>"}</table></div>')
    return _page("시뮬레이션 기록", body)


def render_routes(sim_id: str, grid: dict, log: dict, summary: str) -> str:
    W, H = grid["width"], grid["height"]
    cell = 24
    types = {(c["x"], c["y"]): c["type"] for c in grid["cells"]}
    for d in grid.get("docks", []):
        types[(d["x"], d["y"])] = d["type"]
    spec_b = {r["rack_id"] for r in grid.get("racks", []) if r.get("slot_spec") == "pallet_1200x1000"}
    rack_of = {(c["x"], c["y"]): c.get("rack_id") for c in grid["cells"] if c["type"] == "rack"}
    rects = ""
    for (x, y), t in types.items():
        if t == "aisle":
            continue
        col = CELL_COLOR.get(t, "transparent")
        if t == "rack" and rack_of.get((x, y)) in spec_b:
            col = "rgba(191,90,242,.45)"                       # 1200x1000 규격 랙
        rects += f'<rect x="{x * cell + 1}" y="{(H - 1 - y) * cell + 1}" width="{cell - 2}" height="{cell - 2}" rx="4" fill="{col}"/>'
    ids = [r["id"] for r in log["frames"][0]["robots"]] if log["frames"] else []
    seen = set(ids)
    for f in log["frames"]:                                    # 재계획으로 중간에 추가된 로봇
        for r in f["robots"]:
            if r["id"] not in seen:
                seen.add(r["id"])
                ids.append(r["id"])
    pos = {rid: [] for rid in ids}                             # 스텝별 위치 (없으면 null)
    for f in log["frames"]:
        cur = {r["id"]: (r["x"], r["y"]) for r in f["robots"]}
        for rid in ids:
            pos[rid].append(list(cur[rid]) if rid in cur else None)
    lines, chips = "", ""
    for i, rid in enumerate(ids):
        color = PALETTE[i % len(PALETTE)]
        pts, last = [], None
        for p in pos[rid]:
            if p is not None and p != last:
                pts.append(f"{p[0] * cell + cell / 2:.0f},{(H - 1 - p[1]) * cell + cell / 2:.0f}")
                last = p
        moves = sum(1 for a, b in zip(pos[rid], pos[rid][1:]) if a and b and a != b)
        lines += (f'<polyline class="route" data-r="{escape(rid)}" points="{" ".join(pts)}" fill="none" stroke="{color}" '
                  f'stroke-width="3" stroke-linejoin="round" stroke-linecap="round" opacity=".85"/>')
        chips += (f'<label><input type="checkbox" checked data-r="{escape(rid)}"><span style="width:10px;height:10px;'
                  f'border-radius:5px;background:{color};display:inline-block"></span>{escape(rid)} · {moves}칸</label>')
    dots = "".join(f'<circle class="dot" data-r="{escape(rid)}" r="8" fill="{PALETTE[i % len(PALETTE)]}" '
                   f'stroke="#fff" stroke-width="2"/>' for i, rid in enumerate(ids))
    T = log["total_steps"]
    body = (f'<header><div><h1>로봇 동선 · {escape(sim_id)}</h1><div class="sub">{escape(summary)}</div></div>'
            f'<div class="pills"><a class="btn" href="/sims.html">기록 목록</a><a class="btn" href="/sim/{escape(sim_id)}/report.html">리포트</a>'
            f'<a class="btn" href="/sim/{escape(sim_id)}/routes.csv">CSV</a></div></header>'
            f'<div class="card"><svg class="map" viewBox="0 0 {W * cell} {H * cell}">{rects}{lines}{dots}</svg>'
            f'<div class="ctrl"><button class="pill" id="play">재생</button><input type="range" id="t" min="0" max="{T}" value="{T}">'
            f'<span class="num" id="tl" style="min-width:90px">{T} / {T} 스텝</span></div>'
            f'<div class="robots"><label><input type="checkbox" id="all" checked>전체</label>{chips}</div>'
            f'<div class="lgs"><span class="lg"><i style="background:#0A84FF"></i>입하 도크</span><span class="lg"><i style="background:#FF9F0A"></i>출하 도크</span>'
            f'<span class="lg"><i style="background:rgba(48,209,88,.55)"></i>충전</span><span class="lg"><i style="background:rgba(255,255,255,.16)"></i>랙 1100x1100</span>'
            f'<span class="lg"><i style="background:rgba(191,90,242,.45)"></i>랙 1200x1000</span></div></div>')
    script = f"""<script>
const POS={json.dumps(pos, separators=(",", ":"))},H={H},C={cell};
const t=document.getElementById('t'),tl=document.getElementById('tl'),play=document.getElementById('play');
function show(k){{document.querySelectorAll('.dot').forEach(d=>{{const p=POS[d.dataset.r][k];
 if(!p){{d.style.display='none';return}}d.style.display='';d.setAttribute('cx',p[0]*C+C/2);d.setAttribute('cy',(H-1-p[1])*C+C/2)}});
 tl.textContent=k+' / '+t.max+' 스텝'}}
function vis(r,on){{document.querySelectorAll('[data-r="'+r+'"]').forEach(e=>{{if(e.tagName!=='INPUT')e.style.visibility=on?'visible':'hidden'}})}}
document.querySelectorAll('.robots input[data-r]').forEach(c=>c.onchange=()=>vis(c.dataset.r,c.checked));
document.getElementById('all').onchange=e=>document.querySelectorAll('.robots input[data-r]').forEach(c=>{{c.checked=e.target.checked;vis(c.dataset.r,c.checked)}});
t.oninput=()=>show(+t.value);let timer=null;
play.onclick=()=>{{if(timer){{clearInterval(timer);timer=null;play.textContent='재생';return}}
 if(+t.value>=+t.max)t.value=0;play.textContent='정지';timer=setInterval(()=>{{if(+t.value>=+t.max){{clearInterval(timer);timer=null;play.textContent='재생';return}}t.value=+t.value+1;show(+t.value)}},80)}};
show(+t.value);
</script>"""
    return _page(f"로봇 동선 {sim_id}", body, script)
