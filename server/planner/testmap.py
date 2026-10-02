"""testmap.py - 엔진 단독 테스트용 창고 지도 생성기 (실제 지도는 방유진의 generate_grid_map이 만든다)."""


def make_warehouse(n_lines=8, aisle=3, line_len=12, n_in=2, n_out=1, n_charge=8, one_way_col=None):
    x_l = 4 + (n_lines - 1) * (aisle + 1)           # 마지막 랙 줄의 x
    W, H = x_l + 5, line_len + 6
    cells, racks = [], []
    docks_in = [(1, 4 + 4 * k) for k in range(n_in)]
    docks_out = [(1, 4 + 4 * n_in + 4 * k) for k in range(n_out)]
    dockset = set(docks_in + docks_out)
    for x in range(W):
        for y in range(H):
            if x in (0, W - 1) or y in (0, H - 1) or (x == 1 and (x, y) not in dockset):
                cells.append(dict(x=x, y=y, type="wall"))
    n = 0
    for k in range(n_lines):
        for y in range(3, 3 + line_len):
            n += 1
            rid = f"R{n:03d}"
            cells.append(dict(x=4 + k * (aisle + 1), y=y, type="rack", rack_id=rid))
            racks.append(dict(rack_id=rid, levels=3, slot_spec="pallet_1100x1100", capacity=6))
    for x, y in docks_in:
        cells.append(dict(x=x, y=y, type="dock_in"))
    for x, y in docks_out:
        cells.append(dict(x=x, y=y, type="dock_out"))
    for k in range(n_charge):
        cells.append(dict(x=W - 2, y=2 + k, type="charge"))
    docks = ([dict(dock_id=f"IN{i+1}", type="dock_in", x=x, y=y) for i, (x, y) in enumerate(docks_in)] +
             [dict(dock_id=f"OUT{i+1}", type="dock_out", x=x, y=y) for i, (x, y) in enumerate(docks_out)])
    rules = {}
    if one_way_col is not None:                     # 해당 열을 y 증가 방향 일방통행으로
        rules["one_way"] = [dict(**{"from": [one_way_col, 1], "to": [one_way_col, H - 2]}, dir="N")]
    return dict(schema_version="1.0", cell_size_m=1.0, width=W, height=H,
                cells=cells, racks=racks, docks=docks, rules=rules)


W1 = dict(n_lines=4, n_in=1, n_out=1, n_charge=4)
W2 = dict(n_lines=8, n_in=2, n_out=1, n_charge=8)
