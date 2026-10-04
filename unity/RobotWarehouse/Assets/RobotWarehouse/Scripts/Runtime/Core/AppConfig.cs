using System;

namespace RobotWarehouse.Core
{
    /// <summary>
    /// 서버 주소, 세션, 재생 상수. 서버 주소는 ServerConfig 에셋(에디터 메뉴 1. 서버 연결)에서 읽는다.
    /// </summary>
    public static class AppConfig
    {
        public const float StepsPerSecondAt1x = 2f;     // 1x 배속일 때 초당 스텝 수
        public const int FrameChunkSize = 200;          // 프레임 요청 단위 (SC-05)
        public const int RequestTimeoutSec = 30;        // EX-04 응답 대기 상한
        public const float ReconnectIntervalSec = 5f;   // EX-02 재연결 간격
        public const int MaxRecordSeconds = 15;         // SC-02 녹음 상한
        public const int RecordSampleRate = 16000;
        public const int MaxQuestionRounds = 3;         // SC-03 반복 제한 (서버와 동일)

        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 8000;

        public static readonly string SessionId = Guid.NewGuid().ToString("N").Substring(0, 12);

        static string _hostOverride;
        static int _portOverride;

        /// <summary>실행 중에 주소를 바꿀 때 (에디터 창의 '지금 연결'). 에셋은 바꾸지 않는다.</summary>
        public static void Override(string host, int port)
        {
            _hostOverride = host?.Trim();
            _portOverride = port;
        }

        public static string Host
        {
            get
            {
                if (!string.IsNullOrEmpty(_hostOverride)) return _hostOverride;
                var c = ServerConfig.Load();
                return c != null && !string.IsNullOrWhiteSpace(c.host) ? c.host.Trim() : DefaultHost;
            }
        }

        public static int Port
        {
            get
            {
                if (_portOverride > 0) return _portOverride;
                var c = ServerConfig.Load();
                return c != null && c.port > 0 ? c.port : DefaultPort;
            }
        }

        public static bool AutoConnect
        {
            get
            {
                var c = ServerConfig.Load();
                return c == null || c.autoConnect;
            }
        }

        public static string HttpBase => $"http://{Host}:{Port}";
        public static string WsBase => $"ws://{Host}:{Port}";
    }
}
