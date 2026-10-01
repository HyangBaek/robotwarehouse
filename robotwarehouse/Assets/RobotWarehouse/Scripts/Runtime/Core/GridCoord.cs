using UnityEngine;

namespace RobotWarehouse.Core
{
    /// <summary>
    /// 격자 ↔ Unity 좌표 규칙 (docs/schema.md 와 동일하게 유지).
    /// - 격자 (x, y)의 칸 중심 = 창고 루트 로컬 좌표 (x * cell, 0, y * cell)
    /// - 격자 +x = Unity +X(동쪽), 격자 +y = Unity +Z(북쪽)
    /// - 칸 (0,0)의 중심이 로컬 원점. 칸 경계는 중심에서 ±cell/2
    /// </summary>
    public static class GridCoord
    {
        public static Vector3 CellToLocal(float x, float y, float cellSize = 1f, float height = 0f)
            => new Vector3(x * cellSize, height, y * cellSize);

        public static Vector2Int LocalToCell(Vector3 local, float cellSize = 1f)
            => new Vector2Int(Mathf.RoundToInt(local.x / cellSize), Mathf.RoundToInt(local.z / cellSize));

        /// <summary>격자 전체의 로컬 중심 (바닥 평면 배치용).</summary>
        public static Vector3 GridCenterLocal(int width, int height, float cellSize = 1f)
            => new Vector3((width - 1) * 0.5f * cellSize, 0f, (height - 1) * 0.5f * cellSize);
    }
}
