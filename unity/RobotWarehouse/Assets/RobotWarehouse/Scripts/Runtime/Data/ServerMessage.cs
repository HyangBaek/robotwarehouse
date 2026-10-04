using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RobotWarehouse.Data
{
    /// <summary>
    /// WebSocket 메시지 (서버 -> VR). 구현 시나리오 5장:
    /// transcript, map_ready, question, sim_ready, compare, analysis, error (+ tts, status 선택).
    /// 필드가 최상위에 있든 "data" 아래에 있든 같은 방식으로 읽는다.
    /// </summary>
    public class ServerMessage
    {
        public const string Transcript = "transcript";
        public const string MapReady = "map_ready";
        public const string Question = "question";
        public const string SimReady = "sim_ready";
        public const string Compare = "compare";
        public const string Analysis = "analysis";
        public const string Error = "error";
        public const string Status = "status";
        public const string Scenario = "scenario";
        public const string Report = "report";

        public static readonly HashSet<string> KnownTypes = new HashSet<string>
        {
            Transcript, MapReady, Question, SimReady, Compare, Analysis, Error, Status, Scenario, Report
        };

        public string Type { get; private set; }
        public JObject Root { get; private set; }
        public string Raw { get; private set; }

        public static ServerMessage Parse(string json)
        {
            var root = JObject.Parse(json);
            return new ServerMessage
            {
                Raw = json,
                Root = root,
                Type = (string)root["type"] ?? ""
            };
        }

        public JToken Get(string key)
        {
            var v = Root[key];
            if (v != null) return v;
            if (Root["data"] is JObject data) return data[key];
            return null;
        }

        public string GetString(string key, string fallback = "")
        {
            var v = Get(key);
            if (v == null || v.Type == JTokenType.Null) return fallback;
            return v.Type == JTokenType.String ? (string)v : v.ToString(Newtonsoft.Json.Formatting.None);
        }

        public int GetInt(string key, int fallback = 0)
        {
            var v = Get(key);
            if (v == null || v.Type == JTokenType.Null) return fallback;
            try { return v.Value<int>(); } catch { return fallback; }
        }

        public float GetFloat(string key, float fallback = 0f)
        {
            var v = Get(key);
            if (v == null || v.Type == JTokenType.Null) return fallback;
            try { return v.Value<float>(); } catch { return fallback; }
        }

        /// <summary>[[x,y], ...] 또는 [{x,y}, ...] 형식의 좌표 목록.</summary>
        public List<(int x, int y)> GetCells(string key)
        {
            var result = new List<(int, int)>();
            if (!(Get(key) is JArray arr)) return result;
            foreach (var item in arr)
            {
                if (item is JArray pair && pair.Count >= 2)
                    result.Add(((int)pair[0], (int)pair[1]));
                else if (item is JObject o && o["x"] != null && o["y"] != null)
                    result.Add(((int)o["x"], (int)o["y"]));
            }
            return result;
        }

        public List<string> GetStringList(string key)
        {
            var result = new List<string>();
            var v = Get(key);
            if (v is JArray arr)
                foreach (var item in arr) result.Add(SimParsers.Describe(item));
            else if (v != null && v.Type != JTokenType.Null)
                result.Add(SimParsers.Describe(v));
            return result;
        }
    }
}
