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
        /// <summary>기본(가벼운) 선반 1단 높이(m)</summary>
        public const float ProceduralLevelHeight = 0.6f;
        public static readonly Color UprightColor = new Color(0.16f, 0.36f, 0.72f);   // 파란 기둥
        public static readonly Color BeamColor = new Color(0.95f, 0.55f, 0.12f);      // 주황 선반대
        public const float WallHeight = 1.2f;

        public GridMap Map { get; private set; }
        public float CellSize => Map != null ? Map.cellSizeM : 1f;
        public int RackCount => _rackObjects.Count;
        public int WallCount { get; private set; }
        /// <summary>창고 안에서 가장 높은 구조물 높이(m, 창고 로컬). 실물 보기에서 관람 높이 계산용.</summary>
        public float TallestHeight { get; private set; }
        public bool WallsTranslucent { get; private set; }
        readonly List<Renderer> _wallRenderers = new List<Renderer>();
        Material _wallGlass;
        public IReadOnlyDictionary<Vector2Int, GameObject> RackObjects => _rackObjects;

        /// <summary>히트맵 레이어가 쓰는 바닥 위 투명 평면.</summary>
        public Renderer HeatmapQuad { get; private set; }

        /// <summary>false면 3D 에셋 스킨 없이 기본 도형만 쓴다 (테스트·성능 비교용).</summary>
        public bool useSkin = true;
        public bool Skinned => _skin != null;
        /// <summary>선반 1단 높이 (적재 상자 위치 계산에 사용)</summary>
        public float LevelHeight => _skin != null ? _skin.rackLevelHeight : ProceduralLevelHeight;
        public int GetRackLevels(Vector2Int cell) => _rackLevels.TryGetValue(cell, out var lv) ? lv : 0;

        /// <summary>
        /// 랙 한 칸에서 상자를 놓을 단의 바닥 높이들(창고 로컬 m).
        /// 철제 랙 에셋이면 모델의 Beam(선반대) 높이를 메시에서 읽어 맞추고, 기본 선반이면 단 간격으로 계산한다.
        /// </summary>
        public float[] SlotBaseHeights(int levels)
        {
            levels = Mathf.Max(1, levels);
            if (_skin != null)
            {
                float height = levels * _skin.rackLevelHeight;
                var fr = BeamTopFractions(_skin);
                if (fr.Length > 0)
                {
                    var list = new List<float>();
                    if (fr[0] > 0.12f) list.Add(0.03f);              // 바닥 보관 칸
                    foreach (var f in fr) list.Add(f * height + 0.01f);
                    return list.ToArray();
                }
                var even = new float[levels];
                for (int k = 0; k < levels; k++) even[k] = k == 0 ? 0.03f : k * _skin.rackLevelHeight + 0.02f;
                return even;
            }
            var arr = new float[levels];
            for (int k = 0; k < levels; k++) arr[k] = k == 0 ? 0.1f : k * ProceduralLevelHeight + 0.02f;
            return arr;
        }

        static readonly Dictionary<GameObject, float[]> _beamCache = new Dictionary<GameObject, float[]>();

        /// <summary>
        /// 철제 랙 모델에서 선반대(Beam) 윗면 높이를 전체 높이 대비 비율로 구한다.
        /// Beam 메시 꼭짓점의 높이를 모아 0.02×높이 이상 떨어진 묶음으로 나누고, 묶음마다 가장 높은 값을 쓴다.
        /// 메시 Read/Write가 꺼져 있으면 빈 배열 (균등 간격으로 대체).
        /// </summary>
        static float[] BeamTopFractions(WarehouseSkin skin)
        {
            var prefab = skin.shelfPrefab;
            if (prefab == null) return new float[0];
            if (_beamCache.TryGetValue(prefab, out var cached)) return cached;
            var ys = new List<float>();
            var root = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.IndexOf("beam", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                var mat = root * mf.transform.localToWorldMatrix;
                foreach (var v in m.vertices) ys.Add(mat.MultiplyPoint3x4(v).y);
            }
            float total = Mathf.Max(0.01f, skin.shelfSize.y);
            var tops = new List<float>();
            if (ys.Count > 0)
            {
                ys.Sort();
                float gap = total * 0.02f;
                float clusterTop = ys[0];
                for (int i = 1; i < ys.Count; i++)
                {
                    if (ys[i] - ys[i - 1] > gap) { tops.Add(clusterTop); }
                    clusterTop = ys[i];
                }
                tops.Add(clusterTop);
            }
            var result = new List<float>();
            foreach (var t in tops)
            {
                float f = t / total;
                if (f > 0f && f < 0.95f) result.Add(f);   // 맨 위 마감 부재는 제외
            }
            var arr = result.ToArray();
            _beamCache[prefab] = arr;
            if (arr.Length > 0) Debug.Log($"[RobotWarehouse] 철제 랙 선반대 {arr.Length}단 인식 (높이 비율 {string.Join(", ", System.Array.ConvertAll(arr, a => a.ToString("0.00")))})");
            return arr;
        }

        Transform _blocksRoot, _markersRoot, _skinRoot;
        WarehouseSkin _skin;
        readonly Dictionary<Vector2Int, int> _rackLevels = new Dictionary<Vector2Int, int>();
        GameObject _floor;
        Texture2D _floorTex, _highlightTex;
        Renderer _highlightQuad;
        readonly Dictionary<Vector2Int, GameObject> _rackObjects = new Dictionary<Vector2Int, GameObject>();

        public void Build(GridMap map)
        {
            Clear();
            Map = map;
            var skin = useSkin ? WarehouseSkin.Active : null;
            _skin = skin != null && skin.HasShelf && skin.useAssetShelf ? skin : null;

            _blocksRoot = new GameObject("Blocks").transform;
            _blocksRoot.SetParent(transform, false);
            _markersRoot = new GameObject("Markers").transform;
            _markersRoot.SetParent(transform, false);

            BuildFloor();
            BuildBlocks();
            BuildRackVisuals();
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
            _rackLevels.Clear();
            _wallRenderers.Clear();
            _skinRoot = null;
            WallCount = 0;
            TallestHeight = 0f;
            WallsTranslucent = false;
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
                        WallCount++;
                    }
                }
            }
            BuildWallRuns();
        }

        /// <summary>
        /// 벽 칸을 이어진 줄 단위의 긴 박스 하나로 합친다 (칸마다 박스를 두면 반투명일 때 네모 줄무늬가 보임).
        /// 가로 줄을 먼저 묶고, 남은 칸은 세로 줄로 묶는다.
        /// </summary>
        void BuildWallRuns()
        {
            var used = new bool[Map.width, Map.height];
            float cs = CellSize;
            for (int y = 0; y < Map.height; y++)
            {
                int x = 0;
                while (x < Map.width)
                {
                    if (Map.GetCell(x, y) != CellType.Wall) { x++; continue; }
                    int start = x;
                    while (x < Map.width && Map.GetCell(x, y) == CellType.Wall) x++;
                    int len = x - start;
                    if (len < 2) continue;
                    for (int i = start; i < x; i++) used[i, y] = true;
                    AddWallBox((start + x - 1) * 0.5f, y, len * cs, cs);
                }
            }
            for (int x = 0; x < Map.width; x++)
            {
                int y = 0;
                while (y < Map.height)
                {
                    if (Map.GetCell(x, y) != CellType.Wall || used[x, y]) { y++; continue; }
                    int start = y;
                    while (y < Map.height && Map.GetCell(x, y) == CellType.Wall && !used[x, y]) y++;
                    AddWallBox(x, (start + y - 1) * 0.5f, cs, (y - start) * cs);
                }
            }
        }

        void AddWallBox(float cx, float cy, float sizeX, float sizeZ)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Wall";
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(_blocksRoot, false);
            go.transform.localPosition = GridCoord.CellToLocal(cx, cy, CellSize, WallHeight * 0.5f);
            go.transform.localScale = new Vector3(sizeX, WallHeight, sizeZ);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MaterialLibrary.BlockColored(WallColor);
            _wallRenderers.Add(r);
            TallestHeight = Mathf.Max(TallestHeight, WallHeight);
        }

        GameObject CreateRack(int x, int y, int levels, string rackId)
        {
            float h = levels * LevelHeight;
            TallestHeight = Mathf.Max(TallestHeight, h);
            var go = CreateBlock($"Rack_{rackId}_{x}_{y}", x, y, new Vector3(0.9f, h, 0.9f), RackColor, true);
            var tag = go.AddComponent<RackTag>();
            tag.rackId = rackId;
            tag.cell = new Vector2Int(x, y);
            _rackLevels[tag.cell] = levels;
            // 선반 모양은 RackVisuals가 그린다. 이 박스는 충돌체(편집·조준)와 오류 강조용으로만 남긴다
            go.GetComponent<Renderer>().enabled = false;
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

        /// <summary>실물 보기에서 둘레 벽이 시야를 가리지 않도록 반투명 유리처럼 바꾼다.</summary>
        public void SetWallsTranslucent(bool on)
        {
            WallsTranslucent = on;
            if (on && _wallGlass == null)
            {
                _wallGlass = new Material(MaterialLibrary.Overlay) { name = "WallGlass" };
                MaterialLibrary.SetMaterialColor(_wallGlass, new Color(0.65f, 0.8f, 1f, 0.18f));
            }
            var solid = MaterialLibrary.BlockColored(WallColor);
            foreach (var r in _wallRenderers)
                if (r != null) r.sharedMaterial = on ? _wallGlass : solid;
        }

        // ---------- 3D 에셋 랙 (Unity Warehouse → URP 변환 프리팹) ----------

        /// <summary>
        /// 세로(격자 y 방향)로 이어진 랙 칸을 한 줄로 묶고, 선반 모델을 줄 길이에 맞춰 이어 붙인다.
        /// 선반 칸마다 상자를 boxFill 비율로 올린다. 위치는 결정적(지도 크기 기반 시드).
        /// </summary>
        void BuildRackVisuals()
        {
            if (_skinRoot != null) DestroySafe(_skinRoot.gameObject);
            _skinRoot = new GameObject("RackVisuals").transform;
            _skinRoot.SetParent(transform, false);

            var s = _skin;
            float cs = CellSize;
            bool longX = s == null || s.shelfSize.x >= s.shelfSize.z;
            float shelfLen = s == null ? 1f : Mathf.Max(0.01f, longX ? s.shelfSize.x : s.shelfSize.z);
            float shelfDepth = s == null ? 1f : Mathf.Max(0.01f, longX ? s.shelfSize.z : s.shelfSize.x);
            var rng = new System.Random(Map.width * 7919 + Map.height * 104729);

            for (int x = 0; x < Map.width; x++)
            {
                int y = 0;
                while (y < Map.height)
                {
                    if (!_rackObjects.ContainsKey(new Vector2Int(x, y))) { y++; continue; }
                    int start = y;
                    int levels = 1;
                    while (y < Map.height && _rackObjects.ContainsKey(new Vector2Int(x, y)))
                    {
                        levels = Mathf.Max(levels, _rackLevels.TryGetValue(new Vector2Int(x, y), out var lv) ? lv : 3);
                        y++;
                    }
                    if (s != null) PlaceShelfRun(x, start, y - start, levels, s, cs, longX, shelfLen, shelfDepth, rng);
                    else PlaceProceduralRun(x, start, y - start, levels, cs);
                }
            }

            // 실행 중에는 정적 배칭으로 드로우콜을 줄인다 (메시 Read/Write가 켜져 있어야 효과)
            if (Application.isPlaying)
            {
                try { StaticBatchingUtility.Combine(_skinRoot.gameObject); } catch { }
            }
        }

        void PlaceShelfRun(int x, int startY, int length, int levels, WarehouseSkin s, float cs,
            bool longX, float shelfLen, float shelfDepth, System.Random rng)
        {
            float runLen = length * cs;
            float height = levels * s.rackLevelHeight;
            int n = Mathf.Max(1, Mathf.RoundToInt(runLen / shelfLen));
            float seg = runLen / n;
            float edge = (startY - 0.5f) * cs;
            float sy = height / Mathf.Max(0.01f, s.shelfSize.y);
            float sd = cs * 0.95f / shelfDepth;

            for (int i = 0; i < n; i++)
            {
                var go = Instantiate(s.shelfPrefab, _skinRoot);
                go.name = $"Shelf_{x}_{startY}_{i}";
                go.transform.localPosition = new Vector3(x * cs, 0f, edge + (i + 0.5f) * seg);
                if (longX)
                {
                    go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    go.transform.localScale = new Vector3(seg / shelfLen, sy, sd);
                }
                else
                {
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = new Vector3(sd, sy, seg / shelfLen);
                }
            }

        }

        /// <summary>
        /// 가벼운 기본 선반: 파란 기둥 + 단마다 주황 선반대. 정육면체 몇 개라 Quest에서도 가볍고,
        /// 선반이 열려 있어 적재 상자가 놓이고 빠지는 것이 보인다.
        /// </summary>
        void PlaceProceduralRun(int x, int startY, int length, int levels, float cs)
        {
            float lh = ProceduralLevelHeight;
            float height = levels * lh;
            float half = cs * 0.45f;
            float zStart = (startY - 0.5f) * cs;
            float runLen = length * cs;
            var upMat = MaterialLibrary.BlockColored(UprightColor);
            var beamMat = MaterialLibrary.BlockColored(BeamColor);

            // 기둥: 칸 경계마다 앞뒤 2개
            for (int i = 0; i <= length; i++)
            {
                float z = zStart + i * cs;
                for (int side = -1; side <= 1; side += 2)
                    AddPart(new Vector3(x * cs + side * half, height * 0.5f, z), new Vector3(0.06f, height, 0.06f), upMat);
            }
            // 선반대: 바닥 위 1단부터 맨 위까지 (상자가 놓이는 판)
            for (int k = 0; k <= levels; k++)
            {
                float y = k == 0 ? 0.08f : k * lh;
                AddPart(new Vector3(x * cs, y, zStart + runLen * 0.5f), new Vector3(cs * 0.92f, 0.04f, runLen), beamMat);
            }
        }

        void AddPart(Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(_skinRoot, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
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
                var rr = kv.Value.GetComponent<Renderer>();
                rr.sharedMaterial = MaterialLibrary.BlockColored(hit ? Color.red : RackColor);
                rr.enabled = hit;
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
            if (_rackLevels.TryGetValue(from, out var lv)) { _rackLevels.Remove(from); _rackLevels[to] = lv; }
            BuildRackVisuals();
            return true;
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
