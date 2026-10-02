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

        public void Reset(int totalSteps)
        {
            _frames.Clear();
            _robotIds.Clear();
            _robotOrder.Clear();
            TotalSteps = totalSteps;
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
        }

        /// <summary>롤링 재계획(SC-08): t 이후 프레임을 버리고 새 결과를 받을 준비.</summary>
        public void DropFrom(int t)
        {
            var remove = new List<int>();
            foreach (var k in _frames.Keys) if (k >= t) remove.Add(k);
            foreach (var k in remove) _frames.Remove(k);
            LoadedUntil = Mathf.Min(LoadedUntil, t - 1);
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
