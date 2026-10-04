using System.Collections.Generic;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using RobotWarehouse.Warehouse;
using UnityEngine;

namespace RobotWarehouse.Playback
{
    /// <summary>
    /// 랙 적재 상자 표시. 서버 프레임의 로봇 작업 상태(load/unload)로 "언제 어느 랙에 놓고 가져갔는지"를 복원해
    /// 재생 시간에 맞춰 선반 위 상자를 보이거나 숨긴다.
    /// - 랙 옆 통로에서 unload가 끝나면 -> 옆 랙 칸에 상자 +1 (입하 보관)
    /// - 랙 옆 통로에서 load가 끝나면 -> 옆 랙 칸에서 상자 -1 (출하 피킹)
    /// - 도크에서의 load/unload는 랙 재고와 무관 (로봇 위 상자로만 보임)
    /// 처음 재고는 기록에서 거꾸로 계산한다 (가져가기 전에 있어야 했던 만큼).
    /// 상자는 에셋 대신 일정 규격 정육면체(골판지색)로 그린다.
    /// </summary>
    public class InventoryView : MonoBehaviour
    {
        public RobotPlayback playback;
        public WarehouseRenderer warehouse;

        public static readonly Color BoxColor = new Color(0.78f, 0.58f, 0.34f);
        /// <summary>상자 규격 (칸 대비 비율). 모든 상자 같은 크기.</summary>
        public const float BoxFootprint = 0.62f;

        struct StockEvent
        {
            public int t;
            public Vector2Int cell;
            public int delta;
        }

        readonly List<StockEvent> _events = new List<StockEvent>();
        readonly Dictionary<Vector2Int, int> _initial = new Dictionary<Vector2Int, int>();
        readonly Dictionary<Vector2Int, int> _stock = new Dictionary<Vector2Int, int>();
        readonly Dictionary<Vector2Int, GameObject[]> _slots = new Dictionary<Vector2Int, GameObject[]>();
        Transform _root;
        GridMap _builtMap;
        int _builtFrames = -1;
        int _cursor;          // 다음에 적용할 이벤트 위치
        int _appliedStep = -1;

        public int EventCount => _events.Count;

        /// <summary>현재 재고 (테스트, 디버그용)</summary>
        public int StockAt(Vector2Int cell) => _stock.TryGetValue(cell, out var n) ? n : 0;

        public void Clear()
        {
            _events.Clear();
            _initial.Clear();
            _stock.Clear();
            _slots.Clear();
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _builtFrames = -1;
            _cursor = 0;
            _appliedStep = -1;
        }

        void LateUpdate()
        {
            if (playback == null || warehouse == null || warehouse.Map == null) return;
            var tl = playback.Timeline;
            if (_builtMap != warehouse.Map) { Clear(); _builtMap = warehouse.Map; }
            if (tl.FrameCount == 0) { if (_root != null) Clear(); return; }
            // 프레임이 더 들어왔으면 이벤트를 다시 계산
            if (tl.FrameCount != _builtFrames) Rebuild();
            ApplyStep(tl.CurrentStep);
        }

        void Rebuild()
        {
            var tl = playback.Timeline;
            var map = warehouse.Map;
            _builtFrames = tl.FrameCount;
            _events.Clear();

            // 1) 로봇별로 작업 상태가 끝나는 순간을 찾는다
            var prevState = new Dictionary<string, string>();
            var prevPos = new Dictionary<string, Vector2Int>();
            int last = tl.LoadedUntil;
            for (int t = 0; t <= last; t++)
            {
                if (!tl.TryGetFrame(t, out var f)) continue;
                var seen = new HashSet<string>();
                foreach (var r in f.robots)
                {
                    seen.Add(r.id);
                    var st = Norm(r.state);
                    if (prevState.TryGetValue(r.id, out var ps) && IsWork(ps) && st != ps)
                        AddEvent(t, prevPos[r.id], ps, map);
                    prevState[r.id] = st;
                    prevPos[r.id] = new Vector2Int(r.x, r.y);
                }
            }

            // 2) 처음 재고: 기록을 따라가며 음수가 되지 않을 만큼
            _events.Sort((a, b) => a.t.CompareTo(b.t));
            var running = new Dictionary<Vector2Int, int>();
            var min = new Dictionary<Vector2Int, int>();
            foreach (var e in _events)
            {
                running.TryGetValue(e.cell, out var v);
                v += e.delta;
                running[e.cell] = v;
                min[e.cell] = Mathf.Min(min.TryGetValue(e.cell, out var m) ? m : 0, v);
            }
            _initial.Clear();
            foreach (var kv in min) _initial[kv.Key] = -kv.Value;

            EnsureSlots();
            ResetToInitial();
        }

