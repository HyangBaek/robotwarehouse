using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotWarehouse.UI
{
    public enum LogLevel { Debug, Info, Warn, Error }
    public enum LogModule { Agent, Api, Ws, Sim, Vr, Validator }

    /// <summary>관리자 Log Viewer 한 줄 (관리자·디버그 UI 설계 18~21장).</summary>
    public class LogEntry
    {
        public DateTime Time;
        public LogLevel Level;
        public LogModule Module;
        public string Message;      // 서식 없는 문장
        public string Code;         // 오류 코드 (UNREACHABLE 등)
        public Vector2Int? Cell;    // 3D에서 강조할 칸
        public string Raw;          // 원본 JSON (있으면)
    }

    /// <summary>로그 보관 (최근 500줄). 필터·검색·내보내기.</summary>
    public class AdminLog
    {
        public const int Capacity = 500;
        readonly List<LogEntry> _entries = new List<LogEntry>();
        public IReadOnlyList<LogEntry> Entries => _entries;
        public int Version { get; private set; }

        public LogEntry Add(LogLevel level, LogModule module, string message, string code = null, Vector2Int? cell = null, string raw = null)
        {
            var e = new LogEntry
            {
                Time = DateTime.Now, Level = level, Module = module, Message = StripTags(message),
                Code = code, Cell = cell, Raw = raw
            };
            _entries.Add(e);
            if (_entries.Count > Capacity) _entries.RemoveAt(0);
            Version++;
            var line = $"[RW/{module}] {e.Message}";
            if (level == LogLevel.Error) Debug.LogWarning(line); else Debug.Log(line);
            return e;
        }

        public void Clear()
        {
            _entries.Clear();
            Version++;
        }

        /// <summary>최신 줄이 앞. level null = 전체, Debug 수준은 verbose일 때만.</summary>
        public List<LogEntry> Query(LogLevel? level, LogModule? module, string search, bool verbose)
        {
            var list = new List<LogEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (level.HasValue && e.Level != level.Value) continue;
                if (!level.HasValue && !verbose && e.Level == LogLevel.Debug) continue;
                if (module.HasValue && e.Module != module.Value) continue;
                if (!string.IsNullOrEmpty(search) &&
                    e.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (e.Code == null || e.Code.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                list.Add(e);
            }
            return list;
        }

        /// <summary>로그를 텍스트 파일로 저장하고 경로를 돌려준다 (Quest: 앱 저장소).</summary>
        public string Export()
        {
            var sb = new StringBuilder();
            foreach (var e in _entries)
            {
                sb.Append(e.Time.ToString("HH:mm:ss.fff")).Append('\t').Append(e.Level).Append('\t').Append(e.Module).Append('\t');
                if (e.Code != null) sb.Append('[').Append(e.Code).Append("] ");
                if (e.Cell.HasValue) sb.Append($"({e.Cell.Value.x},{e.Cell.Value.y}) ");
                sb.Append(e.Message).Append('\n');
                if (!string.IsNullOrEmpty(e.Raw)) sb.Append("\t\t").Append(e.Raw.Replace('\n', ' ')).Append('\n');
            }
            var path = Path.Combine(Application.persistentDataPath, $"rw_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        public static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            bool tag = false;
            foreach (var ch in s)
            {
                if (ch == '<') { tag = true; continue; }
                if (ch == '>' && tag) { tag = false; continue; }
                if (!tag) sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    public enum NodeState { Waiting, Running, Completed, Error }
    public enum RunState { Idle, Running, Completed, Question, Error }

    /// <summary>Agent 실행 한 단계 (서버 status 메시지 하나). Agent의 생각이 아니라 실제 시스템 이벤트·도구 호출 기준.</summary>
    public class AgentNode
    {
        public string Name;
        public string Message;
        public string Tool;         // status.tool (있으면, 가정)
        public string Raw;
        public DateTime Start;
        public DateTime? End;
        public NodeState State;
        public double Seconds => ((End ?? DateTime.Now) - Start).TotalSeconds;
    }

    /// <summary>요청 하나에 대한 Agent 실행 기록 (입력 → 노드들 → 결과).</summary>
    public class AgentRun
    {
        public int Number;
        public string Kind;         // 창고 생성 / 답변 반영 / 시뮬레이션 / 병목 분석 / 전략 비교 / 음성 인식 / 랙 편집
        public string Input;
        public DateTime Start;
        public DateTime? End;
        public RunState State;
        public string Result;
        public string ResultRaw;
        public readonly List<AgentNode> Nodes = new List<AgentNode>();
        public string Id => $"RUN-{Number:000}";
        public double Seconds => ((End ?? DateTime.Now) - Start).TotalSeconds;
    }

    /// <summary>Agent Debug 화면용 실행 기록 (최근 20건).</summary>
    public class AgentTracker
    {
        readonly List<AgentRun> _runs = new List<AgentRun>();
        int _counter;
        public IReadOnlyList<AgentRun> Runs => _runs;
        public AgentRun Current => _runs.Count > 0 ? _runs[_runs.Count - 1] : null;
        public int Version { get; private set; }

        public AgentRun Begin(string kind, string input)
        {
            var cur = Current;
            if (cur != null && cur.State == RunState.Running) Finish(RunState.Error, "새 요청으로 중단", null);
            var run = new AgentRun { Number = ++_counter, Kind = kind, Input = input, Start = DateTime.Now, State = RunState.Running };
            _runs.Add(run);
            if (_runs.Count > 20) _runs.RemoveAt(0);
            Version++;
            return run;
        }

        /// <summary>status 메시지: 앞 노드를 완료로 닫고 새 노드를 실행 중으로.</summary>
        public void Node(string name, string message, string tool, string raw)
        {
            var run = Current;
            if (run == null || run.State != RunState.Running) run = Begin("서버 이벤트", "-");
            var now = DateTime.Now;
            foreach (var n in run.Nodes)
                if (n.State == NodeState.Running) { n.State = NodeState.Completed; n.End = now; }
            run.Nodes.Add(new AgentNode { Name = name, Message = message, Tool = tool, Raw = raw, Start = now, State = NodeState.Running });
            Version++;
        }

        public void Finish(RunState state, string result, string raw)
        {
            var run = Current;
            if (run == null || run.State != RunState.Running) return;
            var now = DateTime.Now;
            foreach (var n in run.Nodes)
                if (n.State == NodeState.Running)
                {
                    n.State = state == RunState.Error ? NodeState.Error : NodeState.Completed;
                    n.End = now;
                }
            run.State = state;
            run.End = now;
            run.Result = result;
            run.ResultRaw = raw;
            Version++;
        }

        /// <summary>모든 실행의 노드를 시간 순으로 (Tool Call Timeline).</summary>
        public List<(AgentRun run, AgentNode node)> Timeline(int max)
        {
            var list = new List<(AgentRun, AgentNode)>();
            for (int r = _runs.Count - 1; r >= 0 && list.Count < max; r--)
                for (int i = _runs[r].Nodes.Count - 1; i >= 0 && list.Count < max; i--)
                    list.Add((_runs[r], _runs[r].Nodes[i]));
            return list;
        }
    }
}
