using System.Collections.Generic;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using RobotWarehouse.Warehouse;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWarehouse.Playback
{
    /// <summary>
    /// 관리자, 개발자 모드 전용 3D 표시 (관리자, 디버그 UI 설계 16, 21장).
    /// - 로봇 머리 위 ID / 대기 표시 (항상 사용자를 향함)
    /// - 격자 선
    /// - 칸 강조 (로그, 검증 오류의 Focus in 3D)
    /// 모두 창고 루트 아래에 두어 미니어처, 실물 축척을 그대로 따른다.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        public RobotPlayback playback;
        public WarehouseRenderer warehouse;

        public bool ShowRobotIds { get; private set; }
        public bool ShowWaiting { get; private set; }
        public bool ShowGrid { get; private set; }

        class Label
        {
            public RectTransform root;
            public Text text;
            public Image bg;
        }

        readonly Dictionary<string, Label> _labels = new Dictionary<string, Label>();
        Transform _root;
        GameObject _grid;
        GridMap _gridMap;
        Transform _focus;
        float _focusUntil;
        GridMap _builtFor;

        public void SetRobotIds(bool on) { ShowRobotIds = on; if (!on && !ShowWaiting) HideLabels(); }
        public void SetWaiting(bool on) { ShowWaiting = on; if (!on && !ShowRobotIds) HideLabels(); }
        public void SetGrid(bool on) { ShowGrid = on; if (_grid != null) _grid.SetActive(on); }

        void EnsureRoot()
        {
            if (_root != null && _builtFor == warehouse.Map) return;
            if (_root != null) Destroy(_root.gameObject);
            _labels.Clear();
            _grid = null;
            _focus = null;
            _root = new GameObject("DebugOverlay").transform;
            _root.gameObject.AddComponent<KeepOnRebuild>();
            _root.SetParent(warehouse.transform, false);
            _builtFor = warehouse.Map;
        }

        void LateUpdate()
        {
            if (playback == null || warehouse == null || warehouse.Map == null) return;
            if (!ShowRobotIds && !ShowWaiting && !ShowGrid && _focus == null) return;
            EnsureRoot();
            if (ShowGrid && (_grid == null || _gridMap != warehouse.Map)) BuildGrid();
            if (ShowRobotIds || ShowWaiting) UpdateLabels();
            if (_focus != null)
            {
                float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 6f);
                float cs = warehouse.CellSize;
                _focus.localScale = new Vector3(0.9f * cs * pulse, 0.02f, 0.9f * cs * pulse);
                if (Time.time > _focusUntil) { Destroy(_focus.gameObject); _focus = null; }
            }
        }

        void HideLabels()
        {
            foreach (var kv in _labels) if (kv.Value.root != null) kv.Value.root.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- 로봇 라벨

        void UpdateLabels()
        {
            var tl = playback.Timeline;
            var cam = Camera.main;
            float cs = warehouse.CellSize;
            var seen = new HashSet<string>();
            foreach (var id in tl.RobotIds)
            {
                if (!tl.TryGetRobot(id, out var pos, out var state)) continue;
                seen.Add(id);
                if (!_labels.TryGetValue(id, out var label)) _labels[id] = label = MakeLabel(id);
                bool waiting = IsWait(state);
                string text;
                if (ShowRobotIds && ShowWaiting && waiting) text = $"{id}  WAIT {WaitCount(id, tl.CurrentStep)}";
                else if (ShowRobotIds) text = id;
                else if (waiting) text = $"WAIT {WaitCount(id, tl.CurrentStep)}";
                else text = null;
                label.root.gameObject.SetActive(text != null);
                if (text == null) continue;
                if (label.text.text != text) label.text.text = text;
                var col = waiting ? new Color(0.85f, 0.12f, 0.15f, 0.9f) : new Color(0.05f, 0.07f, 0.1f, 0.85f);
                if (label.bg.color != col) label.bg.color = col;
                label.root.localPosition = GridCoord.CellToLocal(pos.x, pos.y, cs, 1.15f * cs);
                if (cam != null)
                    label.root.rotation = Quaternion.LookRotation(label.root.position - cam.transform.position, Vector3.up);
            }
            foreach (var kv in _labels)
                if (!seen.Contains(kv.Key) && kv.Value.root.gameObject.activeSelf) kv.Value.root.gameObject.SetActive(false);
        }

        Label MakeLabel(string id)
        {
            float cs = warehouse.CellSize;
            var go = new GameObject("Label_" + id, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(220, 56);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.localScale = Vector3.one * (cs / 220f);   // 라벨 폭 = 한 칸
            var bg = go.AddComponent<Image>();
            bg.raycastTarget = false;
            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var t = tgo.AddComponent<Text>();
            t.font = MaterialLibrary.UIFont;
            t.fontSize = 36;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            return new Label { root = rt, text = t, bg = bg };
        }

        static bool IsWait(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "wait":
                case "waiting":
                case "blocked": return true;
                default: return false;
            }
        }

        /// <summary>지금까지 대기한 스텝 수 (0 ~ step).</summary>
        public int WaitCount(string id, int step)
        {
            var tl = playback.Timeline;
            int n = 0;
            for (int t = 0; t <= step; t++)
            {
                if (!tl.TryGetFrame(t, out var f)) continue;
                foreach (var r in f.robots)
                    if (r.id == id) { if (IsWait(r.state)) n++; break; }
            }
            return n;
        }

        // ---------------------------------------------------------------- 격자

        void BuildGrid()
        {
            if (_grid != null) Destroy(_grid);
            var map = warehouse.Map;
            _gridMap = map;
            float cs = warehouse.CellSize, y = 0.015f * cs;
            var verts = new List<Vector3>();
            var idx = new List<int>();
            for (int x = 0; x <= map.width; x++)
            {
                idx.Add(verts.Count); verts.Add(new Vector3((x - 0.5f) * cs, y, -0.5f * cs));
                idx.Add(verts.Count); verts.Add(new Vector3((x - 0.5f) * cs, y, (map.height - 0.5f) * cs));
            }
            for (int z = 0; z <= map.height; z++)
            {
                idx.Add(verts.Count); verts.Add(new Vector3(-0.5f * cs, y, (z - 0.5f) * cs));
                idx.Add(verts.Count); verts.Add(new Vector3((map.width - 0.5f) * cs, y, (z - 0.5f) * cs));
            }
            var mesh = new Mesh { name = "DebugGrid" };
            mesh.SetVertices(verts);
            var colors = new Color[verts.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0.6f, 0.95f, 1f, 0.55f);
            mesh.colors = colors;
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            _grid = new GameObject("Grid");
            _grid.transform.SetParent(_root, false);
            _grid.AddComponent<MeshFilter>().sharedMesh = mesh;
            _grid.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Line;
            _grid.SetActive(ShowGrid);
        }

        // ---------------------------------------------------------------- Focus in 3D

        /// <summary>칸 하나를 몇 초 동안 깜빡이는 노란 판으로 강조 (로그, 오류 -> 3D 위치).</summary>
        public void Focus(Vector2Int cell, float seconds = 6f)
        {
            if (warehouse == null || warehouse.Map == null) return;
            EnsureRoot();
            if (_focus == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Focus";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(_root, false);
                go.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.BlockColored(new Color(1f, 0.85f, 0.1f));
                _focus = go.transform;
            }
            float cs = warehouse.CellSize;
            _focus.localPosition = GridCoord.CellToLocal(cell.x, cell.y, cs, 0.04f * cs);
            _focusUntil = Time.time + seconds;
        }
    }
}
