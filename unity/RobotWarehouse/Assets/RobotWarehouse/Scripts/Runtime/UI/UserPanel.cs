using System;
using System.Collections.Generic;
using RobotWarehouse.Heatmap;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>사용자 패널 화면 (ui_재료 사용자 UI 개선 설계 S01~S08).</summary>
    public enum UserScreen
    {
        Create,         // S01 창고 만들기
        ManualInput,    // 직접 입력 (창고 설명 / 질문 답변)
        Recording,      // 음성 입력 중
        SttResult,      // 인식 결과 확인
        Processing,     // Agent 처리 (창고 생성, 검증 / 시뮬레이션 / 분석 진행 상태)
        Confirm,        // S02 창고 확인
        Question,       // S02-Error 검증 실패 -> Agent 질문
        SimConfig,      // S03 시뮬레이션 설정
        Playback,       // S05 결과 재생
        PathSelect,     // 경로 보기 (로봇 선택)
        Heatmap,        // 히트맵 보기
        Analysis,       // S06 병목 분석
        Approval,       // S07 개선안 승인
        Comparison      // S08 개선 전, 후 비교
    }

    public enum ConnectionView { Connected, Connecting, Reconnecting, Disconnected, Offline }
    public enum ToastKind { Info, Success, Warning, Error }
    public enum ProcState { Pending, Active, Done, Failed }

    /// <summary>
    /// 사용자 작업 패널 한 장 (World Space, 1.2m x 0.73m, 화면에 따라 높이만 줄어듦).
    /// 공통 구조: Header(단계 번호, 제목, 진행 단계, 연결 상태) / Content / Footer(Primary Action).
    /// 서버 주소, 세션, 디버그 정보는 넣지 않는다 (관리자 패널 DevPanel).
    /// 로직은 AppController가 연결한다.
    /// </summary>
    public class UserPanel
    {
        public const float Width = 1200f;
        public const float FullHeight = 730f;
        public static readonly string[] StepNames = { "창고 만들기", "창고 확인", "시뮬레이션", "결과 분석" };

        public Canvas Canvas;
        public UserScreen Current { get; private set; }
        public event Action<UserScreen> OnScreenChanged;

        // Header
        Image _stepBadge;
        Text _stepBadgeText, _title;
        readonly Image[] _progress = new Image[4];
        readonly Text[] _progressText = new Text[4];
        readonly Image[] _progressLine = new Image[3];
        Image _connDot;
        Text _connText;

        RectTransform _body;
        readonly Dictionary<UserScreen, GameObject> _screens = new Dictionary<UserScreen, GameObject>();
        readonly Dictionary<UserScreen, float> _heights = new Dictionary<UserScreen, float>();

        // S01 창고 만들기
        public Button SpeakButton, ManualButton, HistoryButton;
        public Button AdminButton;   // 머리글 오른쪽 '≡' (관리자 모드 진입, 평소 숨김)
        public Text CreateStatus, RequiredInfo;

        /// <summary>디지털 트윈에 필요한 지도 정보 (서버 tools/map_generator.REQUIRED_ITEMS 와 같은 순서). 빠지면 Agent 가 되묻는다.</summary>
        public const string RequiredInfoGuide =
            "필요한 정보: 랙 줄 수, 통로 폭, 입하·출하 도크 수, 랙 단 수, 충전 구역, 구역 분할, 일방통행(두 구역일 때)\n" +
            "빠진 항목은 Agent 가 다시 묻고, \"나머지는 기본값\"이라고 하면 기본값으로 채워요";

        // 직접 입력
        public Text ManualPrompt, ManualHint;
        public InputField ManualInput;
        public Button ManualCancel, ManualSubmit;

        // 음성 입력
        public Text RecTitle, RecTimer, RecHint;
        public Button RecStop;
        Image _recDot;

        // 인식 결과
        public Text SttLabel, SttText;
        public Button SttRetry, SttEdit, SttSubmit;

        // Agent 처리
        public Text ProcFooter, ProcDetail;
        readonly List<(Image badge, Text mark, Text label)> _procItems = new List<(Image, Text, Text)>();
        int _procActive = -1;

        // S02 창고 확인
        public RectTransform ConfirmChips;
        public Text ConfirmNote, ConfirmHint;
        public Button RebuildButton, ConfirmButton, RackEditButton;

        // S02-Error
        public Text QuestionProblem, QuestionText, QuestionRound;
        public RectTransform QuestionOptions;
        public Button QuestionSpeak, QuestionType;
        public readonly List<Button> OptionButtons = new List<Button>();

        // S03 시뮬레이션 설정
        public Stepper Robots, Inbound, Outbound, SpecB;
        public Text TotalText;
        public Button RunButton, ConfigRebuildButton, ScenarioSpeakButton;

        // S05 재생
        public Text PlayStatus;
        public Button PlayButton, RestartButton, PathButton, HeatmapButton, SettingsButton, AnalyzeButton, ReportButton;
        public Button[] SpeedButtons;
        public readonly float[] Speeds = { 1f, 2f, 4f };
        public Slider StepSlider;

        // 경로 보기
        public RectTransform RobotGrid;
        public Button PathOffButton, PathCloseButton;
        public readonly List<Button> RobotButtons = new List<Button>();

        // 히트맵 보기
        public Button MetricWaitButton, MetricPassButton, HeatmapOffButton, HeatmapCloseButton, HeatmapMarkersButton;

        // S06 병목 분석
        public Text BottleneckTitle, BottleneckWhere, BottleneckDetail, AgentExplain, ProposalHint;
        public RectTransform ProposalList;
        public Button AnalysisClose, AnalysisMarkersButton;
        public readonly List<Button> ProposalButtons = new List<Button>();

        // S07 개선안 승인
        public Text ApprovalText, ApprovalEffects;
        public Button ApprovalCancel, ApprovalApply;

        // S08 비교
        public Text CmpTitleNote;
        readonly Text[,] _cmpCells = new Text[4, 4];
        readonly GameObject[] _cmpRows = new GameObject[4];
        public GameObject CmpHeatRow;
        public Button CmpBeforeHeat, CmpAfterHeat, CmpPlayback, CmpRerun;

        // Feedback (토스트)
        RectTransform _toast;
        Image _toastBg;
        Text _toastText;
        Button _toastAction;
        Action _toastCallback;
        float _toastUntil;

        float _time;

        // ================================================================== 만들기

        public void Build(Transform parent)
        {
            Canvas = UIFactory.CreateWorldCanvas("UserPanel", new Vector2(Width, FullHeight), parent, 0.001f, 28, 12);
            var rt = (RectTransform)Canvas.transform;
            rt.pivot = new Vector2(0.5f, 0f);   // 아래 가장자리 기준: 화면마다 높이가 바뀌어도 바닥 위치 유지
            BuildHeader(Canvas.transform);
            UIFactory.Divider(Canvas.transform);

            var body = new GameObject("Screens", typeof(RectTransform));
            body.transform.SetParent(Canvas.transform, false);
            body.AddComponent<LayoutElement>().flexibleHeight = 1;
            _body = (RectTransform)body.transform;

            BuildCreate();
            BuildManual();
            BuildRecording();
            BuildSttResult();
            BuildProcessing();
            BuildConfirm();
            BuildQuestion();
            BuildSimConfig();
            BuildPlayback();
            BuildPathSelect();
            BuildHeatmap();
            BuildAnalysis();
            BuildApproval();
            BuildComparison();
            BuildToast();

            Show(UserScreen.Create, 1, "창고 만들기");
            SetConnection(ConnectionView.Disconnected);
        }

        void BuildHeader(Transform root)
        {
            var row = UIFactory.Row(root, 76, 18, false);
            (_stepBadge, _stepBadgeText) = UIFactory.Badge(row, 54, "1", UIFactory.Primary, false, UIFactory.FontBody);
            _title = UIFactory.Label(row, "", UIFactory.FontTitle, UIFactory.TextMain);
            _title.fontStyle = FontStyle.Bold;
            _title.resizeTextForBestFit = true;   // 긴 제목은 글자를 줄여 진행 표시와 겹치지 않게
            _title.resizeTextMinSize = 26;
            _title.resizeTextMaxSize = UIFactory.FontTitle;
            var tle = _title.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1;
            tle.preferredWidth = 1;

            // 진행 단계 1 -> 2 -> 3 -> 4
            var prog = new GameObject("Progress", typeof(RectTransform));
            prog.transform.SetParent(row, false);
            var hl = prog.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            for (int i = 0; i < 4; i++)
            {
                (_progress[i], _progressText[i]) = UIFactory.Badge(prog.transform, 40, (i + 1).ToString(), UIFactory.Secondary, true, 22);
                if (i < 3)
                {
                    var line = new GameObject("Line", typeof(RectTransform)).AddComponent<Image>();
                    line.transform.SetParent(prog.transform, false);
                    line.raycastTarget = false;
                    var le = line.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = le.minWidth = 26;
                    le.preferredHeight = le.minHeight = 4;
                    _progressLine[i] = line;
                }
            }

            UIFactory.Spacer(row, 24, 0);
            (_connDot, _) = UIFactory.Badge(row, 24, "", UIFactory.Secondary);
            _connText = UIFactory.Label(row, "", UIFactory.FontSmall + 2, UIFactory.TextMain);
            _connText.fontStyle = FontStyle.Bold;
            _connText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.SetWidth(_connText, 190);

            // 관리자 메뉴 버튼: 평소에는 숨김. 컨트롤러 비밀 입력(왼손 그립 + Y 3번)을 하면 잠깐 나타난다
            AdminButton = UIFactory.Button(row, "≡", null, new Color(1f, 1f, 1f, 0.14f), 60, 60, UIFactory.FontTitle);
            AdminButton.gameObject.SetActive(false);
        }

        /// <summary>머리글의 관리자 메뉴 버튼 보이기 (비밀 입력 뒤 일정 시간).</summary>
        public void SetSettingsVisible(bool on)
        {
            if (AdminButton != null && AdminButton.gameObject.activeSelf != on) AdminButton.gameObject.SetActive(on);
        }

        /// <summary>화면 틀: Content(남는 높이) + 구분선 + Footer(높이 120 버튼 줄).</summary>
        (RectTransform content, RectTransform footer) NewScreen(UserScreen id, float height, bool footer = true)
        {
            var go = new GameObject("Screen_" + id, typeof(RectTransform));
            go.transform.SetParent(_body, false);
            UIFactory.Stretch((RectTransform)go.transform);
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(8, 8, 14, 0);
            vl.spacing = 14;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var content = UIFactory.Column(go.transform, 12, TextAnchor.MiddleCenter, "Content");
            content.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            RectTransform f = null;
            if (footer)
            {
                UIFactory.Divider(go.transform);
                f = UIFactory.Row(go.transform, UIFactory.ButtonHeight, UIFactory.ActionGap, false);
                f.name = "Footer";
            }
            _screens[id] = go;
            _heights[id] = height;
            go.SetActive(false);
            return (content, f);
        }

        void BuildCreate()
        {
            var (c, f) = NewScreen(UserScreen.Create, FullHeight);
            var prompt = UIFactory.Label(c, "원하는 창고를 말씀해주세요.", UIFactory.FontLarge, UIFactory.TextMain, TextAnchor.MiddleCenter);
            prompt.fontStyle = FontStyle.Bold;
            var row = UIFactory.Row(c, 130, 0, false);
            UIFactory.Spacer(row);
            SpeakButton = UIFactory.Button(row, "●  말하기", null, UIFactory.Primary, 400, 130, UIFactory.FontLarge);
            UIFactory.Spacer(row);
            UIFactory.Label(c, "또는 컨트롤러 A 버튼을 누른 채 말해도 돼요",
                UIFactory.FontSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            CreateStatus = UIFactory.Label(c, "", UIFactory.FontSmall, UIFactory.Warning, TextAnchor.MiddleCenter);
            RequiredInfo = UIFactory.Label(c, RequiredInfoGuide, UIFactory.FontSmall - 2, UIFactory.TextDim, TextAnchor.MiddleCenter);

            UIFactory.Spacer(f);
            ManualButton = UIFactory.Button(f, "직접 입력하기", null, UIFactory.Secondary, 380);
            HistoryButton = UIFactory.Button(f, "이전 기록", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
        }

        void BuildManual()
        {
            var (c, f) = NewScreen(UserScreen.ManualInput, FullHeight);
            ManualPrompt = UIFactory.Label(c, "창고 조건을 입력하세요.", UIFactory.FontTitle, UIFactory.TextMain);
            ManualPrompt.fontStyle = FontStyle.Bold;
            ManualInput = UIFactory.Input(c, "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개 …", 180, true);
            ManualHint = UIFactory.Label(c, RequiredInfoGuide, UIFactory.FontSmall, UIFactory.TextDim);

            ManualCancel = UIFactory.Button(f, "취소", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
            ManualSubmit = UIFactory.Button(f, "생성하기", null, UIFactory.Primary, 380);
        }

        void BuildRecording()
        {
            var (c, f) = NewScreen(UserScreen.Recording, FullHeight);
            var dotRow = UIFactory.Row(c, 90, 0, false);
            UIFactory.Spacer(dotRow);
            (_recDot, _) = UIFactory.Badge(dotRow, 80, "", UIFactory.Danger);
            UIFactory.Spacer(dotRow);
            RecTitle = UIFactory.Label(c, "듣고 있습니다", UIFactory.FontLarge, UIFactory.TextMain, TextAnchor.MiddleCenter);
            RecTitle.fontStyle = FontStyle.Bold;
            RecTimer = UIFactory.Label(c, "00:00", UIFactory.FontHuge, UIFactory.TextMain, TextAnchor.MiddleCenter);
            RecHint = UIFactory.Label(c, "\"말씀해주세요...\"", UIFactory.FontBody, UIFactory.TextDim, TextAnchor.MiddleCenter);

            UIFactory.Spacer(f);
            RecStop = UIFactory.Button(f, "■  녹음 종료", null, UIFactory.Danger, 460);
            UIFactory.Spacer(f);
        }

        void BuildSttResult()
        {
            var (c, f) = NewScreen(UserScreen.SttResult, 620);
            SttLabel = UIFactory.Label(c, "인식한 내용", UIFactory.FontSmall, UIFactory.TextDim);
            var box = UIFactory.Section(c, null);
            SttText = UIFactory.Label(box, "", UIFactory.FontTitle, UIFactory.TextMain, TextAnchor.MiddleLeft);
            UIFactory.SetHeight(box, 160);

            SttRetry = UIFactory.Button(f, "다시 말하기", null, UIFactory.Secondary, 300);
            SttEdit = UIFactory.Button(f, "고쳐 쓰기", null, UIFactory.Secondary, 280);
            UIFactory.Spacer(f, 0, 1);
            SttSubmit = UIFactory.Button(f, "생성하기", null, UIFactory.Primary, 360);
            ((HorizontalLayoutGroup)f.GetComponent<HorizontalLayoutGroup>()).spacing = 24;
        }

        void BuildProcessing()
        {
            var (c, f) = NewScreen(UserScreen.Processing, 690);
            var list = UIFactory.Column(c, 6, TextAnchor.MiddleCenter, "Checklist");
            list.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(300, 200, 0, 0);
            for (int i = 0; i < 5; i++)
            {
                var row = UIFactory.Row(list, 60, 22, false);
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                var (badge, mark) = UIFactory.Badge(row, 40, "", UIFactory.Secondary, true, 22);
                var label = UIFactory.Label(row, "", UIFactory.FontTitle, UIFactory.TextMuted);
                label.fontStyle = FontStyle.Bold;
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                _procItems.Add((badge, mark, label));
            }
            ProcDetail = UIFactory.Label(c, "", UIFactory.FontSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);

            ProcFooter = UIFactory.Label(f, "잠시만 기다려주세요", UIFactory.FontTitle, UIFactory.TextMain, TextAnchor.MiddleCenter);
            ProcFooter.fontStyle = FontStyle.Bold;
            ProcFooter.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        void BuildConfirm()
        {
            var (c, f) = NewScreen(UserScreen.Confirm, 640);
            ConfirmHint = UIFactory.Label(c, "테이블 위 창고를 둘러보고 맞으면 확인을 누르세요", UIFactory.FontSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            var grid = new GameObject("Chips", typeof(RectTransform));
            grid.transform.SetParent(c, false);
            var gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(364, 58);
            gl.spacing = new Vector2(18, 12);
            gl.childAlignment = TextAnchor.UpperCenter;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;
            ConfirmChips = (RectTransform)grid.transform;
            ConfirmNote = UIFactory.Label(c, "", UIFactory.FontSmall, UIFactory.Warning, TextAnchor.MiddleCenter);

            f.GetComponent<HorizontalLayoutGroup>().spacing = 40;
            RebuildButton = UIFactory.Button(f, "다시 만들기", null, UIFactory.Warning, 300);
            RackEditButton = UIFactory.Button(f, "랙 옮기기: 끔", null, UIFactory.Secondary, 270, UIFactory.ButtonHeight, UIFactory.FontSmall + 2);
            UIFactory.Spacer(f);
            ConfirmButton = UIFactory.Button(f, "√  확인", null, UIFactory.Success, 340);
        }

        void BuildQuestion()
        {
            var (c, f) = NewScreen(UserScreen.Question, 760);
            var top = UIFactory.Row(c, 44, 12, false);
            QuestionProblem = UIFactory.Label(top, "", UIFactory.FontSmall, UIFactory.TextDim);
            QuestionProblem.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            QuestionRound = UIFactory.Label(top, "", UIFactory.FontSmall, UIFactory.TextMuted, TextAnchor.MiddleRight);
            UIFactory.SetWidth(QuestionRound, 200);
            var box = UIFactory.Section(c, null);
            QuestionText = UIFactory.Label(box, "", UIFactory.FontTitle - 2, UIFactory.TextMain);
            box.gameObject.AddComponent<LayoutElement>().minHeight = 140;   // 긴 질문이면 늘어남
            var opt = UIFactory.Row(c, UIFactory.ButtonHeight, 24, false);
            opt.name = "Options";
            QuestionOptions = opt;

            QuestionType = UIFactory.Button(f, "직접 입력", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
            QuestionSpeak = UIFactory.Button(f, "●  말해서 답하기", null, UIFactory.Primary, 420);
        }

        void BuildSimConfig()
        {
            var (c, f) = NewScreen(UserScreen.SimConfig, FullHeight);
            Robots = UIFactory.Stepper(c, "가동 로봇", "대", 1, 16, 1, 4, 80f);
            Inbound = UIFactory.Stepper(c, "입하 물량", "건", 0, 200, 5, 25, 80f);
            Outbound = UIFactory.Stepper(c, "출하 물량", "건", 0, 200, 5, 25, 80f);
            SpecB = UIFactory.Stepper(c, "1200x1000 규격", "%", 0, 100, 10, 0, 80f);   // 제품 규격 (FR-11), 나머지는 1100x1100
            var voice = UIFactory.Row(c, 72, 16, false);
            var vh = UIFactory.Label(voice, "말로 설정: \"로봇 6대, 입하 30건, 1200 규격 30%, 실행해줘\"", UIFactory.FontSmall - 2, UIFactory.TextDim);
            vh.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            ScenarioSpeakButton = UIFactory.Button(voice, "●  말로 설정", null, UIFactory.Secondary, 300, 72, UIFactory.FontSmall + 2);
            Inbound.OnChanged += _ => UpdateTotal();
            Outbound.OnChanged += _ => UpdateTotal();

            ConfigRebuildButton = UIFactory.Button(f, "창고 다시 만들기", null, UIFactory.Secondary, 300, UIFactory.ButtonHeight, UIFactory.FontSmall + 2);
            TotalText = UIFactory.Label(f, "", UIFactory.FontTitle, UIFactory.TextMain, TextAnchor.MiddleCenter);
            TotalText.fontStyle = FontStyle.Bold;
            TotalText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            RunButton = UIFactory.Button(f, "▶  시뮬레이션 실행", null, UIFactory.Primary, 440);
            f.GetComponent<HorizontalLayoutGroup>().spacing = 24;
            UpdateTotal();
        }

        void UpdateTotal()
        {
            if (TotalText != null && Inbound != null && Outbound != null)
                TotalText.text = $"총 {Inbound.Value + Outbound.Value}건";
        }

        void BuildPlayback()
        {
            var (c, f) = NewScreen(UserScreen.Playback, 520);
            PlayStatus = UIFactory.Label(c, "", UIFactory.FontSmall + 2, UIFactory.TextDim, TextAnchor.MiddleCenter);
            // 자주 바뀌는 글자는 하위 캔버스로 분리 -> 바뀔 때 패널 전체를 다시 그리지 않음 (Quest 프레임 유지)
            PlayStatus.gameObject.AddComponent<Canvas>();
            UIFactory.SetHeight(PlayStatus, 40);

            var ctl = UIFactory.Row(c, UIFactory.ButtonHeight, 18, false);
            ctl.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;   // 진행 바는 64px 그대로
            PlayButton = UIFactory.Button(ctl, "▶", null, UIFactory.Primary, 150, UIFactory.ButtonHeight, UIFactory.FontLarge);
            RestartButton = UIFactory.Button(ctl, "|◀", null, UIFactory.Secondary, 110, UIFactory.ButtonHeight, UIFactory.FontBody);
            StepSlider = UIFactory.Slider(ctl, 64);
            SpeedButtons = new Button[Speeds.Length];
            for (int i = 0; i < Speeds.Length; i++)
                SpeedButtons[i] = UIFactory.Button(ctl, $"{Speeds[i]:0}x", null, UIFactory.Secondary, 96, UIFactory.ButtonHeight, UIFactory.FontBody);

            var hl = f.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 20;
            hl.childForceExpandWidth = true;
            PathButton = UIFactory.Button(f, "경로", null, UIFactory.Secondary);
            HeatmapButton = UIFactory.Button(f, "히트맵", null, UIFactory.Secondary);
            SettingsButton = UIFactory.Button(f, "조건 바꾸기", null, UIFactory.Secondary);
            AnalyzeButton = UIFactory.Button(f, "병목 분석", null, UIFactory.Warning);
            ReportButton = UIFactory.Button(f, "리포트", null, UIFactory.Primary);
        }

        void BuildPathSelect()
        {
            var (c, f) = NewScreen(UserScreen.PathSelect, 640);
            UIFactory.Label(c, "로봇을 고르면 그 로봇이 지나간 길을 바닥에 선으로 보여줘요", UIFactory.FontSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            var grid = new GameObject("Robots", typeof(RectTransform));
            grid.transform.SetParent(c, false);
            var gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(250, 84);
            gl.spacing = new Vector2(20, 16);
            gl.childAlignment = TextAnchor.UpperCenter;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            RobotGrid = (RectTransform)grid.transform;

            PathOffButton = UIFactory.Button(f, "경로 끄기", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
            PathCloseButton = UIFactory.Button(f, "닫기", null, UIFactory.Primary, 300);
        }

        void BuildHeatmap()
        {
            var (c, f) = NewScreen(UserScreen.Heatmap, 630);
            UIFactory.Label(c, "창고 바닥에 로봇이 몰린 정도를 색으로 표시해요", UIFactory.FontSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            var legend = UIFactory.Row(c, 56, 18, false);
            UIFactory.Label(legend, "낮음", UIFactory.FontBody, UIFactory.TextMain, TextAnchor.MiddleRight).gameObject
                .AddComponent<LayoutElement>().preferredWidth = 120;
            var bar = new GameObject("Gradient", typeof(RectTransform)).AddComponent<RawImage>();
            bar.transform.SetParent(legend, false);
            bar.texture = GradientTexture();
            bar.raycastTarget = false;
            var ble = bar.gameObject.AddComponent<LayoutElement>();
            ble.preferredWidth = ble.minWidth = 640;
            UIFactory.Label(legend, "높음", UIFactory.FontBody, UIFactory.TextMain).gameObject
                .AddComponent<LayoutElement>().preferredWidth = 120;
            UIFactory.Label(c, "표시 기준", UIFactory.FontSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            var m = UIFactory.Row(c, UIFactory.ButtonHeight, 0, false);
            UIFactory.Spacer(m);
            MetricWaitButton = UIFactory.Button(m, "대기 횟수", null, UIFactory.Primary, 300);
            UIFactory.Spacer(m, 24, 0);
            MetricPassButton = UIFactory.Button(m, "통과 횟수", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(m);

            HeatmapOffButton = UIFactory.Button(f, "히트맵 끄기", null, UIFactory.Secondary, 300);
            HeatmapMarkersButton = UIFactory.Button(f, "병목 기둥: 켬", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
            HeatmapCloseButton = UIFactory.Button(f, "닫기", null, UIFactory.Primary, 300);
        }

        static Texture2D GradientTexture()
        {
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < 64; i++)
            {
                var col = HeatmapMath.ToColor(Mathf.Max(0.02f, i / 63f));
                col.a = 1f;
                tex.SetPixel(i, 0, col);
            }
            tex.Apply(false, true);
            return tex;
        }

        void BuildAnalysis()
        {
            var (c, f) = NewScreen(UserScreen.Analysis, 840);
            var cols = UIFactory.Row(c, 280, 28, false);
            cols.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            cols.GetComponent<LayoutElement>().flexibleHeight = 1;

            var left = UIFactory.Section(cols, null);
            UIFactory.SetWidth(left, 560);
            left.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            BottleneckTitle = UIFactory.Label(left, "▲ 주요 병목 발견", UIFactory.FontBody, UIFactory.Warning);
            BottleneckTitle.fontStyle = FontStyle.Bold;
            BottleneckWhere = UIFactory.Label(left, "", UIFactory.FontLarge, UIFactory.TextMain);
            BottleneckWhere.fontStyle = FontStyle.Bold;
            BottleneckDetail = UIFactory.Label(left, "", UIFactory.FontBody, UIFactory.TextDim);
            UIFactory.Divider(left);
            var agentTitle = UIFactory.Label(left, "Agent 분석", UIFactory.FontSmall, UIFactory.TextDim);
            agentTitle.fontStyle = FontStyle.Bold;
            AgentExplain = UIFactory.Label(left, "", UIFactory.FontSmall, UIFactory.TextMain, TextAnchor.UpperLeft);
            // 설명이 길면 남은 칸 안에서 글자를 줄인다 (최소 20px)
            AgentExplain.resizeTextForBestFit = true;
            AgentExplain.resizeTextMinSize = 20;
            AgentExplain.resizeTextMaxSize = UIFactory.FontSmall;
            AgentExplain.verticalOverflow = VerticalWrapMode.Truncate;
            var ale = AgentExplain.gameObject.AddComponent<LayoutElement>();
            ale.minHeight = 120;
            ale.preferredHeight = 120;
            ale.flexibleHeight = 1;

            var right = UIFactory.Column(cols, 16, TextAnchor.UpperCenter, "Proposals");
            right.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var pt = UIFactory.Label(right, "개선안", UIFactory.FontBody, UIFactory.TextMain);
            pt.fontStyle = FontStyle.Bold;
            var list = UIFactory.Column(right, 16, TextAnchor.UpperCenter, "List");
            ProposalList = list;
            ProposalHint = UIFactory.Label(right, "고르면 적용 전에 한 번 더 확인해요", UIFactory.FontSmall, UIFactory.TextDim);

            AnalysisMarkersButton = UIFactory.Button(f, "병목 기둥: 켬", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
            AnalysisClose = UIFactory.Button(f, "닫기", null, UIFactory.Primary, 320);
        }

        void BuildApproval()
        {
            var (c, f) = NewScreen(UserScreen.Approval, 660);
            UIFactory.Label(c, "선택한 개선안", UIFactory.FontSmall, UIFactory.TextDim);
            var box = UIFactory.Section(c, null);
            ApprovalText = UIFactory.Label(box, "", UIFactory.FontLarge - 4, UIFactory.TextMain);
            ApprovalText.fontStyle = FontStyle.Bold;
            UIFactory.Label(c, "예상 효과", UIFactory.FontSmall, UIFactory.TextDim);
            ApprovalEffects = UIFactory.Label(c, "", UIFactory.FontBody, UIFactory.TextMain, TextAnchor.UpperLeft);

            ApprovalCancel = UIFactory.Button(f, "취소", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
            ApprovalApply = UIFactory.Button(f, "√  적용", null, UIFactory.Success, 380);
        }

        void BuildComparison()
        {
            var (c, f) = NewScreen(UserScreen.Comparison, 780);
            CmpTitleNote = UIFactory.Label(c, "", UIFactory.FontSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            var table = UIFactory.Section(c, null);
            for (int r = 0; r < 4; r++)
            {
                var row = UIFactory.Row(table, r == 0 ? 40 : 56, 12);
                _cmpRows[r] = row.gameObject;
                for (int k = 0; k < 4; k++)
                {
                    int size = r == 0 ? UIFactory.FontSmall : (k == 0 ? UIFactory.FontBody : UIFactory.FontLarge - 4);
                    var t = UIFactory.Label(row, "", size, r == 0 || k == 0 ? UIFactory.TextDim : UIFactory.TextMain,
                        k == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                    if (r > 0 && k > 0) t.fontStyle = FontStyle.Bold;
                    _cmpCells[r, k] = t;
                }
            }
            var heat = UIFactory.Row(c, 84, 24, false);
            CmpHeatRow = heat.gameObject;
            UIFactory.Label(heat, "히트맵", UIFactory.FontBody, UIFactory.TextDim).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            CmpBeforeHeat = UIFactory.Button(heat, "적용 전", null, UIFactory.Secondary, 280, 84);
            CmpAfterHeat = UIFactory.Button(heat, "적용 후", null, UIFactory.Secondary, 280, 84);

            CmpPlayback = UIFactory.Button(f, "재생 보기", null, UIFactory.Secondary, 300);
            UIFactory.Spacer(f);
            CmpRerun = UIFactory.Button(f, "다시 시뮬레이션", null, UIFactory.Primary, 420);
        }

        void BuildToast()
        {
            var go = new GameObject("Toast", typeof(RectTransform));
            go.transform.SetParent(Canvas.transform, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            _toast = (RectTransform)go.transform;
            _toast.anchorMin = _toast.anchorMax = new Vector2(0.5f, 1f);
            _toast.pivot = new Vector2(0.5f, 0f);
            _toast.anchoredPosition = new Vector2(0, 18);
            _toast.sizeDelta = new Vector2(Width - 60, 96);
            // 패널 위쪽에 따로 그리므로 하위 캔버스 (정렬 순서 위)
            var cv = go.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 5;
            go.AddComponent<GraphicRaycaster>();
            _toastBg = go.AddComponent<Image>();
            UIFactory.MakeRounded(_toastBg, 20);
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(28, 16, 12, 12);
            hl.spacing = 20;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            _toastText = UIFactory.Label(go.transform, "", UIFactory.FontSmall + 2, Color.white);
            _toastText.fontStyle = FontStyle.Bold;
            _toastText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            _toastAction = UIFactory.Button(go.transform, "다시 시도", () =>
            {
                var cb = _toastCallback;
                HideToast();
                cb?.Invoke();
            }, new Color(1f, 1f, 1f, 0.22f), 230, 72, UIFactory.FontSmall);
            go.SetActive(false);
        }

        // ================================================================== 화면 전환

        /// <summary>
        /// 화면을 바꾼다. step = 진행 단계(1 창고 만들기, 2 창고 확인, 3 시뮬레이션, 4 결과 분석).
        /// </summary>
        public void Show(UserScreen screen, int step, string title, Color? titleColor = null)
        {
            foreach (var kv in _screens) kv.Value.SetActive(kv.Key == screen);
            _title.text = title;
            _title.color = titleColor ?? UIFactory.TextMain;
            SetStep(step);
            FitHeight(screen);
            bool changed = Current != screen;
            Current = screen;
            if (changed) OnScreenChanged?.Invoke(screen);
        }

        /// <summary>
        /// 화면 높이를 정해진 값으로 두되, 글자가 길어 내용이 넘치면 패널을 위로 늘린다 (아래 가장자리 고정).
        /// 글자를 바꾼 뒤 다시 맞추려면 Refit().
        /// </summary>
        void FitHeight(UserScreen screen)
        {
            var rt = (RectTransform)Canvas.transform;
            float baseH = _heights.TryGetValue(screen, out var h) ? h : FullHeight;
            rt.sizeDelta = new Vector2(Width, baseH);
            if (!_screens.TryGetValue(screen, out var go)) return;
            var sr = (RectTransform)go.transform;
            // 화면(Screen_*)은 레이아웃 그룹이 없는 Screens 아래에 늘여 붙인 별도 레이아웃 루트라서,
            // 캔버스만 다시 계산하면 화면 안 글자 폭이 갱신되지 않는다 (폭 100px로 계산돼 높이가 몇 배로 커짐).
            // 캔버스 → 화면 순서로 둘 다 다시 계산한 뒤 잰다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            LayoutRebuilder.ForceRebuildLayoutImmediate(sr);
            float chrome = rt.rect.height - _body.rect.height;          // 여백 + 머리글 + 구분선
            float need = chrome + LayoutUtility.GetPreferredHeight(sr) + 8f;
            if (need > baseH + 1f)
            {
                rt.sizeDelta = new Vector2(Width, Mathf.Ceil(need));
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                LayoutRebuilder.ForceRebuildLayoutImmediate(sr);
            }
        }

        public void Refit() => FitHeight(Current);

        void SetStep(int step)
        {
            step = Mathf.Clamp(step, 1, 4);
            _stepBadgeText.text = step.ToString();
            _stepBadge.color = UIFactory.Primary;
            for (int i = 0; i < 4; i++)
            {
                bool done = i + 1 < step, now = i + 1 == step;
                _progress[i].sprite = done || now ? UIFactory.CircleSprite : UIFactory.RingSprite;
                _progress[i].color = done ? UIFactory.Success : now ? UIFactory.Primary : UIFactory.TextMuted;
                _progressText[i].text = done ? "√" : (i + 1).ToString();
                _progressText[i].color = done || now ? Color.white : UIFactory.TextMuted;
                if (i < 3) _progressLine[i].color = done ? UIFactory.Success : UIFactory.DividerColor;
            }
        }

        public void SetConnection(ConnectionView v)
        {
            switch (v)
            {
                case ConnectionView.Connected: _connDot.color = UIFactory.Success; _connText.text = "연결됨"; break;
                case ConnectionView.Connecting: _connDot.color = UIFactory.Warning; _connText.text = "연결 중"; break;
                case ConnectionView.Reconnecting: _connDot.color = UIFactory.Warning; _connText.text = "재연결 중"; break;
                case ConnectionView.Offline: _connDot.color = UIFactory.Secondary; _connText.text = "저장된 결과"; break;
                default: _connDot.color = UIFactory.Danger; _connText.text = "연결 안 됨"; break;
            }
        }

        // ================================================================== 화면별 채우기

        /// <summary>Agent 처리 체크리스트 항목을 정한다 (최대 5개).</summary>
        public void SetProcessing(IList<string> items, string footer = "잠시만 기다려주세요")
        {
            for (int i = 0; i < _procItems.Count; i++)
            {
                bool on = i < items.Count;
                _procItems[i].badge.transform.parent.gameObject.SetActive(on);
                if (on) _procItems[i].label.text = items[i];
            }
            ProcFooter.text = footer;
            ProcDetail.text = "";
            SetProcessingIndex(0);
        }

        public int ProcessingIndex => _procActive;
        public int ProcessingCount
        {
            get
            {
                int n = 0;
                foreach (var it in _procItems) if (it.badge.transform.parent.gameObject.activeSelf) n++;
                return n;
            }
        }

        /// <summary>active 이전 항목은 완료, active는 진행 중, 이후는 대기. active >= 개수면 모두 완료.</summary>
        public void SetProcessingIndex(int active, bool failed = false)
        {
            _procActive = active;
            for (int i = 0; i < _procItems.Count; i++)
            {
                var (badge, mark, label) = _procItems[i];
                var st = i < active ? ProcState.Done : i == active ? (failed ? ProcState.Failed : ProcState.Active) : ProcState.Pending;
                switch (st)
                {
                    case ProcState.Done:
                        badge.sprite = UIFactory.CircleSprite; badge.color = UIFactory.Success;
                        mark.text = "√"; label.color = UIFactory.TextMain; break;
                    case ProcState.Active:
                        badge.sprite = UIFactory.RingSprite; badge.color = UIFactory.Primary;
                        mark.text = "●"; mark.color = UIFactory.Primary; label.color = UIFactory.Lighten(UIFactory.Primary, 0.35f); break;
                    case ProcState.Failed:
                        badge.sprite = UIFactory.CircleSprite; badge.color = UIFactory.Danger;
                        mark.text = "!"; label.color = UIFactory.Danger; break;
                    default:
                        badge.sprite = UIFactory.RingSprite; badge.color = UIFactory.TextMuted;
                        mark.text = ""; label.color = UIFactory.TextMuted; break;
                }
                if (st != ProcState.Active) mark.color = Color.white;
            }
        }

        /// <summary>S02 요약 칩 ("랙  8개" 처럼 이름, 값 두 칸씩).</summary>
        public void SetConfirmSummary(IList<(string label, string value)> chips)
        {
            for (int i = ConfirmChips.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(ConfirmChips.GetChild(i).gameObject);
            foreach (var (label, value) in chips)
            {
                var go = new GameObject("Chip", typeof(RectTransform));
                go.transform.SetParent(ConfirmChips, false);
                var img = go.AddComponent<Image>();
                img.color = UIFactory.SectionBg;
                img.raycastTarget = false;
                UIFactory.MakeRounded(img, 14);
                var t = UIFactory.Label(go.transform,
                    $"<color={UIFactory.Hex(UIFactory.TextDim)}>{label}</color>  <b>{value}</b>", UIFactory.FontSmall + 2, UIFactory.TextMain);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Stretch(t.rectTransform, 18, 0);
            }
            int rows = Mathf.CeilToInt(chips.Count / 3f);
            UIFactory.SetHeight(ConfirmChips, rows * 58 + Mathf.Max(0, rows - 1) * 12);
        }

        public void ClearOptions()
        {
            foreach (var b in OptionButtons) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            OptionButtons.Clear();
            for (int i = QuestionOptions.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(QuestionOptions.GetChild(i).gameObject);
        }

        public Button AddOption(string label, Action onClick)
        {
            if (OptionButtons.Count == 0) UIFactory.Spacer(QuestionOptions);
            var b = UIFactory.Button(QuestionOptions, label, () => onClick(), UIFactory.Primary, 280);
            OptionButtons.Add(b);
            return b;
        }

        public void EndOptions()
        {
            if (OptionButtons.Count > 0) UIFactory.Spacer(QuestionOptions);
            QuestionOptions.gameObject.SetActive(OptionButtons.Count > 0);
            // 선택 버튼이 있으면 그것이 주 행동, 없으면 말해서 답하기
            UIFactory.SetButtonColor(QuestionSpeak, OptionButtons.Count > 0 ? UIFactory.Secondary : UIFactory.Primary);
        }

        public void ClearProposals()
        {
            foreach (var b in ProposalButtons) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            ProposalButtons.Clear();
        }

        public Button AddProposal(string text, Action onClick)
        {
            var b = UIFactory.Button(ProposalList, text, () => onClick(), UIFactory.Primary, -1, 92, UIFactory.FontBody);
            ProposalButtons.Add(b);
            return b;
        }

        /// <summary>경로 보기: 로봇 버튼을 다시 만든다 (선택된 로봇 강조).</summary>
        public void SetRobots(IReadOnlyList<string> ids, string selected, bool pathOn, Action<string> onPick)
        {
            foreach (var b in RobotButtons) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            RobotButtons.Clear();
            int rows = Mathf.Max(1, Mathf.CeilToInt(ids.Count / 4f));
            _heights[UserScreen.PathSelect] = 640f + Mathf.Max(0, rows - 2) * 112f;
            foreach (var id in ids)
            {
                var rid = id;
                var b = UIFactory.Button(RobotGrid, rid, () => onPick(rid), pathOn && rid == selected ? UIFactory.Accent : UIFactory.Secondary, 250, 84);
                RobotButtons.Add(b);
            }
        }

        public void HighlightRobot(string selected, bool pathOn)
        {
            foreach (var b in RobotButtons)
            {
                var t = b.GetComponentInChildren<Text>();
                UIFactory.SetButtonColor(b, pathOn && t != null && t.text == selected ? UIFactory.Accent : UIFactory.Secondary);
            }
        }

        /// <summary>S08 표. rows: (항목, 앞, 뒤, 변화, 좋아졌는지).</summary>
        public void SetComparison(string headA, string headB, IList<(string label, string a, string b, string change, bool better)> rows,
            string note)
        {
            CmpTitleNote.text = note;
            _cmpCells[0, 0].text = "";
            _cmpCells[0, 1].text = headA;
            _cmpCells[0, 2].text = headB;
            _cmpCells[0, 3].text = "변화";
            for (int r = 1; r < 4; r++)
            {
                bool on = r - 1 < rows.Count;
                _cmpRows[r].SetActive(on);
                if (!on) continue;
                var row = rows[r - 1];
                _cmpCells[r, 0].text = row.label;
                _cmpCells[r, 1].text = row.a;
                _cmpCells[r, 2].text = row.b;
                _cmpCells[r, 3].text = row.change;
                _cmpCells[r, 2].color = row.better ? UIFactory.Lighten(UIFactory.Success, 0.35f) : UIFactory.TextMain;
                _cmpCells[r, 3].color = row.better ? UIFactory.Lighten(UIFactory.Success, 0.35f) : UIFactory.Warning;
            }
        }

        public void HighlightSpeed(float speed)
        {
            for (int i = 0; i < SpeedButtons.Length; i++)
                UIFactory.SetButtonColor(SpeedButtons[i], Mathf.Approximately(Speeds[i], speed) ? UIFactory.Primary : UIFactory.Secondary);
        }

        public void SetToggle(Button b, bool on, Color onColor) => UIFactory.SetButtonColor(b, on ? onColor : UIFactory.Secondary);

        // ================================================================== 토스트, 애니메이션

        public void Toast(string message, ToastKind kind = ToastKind.Info, float seconds = 4f, string actionLabel = null, Action action = null)
        {
            _toast.gameObject.SetActive(true);
            _toastText.text = message;
            _toastBg.color = kind switch
            {
                ToastKind.Success => UIFactory.Darken(UIFactory.Success, 0.15f),
                ToastKind.Warning => UIFactory.Darken(UIFactory.Warning, 0.15f),
                ToastKind.Error => UIFactory.Darken(UIFactory.Danger, 0.15f),
                _ => new Color(0.16f, 0.19f, 0.24f, 0.96f)
            };
            _toastCallback = action;
            _toastAction.gameObject.SetActive(action != null);
            if (action != null) UIFactory.SetButtonText(_toastAction, actionLabel ?? "다시 시도");
            _toastUntil = action != null ? float.MaxValue : _time + seconds;
        }

        public void HideToast()
        {
            _toastCallback = null;
            if (_toast != null) _toast.gameObject.SetActive(false);
        }

        /// <summary>AppController.Update에서 매 프레임 호출 (녹음 표시, 진행 표시 깜빡임, 토스트 시간).</summary>
        public void Tick(float dt, float recordingSeconds)
        {
            _time += dt;
            if (_toast.gameObject.activeSelf && _time > _toastUntil) HideToast();
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_time * 3f));
            if (Current == UserScreen.Recording)
            {
                var c = UIFactory.Danger; c.a = pulse;
                _recDot.color = c;
                int s = Mathf.FloorToInt(recordingSeconds);
                var txt = $"{s / 60:00}:{s % 60:00}";
                if (RecTimer.text != txt) RecTimer.text = txt;
            }
            else if (Current == UserScreen.Processing && _procActive >= 0 && _procActive < _procItems.Count)
            {
                var badge = _procItems[_procActive].badge;
                var c = badge.color; c.a = pulse;
                badge.color = c;
            }
        }
    }
}
