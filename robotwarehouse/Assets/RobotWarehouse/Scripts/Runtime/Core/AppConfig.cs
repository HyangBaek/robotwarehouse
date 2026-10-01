using System;
using UnityEngine;

namespace RobotWarehouse.Core
{
    /// <summary>서버 주소·세션 설정. 서버 IP는 PlayerPrefs에 저장해 다음 실행 때 다시 쓴다.</summary>
    public static class AppConfig
    {
        const string KeyHost = "rw.server.host";
        const string KeyPort = "rw.server.port";

        public const float StepsPerSecondAt1x = 2f;     // 1x 배속일 때 초당 스텝 수
        public const int FrameChunkSize = 200;          // 프레임 요청 단위 (SC-05)
        public const int RequestTimeoutSec = 30;        // EX-04 응답 대기 상한
        public const float ReconnectIntervalSec = 5f;   // EX-02 재연결 간격
        public const int MaxRecordSeconds = 15;         // SC-02 녹음 상한
        public const int RecordSampleRate = 16000;
        public const int MaxQuestionRounds = 3;         // SC-03 반복 제한 (서버와 동일)

        public static readonly string SessionId = Guid.NewGuid().ToString("N").Substring(0, 12);

        public static string Host
        {
            get => PlayerPrefs.GetString(KeyHost, "192.168.0.10");
            set { PlayerPrefs.SetString(KeyHost, value?.Trim() ?? ""); PlayerPrefs.Save(); }
        }

        public static int Port
        {
            get => PlayerPrefs.GetInt(KeyPort, 8000);
            set { PlayerPrefs.SetInt(KeyPort, value); PlayerPrefs.Save(); }
        }

        public static string HttpBase => $"http://{Host}:{Port}";
        public static string WsBase => $"ws://{Host}:{Port}";
    }
}
