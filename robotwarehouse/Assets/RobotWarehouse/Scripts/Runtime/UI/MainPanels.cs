using System.Collections.Generic;
using RobotWarehouse.XR;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// VR 패널 두 장 (IR-06). 로직은 AppController가 연결한다.
    /// 왼쪽: 연결 · 창고 설명(텍스트·음성) · Agent 메시지(질문·답변·확인)
    /// 오른쪽: 시나리오 · 재생 컨트롤 · 분석/비교/개선안
    /// </summary>
    public class MainPanels
    {
        public const string WaitSelectRobot = "(로봇 선택)";

        public static readonly (string id, string text)[] ExampleSentences =
        {
            ("W1", "랙 4줄, 통로 폭 3m, 입하 도크 1개, 출하 도크 1개로 만들어줘"),
            ("W2", "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽 벽 쪽에"),
            ("W3", "랙 6줄을 좌우 두 구역으로 나누고 가운데 큰 통로는 북쪽 방향 일방통행"),
            ("W4", "랙 10줄을 통로 없이 붙여서 배치해줘"),
            ("W5", "랙 6줄, 통로 폭 3m"),
            ("W6", "적당한 크기로 창고 하나 만들어줘"),
        };

        public Canvas Left, Right;

        // 연결
        public InputField HostInput, PortInput;
        public Button ConnectButton, OfflineButton, ViewModeButton;
        public Text ConnectionStatus;

        // 창고 설명
        public InputField DescribeInput;
        public Button GenerateButton;
        public HoldButton RecordButton;
        public Text TranscriptText;

        // Agent
        public Text AgentLog;
        public InputField AnswerInput;
        public Button AnswerButton, ConfirmButton, RetryButton, EditRacksButton;
        public HoldButton AnswerRecordButton;
        public GameObject AnswerRow;

        // 시나리오
        public Stepper Robots, Inbound, Outbound;
        public Button RunButton, CompareButton;
        public Text ScenarioStatus;

        // 재생
        public Button PlayButton, HeatmapButton, MetricButton, PathButton, PrevRobotButton, NextRobotButton;
        public Button[] SpeedButtons;
        public float[] Speeds = { 1f, 2f, 4f };
        public Slider StepSlider;
        public Text StepText, SelectedRobotText, Legend;
        public Button AddOrdersButton, ChangeRobotsButton;

        // 분석
        public Button AnalyzeButton;
        public Text AnalysisText;
        public RectTransform ProposalContainer;
        public readonly List<Button> ProposalButtons = new List<Button>();

        const int LogLines = 9;
        readonly List<string> _log = new List<string>();

        public void Build(Transform parent)
        {
            Left = UIFactory.CreateWorldCanvas("Panel_Agent", new Vector2(900, 1450), parent);
            Right = UIFactory.CreateWorldCanvas("Panel_Simulation", new Vector2(900, 1450), parent);
            BuildLeft(Left.transform);
            BuildRight(Right.transform);
            XRSupport.ConfigureCanvas(Left);
            XRSupport.ConfigureCanvas(Right);
        }

        void BuildLeft(Transform root)
        {
            var title = UIFactory.Label(root, "로봇웨어하우스 · Agent", UIFactory.FontTitle + 6, UIFactory.TextMain, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;

            // 연결
            var conn = UIFactory.Section(root, "서버 연결");
            var row = UIFactory.Row(conn);
            HostInput = UIFactory.Input(row, "서버 IP");
            PortInput = UIFactory.Input(row, "포트", 64, false, InputField.ContentType.IntegerNumber);
            PortInput.GetComponent<LayoutElement>().preferredWidth = 140;
            PortInput.GetComponent<LayoutElement>().flexibleWidth = 0;
            ConnectButton = UIFactory.Button(row, "연결", null, null, 150);
            BuildKeypad(conn, HostInput);
            ConnectionStatus = UIFactory.Label(conn, "연결 안 됨", UIFactory.FontSmall, UIFactory.TextDim);
            var row2 = UIFactory.Row(conn);
            OfflineButton = UIFactory.Button(row2, "오프라인 재생", null, UIFactory.ButtonAlt);
            ViewModeButton = UIFactory.Button(row2, "보기: 미니어처", null, UIFactory.ButtonAlt);

            // 창고 설명
            var desc = UIFactory.Section(root, "창고 설명");
            DescribeInput = UIFactory.Input(desc, "예: 랙 8줄, 통로 폭 3m, 입하 도크 2개 …", 110, true);
            var ex = UIFactory.Row(desc, 52, 6);
            foreach (var (id, text) in ExampleSentences)
            {
                var t = text;
                UIFactory.Button(ex, id, () => DescribeInput.text = t, UIFactory.ButtonAlt);
            }
            var row3 = UIFactory.Row(desc, 72);
            GenerateButton = UIFactory.Button(row3, "창고 생성", null);
            var rec = UIFactory.Button(row3, "누르고 말하기 (A)", null, UIFactory.ButtonWarn);
            RecordButton = rec.gameObject.AddComponent<HoldButton>();
            Object.Destroy(rec); // 클릭 버튼 대신 누름·뗌 버튼으로 사용
            TranscriptText = UIFactory.Label(desc, "", UIFactory.FontSmall, UIFactory.TextDim);

            // Agent
            var agent = UIFactory.Section(root, "Agent");
            AgentLog = UIFactory.Label(agent, "", UIFactory.FontSmall, UIFactory.TextMain, TextAnchor.UpperLeft);
            AgentLog.gameObject.AddComponent<LayoutElement>().minHeight = 250;
            AnswerRow = UIFactory.Row(agent, 64).gameObject;
            AnswerInput = UIFactory.Input(AnswerRow.transform, "답변 입력 (예: 통로 3m로 해줘)");
            AnswerButton = UIFactory.Button(AnswerRow.transform, "답변", null, null, 120);
            var arec = UIFactory.Button(AnswerRow.transform, "말하기", null, UIFactory.ButtonWarn, 120);
            AnswerRecordButton = arec.gameObject.AddComponent<HoldButton>();
            Object.Destroy(arec);
            AnswerRow.SetActive(false);
            var row4 = UIFactory.Row(agent, 72);
            ConfirmButton = UIFactory.Button(row4, "지도 확인", null, new Color(0.2f, 0.65f, 0.35f));
            EditRacksButton = UIFactory.Button(row4, "랙 편집: 끔", null, UIFactory.ButtonAlt);
            RetryButton = UIFactory.Button(row4, "다시 시도", null, UIFactory.ButtonWarn);
            RetryButton.gameObject.SetActive(false);
        }

        void BuildKeypad(Transform parent, InputField target)
        {
            // Quest에서 IP를 넣기 위한 숫자 패드 (시스템 키보드가 안 뜰 때 대비)
            var keys = UIFactory.Row(parent, 52, 4);
            foreach (var k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", ".", "←" })
            {
                var key = k;
                UIFactory.Button(keys, key, () =>
                {
                    if (key == "←") { if (target.text.Length > 0) target.text = target.text.Substring(0, target.text.Length - 1); }
                    else target.text += key;
                }, UIFactory.ButtonAlt);
            }
        }

        void BuildRight(Transform root)
        {
            var title = UIFactory.Label(root, "시뮬레이션", UIFactory.FontTitle + 6, UIFactory.TextMain, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;

            var sc = UIFactory.Section(root, "시나리오");
            Robots = UIFactory.Stepper(sc, "로봇 수", 1, 16, 1, 4);
            Inbound = UIFactory.Stepper(sc, "입하 주문", 0, 200, 5, 25);
            Outbound = UIFactory.Stepper(sc, "출하 주문", 0, 200, 5, 25);
            var row = UIFactory.Row(sc, 72);
            RunButton = UIFactory.Button(row, "실행", null);
            CompareButton = UIFactory.Button(row, "기준 전략과 비교", null, UIFactory.ButtonAlt);
            ScenarioStatus = UIFactory.Label(sc, "지도 확인 후 실행할 수 있어요", UIFactory.FontSmall, UIFactory.TextDim);

            var pb = UIFactory.Section(root, "재생");
            var r1 = UIFactory.Row(pb);
            PlayButton = UIFactory.Button(r1, "재생", null, null, 200);
            SpeedButtons = new Button[Speeds.Length];
            for (int i = 0; i < Speeds.Length; i++)
                SpeedButtons[i] = UIFactory.Button(r1, $"{Speeds[i]:0}x", null, UIFactory.ButtonAlt);
            StepSlider = UIFactory.Slider(pb);
            StepText = UIFactory.Label(pb, "스텝 0 / 0", UIFactory.FontSmall, UIFactory.TextDim);
            var r2 = UIFactory.Row(pb);
            HeatmapButton = UIFactory.Button(r2, "히트맵: 끔", null, UIFactory.ButtonAlt);
            MetricButton = UIFactory.Button(r2, "지표: 대기", null, UIFactory.ButtonAlt);
            var r3 = UIFactory.Row(pb);
            PathButton = UIFactory.Button(r3, "경로 선: 끔", null, UIFactory.ButtonAlt);
            PrevRobotButton = UIFactory.Button(r3, "<", null, UIFactory.ButtonAlt, 80);
            SelectedRobotText = UIFactory.Label(r3, WaitSelectRobot, UIFactory.FontSmall, UIFactory.TextMain, TextAnchor.MiddleCenter);
            NextRobotButton = UIFactory.Button(r3, ">", null, UIFactory.ButtonAlt, 80);
            var r4 = UIFactory.Row(pb);
            AddOrdersButton = UIFactory.Button(r4, "주문 +10", null, UIFactory.ButtonAlt);
            ChangeRobotsButton = UIFactory.Button(r4, "로봇 수 적용", null, UIFactory.ButtonAlt);
            Legend = UIFactory.Label(pb,
                "<color=#4D8CFF>■</color> 이동  <color=#FF9A1A>■</color> 적재  <color=#FF3333>■</color> 대기  " +
                "<color=#4DD966>■</color> 충전  <color=#BFBFC7>■</color> 유휴",
                UIFactory.FontSmall, UIFactory.TextDim);

            var an = UIFactory.Section(root, "분석 · 개선");
            AnalyzeButton = UIFactory.Button(an, "병목 분석 (어디가 막혀?)", null);
            AnalysisText = UIFactory.Label(an, "", UIFactory.FontSmall, UIFactory.TextMain, TextAnchor.UpperLeft);
            var pc = new GameObject("Proposals", typeof(RectTransform));
            pc.transform.SetParent(an, false);
            var vl = pc.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 8;
            vl.childControlHeight = true;
            vl.childControlWidth = true;
            vl.childForceExpandHeight = false;
            ProposalContainer = (RectTransform)pc.transform;
        }

        public void Log(string line)
        {
            _log.Add(line);
            while (_log.Count > LogLines) _log.RemoveAt(0);
            AgentLog.text = string.Join("\n", _log);
        }

        public void ClearProposals()
        {
            foreach (var b in ProposalButtons) if (b != null) Object.Destroy(b.gameObject);
            ProposalButtons.Clear();
        }

        public void SetToggleText(Button b, string name, bool on) => UIFactory.SetButtonText(b, $"{name}: {(on ? "켬" : "끔")}");

        public void HighlightSpeed(float speed)
        {
            for (int i = 0; i < SpeedButtons.Length; i++)
                UIFactory.SetButtonColor(SpeedButtons[i], Mathf.Approximately(Speeds[i], speed) ? UIFactory.ButtonBg : UIFactory.ButtonAlt);
        }
    }
}
