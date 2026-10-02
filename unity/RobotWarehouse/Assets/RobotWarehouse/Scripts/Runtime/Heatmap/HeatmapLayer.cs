using System.Collections.Generic;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using RobotWarehouse.Warehouse;
using UnityEngine;

namespace RobotWarehouse.Heatmap
{
    /// <summary>
    /// 칸별 통과·대기 히트맵 (FR-24). 칸마다 오브젝트를 만들지 않고
    /// 격자 크기 Texture2D 한 장에 픽셀로 칠해 바닥 위 평면에 입힌다.
    /// </summary>
    public class HeatmapLayer : MonoBehaviour
    {
        public WarehouseRenderer warehouse;
        public HeatmapMetric Metric { get; private set; } = HeatmapMetric.Wait;
        public bool Visible { get; private set; }
        public IReadOnlyList<CellStat> Stats => _stats;

        List<CellStat> _stats = new List<CellStat>();
        Texture2D _tex;

        public void SetStats(List<CellStat> stats)
        {
            _stats = stats ?? new List<CellStat>();
            Repaint();
        }

        public void SetMetric(HeatmapMetric m)
        {
            Metric = m;
            Repaint();
        }

        public void SetVisible(bool v)
        {
            Visible = v;
            if (warehouse != null && warehouse.HeatmapQuad != null)
                warehouse.HeatmapQuad.gameObject.SetActive(v);
            if (v) Repaint();
        }

        public void Repaint()
        {
            if (warehouse == null || warehouse.Map == null || warehouse.HeatmapQuad == null) return;
            var map = warehouse.Map;
            var mat = warehouse.HeatmapQuad.sharedMaterial;
            if (_tex == null || _tex.width != map.width || _tex.height != map.height)
            {
                if (_tex != null) Destroy(_tex);
                _tex = WarehouseRenderer.NewTexture(map.width, map.height, Color.clear);
            }
            var px = new Color[map.width * map.height];
            foreach (var kv in HeatmapMath.Normalize(_stats, Metric))
            {
                if (!map.InBounds(kv.Key.x, kv.Key.y)) continue;
                px[kv.Key.y * map.width + kv.Key.x] = HeatmapMath.ToColor(kv.Value);
            }
            _tex.SetPixels(px);
            _tex.Apply();
            MaterialLibrary.SetTexture(mat, _tex);
            warehouse.HeatmapQuad.gameObject.SetActive(Visible);
        }

        /// <summary>대기 합계 (개선 전·후 비교용).</summary>
        public int TotalWait()
        {
            int sum = 0;
            foreach (var s in _stats) sum += s.wait;
            return sum;
        }
    }
}
