using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// 관리자, 개발자 패널 (사용자 패널에서 뺀 기능 모음). 기본은 숨김.
    /// 왼손 Y 버튼 또는 키보드 F1로 켜고 끈다. 세부 설계(관리자, 디버그 UI 설계서)는 다음 단계에서 반영.
    /// </summary>
    public class DevPanel
    {
        public static readonly (string id, string text)[] ExampleSentences =
        {
            ("W1", "랙 4줄, 통로 폭 3m, 입하 도크 1개, 출하 도크 1개로 만들어줘"),
            ("W2", "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽 벽 쪽에"),
            ("W3", "랙 6줄을 좌우 두 구역으로 나누고 가운데 큰 통로는 북쪽 방향 일방통행"),
            ("W4", "랙 10줄을 통로 없이 붙여서 배치해줘"),
            ("W5", "랙 6줄, 통로 폭 3m"),
            ("W6", "적당한 크기로 창고 하나 만들어줘"),
        };

        public Canvas Canvas;
        public Text ConnectionDetail, StepText, LogText;
        public Button ConnectButton, OfflineButton, ViewModeButton, CompareButton, AddOrdersButton, ChangeRobotsButton;
        public readonly List<Button> ExampleButtons = new List<Button>();

        const int LogLines = 12;
        readonly List<string> _log = new List<string>();

        public bool Visible => Canvas != null && Canvas.gameObject.activeSelf;

        public void Build(Transform parent)
        {
            Canvas = UIFactory.CreateWorldCanvas("DevPanel", new Vector2(760, 1180), parent, 0.001f, 22, 12);
            ((RectTransform)Canvas.transform).pivot = new Vector2(0.5f, 0f);
            var root = Canvas.transform;
            var title = UIFactory.Label(root, "관리자 · 디버그", UIFactory.FontTitle - 4, UIFactory.TextMain);
            title.fontStyle = FontStyle.Bold;
            UIFactory.Label(root, "왼손 Y 또는 F1로 숨기기", UIFactory.FontSmall - 4, UIFactory.TextMuted);

            var conn = UIFactory.Section(root, "서버");
            ConnectionDetail = UIFactory.Label(conn, "연결 안 됨", UIFactory.FontSmall - 2, UIFactory.TextDim);
            var r1 = UIFactory.Row(conn, 72);
            ConnectButton = UIFactory.Button(r1, "다시 연결", null, UIFactory.Secondary, -1, 72, UIFactory.FontSmall);
            OfflineButton = UIFactory.Button(r1, "오프라인 재생", null, UIFactory.Secondary, -1, 72, UIFactory.FontSmall);

            var tools = UIFactory.Section(root, "보기 · 시뮬레이션");
            var r2 = UIFactory.Row(tools, 72);
            ViewModeButton = UIFactory.Button(r2, "보기: 미니어처", null, UIFactory.Secondary, -1, 72, UIFactory.FontSmall);
            CompareButton = UIFactory.Button(r2, "기준 전략 비교", null, UIFactory.Secondary, -1, 72, UIFactory.FontSmall);
            var r3 = UIFactory.Row(tools, 72);
            AddOrdersButton = UIFactory.Button(r3, "주문 +10 (재계획)", null, UIFactory.Secondary, -1, 72, UIFactory.FontSmall);
            ChangeRobotsButton = UIFactory.Button(r3, "로봇 수 적용", null, UIFactory.Secondary, -1, 72, UIFactory.FontSmall);

            var ex = UIFactory.Section(root, "예시 문장 (직접 입력 칸에 채움)");
            var r4 = UIFactory.Row(ex, 64, 8);
            foreach (var (id, _) in ExampleSentences)
                ExampleButtons.Add(UIFactory.Button(r4, id, null, UIFactory.Secondary, 100, 64, UIFactory.FontSmall));

            var dbg = UIFactory.Section(root, "상태");
            StepText = UIFactory.Label(dbg, "", UIFactory.FontSmall - 2, UIFactory.TextDim);
            StepText.gameObject.AddComponent<Canvas>();   // 자주 바뀌는 글자는 하위 캔버스
            LogText = UIFactory.Label(dbg, "", UIFactory.FontSmall - 4, UIFactory.TextMain, TextAnchor.UpperLeft);
            LogText.gameObject.AddComponent<LayoutElement>().minHeight = 300;
            LogText.gameObject.AddComponent<Canvas>();
        }

        public void Log(string line)
        {
            Debug.Log("[RobotWarehouse] " + line);
            _log.Add(line);
            while (_log.Count > LogLines) _log.RemoveAt(0);
            if (LogText != null) LogText.text = string.Join("\n", _log);
        }

        public void SetVisible(bool v)
        {
            if (Canvas != null) Canvas.gameObject.SetActive(v);
        }
    }
}
