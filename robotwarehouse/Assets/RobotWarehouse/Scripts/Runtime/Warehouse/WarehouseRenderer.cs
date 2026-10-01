using System.Collections.Generic;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using UnityEngine;

namespace RobotWarehouse.Warehouse
{
    /// <summary>
    /// 격자 지도 → 3D 블록 (FR-20).
    /// 랙은 박스, 벽은 블록, 도크·충전 구역은 바닥 색. 바닥은 격자 크기 텍스처 한 장으로 칠한다.
    /// 블록은 색별 공유 머티리얼을 써서 SRP Batcher·인스턴싱으로 드로우콜을 줄인다.
    /// </summary>
    public class WarehouseRenderer : MonoBehaviour
    {
        public static readonly Color AisleA = new Color(0.30f, 0.31f, 0.33f);
        public static readonly Color AisleB = new Color(0.27f, 0.28f, 0.30f);
        public static readonly Color DockInColor = new Color(0.20f, 0.50f, 0.95f);
        public static readonly Color DockOutColor = new Color(0.98f, 0.55f, 0.15f);
        public static readonly Color ChargeColor = new Color(0.25f, 0.75f, 0.35f);
        public static readonly Color RackColor = new Color(0.62f, 0.45f, 0.28f);
        public static readonly Color WallColor = new Color(0.55f, 0.57f, 0.60f);
        public static readonly Color UnderBlockColor = new Color(0.18f, 0.18f, 0.20f);
        public static readonly Color HighlightColor = new Color(1f, 0.1f, 0.1f, 0.75f);

        public const float RackLevelHeight = 0.5f;
        public const float WallHeight = 1.2f;

        public GridMap Map { get; private set; }
        public float CellSize => Map != null ? Map.cellSizeM : 1f;
        public int RackCount => _rackObjects.Count;
        public int WallCount { get; private set; }
        public IReadOnlyDictionary<Vector2Int, GameObject> RackObjects => _rackObjects;

        /// <summary>히트맵 레이어가 쓰는 바닥 위 투명 평면.</summary>
        public Renderer HeatmapQuad { get; private set; }

        Transform _blocksRoot, _markersRoot;
        GameObject _floor;
        Texture2D _floorTex, _highlightTex;
        Renderer _highlightQuad;
        readonly Dictionary<Vector2Int, GameObject> _rackObjects = new Dictionary<Vector2Int, GameObject>();

        public void Build(GridMap map)
        {
            Clear();
            Map = map;

            _blocksRoot = new GameObject("Blocks").transform;
            _blocksRoot.SetParent(transform, false);
            _markersRoot = new GameObject("Markers").transform;
            _markersRoot.SetParent(transform, false);

            BuildFloor();
            BuildBlocks();
            _highlightTex = NewTexture(map.width, map.height, Color.clear);
            _highlightQuad = CreateFlatQuad("ErrorHighlight", 0.02f, _highlightTex);
            _highlightQuad.gameObject.SetActive(false);
            var heatTex = NewTexture(map.width, map.height, Color.clear);
            HeatmapQuad = CreateFlatQuad("HeatmapQuad", 0.01f, heatTex);
            HeatmapQuad.gameObject.SetActive(false);
        }

