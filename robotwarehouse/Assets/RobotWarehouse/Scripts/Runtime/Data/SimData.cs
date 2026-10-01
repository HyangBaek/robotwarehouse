using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobotWarehouse.Data
{
    /// <summary>스텝별 로봇 위치 (DR-03): {t, robots: [{id, x, y, state, task_id}]}</summary>
    [Serializable]
    public class RobotState
    {
        [JsonProperty("id")] public string id;
        [JsonProperty("x")] public int x;
        [JsonProperty("y")] public int y;
        [JsonProperty("state")] public string state;
        [JsonProperty("task_id")] public string taskId;
    }

    [Serializable]
    public class SimFrame
    {
        [JsonProperty("t")] public int t;
        [JsonProperty("robots")] public List<RobotState> robots = new List<RobotState>();
    }

    /// <summary>칸별 통과·대기 (DR-03): {x, y, pass, wait}</summary>
    [Serializable]
    public class CellStat
    {
        [JsonProperty("x")] public int x;
        [JsonProperty("y")] public int y;
        [JsonProperty("pass")] public int pass;
        [JsonProperty("wait")] public int wait;
    }

    [Serializable]
    public class Bottleneck
    {
        [JsonProperty("x")] public int x;
        [JsonProperty("y")] public int y;
        [JsonProperty("wait")] public int wait;
    }

    [Serializable]
    public class Proposal
    {
        [JsonProperty("proposal_id")] public string proposalId;
        [JsonProperty("type")] public string type;
        [JsonProperty("text")] public string text;
    }

    /// <summary>
    /// 서버 응답 파싱 도우미. docs/api.md 확정 전이라 배열 그대로 / {frames:[...]} / {data:[...]} 모두 받는다.
    /// </summary>
    public static class SimParsers
    {
        public static List<SimFrame> ParseFrames(string json)
        {
            var token = JToken.Parse(json);
            var arr = UnwrapArray(token, "frames");
            return arr == null ? new List<SimFrame>() : arr.ToObject<List<SimFrame>>();
        }

        public static List<CellStat> ParseStats(string json)
        {
            var token = JToken.Parse(json);
            var arr = UnwrapArray(token, "cells") ?? UnwrapArray(token, "stats");
            return arr == null ? new List<CellStat>() : arr.ToObject<List<CellStat>>();
        }

        public static JArray UnwrapArray(JToken token, string key)
        {
            if (token is JArray a) return a;
            if (token is JObject o)
            {
                if (o[key] is JArray k) return k;
                if (o["data"] is JArray d) return d;
                if (o["data"] is JObject dobj && dobj[key] is JArray dk) return dk;
            }
            return null;
        }

        /// <summary>summary처럼 문자열일 수도 객체일 수도 있는 값을 사람이 읽을 문장으로 바꾼다.</summary>
        public static string Describe(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            if (token.Type == JTokenType.String) return token.ToString();
            if (token is JObject o)
            {
                var parts = new List<string>();
                foreach (var p in o.Properties())
                    parts.Add($"{p.Name}: {Describe(p.Value)}");
                return string.Join(", ", parts);
            }
            if (token is JArray arr)
            {
                var parts = new List<string>();
                foreach (var item in arr) parts.Add(Describe(item));
                return string.Join(", ", parts);
            }
            return token.ToString(Formatting.None);
        }
    }
}
