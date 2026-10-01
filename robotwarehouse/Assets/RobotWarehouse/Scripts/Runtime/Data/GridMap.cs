using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobotWarehouse.Data
{
    /// <summary>칸 유형. 요구사항 명세서 DR-01 기준.</summary>
    public enum CellType
    {
        Aisle,
        Rack,
        Wall,
        DockIn,
        DockOut,
        Charge
    }

    public static class CellTypeNames
    {
        public static CellType Parse(string s)
        {
            switch ((s ?? "aisle").Trim().ToLowerInvariant())
            {
                case "rack": return CellType.Rack;
                case "wall": return CellType.Wall;
                case "dock_in": return CellType.DockIn;
                case "dock_out": return CellType.DockOut;
                case "charge": return CellType.Charge;
                default: return CellType.Aisle;
            }
        }

        public static string ToName(CellType t)
        {
            switch (t)
            {
                case CellType.Rack: return "rack";
                case CellType.Wall: return "wall";
                case CellType.DockIn: return "dock_in";
                case CellType.DockOut: return "dock_out";
                case CellType.Charge: return "charge";
                default: return "aisle";
            }
        }
    }

    [Serializable]
    public class MapCell
    {
        [JsonProperty("x")] public int x;
        [JsonProperty("y")] public int y;
        [JsonProperty("type")] public string type;
        [JsonProperty("rack_id", NullValueHandling = NullValueHandling.Ignore)] public string rackId;
    }

    [Serializable]
    public class RackInfo
    {
        [JsonProperty("rack_id")] public string rackId;
        [JsonProperty("levels")] public int levels = 1;
        [JsonProperty("slot_spec")] public string slotSpec;
        [JsonProperty("capacity")] public int capacity;
    }

    [Serializable]
    public class DockInfo
    {
        [JsonProperty("dock_id")] public string dockId;
        [JsonProperty("type")] public string type;
        [JsonProperty("x")] public int x;
        [JsonProperty("y")] public int y;
    }

    /// <summary>
    /// 격자 지도 (DR-01). VR·엔진·Agent가 함께 쓰는 단일 데이터 계약.
    /// cells에 없는 칸은 aisle로 본다.
    /// </summary>
    public class GridMap
    {
        public const string SupportedSchemaVersion = "1.0";

        [JsonProperty("schema_version")] public string schemaVersion = SupportedSchemaVersion;
        [JsonProperty("cell_size_m")] public float cellSizeM = 1f;
        [JsonProperty("width")] public int width;
        [JsonProperty("height")] public int height;
        [JsonProperty("cells")] public List<MapCell> cells = new List<MapCell>();
        [JsonProperty("racks")] public List<RackInfo> racks = new List<RackInfo>();
        [JsonProperty("docks")] public List<DockInfo> docks = new List<DockInfo>();
        [JsonProperty("rules")] public JToken rules;

        [JsonIgnore] private CellType[,] _grid;
        [JsonIgnore] private string[,] _rackIds;

        /// <summary>cells 목록으로 2차원 배열을 만든다. 파싱 뒤와 편집 뒤에 호출.</summary>
        public void BuildIndex()
        {
            if (width <= 0 || height <= 0)
                throw new FormatException($"격자 크기가 잘못되었습니다: width={width}, height={height}");
            _grid = new CellType[width, height];
            _rackIds = new string[width, height];
            if (cells != null)
            {
                foreach (var c in cells)
                {
                    if (!InBounds(c.x, c.y)) continue;
                    _grid[c.x, c.y] = CellTypeNames.Parse(c.type);
                    _rackIds[c.x, c.y] = c.rackId;
                }
            }
            // docks[]에만 적힌 도크도 칸에 반영한다.
            if (docks != null)
            {
                foreach (var d in docks)
                {
                    if (!InBounds(d.x, d.y)) continue;
                    var t = CellTypeNames.Parse(d.type);
                    if (t == CellType.DockIn || t == CellType.DockOut) _grid[d.x, d.y] = t;
                }
            }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        public CellType GetCell(int x, int y)
        {
            if (_grid == null) BuildIndex();
            return InBounds(x, y) ? _grid[x, y] : CellType.Wall;
        }

        public string GetRackId(int x, int y)
        {
            if (_grid == null) BuildIndex();
            return InBounds(x, y) ? _rackIds[x, y] : null;
        }

        public int CountCells(CellType type)
        {
            if (_grid == null) BuildIndex();
            int n = 0;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (_grid[x, y] == type) n++;
            return n;
        }

        /// <summary>랙 한 칸을 옮긴다 (SC-11). 성공하면 cells 목록도 갱신한다.</summary>
        public bool MoveRackCell(int fromX, int fromY, int toX, int toY)
        {
            if (_grid == null) BuildIndex();
            if (GetCell(fromX, fromY) != CellType.Rack) return false;
            if (!RackPlacement.CanPlace(this, toX, toY)) return false;
            string id = _rackIds[fromX, fromY];
            _grid[fromX, fromY] = CellType.Aisle;
            _rackIds[fromX, fromY] = null;
            _grid[toX, toY] = CellType.Rack;
            _rackIds[toX, toY] = id;
            cells.RemoveAll(c => (c.x == fromX && c.y == fromY) || (c.x == toX && c.y == toY));
            cells.Add(new MapCell { x = toX, y = toY, type = "rack", rackId = id });
            return true;
        }

        public static GridMap FromJson(string json) => FromToken(JToken.Parse(json));

        public static GridMap FromToken(JToken token)
        {
            if (token == null || token.Type != JTokenType.Object)
                throw new FormatException("격자 지도 JSON이 객체가 아닙니다.");
            var map = token.ToObject<GridMap>();
            if (map.cells == null) map.cells = new List<MapCell>();
            if (map.racks == null) map.racks = new List<RackInfo>();
            if (map.docks == null) map.docks = new List<DockInfo>();
            if (map.cellSizeM <= 0) map.cellSizeM = 1f;
            map.BuildIndex();
            return map;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);
    }

    /// <summary>랙 배치 가능 여부 (TC-VR-06).</summary>
    public static class RackPlacement
    {
        public static bool CanPlace(GridMap map, int x, int y)
        {
            if (!map.InBounds(x, y)) return false;
            return map.GetCell(x, y) == CellType.Aisle;
        }
    }
}
