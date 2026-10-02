using System.Collections.Generic;
using System.Linq;
using RobotWarehouse.Data;
using UnityEngine;

namespace RobotWarehouse.Heatmap
{
    public enum HeatmapMetric { Wait, Pass }

    /// <summary>히트맵 계산 (FR-24, TC-VR-05). 값 0~1 정규화와 색 변환.</summary>
    public static class HeatmapMath
    {
        public static int Value(CellStat s, HeatmapMetric m) => m == HeatmapMetric.Wait ? s.wait : s.pass;

        /// <summary>최댓값으로 나눠 0~1. 최댓값이 0이면 모두 0.</summary>
        public static Dictionary<Vector2Int, float> Normalize(IEnumerable<CellStat> stats, HeatmapMetric metric)
        {
            var list = stats?.ToList() ?? new List<CellStat>();
            int max = 0;
            foreach (var s in list) max = Mathf.Max(max, Value(s, metric));
            var result = new Dictionary<Vector2Int, float>();
            foreach (var s in list)
                result[new Vector2Int(s.x, s.y)] = max > 0 ? Mathf.Clamp01((float)Value(s, metric) / max) : 0f;
            return result;
        }

        /// <summary>0 = 투명, 낮음 = 노랑, 높음 = 진한 빨강. 값이 클수록 진하다.</summary>
        public static Color ToColor(float v)
        {
            if (v <= 0f) return Color.clear;
            var low = new Color(1f, 0.95f, 0.2f, 0.35f);
            var mid = new Color(1f, 0.5f, 0.05f, 0.65f);
            var high = new Color(0.75f, 0f, 0.05f, 0.9f);
            return v < 0.5f ? Color.Lerp(low, mid, v * 2f) : Color.Lerp(mid, high, (v - 0.5f) * 2f);
        }

        /// <summary>값 상위 N칸 (SC-06 수용 체크: DB 집계 상위 5칸과 비교).</summary>
        public static List<CellStat> Top(IEnumerable<CellStat> stats, HeatmapMetric metric, int n)
            => stats.OrderByDescending(s => Value(s, metric)).ThenBy(s => s.x).ThenBy(s => s.y).Take(n).ToList();
    }
}
