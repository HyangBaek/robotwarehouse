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

        /// <summary>로봇 한 대의 표시 요소. 3D 에셋이 있으면 Palletrobot 모델 + 상태 표시등 + 적재 상자.</summary>
        class RobotVisual
        {
            public Transform root;
            public Renderer status;     // 상태 색을 보여 주는 렌더러 (기본 도형이면 몸체 자체)
            public GameObject cargo;    // 적재 중일 때만 보이는 상자
            public Vector2 lastPos;
            public bool hasLast;
            public float baseHeight;    // 기본 도형은 0.2m 띄움, 모델은 바닥
            public bool cargoFollows;   // 기본 도형 로봇: 상자가 자식이 아니라 따로 따라다님 (몸체 스케일 영향 방지)
        }

        readonly Dictionary<string, RobotVisual> _robots = new Dictionary<string, RobotVisual>();
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
                case "move_empty":
                case "to_pick":
                case "to_drop": return new Color(0.25f, 0.55f, 1f);
                case "carry":
                case "carrying":
                case "move_loaded":
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
            foreach (var kv in _robots)
            {
                if (kv.Value?.root != null) Destroy(kv.Value.root.gameObject);
                if (kv.Value != null && kv.Value.cargoFollows && kv.Value.cargo != null) Destroy(kv.Value.cargo);
            }
            _robots.Clear();
            SelectedRobot = null;
            if (_selectMarker != null) _selectMarker.gameObject.SetActive(false);
        }

        public void Play() { if (Timeline.Finished) Timeline.Seek(0); Timeline.Playing = true; }
        public void Pause() => Timeline.Playing = false;
        public void TogglePlay() { if (Timeline.Playing) Pause(); else Play(); }
        public void SetSpeed(float s) => Timeline.Speed = s;

        /// <summary>경로 보기에서 로봇 하나를 고른다 (null이면 선택 해제).</summary>
        public void SelectRobot(string id) => SelectedRobot = id;

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

        static bool IsLoaded(string state)
        {
            switch ((state ?? "").ToLowerInvariant())
            {
                case "carry":
                case "carrying":
                case "move_loaded":
                case "loaded":
                case "unload":
                case "drop": return true;
                default: return false;
            }
        }

        RobotVisual GetOrCreateRobot(string id)
        {
            if (_robots.TryGetValue(id, out var v) && v.root != null) return v;
            EnsureRoot();
            float cs = warehouse.CellSize;
            var skin = warehouse.useSkin ? WarehouseSkin.Active : null;
            v = new RobotVisual();

            if (skin != null && skin.HasRobot)
            {
                var root = new GameObject("Robot_" + id).transform;
                root.SetParent(_root, false);
                float k = cs * skin.robotFootprint / Mathf.Max(skin.robotSize.x, skin.robotSize.z);
                var model = Instantiate(skin.robotPrefab, root);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, skin.robotYawOffset, 0f);
                model.transform.localScale = Vector3.one * k;
                float top = skin.robotSize.y * k;

                var lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lamp.name = "StatusLamp";
                Destroy(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = new Vector3(0f, top + 0.06f * cs, 0f);
                lamp.transform.localScale = new Vector3(0.22f * cs, 0.03f * cs, 0.22f * cs);
                v.status = lamp.GetComponent<Renderer>();

                // 적재 상자: 랙 상자와 같은 규격의 정육면체
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(box.GetComponent<Collider>());
                box.name = "Cargo";
                box.transform.SetParent(root, false);
                float bs = cs * InventoryView.BoxFootprint;
                float bh = bs * 0.8f;
                box.transform.localPosition = new Vector3(0f, top + bh * 0.5f, 0f);
                box.transform.localScale = new Vector3(bs, bh, bs);
                box.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.BlockColored(InventoryView.BoxColor);
                box.SetActive(false);
                v.cargo = box;
                v.root = root;
                v.baseHeight = 0f;
            }
            else
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Robot_" + id;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(_root, false);
                go.transform.localScale = new Vector3(0.7f * cs, 0.4f, 0.7f * cs);
                v.root = go.transform;
                v.status = go.GetComponent<Renderer>();
                v.baseHeight = 0.2f;
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(box.GetComponent<Collider>());
                box.name = "Cargo";
                box.transform.SetParent(_root, false);
                box.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.BlockColored(InventoryView.BoxColor);
                box.SetActive(false);
                v.cargo = box;
                v.cargoFollows = true;
            }
            _robots[id] = v;
            return v;
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
                var v = GetOrCreateRobot(id);
                var tr = v.root;
                if (Timeline.TryGetRobot(id, out var p, out var state))
                {
                    if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
                    tr.localPosition = GridCoord.CellToLocal(p.x, p.y, cs, v.baseHeight);

                    // 진행 방향으로 회전
                    if (v.hasLast)
                    {
                        var d = p - v.lastPos;
                        if (d.sqrMagnitude > 1e-5f)
                        {
                            var target = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up);
                            tr.localRotation = Quaternion.Slerp(tr.localRotation, target, Mathf.Clamp01(Time.deltaTime * 12f));
                        }
                    }
                    v.lastPos = p;
                    v.hasLast = true;

                    var mat = MaterialLibrary.BlockColored(StateColor(state));
                    if (v.status.sharedMaterial != mat) v.status.sharedMaterial = mat;
                    if (v.cargo != null)
                    {
                        bool loaded = IsLoaded(state);
                        if (v.cargo.activeSelf != loaded) v.cargo.SetActive(loaded);
                        if (loaded && v.cargoFollows)
                        {
                            float bs = cs * InventoryView.BoxFootprint;
                            v.cargo.transform.localScale = new Vector3(bs, bs * 0.8f, bs);
                            v.cargo.transform.localPosition = tr.localPosition + Vector3.up * (0.2f + bs * 0.4f);
                        }
                    }
                }
                else if (tr.gameObject.activeSelf)
                {
                    tr.gameObject.SetActive(false);
                    v.hasLast = false;
                }
            }

            if (_selectMarker != null)
            {
                RobotVisual sel = null;
                bool show = SelectedRobot != null && _robots.TryGetValue(SelectedRobot, out sel)
                            && sel.root != null && sel.root.gameObject.activeSelf;
                _selectMarker.gameObject.SetActive(show);
                if (show) _selectMarker.localPosition = sel.root.localPosition + Vector3.up * 1.2f;
            }
        }

        /// <summary>검증용: 로봇의 현재 로컬 위치를 격자 좌표로 (TC-VR-15).</summary>
        public bool TryGetRobotCell(string id, out Vector2 cell)
        {
            cell = default;
            if (!_robots.TryGetValue(id, out var v) || v.root == null) return false;
            var lp = v.root.localPosition / warehouse.CellSize;
            cell = new Vector2(lp.x, lp.z);
            return true;
        }
    }
}
