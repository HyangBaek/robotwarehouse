using System.IO;
using RobotWarehouse.App;
using RobotWarehouse.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace RobotWarehouse.EditorTools
{
    /// <summary>
    /// RobotWarehouse > 1. 서버 연결
    /// 서버 주소를 입력해 ServerConfig 에셋(Resources)에 저장한다. 앱은 시작할 때 이 주소로 자동 연결한다.
    /// </summary>
    public class ServerConnectWindow : EditorWindow
    {
        const string AssetPath = "Assets/RobotWarehouse/Resources/ServerConfig.asset";

        string _host = AppConfig.DefaultHost;
        int _port = AppConfig.DefaultPort;
        bool _autoConnect = true;
        string _result = "";
        MessageType _resultType = MessageType.None;
        UnityWebRequest _req;
        bool _loaded;

        [MenuItem("RobotWarehouse/1. 서버 연결", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<ServerConnectWindow>(true, "서버 연결");
            w.minSize = new Vector2(440, 300);
            w.LoadFromAsset();
            w.Show();
        }

        public static ServerConfig LoadOrCreate()
        {
            var c = AssetDatabase.LoadAssetAtPath<ServerConfig>(AssetPath);
            if (c != null) return c;
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            c = CreateInstance<ServerConfig>();
            AssetDatabase.CreateAsset(c, AssetPath);
            AssetDatabase.SaveAssets();
            return c;
        }

        void LoadFromAsset()
        {
            var c = AssetDatabase.LoadAssetAtPath<ServerConfig>(AssetPath);
            if (c != null)
            {
                _host = c.host;
                _port = c.port;
                _autoConnect = c.autoConnect;
            }
            _loaded = true;
        }

        void OnGUI()
        {
            if (!_loaded) LoadFromAsset();

            EditorGUILayout.HelpBox(
                "에디터에서 확인: 127.0.0.1\n" +
                "Quest 2: 서버를 띄운 노트북의 Wi-Fi IP (명령 프롬프트 → ipconfig → IPv4 주소)\n" +
                "저장하면 빌드에 포함되고, 앱은 시작할 때 이 주소로 자동 연결합니다.",
                MessageType.Info);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            var hostInput = EditorGUILayout.TextField("서버 주소", _host);
            if (EditorGUI.EndChangeCheck()) ParseHost(hostInput);
            _port = EditorGUILayout.IntField("포트", _port);
            _autoConnect = EditorGUILayout.Toggle("시작 시 자동 연결", _autoConnect);
            EditorGUILayout.LabelField("접속 주소", $"http://{_host}:{_port}   ws://{_host}:{_port}/ws/…", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("저장", GUILayout.Height(28))) Save();
                using (new EditorGUI.DisabledScope(_req != null))
                {
                    if (GUILayout.Button(_req != null ? "확인 중…" : "연결 테스트", GUILayout.Height(28))) Test();
                }
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("지금 연결 (Play 중)", GUILayout.Height(28))) ConnectNow();
                }
            }

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_result, _resultType);
            }
        }

        /// <summary>"http://1.2.3.4:8000/" 같은 입력도 받아 호스트·포트로 나눈다.</summary>
        void ParseHost(string input)
        {
            var s = (input ?? "").Trim();
            foreach (var prefix in new[] { "http://", "https://", "ws://", "wss://" })
                if (s.StartsWith(prefix)) s = s.Substring(prefix.Length);
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            int colon = s.LastIndexOf(':');
            if (colon > 0 && int.TryParse(s.Substring(colon + 1), out var p))
            {
                _port = p;
                s = s.Substring(0, colon);
            }
            _host = s;
        }

        void Save()
        {
            var c = LoadOrCreate();
            c.host = _host;
            c.port = _port;
            c.autoConnect = _autoConnect;
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            _result = $"저장했습니다: {_host}:{_port} ({AssetPath})";
            _resultType = MessageType.Info;
        }

        void Test()
        {
            _req = UnityWebRequest.Get($"http://{_host}:{_port}/health");
            _req.timeout = 4;
            _req.SendWebRequest();
            _result = "확인 중…";
            _resultType = MessageType.None;
            EditorApplication.update += PollTest;
        }

        void PollTest()
        {
            if (_req == null) { EditorApplication.update -= PollTest; return; }
            if (!_req.isDone) return;
            EditorApplication.update -= PollTest;
            if (_req.result == UnityWebRequest.Result.Success)
            {
                _result = $"연결 성공: {_req.downloadHandler.text}";
                _resultType = MessageType.Info;
            }
            else
            {
                _result = $"연결 실패: {_req.error}\n서버 실행 여부, 주소·포트, 방화벽(8000 인바운드)을 확인하세요.";
                _resultType = MessageType.Error;
            }
            _req.Dispose();
            _req = null;
            Repaint();
        }

        void ConnectNow()
        {
            var app = FindAnyObjectByType<AppController>();
            if (app == null)
            {
                _result = "씬에 AppController가 없습니다.";
                _resultType = MessageType.Warning;
                return;
            }
            app.ConnectTo(_host, _port);
            _result = $"Play 중인 앱을 {_host}:{_port}로 연결했습니다. (저장하지 않으면 다음 실행에는 저장된 주소 사용)";
            _resultType = MessageType.Info;
        }

        void OnDisable()
        {
            EditorApplication.update -= PollTest;
            _req?.Dispose();
            _req = null;
        }
    }
}