        public void Clear()
        {
            _rackObjects.Clear();
            WallCount = 0;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.GetComponent<KeepOnRebuild>() != null) continue;
                DestroySafe(child);
            }
            if (_floorTex != null) DestroySafe(_floorTex);
            if (_highlightTex != null) DestroySafe(_highlightTex);
            Map = null;
        }

        void BuildFloor()
        {
            _floorTex = NewTexture(Map.width, Map.height, AisleA);
            RepaintFloor();
            var r = CreateFlatQuad("Floor", 0f, _floorTex);
            _floor = r.gameObject;
            // 랙 편집(SC-11)·포인터 조준용 충돌체
            var col = _floor.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 1f, 0.01f);
        }

        public void RepaintFloor()
        {
            for (int x = 0; x < Map.width; x++)
                for (int y = 0; y < Map.height; y++)
                    _floorTex.SetPixel(x, y, FloorColor(Map.GetCell(x, y), x, y));
            _floorTex.Apply();
        }

        static Color FloorColor(CellType t, int x, int y)
        {
            switch (t)
            {
                case CellType.DockIn: return DockInColor;
                case CellType.DockOut: return DockOutColor;
                case CellType.Charge: return ChargeColor;
                case CellType.Rack:
                case CellType.Wall: return UnderBlockColor;
                default: return ((x + y) & 1) == 0 ? AisleA : AisleB;
            }
        }

        void BuildBlocks()
        {
            var levelsById = new Dictionary<string, int>();
            foreach (var r in Map.racks)
                if (!string.IsNullOrEmpty(r.rackId)) levelsById[r.rackId] = Mathf.Max(1, r.levels);

            for (int x = 0; x < Map.width; x++)
            {
                for (int y = 0; y < Map.height; y++)
                {
                    var t = Map.GetCell(x, y);
                    if (t == CellType.Rack)
                    {
                        var id = Map.GetRackId(x, y);
                        int levels = id != null && levelsById.TryGetValue(id, out var lv) ? lv : 3;
                        _rackObjects[new Vector2Int(x, y)] = CreateRack(x, y, levels, id);
                    }
                    else if (t == CellType.Wall)
                    {
                        CreateBlock($"Wall_{x}_{y}", x, y, new Vector3(1f, WallHeight, 1f), WallColor, false);
                        WallCount++;
                    }
                }
            }
        }

        GameObject CreateRack(int x, int y, int levels, string rackId)
        {
            float h = levels * RackLevelHeight;
            var go = CreateBlock($"Rack_{rackId}_{x}_{y}", x, y, new Vector3(0.9f, h, 0.9f), RackColor, true);
            var tag = go.AddComponent<RackTag>();
            tag.rackId = rackId;
            tag.cell = new Vector2Int(x, y);
            return go;
        }

        GameObject CreateBlock(string name, int x, int y, Vector3 size, Color color, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!keepCollider) DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(_blocksRoot, false);
            float cs = CellSize;
            go.transform.localPosition = GridCoord.CellToLocal(x, y, cs, size.y * 0.5f);
            go.transform.localScale = new Vector3(size.x * cs, size.y, size.z * cs);
            go.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.BlockColored(color);
            return go;
        }

        Renderer CreateFlatQuad(string name, float height, Texture2D tex)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            float cs = CellSize;
            var center = GridCoord.GridCenterLocal(Map.width, Map.height, cs);
            go.transform.localPosition = center + Vector3.up * height;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(Map.width * cs, Map.height * cs, 1f);
            var rend = go.GetComponent<Renderer>();
            var mat = new Material(MaterialLibrary.Overlay);
            MaterialLibrary.SetTexture(mat, tex);
            MaterialLibrary.SetMaterialColor(mat, Color.white);
            rend.sharedMaterial = mat;
            return rend;
        }

        public static Texture2D NewTexture(int w, int h, Color fill)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = fill;
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ---------- 좌표 ----------

        public Vector3 CellToWorld(float x, float y, float height = 0f)
            => transform.TransformPoint(GridCoord.CellToLocal(x, y, CellSize, height));

        public Vector2Int WorldToCell(Vector3 world)
            => GridCoord.LocalToCell(transform.InverseTransformPoint(world), CellSize);

        // ---------- 검증 오류 칸 강조 (SC-03) ----------

        public void SetHighlight(IEnumerable<(int x, int y)> cells)
        {
            if (Map == null) return;
            var px = new Color[Map.width * Map.height];
            int n = 0;
            foreach (var (x, y) in cells)
            {
                if (!Map.InBounds(x, y)) continue;
                px[y * Map.width + x] = HighlightColor;
                n++;
            }
            _highlightTex.SetPixels(px);
            _highlightTex.Apply();
            _highlightQuad.gameObject.SetActive(n > 0);
            // 랙 위 문제 칸은 블록 색도 바꿔 위에서 잘 보이게 한다.
            foreach (var kv in _rackObjects)
            {
                bool hit = false;
                foreach (var (x, y) in cells) if (kv.Key.x == x && kv.Key.y == y) { hit = true; break; }
                kv.Value.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.BlockColored(hit ? Color.red : RackColor);
            }
        }

        public void ClearHighlight() => SetHighlight(new List<(int, int)>());

        // ---------- 병목 마커 (SC-09) ----------

        public void SetBottleneckMarkers(IList<Bottleneck> bottlenecks)
        {
            ClearMarkers();
            if (bottlenecks == null || bottlenecks.Count == 0 || Map == null) return;
            int max = 1;
            foreach (var b in bottlenecks) max = Mathf.Max(max, b.wait);
            foreach (var b in bottlenecks)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = $"Bottleneck_{b.x}_{b.y}";
                DestroySafe(go.GetComponent<Collider>());
                go.transform.SetParent(_markersRoot, false);
                float h = 0.5f + 2.5f * b.wait / max;
                go.transform.localPosition = GridCoord.CellToLocal(b.x, b.y, CellSize, h * 0.5f);
                go.transform.localScale = new Vector3(0.35f * CellSize, h * 0.5f, 0.35f * CellSize);
                go.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.BlockColored(new Color(1f, 0.15f, 0.25f));
            }
        }

        public void ClearMarkers()
        {
            if (_markersRoot == null) return;
            for (int i = _markersRoot.childCount - 1; i >= 0; i--)
                DestroySafe(_markersRoot.GetChild(i).gameObject);
        }

        // ---------- 랙 편집 (SC-11) ----------

        public bool MoveRack(Vector2Int from, Vector2Int to)
        {
            if (Map == null || !_rackObjects.TryGetValue(from, out var go)) return false;
            if (!Map.MoveRackCell(from.x, from.y, to.x, to.y)) return false;
            _rackObjects.Remove(from);
            _rackObjects[to] = go;
            var p = go.transform.localPosition;
            var target = GridCoord.CellToLocal(to.x, to.y, CellSize, p.y);
            go.transform.localPosition = target;
            go.GetComponent<RackTag>().cell = to;
            _floorTex.SetPixel(from.x, from.y, FloorColor(CellType.Aisle, from.x, from.y));
            _floorTex.SetPixel(to.x, to.y, UnderBlockColor);
            _floorTex.Apply();
            return true;
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
