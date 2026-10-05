using System.Collections.Generic;
using RobotWarehouse.Data;
using UnityEngine;

namespace RobotWarehouse.Playback
{
    /// <summary>
    /// 스텝 프레임 버퍼 + 재생 시계 (SC-05). MonoBehaviour와 분리해 EditMode 테스트가 가능하다.
    /// - 시간 단위는 스텝(float). 정수 부분이 현재 프레임, 소수 부분이 다음 프레임까지 보간 비율.
    /// - 다음 프레임이 아직 도착하지 않았으면 그 자리에서 멈추고 IsBuffering = true.
    /// </summary>
    public class FrameTimeline
    {
        readonly Dictionary<int, SimFrame> _frames = new Dictionary<int, SimFrame>();
        readonly HashSet<string> _robotIds = new HashSet<string>();
        readonly List<string> _robotOrder = new List<string>();

        public int TotalSteps { get; set; }            // 마지막 스텝 번호(t 최댓값)
        public float Time { get; private set; }
        public float Speed { get; set; } = 1f;
        public float StepsPerSecond { get; set; } = 2f;
        public bool Playing { get; set; }
        public bool IsBuffering { get; private set; }
        public int LoadedUntil { get; private set; } = -1;   // 0부터 끊김 없이 받은 마지막 t
        public int CurrentStep => Mathf.FloorToInt(Time);
        public bool Finished => TotalSteps > 0 && Time >= TotalSteps;
        public IReadOnlyList<string> RobotIds => _robotOrder;
        public int FrameCount => _frames.Count;
        /// <summary>프레임이 바뀔 때마다 증가 (파생 데이터 캐시 무효화용).</summary>
        public int Version { get; private set; }

        // 로봇별, 스텝별 '파레트를 들고 있는지' (서버 상태에 적재 여부가 없을 때를 위해 기록에서 복원)
        readonly Dictionary<string, HashSet<int>> _carrying = new Dictionary<string, HashSet<int>>();
        int _carryVersion = -1;

        public void Reset(int totalSteps)
        {
            _frames.Clear();
            _robotIds.Clear();
            _robotOrder.Clear();
            TotalSteps = totalSteps;
            Version++;
            Time = 0f;
            LoadedUntil = -1;
            IsBuffering = false;
        }

        public void AddFrames(IEnumerable<SimFrame> frames)
        {
            foreach (var f in frames)
            {
                _frames[f.t] = f;
                if (f.t > TotalSteps) TotalSteps = f.t;
                foreach (var r in f.robots)
                    if (_robotIds.Add(r.id)) _robotOrder.Add(r.id);
            }
            while (_frames.ContainsKey(LoadedUntil + 1)) LoadedUntil++;
            Version++;
        }

        /// <summary>롤링 재계획(SC-08): t 이후 프레임을 버리고 새 결과를 받을 준비.</summary>
        public void DropFrom(int t)
        {
            var remove = new List<int>();
            foreach (var k in _frames.Keys) if (k >= t) remove.Add(k);
            foreach (var k in remove) _frames.Remove(k);
            LoadedUntil = Mathf.Min(LoadedUntil, t - 1);
            Version++;
            if (Time > t) Time = t;
        }

        public bool TryGetFrame(int t, out SimFrame frame) => _frames.TryGetValue(t, out frame);

        /// <summary>재생 시간을 진행한다. 반환값은 이번에 진행한 스텝 수.</summary>
        public float Advance(float deltaSeconds)
        {
            if (!Playing || TotalSteps <= 0) return 0f;
            float before = Time;
            float target = Mathf.Min(Time + deltaSeconds * StepsPerSecond * Speed, TotalSteps);
            // 받아 둔 프레임 범위까지만 진행
            float limit = Mathf.Min(target, Mathf.Max(0, LoadedUntil));
            IsBuffering = limit < target && LoadedUntil < TotalSteps;
            Time = Mathf.Max(Time, limit);
            if (Time >= TotalSteps) Playing = false;
            return Time - before;
        }

        public void Seek(float t)
        {
            Time = Mathf.Clamp(t, 0f, Mathf.Max(0, TotalSteps));
        }

        /// <summary>로봇의 보간 위치(격자 좌표). 현재 프레임에 없으면 false.</summary>
        public bool TryGetRobot(string id, float time, out Vector2 pos, out string state)
        {
            pos = default;
            state = null;
            int t0 = Mathf.FloorToInt(time);
            if (!_frames.TryGetValue(t0, out var f0)) return false;
            var r0 = Find(f0, id);
            if (r0 == null) return false;
            state = r0.state;
            pos = new Vector2(r0.x, r0.y);
            float frac = time - t0;
            if (frac > 0f && _frames.TryGetValue(t0 + 1, out var f1))
            {
                var r1 = Find(f1, id);
                if (r1 != null) pos = Vector2.Lerp(pos, new Vector2(r1.x, r1.y), frac);
            }
            return true;
        }

        public bool TryGetRobot(string id, out Vector2 pos, out string state) => TryGetRobot(id, Time, out pos, out state);

