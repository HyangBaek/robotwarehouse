using System;
using System.Collections.Generic;
using RobotWarehouse.Core;
using RobotWarehouse.Warehouse;
using UnityEngine;

namespace RobotWarehouse.Playback
{
    /// <summary>
    /// 로봇 재생기 (FR-21, FR-22). 경로 계산 없이 서버 프레임을 보간해 보여 주기만 한다 (NFR-02).
    /// 상태별 색: 이동(파랑) · 적재(주황) · 대기(빨강) · 충전(초록) · 유휴(회색).
    /// </summary>
    public class RobotPlayback : MonoBehaviour
    {
        public WarehouseRenderer warehouse;
        public FrameTimeline Timeline { get; } = new FrameTimeline();

        public event Action<int> OnStepChanged;
        public event Action OnFinished;

        public string SelectedRobot { get; private set; }

        readonly Dictionary<string, Transform> _robots = new Dictionary<string, Transform>();
        Transform _root;
        Transform _selectMarker;
        int _lastStep = -1;
        bool _wasPlaying;

        public static Color StateColor(string state)
        {
            switch ((state ?? "").ToLowerInvariant())
            {
                case "move":
                case "moving":
                case "to_pick":
                case "to_drop": return new Color(0.25f, 0.55f, 1f);
                case "carry":
                case "carrying":
                case "loaded":
                case "load":
                case "unload":
                case "pick":
                case "drop": return new Color(1f, 0.6f, 0.1f);
                case "wait":
                case "waiting":
                case "blocked": return new Color(1f, 0.2f, 0.2f);
                case "charge":
                case "charging": return new Color(0.3f, 0.85f, 0.4f);
                default: return new Color(0.75f, 0.75f, 0.78f);
            }
        }

        public void Begin(int totalSteps)
        {
            ClearRobots();
            Timeline.Reset(totalSteps);
            Timeline.StepsPerSecond = AppConfig.StepsPerSecondAt1x;
            Timeline.Playing = false;
            _lastStep = -1;
        }

        public void ClearRobots()
        {
            foreach (var kv in _robots) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _robots.Clear();
            SelectedRobot = null;
            if (_selectMarker != null) _selectMarker.gameObject.SetActive(false);
        }

        public void Play() { if (Timeline.Finished) Timeline.Seek(0); Timeline.Playing = true; }
        public void Pause() => Timeline.Playing = false;
        public void TogglePlay() { if (Timeline.Playing) Pause(); else Play(); }
        public void SetSpeed(float s) => Timeline.Speed = s;

        public void SelectNextRobot(int dir)
        {
            var ids = Timeline.RobotIds;
            if (ids.Count == 0) { SelectedRobot = null; return; }
            int idx = SelectedRobot == null ? -1 : IndexOf(ids, SelectedRobot);
            idx = ((idx + dir) % ids.Count + ids.Count) % ids.Count;
            SelectedRobot = ids[idx];
        }

        static int IndexOf(IReadOnlyList<string> list, string v)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == v) return i;
            return -1;
        }

        void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("Robots").transform;
            _root.gameObject.AddComponent<KeepOnRebuild>();
            _root.SetParent(warehouse.transform, false);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "SelectedMarker";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(_root, false);
            marker.transform.localScale = new Vector3(0.15f, 0.6f, 0.15f);
            marker.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.BlockColored(Color.white);
            _selectMarker = marker.transform;
            _selectMarker.gameObject.SetActive(false);
        }

        Transform GetOrCreateRobot(string id)
        {
            if (_robots.TryGetValue(id, out var t) && t != null) return t;
            EnsureRoot();
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Robot_" + id;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            float cs = warehouse.CellSize;
            go.transform.localScale = new Vector3(0.7f * cs, 0.4f, 0.7f * cs);
            _robots[id] = go.transform;
            return go.transform;
        }

        void Update()
        {
            if (warehouse == null || warehouse.Map == null) return;
            Timeline.Advance(Time.deltaTime);
            ApplyPositions();

            int step = Timeline.CurrentStep;
            if (step != _lastStep)
            {
                _lastStep = step;
                OnStepChanged?.Invoke(step);
            }
            if (_wasPlaying && !Timeline.Playing && Timeline.Finished) OnFinished?.Invoke();
            _wasPlaying = Timeline.Playing;
        }

        /// <summary>현재 시간의 프레임을 로봇 오브젝트에 반영.</summary>
        public void ApplyPositions()
        {
            if (Timeline.FrameCount == 0) return;
            float cs = warehouse.CellSize;
            foreach (var id in Timeline.RobotIds)
            {
                var tr = GetOrCreateRobot(id);
                if (Timeline.TryGetRobot(id, out var p, out var state))
                {
                    if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
                    tr.localPosition = GridCoord.CellToLocal(p.x, p.y, cs, 0.2f);
                    var rend = tr.GetComponent<Renderer>();
                    var mat = MaterialLibrary.BlockColored(StateColor(state));
                    if (rend.sharedMaterial != mat) rend.sharedMaterial = mat;
                }
                else if (tr.gameObject.activeSelf)
                {
                    tr.gameObject.SetActive(false);
                }
            }

            if (_selectMarker != null)
            {
                bool show = SelectedRobot != null && _robots.TryGetValue(SelectedRobot, out var sel) && sel.gameObject.activeSelf;
                _selectMarker.gameObject.SetActive(show);
                if (show) _selectMarker.localPosition = _robots[SelectedRobot].localPosition + Vector3.up * 0.9f;
            }
        }

        /// <summary>검증용: 로봇의 현재 로컬 위치를 격자 좌표로 (TC-VR-15).</summary>
        public bool TryGetRobotCell(string id, out Vector2 cell)
        {
            cell = default;
            if (!_robots.TryGetValue(id, out var tr) || tr == null) return false;
            var lp = tr.localPosition / warehouse.CellSize;
            cell = new Vector2(lp.x, lp.z);
            return true;
        }
    }
}