        static string Norm(string s) => (s ?? "").ToLowerInvariant();
        static bool IsWork(string s) => s == "load" || s == "unload" || s == "pick" || s == "drop";

        void AddEvent(int t, Vector2Int pos, string work, GridMap map)
        {
            var c = map.GetCell(pos.x, pos.y);
            if (c == CellType.DockIn || c == CellType.DockOut) return;   // 도크 작업은 랙 재고와 무관
            var rack = AdjacentRack(pos, map);
            if (!rack.HasValue) return;
            bool put = work == "unload" || work == "drop";
            _events.Add(new StockEvent { t = t, cell = rack.Value, delta = put ? 1 : -1 });
        }

        /// <summary>작업 위치 옆 랙 칸 (여러 개면 좌표 순 첫 번째 - 서버와 같은 규칙일 필요는 없고 일관성만 유지)</summary>
        static Vector2Int? AdjacentRack(Vector2Int p, GridMap map)
        {
            Vector2Int? best = null;
            foreach (var d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up })
            {
                var n = p + d;
                if (map.GetCell(n.x, n.y) != CellType.Rack) continue;
                if (!best.HasValue || n.x < best.Value.x || (n.x == best.Value.x && n.y < best.Value.y)) best = n;
            }
            return best;
        }

        void EnsureSlots()
        {
            if (_root != null) return;
            _root = new GameObject("Inventory").transform;
            _root.gameObject.AddComponent<KeepOnRebuild>();
            _root.SetParent(warehouse.transform, false);
            float cs = warehouse.CellSize;
            float size = cs * BoxFootprint;
            var mat = MaterialLibrary.BlockColored(BoxColor);
            foreach (var kv in warehouse.RackObjects)
            {
                int levels = Mathf.Max(1, warehouse.GetRackLevels(kv.Key));
                var bases = warehouse.SlotBaseHeights(levels);
                // 상자 높이: 단 사이 간격의 65% 이내
                float minGap = float.MaxValue;
                for (int k = 1; k < bases.Length; k++) minGap = Mathf.Min(minGap, bases[k] - bases[k - 1]);
                if (minGap == float.MaxValue) minGap = warehouse.LevelHeight;
                float h = Mathf.Min(size * 0.8f, minGap * 0.65f);
                var arr = new GameObject[bases.Length];
                for (int k = 0; k < bases.Length; k++)
                {
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = $"Box_{kv.Key.x}_{kv.Key.y}_{k}";
                    Destroy(box.GetComponent<Collider>());
                    box.transform.SetParent(_root, false);
                    box.transform.localPosition = GridCoord.CellToLocal(kv.Key.x, kv.Key.y, cs, bases[k] + h * 0.5f);
                    box.transform.localScale = new Vector3(size, h, size);
                    box.GetComponent<Renderer>().sharedMaterial = mat;
                    box.SetActive(false);
                    arr[k] = box;
                }
                _slots[kv.Key] = arr;
            }
        }

        void ResetToInitial()
        {
            _stock.Clear();
            foreach (var kv in _slots) _stock[kv.Key] = _initial.TryGetValue(kv.Key, out var n) ? n : 0;
            foreach (var kv in _slots) Show(kv.Key);
            _cursor = 0;
            _appliedStep = -1;
        }

        void ApplyStep(int step)
        {
            if (step == _appliedStep) return;
            if (step < _appliedStep) ResetToInitial();   // 되감기
            while (_cursor < _events.Count && _events[_cursor].t <= step)
            {
                var e = _events[_cursor++];
                _stock.TryGetValue(e.cell, out var n);
                _stock[e.cell] = Mathf.Max(0, n + e.delta);
                Show(e.cell);
            }
            _appliedStep = step;
        }

        void Show(Vector2Int cell)
        {
            if (!_slots.TryGetValue(cell, out var arr)) return;
            int n = _stock.TryGetValue(cell, out var v) ? v : 0;
            for (int k = 0; k < arr.Length; k++)
            {
                bool on = k < n;
                if (arr[k] != null && arr[k].activeSelf != on) arr[k].SetActive(on);
            }
        }

        void OnDisable() => Clear();
    }
}
