using RobotWarehouse.Core;
using RobotWarehouse.Warehouse;
using UnityEngine;

namespace RobotWarehouse.Playback
{
    /// <summary>선택한 로봇의 전체 경로 선 (FR-23). 받은 프레임이 늘거나 선택이 바뀌면 다시 그린다.</summary>
    public class PathLineView : MonoBehaviour
    {
        public RobotPlayback playback;
        public bool Visible { get; private set; }

        LineRenderer _line;
        string _drawnRobot;
        int _drawnFrameCount = -1;

        public void SetVisible(bool v)
        {
            Visible = v;
            _drawnFrameCount = -1;
            if (_line != null) _line.enabled = v;
        }

        void EnsureLine()
        {
            if (_line != null) return;
            var go = new GameObject("PathLine");
            go.AddComponent<KeepOnRebuild>();
            go.transform.SetParent(playback.warehouse.transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.sharedMaterial = MaterialLibrary.Line;
            _line.startColor = _line.endColor = new Color(0.2f, 1f, 1f, 0.9f);
            _line.numCornerVertices = 2;
            _line.enabled = Visible;
        }

        void LateUpdate()
        {
            if (!Visible || playback == null || playback.warehouse == null || playback.warehouse.Map == null) return;
            EnsureLine();
            // 선 두께는 월드 단위라 창고 축척(미니어처/실물)에 맞춘다.
            float scale = playback.warehouse.transform.lossyScale.x;
            _line.widthMultiplier = 0.12f * playback.warehouse.CellSize * scale;

            var id = playback.SelectedRobot;
            int count = playback.Timeline.FrameCount;
            if (id == _drawnRobot && count == _drawnFrameCount) return;
            _drawnRobot = id;
            _drawnFrameCount = count;

            if (id == null) { _line.positionCount = 0; return; }
            var path = playback.Timeline.GetPath(id);
            _line.positionCount = path.Count;
            float cs = playback.warehouse.CellSize;
            for (int i = 0; i < path.Count; i++)
                _line.SetPosition(i, GridCoord.CellToLocal(path[i].x, path[i].y, cs, 0.05f));
        }
    }
}
