using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RobotWarehouse.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace RobotWarehouse.Network
{
    /// <summary>REST 호출 결과.</summary>
    public class ApiResult
    {
        public bool Ok;
        public long Status;
        public string Text;
        public string Error;
        public bool TimedOut;

        public JToken Json
        {
            get
            {
                if (string.IsNullOrEmpty(Text)) return null;
                try { return JToken.Parse(Text); } catch { return null; }
            }
        }

        /// <summary>서버 오류 본문에서 사람이 읽을 메시지를 뽑는다.</summary>
        public string ErrorMessage
        {
            get
            {
                if (TimedOut) return "응답이 늦어요. 다시 시도해 주세요";
                var j = Json as JObject;
                var msg = (string)j?["error"]?["message"] ?? (string)j?["message"] ?? (string)j?["detail"];
                if (!string.IsNullOrEmpty(msg)) return $"[{Status}] {msg}";
                return $"[{Status}] {Error}";
            }
        }
    }

    /// <summary>
    /// VR → 서버 REST 클라이언트 (IR-01). UnityWebRequest 코루틴 기반.
    /// 호출하는 쪽 MonoBehaviour가 StartCoroutine으로 실행한다.
    /// </summary>
    public class ApiClient
    {
        public string BaseUrl { get; set; }
        public int TimeoutSec { get; set; } = AppConfig.RequestTimeoutSec;

        public ApiClient(string baseUrl) { BaseUrl = baseUrl.TrimEnd('/'); }

        public IEnumerator Get(string path, Action<ApiResult> done)
        {
            using (var req = UnityWebRequest.Get(BaseUrl + path))
            {
                req.timeout = TimeoutSec;
                yield return req.SendWebRequest();
                done?.Invoke(ToResult(req));
            }
        }

        public IEnumerator PostJson(string path, object body, Action<ApiResult> done)
        {
            var json = body is string s ? s : JsonConvert.SerializeObject(body);
            using (var req = new UnityWebRequest(BaseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = TimeoutSec;
                Debug.Log($"[API] POST {path} {json}");
                yield return req.SendWebRequest();
                var r = ToResult(req);
                Debug.Log($"[API] POST {path} -> {r.Status} {Truncate(r.Text)}");
                done?.Invoke(r);
            }
        }

        public IEnumerator PostAudio(string path, string sessionId, byte[] wav, Action<ApiResult> done, string questionId = null,
            bool sttOnly = false)
        {
            var form = new WWWForm();
            form.AddField("session_id", sessionId);
            // stt_only: 인식 결과만 받고 창고 생성은 사용자 확인 뒤 /map/text·/map/answer 로 따로 요청 (사용자 UI 설계 10장)
            if (sttOnly) form.AddField("stt_only", "true");
            // 음성 답변(SC-03)일 때는 question_id를 함께 보낸다 (docs/api.md 확정 시 맞출 것)
            if (!string.IsNullOrEmpty(questionId)) form.AddField("question_id", questionId);
            form.AddBinaryData(ApiRoutes.AudioFieldName, wav, "voice.wav", "audio/wav");
            using (var req = UnityWebRequest.Post(BaseUrl + path, form))
            {
                req.timeout = TimeoutSec;
                Debug.Log($"[API] POST {path} (audio {wav.Length} bytes)");
                yield return req.SendWebRequest();
                done?.Invoke(ToResult(req));
            }
        }

        public IEnumerator GetAudioClip(string url, Action<AudioClip> done)
        {
            if (!url.StartsWith("http")) url = BaseUrl + url;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN))
            {
                req.timeout = TimeoutSec;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                    done?.Invoke(DownloadHandlerAudioClip.GetContent(req));
                else
                    done?.Invoke(null);
            }
        }

        static ApiResult ToResult(UnityWebRequest req)
        {
            var text = req.downloadHandler != null ? req.downloadHandler.text : null;
            bool ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
            return new ApiResult
            {
                Ok = ok,
                Status = req.responseCode,
                Text = text,
                Error = req.error,
                TimedOut = !ok && req.error != null && req.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
            };
        }

        static string Truncate(string s) => s == null ? "" : (s.Length > 300 ? s.Substring(0, 300) + "…" : s);
    }
}