        /// <summary>
        /// step ~ step+1 구간에 로봇이 파레트를 들고 있는지 (상자 표시용).
        /// 서버 상태만으로는 부족해서 기록 전체로 정한다.
        /// - 적재(load)가 끝나면 들고 있고, 하역(unload)이 끝나거나 새 주문을 받거나 빈 이동이 나오면 내려놓은 것.
        /// - 대기(wait), 유휴(idle) 같은 상태는 들고 있는지를 바꾸지 않는다 (멈춰 기다려도, 서버가 idle로 적어도 유지).
        /// - 프레임 t의 상태는 't로 들어온 이동'이라 t -> t+1 이동은 t+1 상태로 판단한다.
        ///   그래서 적재 직후 첫 칸을 떠날 때도 상자가 보인다.
        /// </summary>
        public bool IsCarrying(string id, int step)
        {
            if (_carryVersion != Version) RebuildCarrying();
            return _carrying.TryGetValue(id, out var set) && set.Contains(step);
        }

        void RebuildCarrying()
        {
            _carryVersion = Version;
            _carrying.Clear();
            // 로봇별 (t, 상태 종류, 적재 여부) 순서 목록
            var seq = new Dictionary<string, List<(int t, int kind, bool hold)>>();
            var holding = new Dictionary<string, bool>();
            var lastTask = new Dictionary<string, string>();
            for (int t = 0; t <= TotalSteps; t++)
            {
                if (!_frames.TryGetValue(t, out var f)) continue;
                foreach (var r in f.robots)
                {
                    holding.TryGetValue(r.id, out bool h);
                    lastTask.TryGetValue(r.id, out var prevTask);
                    if (!string.IsNullOrEmpty(r.taskId) && !string.IsNullOrEmpty(prevTask) && r.taskId != prevTask)
                        h = false;                                   // 새 주문 = 이전 짐은 이미 내려놓음
                    if (!string.IsNullOrEmpty(r.taskId)) lastTask[r.id] = r.taskId;

                    int kind = CarryKind(r.state);
                    switch (kind)
                    {
                        case 1: h = true; break;      // 실고 이동
                        case 2: h = false; break;     // 하역 (이 스텝이 끝나면 내려놓음)
                        case 3: h = true; break;      // 적재 (이 스텝이 끝나면 들고 있음)
                        case 5: h = false; break;     // 빈 이동, 충전 = 확실히 빈 상태
                        default: break;               // 대기, 유휴 등: 직전 상태 유지
                    }
                    holding[r.id] = h;
                    if (!seq.TryGetValue(r.id, out var list)) seq[r.id] = list = new List<(int, int, bool)>();
                    list.Add((t, kind, h));       // h = 이 스텝이 끝난 뒤 들고 있는지
                }
            }
            foreach (var kv in seq)
            {
                var list = kv.Value;
                HashSet<int> set = null;
                for (int i = 0; i < list.Count; i++)
                {
                    // t -> t+1 구간은 다음 프레임 상태로 판단한다
                    //   다음이 실고 이동, 하역이면 들고 있음 / 적재, 빈 이동이면 없음 / 대기, 유휴면 지금까지의 적재 여부
                    bool show;
                    int nk = i + 1 < list.Count ? list[i + 1].kind : -1;
                    if (nk == 1 || nk == 2) show = true;
                    else if (nk == 3 || nk == 5) show = false;
                    else show = list[i].hold;
                    if (!show) continue;
                    if (set == null) _carrying[kv.Key] = set = new HashSet<int>();
                    set.Add(list[i].t);
                }
            }
        }

        /// <summary>0: 그 밖(유휴 등, 적재 여부 유지), 1: 실고 이동, 2: 하역, 3: 적재, 4: 대기, 5: 확실히 빈 상태(빈 이동, 충전)</summary>
        public static int CarryKind(string state)
        {
            switch ((state ?? "").ToLowerInvariant())
            {
                case "carry":
                case "carrying":
                case "move_loaded":
                case "loaded": return 1;
                case "unload":
                case "drop": return 2;
                case "load":
                case "pick": return 3;
                case "wait":
                case "waiting":
                case "blocked": return 4;
                case "move_empty":
                case "charge":
                case "charging": return 5;
                default: return 0;
            }
        }

        /// <summary>경로 선(SC-06)용: 받아 둔 프레임에서 로봇이 지나간 칸 목록 (연속 중복 제거).</summary>
        public List<Vector2Int> GetPath(string id)
        {
            var path = new List<Vector2Int>();
            for (int t = 0; t <= TotalSteps; t++)
            {
                if (!_frames.TryGetValue(t, out var f)) continue;
                var r = Find(f, id);
                if (r == null) continue;
                var p = new Vector2Int(r.x, r.y);
                if (path.Count == 0 || path[path.Count - 1] != p) path.Add(p);
            }
            return path;
        }

        static RobotState Find(SimFrame f, string id)
        {
            var list = f.robots;
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == id) return list[i];
            return null;
        }
    }
}
