namespace RobotWarehouse.Network
{
    /// <summary>
    /// 엔드포인트 경로 모음. 구현 시나리오 5장 기준 설계 예시이며,
    /// docs/api.md 와 다르면 이 파일만 고친다.
    /// </summary>
    public static class ApiRoutes
    {
        public const string Health = "/health";

        public const string MapText = "/map/text";          // SC-01
        public const string MapVoice = "/map/voice";        // SC-02 (multipart: session_id, audio)
        public const string MapAnswer = "/map/answer";      // SC-03
        public const string MapConfirm = "/map/confirm";    // SC-01
        public const string MapEdit = "/map/edit";          // SC-11

        public const string Scenario = "/scenario";         // SC-04
        public const string Analyze = "/analyze";           // SC-09
        public const string Compare = "/compare";           // SC-07 (문서에 경로가 없어 가정한 이름)
        public const string ImproveApprove = "/improve/approve"; // SC-10

        public static string SimFrames(string simId, int from, int to) => $"/sim/{simId}/frames?from={from}&to={to}";
        public static string SimStats(string simId) => $"/sim/{simId}/stats";
        public static string SimEvent(string simId) => $"/sim/{simId}/event";   // SC-08

        /// <summary>WebSocket 경로. 세션별로 이벤트를 받는다.</summary>
        public static string WebSocket(string sessionId) => $"/ws/{sessionId}";

        public const string AudioFieldName = "audio";
    }
}
