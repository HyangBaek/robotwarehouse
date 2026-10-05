using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using RobotWarehouse.Heatmap;
using RobotWarehouse.InputModule;
using RobotWarehouse.Network;
using RobotWarehouse.Playback;
using RobotWarehouse.UI;
using RobotWarehouse.Warehouse;
using RobotWarehouse.XR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RobotWarehouse.App
{
    /// <summary>
    /// VR 클라이언트 흐름 제어 (구현 시나리오 SC-01~12, EX-01~04).
    /// 입력 -> REST 요청 -> WebSocket 이벤트 -> 창고, 로봇, 히트맵, 패널 갱신.
    /// 사용자 패널은 단계별 화면(S01 창고 만들기 -> S02 확인 -> S03 설정 -> S04 실행 -> S05 재생 -> S06 분석 -> S07 승인 -> S08 비교)으로 바뀐다.
    /// </summary>
    public partial class AppController : MonoBehaviour
    {
        public enum Phase { Idle, Generating, AwaitConfirm, Question, Confirmed, Simulating, Ready }
        enum VoiceTarget { Describe, Answer, Scenario }
        /// <summary>지금 사용자 패널에 진행 상태를 보여 주는 작업</summary>
        enum Work { None, Stt, Map, Sim, Analysis, Compare }

        [Header("배치")]
        [Tooltip("미니어처 창고를 올릴 테이블. 지정하면 창고는 이 테이블 윗면에, 패널은 테이블 옆에 놓인다")]
        public GameObject table;
        [Tooltip("테이블 윗면 중 창고가 차지할 비율")]
        [Range(0.5f, 1f)] public float tableMargin = 0.92f;
        [Tooltip("사용자 패널 아래 가장자리 높이 (바닥 기준, m)")]
        public float panelBottom = 0.8f;
        [Tooltip("관리자 모드로 시작 (평소에는 왼손 그립을 누른 채 Y 3번 → 사용자 패널 머리글의 ≡ 버튼으로 진입)")]
        public bool showDevPanel = false;

        [Header("사용자 위치")]
        [Tooltip("헤드셋 추적이 잡히면 XR Origin을 옮겨, 사용자가 테이블 앞에서 테이블을 정면으로 보게 맞춘다 (씬의 XR Origin 위치·방향에 의존하지 않음)")]
        public bool alignUserToTable = true;
        [Tooltip("테이블 중심에서 사용자까지 거리(m)")]
        public float standDistance = 1.6f;

        [Header("실물 1:1 (관제 보기)")]
        [Tooltip("실물 창고 바닥을 사용자 바닥보다 몇 m 아래에 둘지. 무인 창고를 위에서 내려다보는 관제 시점")]
        public float lifeSizeDepth = 2f;
        [Tooltip("랙이 사용자 바닥 위로 튀어나오지 않게 깊이를 자동으로 늘림 (가장 높은 구조물 + 0.3m 이상)")]
        public bool autoDepth = true;
        [Tooltip("실물 보기에서 숨길 씬 오브젝트(바닥·벽 등). 비우면 이름이 Floor인 오브젝트를 숨긴다. 충돌체는 그대로 둬서 떨어지지 않음")]
        public GameObject[] hideInLifeSize;
        [Tooltip("사용자 발밑 유리 바닥의 불투명도 (0이면 안 보임)")]
        [Range(0f, 0.5f)] public float glassFloorAlpha = 0.1f;
        [Tooltip("실물 보기에서 창고 둘레 벽을 반투명하게")]
        public bool translucentWallsInLifeSize = true;

        [Header("테이블이 없을 때")]
        [Tooltip("미니어처 창고를 놓을 위치 (월드 좌표)")]
        public Vector3 tableCenter = new Vector3(0f, 0.8f, 1.1f);
        [Tooltip("미니어처 모드에서 창고 긴 변 길이(m)")]
        public float miniatureSize = 1.4f;
        [Tooltip("XR Origin 기준 사용자 패널 위치 (아래 가장자리)")]
        public Vector3 userPanelOffset = new Vector3(0.75f, 0.8f, 1.2f);
        [Tooltip("XR Origin 기준 관리자 패널 위치 (아래 가장자리, 테이블 없을 때)")]
        public Vector3 devPanelOffset = new Vector3(0.6f, 0.8f, 1.5f);

        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public WarehouseRenderer Warehouse => _warehouse;
        public RobotPlayback Playback => _playback;

        WarehouseRenderer _warehouse;
        RobotPlayback _playback;
        PathLineView _pathView;
        HeatmapLayer _heatmap;
        RackGrabEditor _rackEditor;
        InventoryView _inventory;
        VoiceRecorder _recorder;
        MainPanels _ui;
        UserPanel _user;
        DevPanel _dev;
        ApiClient _api;
        readonly WsClient _ws = new WsClient();

        string _mapVersion;
        bool _mapConfirmed;
        string _simId;
        string _questionId;
        string _questionText;
        int _questionRounds;
        bool _offline;
        bool _miniature = true;
        bool _updatingSlider;
        int _fetchGeneration;
        bool _framesComplete;
        bool _prevA, _prevB, _prevY, _recordingByA;
        Vector3? _approachDir;
        bool _aligned;
        float? _groundY;
        bool _realignRequested;
        Action _unsubscribeRecenter;
        bool? _lastXrActive;
        VoiceTarget _voiceTarget;
        string _lastTranscript = "";
        Work _work;
        int _screenToken;
        string _simSummary = "";
        string _lastRequestText = "";
        string _serverMapJson;       // 서버가 마지막으로 보낸 지도 (랙 편집 실패 시 되돌리기용, 원본 보존)
        bool _editPending;           // 랙 편집 재검증 대기 중
        bool _discardRecording;      // 오류로 녹음을 버릴 때
        List<Proposal> _proposals = new List<Proposal>();

        // 최종 리포트, 시뮬레이션 기록 (visionOS 창)
        ReportPanel _report;
        HistoryPanel _history;
        JObject _lastReport;
        string _reportSimId, _reportShownFor;
        bool _showReportWhenReady, _loadingHistory;

        // 개선안 전, 후 비교 (SC-10)
        bool _awaitingImprove;
        (int steps, int wait)? _beforeImprove;
        List<CellStat> _statsBefore, _statsAfter;

        static readonly string[] MapSteps = { "요구사항 확인", "창고 구조 생성", "창고 검증", "생성 완료" };
        static readonly string[] SimSteps = { "작업 할당", "경로 계산", "충돌 검사" };
        static readonly string[] ImproveSteps = { "창고·조건 수정", "경로 계산", "충돌 검사" };
        static readonly string[] AnalysisSteps = { "로그 분석", "병목 찾기", "개선안 제안" };
        static readonly string[] CompareSteps = { "같은 조건으로 기준 전략 실행", "결과 비교" };
        static readonly string[] SttSteps = { "음성 받기", "음성 인식", "내용 확인" };
        static readonly string[] HistorySteps = { "지도 불러오기", "로봇 동선 불러오기", "재생 준비" };

        bool Connected => _ws.State == WsState.Connected;

        // ------------------------------------------------------------------ 초기화

        void Start()
        {
            EnsureEventSystem();
            BuildWorld();
            BuildUI();
            StartCoroutine(AlignWhenTracked());
            _unsubscribeRecenter = UserAligner.SubscribeRecenter(() => _realignRequested = true);
            _ws.ReconnectInterval = AppConfig.ReconnectIntervalSec;
            _ws.OnMessage += HandleWsMessage;
            _ws.OnStateChanged += HandleWsState;
            SetPhase(Phase.Idle);
            GoCreate();
            if (AppConfig.AutoConnect) Connect();
            else Log(LogLevel.Info, LogModule.Ws, "자동 연결 꺼짐: 관리자 모드 Connection의 Reconnect로 연결합니다");
        }

        void OnDestroy()
        {
            _ws.Dispose();
            _unsubscribeRecenter?.Invoke();
        }

        /// <summary>헤드셋 추적이 잡힐 때까지(최대 3초) 기다렸다가 사용자를 테이블 앞으로 맞춘다.</summary>
        IEnumerator AlignWhenTracked()
        {
            var origin = UserAligner.FindOrigin();
            UserAligner.EnsureGround(origin);
            if (origin != null) _groundY = origin.transform.position.y;
            // 헤드셋을 쓰기 전에는 맞추지 않는다 (Update에서 추적이 잡히는 순간 맞춤)
            yield return null;
            if (UserAligner.HeadReady(origin)) RealignToTable();
            else PlacePanels();
        }

        /// <summary>
        /// 사용자를 테이블 앞(standDistance)으로 옮기고 테이블을 보게 한 뒤, 패널, 창고를 다시 배치.
        /// 서는 방향은 처음 한 번 '테이블 -> 씬의 XR Origin' 방향으로 정해 두고 계속 같은 쪽을 쓴다.
        /// </summary>
        public void RealignToTable()
        {
            if (alignUserToTable && TableLayout.TryGetTop(table, out var top))
            {
                var origin = UserAligner.FindOrigin();
                if (origin != null)
                {
                    if (!_approachDir.HasValue)
                    {
                        var d = origin.transform.position - top.center;
                        d.y = 0f;
                        if (d.sqrMagnitude < 0.04f) d = top.yaw * Vector3.back;
                        _approachDir = d.normalized;
                    }
                    if (UserAligner.HeadReady(origin))
                    {
                        UserAligner.Align(origin, top.center, standDistance, _approachDir.Value);
                        _aligned = true;
                    }
                }
            }
            PlacePanels();
            ApplyViewMode();
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<XRUIInputModule>();
        }


        void BuildWorld()
        {
            var root = new GameObject("Warehouse");
            _warehouse = root.AddComponent<WarehouseRenderer>();
            _playback = root.AddComponent<RobotPlayback>();
            _playback.warehouse = _warehouse;
            _pathView = root.AddComponent<PathLineView>();
            _pathView.playback = _playback;
            _heatmap = root.AddComponent<HeatmapLayer>();
            _heatmap.warehouse = _warehouse;
            _inventory = root.AddComponent<InventoryView>();
            _inventory.playback = _playback;
            _inventory.warehouse = _warehouse;
            _rackEditor = root.AddComponent<RackGrabEditor>();
            _rackEditor.warehouse = _warehouse;
            _rackEditor.OnRackMoved += OnRackMoved;
            _rackEditor.OnRejected += msg => _user.Toast(msg, ToastKind.Warning);

            _recorder = gameObject.AddComponent<VoiceRecorder>();
            _recorder.OnRecorded += OnVoiceRecorded;
            _playback.OnFinished += () =>
            {
                Log(LogLevel.Info, LogModule.Sim, $"재생 완료: 총 {_playback.Timeline.TotalSteps} 스텝");
                if (_user.Current == UserScreen.Playback) _user.Toast("재생이 끝났어요. ▶ 를 누르면 처음부터 다시 봅니다", ToastKind.Info);
                OnPlaybackFinished();
            };
        }

        void BuildUI()
        {
            _ui = new MainPanels();
            var panelRoot = new GameObject("Panels").transform;
            _ui.Build(panelRoot);
            _user = _ui.User;
            _dev = _ui.Dev;
            // 화면이 바뀌면 예약된 화면 전환, 응답 대기 시간 초과를 무효로
            _user.OnScreenChanged += _ => _screenToken++;
            BuildAdmin();
            PlacePanels();
            _report = new ReportPanel();
            _report.Build(null);
            // 리포트는 사용자 패널 자리에 대신 열린다 → 닫으면 사용자 패널을 다시 보인다 (창 겹침 방지)
            _report.OnClosed += () => { if (!_adminMode) _user.Canvas.gameObject.SetActive(true); };
            _history = new HistoryPanel();
            _history.Build(null);
            _history.OnLoad += LoadHistory;
            _history.OnRefresh += OpenHistory;

            // S01 창고 만들기
            _user.SpeakButton.onClick.AddListener(() => StartVoice(VoiceTarget.Describe));
            _user.ManualButton.onClick.AddListener(() => GoManual(VoiceTarget.Describe, _user.ManualInput.text));
            _user.ManualCancel.onClick.AddListener(() => { if (_voiceTarget == VoiceTarget.Answer) GoQuestion(); else GoCreate(); });
            _user.ManualSubmit.onClick.AddListener(SubmitManual);
            _user.HistoryButton.onClick.AddListener(OpenHistory);
            _user.RecStop.onClick.AddListener(StopVoice);
            _user.SttRetry.onClick.AddListener(() => StartVoice(_voiceTarget));
            _user.SttEdit.onClick.AddListener(() => GoManual(_voiceTarget, _lastTranscript));
            _user.SttSubmit.onClick.AddListener(() =>
            {
                if (_voiceTarget == VoiceTarget.Answer) SendAnswer(_lastTranscript);
                else RequestMapFromText(_lastTranscript);
            });

            // S02 창고 확인 / 검증 실패
            _user.RebuildButton.onClick.AddListener(GoCreate);
            _user.ConfirmButton.onClick.AddListener(ConfirmMap);
            _user.RackEditButton.onClick.AddListener(ToggleRackEdit);
            _user.QuestionSpeak.onClick.AddListener(() => StartVoice(VoiceTarget.Answer));
            _user.QuestionType.onClick.AddListener(() => GoManual(VoiceTarget.Answer, ""));

            // S03 설정
            _user.RunButton.onClick.AddListener(RunScenario);
            _user.ConfigRebuildButton.onClick.AddListener(GoCreate);
            _user.ScenarioSpeakButton.onClick.AddListener(() => StartVoice(VoiceTarget.Scenario));

            // S05 재생
            _user.PlayButton.onClick.AddListener(() => _playback.TogglePlay());
            _user.RestartButton.onClick.AddListener(() => { _playback.Timeline.Seek(0); _playback.Play(); });
            for (int i = 0; i < _user.SpeedButtons.Length; i++)
            {
                float s = _user.Speeds[i];
                _user.SpeedButtons[i].onClick.AddListener(() => { _playback.SetSpeed(s); _user.HighlightSpeed(s); });
            }
            _user.HighlightSpeed(1f);
            _user.StepSlider.onValueChanged.AddListener(v => { if (!_updatingSlider) _playback.Timeline.Seek(v); });
            _user.PathButton.onClick.AddListener(GoPathSelect);
            _user.HeatmapButton.onClick.AddListener(GoHeatmap);
            _user.SettingsButton.onClick.AddListener(GoSimConfig);
            _user.AnalyzeButton.onClick.AddListener(RequestAnalysis);
            _user.ReportButton.onClick.AddListener(OpenReport);

            // 경로, 히트맵
            _user.PathOffButton.onClick.AddListener(() => { _pathView.SetVisible(false); _user.HighlightRobot(null, false); });
            _user.PathCloseButton.onClick.AddListener(GoPlayback);
            _user.MetricWaitButton.onClick.AddListener(() => SetMetric(HeatmapMetric.Wait));
            _user.MetricPassButton.onClick.AddListener(() => SetMetric(HeatmapMetric.Pass));
            _user.HeatmapOffButton.onClick.AddListener(() => { _heatmap.SetVisible(false); GoPlayback(); });
            _user.HeatmapCloseButton.onClick.AddListener(GoPlayback);

            // S06~S08
            _user.AnalysisClose.onClick.AddListener(GoPlayback);
            _user.ApprovalCancel.onClick.AddListener(() => _user.Show(UserScreen.Analysis, 4, "병목 분석"));
            _user.CmpBeforeHeat.onClick.AddListener(() => ShowStatsHeatmap(true));
            _user.CmpAfterHeat.onClick.AddListener(() => ShowStatsHeatmap(false));
            _user.CmpPlayback.onClick.AddListener(() => { RestoreAfterStats(); GoPlayback(); });
            _user.CmpRerun.onClick.AddListener(GoSimConfig);
        }

        Transform Rig => Camera.main != null ? Camera.main.transform.root : null;

        Vector3 ViewerPosition()
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position;
            return Rig != null ? Rig.position : Vector3.zero;
        }

        /// <summary>
        /// 테이블이 있으면 테이블 오른쪽(사용자 시점)에 사용자 패널, 왼쪽에 관리자 패널. 없으면 XR Origin 기준 고정 위치.
        /// 오른손 B 버튼으로 현재 서 있는 위치 기준으로 다시 배치한다.
        /// </summary>
        void PlacePanels()
        {
            var user = _user.Canvas.transform;
            var dev = _dev.Canvas.transform;
            float userW = UserPanel.Width * user.localScale.x;
            float userH = UserPanel.FullHeight * user.localScale.y;
            float devW = DevPanel.Width * DevPanel.MetersPerPixel;
            float devH = DevPanel.Height * DevPanel.MetersPerPixel;
            if (TableLayout.TryGetTop(table, out var top))
            {
                // 사용자 패널과 관리자 패널은 같은 자리(테이블 오른쪽)에 하나만 보인다
                user.parent.SetParent(null, false);
                float floorY = Rig != null ? Rig.position.y : 0f;
                TableLayout.PlacePanels(top, ViewerPosition(), floorY, panelBottom, 0f, userW, userH, null, user);
                TableLayout.PlacePanels(top, ViewerPosition(), floorY, panelBottom, 0f, devW, devH, null, dev);
                return;
            }
            user.parent.SetParent(Rig, false);
            PlacePanel(user, userPanelOffset, userH);
            PlacePanel(dev, devPanelOffset, devH);
        }

        void PlacePanel(Transform panel, Vector3 offset, float height)
        {
            var parent = panel.parent;
            var pos = parent != null ? parent.TransformPoint(offset) : offset;
            var eye = parent != null ? parent.TransformPoint(new Vector3(0f, 1.6f, 0f)) : new Vector3(0f, 1.6f, 0f);
            TableLayout.Place(panel, pos, eye, height);
        }

        // ------------------------------------------------------------------ 상태

        void SetPhase(Phase p)
        {
            CurrentPhase = p;
            RefreshInteractable();
        }

        /// <summary>단계, 연결 상태에 따라 버튼을 켜고 끈다 (확인 전 시뮬레이션 실행 불가: FR-09).</summary>
        void RefreshInteractable()
        {
            if (_user == null) return;
            bool busy = CurrentPhase == Phase.Generating || CurrentPhase == Phase.Simulating;
            bool hasSim = !string.IsNullOrEmpty(_simId) && !_offline && Connected;
            UIFactory.SetInteractable(_user.SpeakButton, Connected && !busy);
            UIFactory.SetInteractable(_user.ManualButton, !busy);
            UIFactory.SetInteractable(_user.ConfirmButton, CurrentPhase == Phase.AwaitConfirm && Connected && !_offline);
            UIFactory.SetInteractable(_user.RebuildButton, !busy);
            UIFactory.SetInteractable(_user.RackEditButton, _warehouse.Map != null && !_offline && Connected && !busy);
            UIFactory.SetInteractable(_user.RunButton, Connected && _mapConfirmed && !busy);
            _user.Robots.Interactable = !busy;
            _user.Inbound.Interactable = !busy;
            _user.Outbound.Interactable = !busy;
            _user.SpecB.Interactable = !busy;
            UIFactory.SetInteractable(_user.ScenarioSpeakButton, Connected && _mapConfirmed && !busy && !_offline);
            UIFactory.SetInteractable(_user.ReportButton, hasSim && !busy);
            UIFactory.SetInteractable(_user.HistoryButton, Connected && !busy && !_offline);
            UIFactory.SetInteractable(_user.AnalyzeButton, hasSim && !busy);
            UIFactory.SetInteractable(_user.SettingsButton, Connected && _mapConfirmed && !_offline && !busy);
            UIFactory.SetInteractable(_user.QuestionSpeak, Connected && !busy);
            UIFactory.SetInteractable(_user.CmpRerun, Connected && _mapConfirmed && !_offline);
            _user.CreateStatus.text = Connected ? "" : _offline ? "저장된 결과를 재생하는 중이에요" : "서버 연결을 기다리는 중이에요";
        }

        // ------------------------------------------------------------------ 사용자 패널 화면 전환

        void GoCreate()
        {
            _work = Work.None;
            SetRackEditing(false);
            _voiceTarget = VoiceTarget.Describe;
            _user.Show(UserScreen.Create, 1, "창고 만들기");
            RefreshInteractable();
        }

        void GoManual(VoiceTarget target, string prefill)
        {
            _voiceTarget = target;
            bool answer = target == VoiceTarget.Answer;
            _user.ManualPrompt.text = answer ? "Agent 질문에 답해 주세요." : "창고 조건을 입력하세요.";
            _user.ManualHint.text = answer
                ? _questionText
                : "랙 줄 수 · 통로 폭 · 입하/출하 도크 수 · 충전 구역 위치를 적으면 정확해져요";
            UIFactory.SetButtonText(_user.ManualSubmit, answer ? "보내기" : "생성하기");
            _user.ManualInput.text = prefill ?? "";
            _user.Show(UserScreen.ManualInput, answer ? 2 : 1, answer ? "답변 입력" : "창고 만들기");
            _user.ManualInput.Select();
            _user.ManualInput.ActivateInputField();
        }

        void SubmitManual()
        {
            var text = _user.ManualInput.text?.Trim();
            if (string.IsNullOrEmpty(text)) { _user.Toast("내용을 입력해 주세요", ToastKind.Warning); return; }
            if (_voiceTarget == VoiceTarget.Answer) SendAnswer(text);
            else RequestMapFromText(text);
        }

        void GoQuestion()
        {
            _work = Work.None;
            _user.Show(UserScreen.Question, 2, "▲ 창고 확인이 필요합니다", UIFactory.Lighten(UIFactory.Warning, 0.2f));
            RefreshInteractable();
        }

        void GoConfirm()
        {
            _work = Work.None;
            if (_rackEditor.Editing) ToggleRackEdit();
            _user.Show(UserScreen.Confirm, 2, "창고 확인");
            RefreshInteractable();
        }

        void GoSimConfig()
        {
            _work = Work.None;
            if (_user.Current == UserScreen.Comparison) RestoreAfterStats();
            _user.Show(UserScreen.SimConfig, 3, "시뮬레이션 설정");
            RefreshInteractable();
        }

        void GoPlayback()
        {
            _work = Work.None;
            _uiStep = -1;   // 재생 화면으로 돌아오면 진행 바, 글자를 바로 갱신
            _user.Show(UserScreen.Playback, 3, _offline ? "결과 재생 (저장된 결과)" : "결과 재생");
            _user.SetToggle(_user.PathButton, _pathView.Visible, UIFactory.Accent);
            _user.SetToggle(_user.HeatmapButton, _heatmap.Visible, UIFactory.Highlight);
            RefreshInteractable();
        }

        void GoPathSelect()
        {
            var ids = _playback.Timeline.RobotIds;
            if (ids.Count == 0) { _user.Toast("재생할 로봇이 아직 없어요", ToastKind.Warning); return; }
            _user.SetRobots(ids, _playback.SelectedRobot, _pathView.Visible, id =>
            {
                _playback.SelectRobot(id);
                _pathView.SetVisible(true);
                _user.HighlightRobot(id, true);
            });
            _user.Show(UserScreen.PathSelect, 3, "경로 보기");
        }

        void GoHeatmap()
        {
            if (_heatmap.Stats == null || _heatmap.Stats.Count == 0)
            {
                _user.Toast("혼잡도 기록을 아직 받지 못했어요", ToastKind.Warning);
                return;
            }
            _heatmap.SetVisible(true);
            SetMetric(_heatmap.Metric);
            _user.Show(UserScreen.Heatmap, 3, "혼잡도 (히트맵)");
        }

        void SetMetric(HeatmapMetric m)
        {
            _heatmap.SetMetric(m);
            UIFactory.SetButtonColor(_user.MetricWaitButton, m == HeatmapMetric.Wait ? UIFactory.Primary : UIFactory.Secondary);
            UIFactory.SetButtonColor(_user.MetricPassButton, m == HeatmapMetric.Pass ? UIFactory.Primary : UIFactory.Secondary);
        }

        /// <summary>Agent 처리 화면 (진행 체크리스트). 상태 메시지(status)가 올 때마다 다음 항목으로 넘어간다.</summary>
        void ShowProcessing(Work work, int step, string title, string[] items, int active = 0)
        {
            _work = work;
            _screenToken++;
            BeginAgentRun(work, title);
            _user.SetProcessing(items);
            _user.SetProcessingIndex(active);
            _user.Show(UserScreen.Processing, step, title);
            StartCoroutine(Watchdog(_screenToken, work));
        }

        /// <summary>WebSocket 응답이 오지 않으면 진행 화면에 갇히지 않게 오류로 빠져나온다 (EX-04).</summary>
        IEnumerator Watchdog(int token, Work work)
        {
            float limit = work == Work.Stt ? 30f : work == Work.Sim || work == Work.Compare ? 150f : 75f;
            yield return new WaitForSeconds(limit);
            if (token == _screenToken && _user.Current == UserScreen.Processing && _work == work)
                ShowError("서버 응답이 늦어요. 잠시 뒤 다시 시도해 주세요", RetryFor(work));
        }

        Action RetryFor(Work work)
        {
            switch (work)
            {
                case Work.Map: return string.IsNullOrEmpty(_questionId) && !string.IsNullOrEmpty(_lastRequestText)
                    ? () => RequestMapFromText(_lastRequestText) : (Action)null;
                case Work.Sim: return _awaitingImprove || !_mapConfirmed ? null : (Action)RunScenario;
                case Work.Analysis: return RequestAnalysis;
                case Work.Compare: return RequestCompare;
                default: return null;
            }
        }

        void AdvanceProcessing(int active)
        {
            if (_user.Current != UserScreen.Processing) return;
            if (active > _user.ProcessingIndex) _user.SetProcessingIndex(Mathf.Min(active, _user.ProcessingCount));
        }

        /// <summary>체크리스트를 모두 완료로 바꾸고 잠깐 보여 준 뒤 다음 화면으로.</summary>
        /// <summary>
        /// 진행 화면이 아닐 때(시간 초과 뒤 늦게 온 응답 등)는 사용자를 끌고 가지 않고 알림 버튼만 띄운다.
        /// failed면 현재 항목을 빨간 '!'로 표시 (검증 실패 -> 질문).
        /// </summary>
        void FinishProcessing(Action next, float delay = 0.6f, bool failed = false, string lateLabel = "보기")
        {
            if (_user.Current != UserScreen.Processing)
            {
                if (lateLabel != null) _user.Toast("결과가 도착했어요", ToastKind.Info, 6f, lateLabel, next);
                return;
            }
            if (failed) _user.SetProcessingIndex(Mathf.Max(0, _user.ProcessingIndex), true);
            else
            {
                _user.SetProcessingIndex(_user.ProcessingCount);
                _user.ProcFooter.text = "완료";
            }
            StartCoroutine(AfterDelay(delay, ++_screenToken, next));
        }

        IEnumerator AfterDelay(float seconds, int token, Action next)
        {
            yield return new WaitForSeconds(seconds);
            if (token == _screenToken) next();
        }

        // ------------------------------------------------------------------ 연결 (TC-VR-11)

        /// <summary>실행 중에 다른 주소로 연결 (에디터 창 '지금 연결').</summary>
        public void ConnectTo(string host, int port)
        {
            AppConfig.Override(host, port);
            Connect();
        }

        public void Connect()
        {
            _offline = false;
            _api = new ApiClient(AppConfig.HttpBase);
            Log(LogLevel.Info, LogModule.Ws, $"연결 시도 {AppConfig.HttpBase}");
            _wsReconnects = 0;
            _user.SetConnection(ConnectionView.Connecting);
            _ws.Connect(AppConfig.WsBase + ApiRoutes.WebSocket(AppConfig.SessionId));
            Ping();
        }

        void HandleWsState(WsState s)
        {
            switch (s)
            {
                case WsState.Connected:
                    Log(LogLevel.Info, LogModule.Ws, $"WebSocket 연결됨 (세션 {AppConfig.SessionId})");
                    _user.SetConnection(_offline ? ConnectionView.Offline : ConnectionView.Connected);
                    // EX-02: 재연결 뒤 마지막 sim_id 프레임 이어 받기
                    if (!string.IsNullOrEmpty(_simId) && !_framesComplete && !_offline)
                        StartCoroutine(FetchFrames(_simId, _playback.Timeline.LoadedUntil + 1, ++_fetchGeneration));
                    break;
                case WsState.Connecting:
                    _user.SetConnection(_offline ? ConnectionView.Offline : ConnectionView.Connecting);
                    break;
                case WsState.Reconnecting:
                    _wsReconnects++;
                    Log(LogLevel.Warn, LogModule.Ws, "WebSocket 끊김 → 5초 간격 재연결");
                    _user.SetConnection(_offline ? ConnectionView.Offline : ConnectionView.Reconnecting);
                    AbortOnDisconnect();
                    break;
                default:
                    Log(LogLevel.Warn, LogModule.Ws, "WebSocket 연결 안 됨");
                    _user.SetConnection(_offline ? ConnectionView.Offline : ConnectionView.Disconnected);
                    AbortOnDisconnect();
                    break;
            }
            RefreshInteractable();
        }

        /// <summary>진행 중에 연결이 끊기면 응답(WebSocket)을 받을 수 없으므로 바로 빠져나온다.</summary>
        void AbortOnDisconnect()
        {
            if (_user.Current == UserScreen.Processing && _work != Work.None)
                ShowError("서버 연결이 끊겼어요. 다시 연결되면 한 번 더 시도해 주세요", null);
        }

        // ------------------------------------------------------------------ 요청 공통

        void Post(string path, object body, Action<ApiResult> onOk, string logText = null, Action retry = null)
        {
            if (_api == null || !Connected)
            {
                ShowError("서버에 연결되어 있지 않아요", null);
                return;
            }
            _user.HideToast();
            _txCount++;
            _lastTx = DateTime.Now;
            Log(LogLevel.Info, LogModule.Api, $"POST {path}" + (logText != null ? $" · {logText}" : ""));
            StartCoroutine(_api.PostJson(path, body, r =>
            {
                if (!r.Ok) Log(LogLevel.Error, LogModule.Api, $"POST {path} 실패: {r.ErrorMessage}", r.TimedOut ? "TIMEOUT" : $"HTTP_{r.Status}", null, r.Text);
                if (r.Ok) onOk?.Invoke(r);
                else ShowError(r.ErrorMessage, retry);
            }));
        }

        /// <summary>오류: 토스트로 알리고(다시 시도 버튼), 진행 중이던 화면에서 알맞은 이전 화면으로 돌아간다.</summary>
        void ShowError(string message, Action retry)
        {
            Log(LogLevel.Error, ModuleOf(_work), message);
            _agent.Finish(RunState.Error, message, null);
            _user.Toast(string.IsNullOrEmpty(message) ? "요청을 처리하지 못했어요" : message, ToastKind.Error, 6f,
                retry != null ? "다시 시도" : null, retry);
            var work = _work;
            _work = Work.None;
            _awaitingImprove = false;
            if (_recorder.IsRecording) { _discardRecording = true; _recorder.End(); }
            if (_editPending)
            {
                // 랙 편집이 서버에 반영되지 않았으면 서버가 가진 지도로 되돌린다
                _editPending = false;
                if (_serverMapJson != null) { try { ShowMap(GridMap.FromToken(JToken.Parse(_serverMapJson))); } catch { } }
            }
            if (CurrentPhase == Phase.Generating)
                SetPhase(!string.IsNullOrEmpty(_questionId) ? Phase.Question : _warehouse.Map != null && !string.IsNullOrEmpty(_mapVersion) ? Phase.AwaitConfirm : Phase.Idle);
            else if (CurrentPhase == Phase.Simulating)
                SetPhase(string.IsNullOrEmpty(_simId) ? Phase.Confirmed : Phase.Ready);
            RefreshInteractable();

            if (_user.Current != UserScreen.Processing && _user.Current != UserScreen.Recording) return;
            bool answering = _voiceTarget == VoiceTarget.Answer && !string.IsNullOrEmpty(_questionId);
            switch (work)
            {
                case Work.Stt:
                    if (answering) GoQuestion(); else GoCreate();
                    break;
                case Work.Map:
                    if (!string.IsNullOrEmpty(_questionId)) GoQuestion();
                    else if (CurrentPhase == Phase.AwaitConfirm && string.IsNullOrEmpty(_lastRequestText)) GoConfirm();
                    else if (!string.IsNullOrEmpty(_lastRequestText)) GoManual(VoiceTarget.Describe, _lastRequestText);
                    else GoCreate();
                    break;
                case Work.Sim:
                    if (string.IsNullOrEmpty(_simId)) GoSimConfig(); else GoPlayback();
                    break;
                case Work.Analysis:
                case Work.Compare:
                    GoPlayback();
                    break;
                default:
                    if (_voiceTarget == VoiceTarget.Answer && !string.IsNullOrEmpty(_questionId)) GoQuestion(); else GoCreate();
                    break;
            }
        }

        // ------------------------------------------------------------------ SC-01 텍스트로 창고 구성

        void RequestMapFromText(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) { _user.Toast("창고 설명이 비어 있어요", ToastKind.Warning); return; }
            if (!Connected) { _user.Toast("서버에 연결된 뒤 만들 수 있어요", ToastKind.Warning); return; }
            ResetQuestion();
            _mapConfirmed = false;
            _lastRequestText = text;
            _editPending = false;
            _user.ManualInput.text = text;
            SetPhase(Phase.Generating);
            ShowProcessing(Work.Map, 1, "창고를 만들고 있어요", MapSteps);
            Post(ApiRoutes.MapText, new { session_id = AppConfig.SessionId, text }, _ => { },
                $"창고 생성 요청: \"{text}\"", () => RequestMapFromText(text));
        }

        // ------------------------------------------------------------------ SC-02 음성 입력 (말하기 -> 녹음 종료 -> 인식 결과 확인 -> 생성)

        void StartVoice(VoiceTarget target)
        {
            if (_recorder.IsRecording) return;
            if (!Connected) { _user.Toast("서버에 연결된 뒤 말할 수 있어요", ToastKind.Warning); return; }
            if (CurrentPhase == Phase.Generating || CurrentPhase == Phase.Simulating) return;
            _voiceTarget = target;
            _recorder.Begin();
            if (!_recorder.IsRecording) return;   // 마이크 없음 등 -> OnVoiceRecorded(null, 이유)
            bool answer = target == VoiceTarget.Answer;
            _user.RecTitle.text = "듣고 있습니다";
            _user.RecTimer.text = "00:00";
            if (target == VoiceTarget.Scenario)
            {
                _user.RecHint.text = "\"로봇 6대, 입하 30건 출하 20건, 1200 규격 30%, 실행해줘\"";
                _user.Show(UserScreen.Recording, 3, "시뮬레이션 설정");
                return;
            }
            _user.RecHint.text = answer ? $"질문: {_questionText}" : "\"랙 8줄, 통로 폭 3m, 입하 도크 2개 …\"";
            _user.Show(UserScreen.Recording, answer ? 2 : 1, answer ? "답변하기" : "창고 만들기");
        }

        void StopVoice()
        {
            if (!_recorder.IsRecording) return;
            _recorder.End();
        }

        void OnVoiceRecorded(byte[] wav, string error)
        {
            if (_discardRecording) { _discardRecording = false; return; }
            bool answer = _voiceTarget == VoiceTarget.Answer && !string.IsNullOrEmpty(_questionId);
            if (_voiceTarget == VoiceTarget.Scenario)
            {
                if (wav == null) { _user.Toast(error, ToastKind.Warning); GoSimConfig(); return; }
                // 시나리오 음성: 지금 패널 값을 같이 보내 말한 항목만 바뀌게 한다 -> WS transcript, scenario
                ShowProcessing(Work.Stt, 3, "시뮬레이션 조건을 듣고 있어요", SttSteps, 1);
                var fields = new Dictionary<string, string>
                {
                    { "session_id", AppConfig.SessionId }, { "map_version", _mapVersion },
                    { "robots", _user.Robots.Value.ToString() }, { "inbound", _user.Inbound.Value.ToString() },
                    { "outbound", _user.Outbound.Value.ToString() }, { "spec_b_pct", _user.SpecB.Value.ToString() },
                };
                StartCoroutine(_api.PostAudioForm(ApiRoutes.ScenarioVoice, fields, wav, r =>
                {
                    if (!r.Ok) ShowError(r.ErrorMessage, null);
                }));
                return;
            }
            if (wav == null)
            {
                _user.Toast(error, ToastKind.Warning);   // EX-03
                if (answer) GoQuestion(); else if (_user.Current == UserScreen.Recording) GoCreate();
                return;
            }
            ShowProcessing(Work.Stt, answer ? 2 : 1, "음성을 인식하고 있어요", SttSteps, 1);
            Log(LogLevel.Info, LogModule.Vr, $"음성 전송 ({wav.Length / 1024} KB, stt_only)");
            StartCoroutine(_api.PostAudio(ApiRoutes.MapVoice, AppConfig.SessionId, wav, r =>
            {
                if (!r.Ok) ShowError(r.ErrorMessage, null);
            }, answer ? _questionId : null, sttOnly: true));
        }

        void OnTranscript(ServerMessage msg)
        {
            var text = msg.GetString("text");
            if (_voiceTarget == VoiceTarget.Scenario)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _work = Work.None;
                    _user.Toast("잘 못 들었어요. 다시 말하거나 -/+ 로 정해 주세요", ToastKind.Warning);
                    GoSimConfig();
                    return;
                }
                Log(LogLevel.Info, LogModule.Agent, $"시나리오 음성: \"{text.Trim()}\"");
                AdvanceProcessing(2);
                return;   // 결과는 WS scenario 로 온다 (OnScenario)
            }
            bool answer = _voiceTarget == VoiceTarget.Answer && !string.IsNullOrEmpty(_questionId);
            if (string.IsNullOrWhiteSpace(text))
            {
                _work = Work.None;
                _user.Toast("잘 못 들었어요. 다시 말하거나 직접 입력해 주세요", ToastKind.Warning);   // EX-03
                if (answer) GoQuestion(); else GoCreate();
                return;
            }
            _lastTranscript = text.Trim();
            Log(LogLevel.Info, LogModule.Agent, $"STT 인식: \"{_lastTranscript}\"", null, null, msg.Raw);
            var flag = msg.Get("stt_only");
            bool sttOnly = flag != null && flag.Type == JTokenType.Boolean && (bool)flag;
            if (sttOnly)
            {
                _agent.Finish(RunState.Completed, "transcript (stt_only)", msg.Raw);
                // 인식 결과를 보여 주고 사용자가 '생성하기'를 눌러야 만든다 (STT 오류 확인)
                FinishProcessing(() =>
                {
                    _user.SttLabel.text = answer ? "인식한 답변" : "인식한 내용";
                    _user.SttText.text = $"“{_lastTranscript}”";
                    UIFactory.SetButtonText(_user.SttSubmit, answer ? "답변 보내기" : "생성하기");
                    _user.Show(UserScreen.SttResult, answer ? 2 : 1, "입력 내용을 확인하세요");
                }, 0.3f);
            }
            else
            {
                // 서버가 stt_only를 모르면 인식과 동시에 생성까지 진행한다 -> 바로 생성 진행 화면
                _mapConfirmed = false;
                if (!answer) ResetQuestion();
                SetPhase(Phase.Generating);
                ShowProcessing(Work.Map, answer ? 2 : 1, answer ? "답변을 반영하고 있어요" : "창고를 만들고 있어요", MapSteps, 1);
            }
        }

        // ------------------------------------------------------------------ SC-03 수정 질문 답변

        void SendAnswer(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_questionId)) { GoQuestion(); return; }
            var qid = _questionId;
            _lastAnswerText = text;
            SetPhase(Phase.Generating);
            ShowProcessing(Work.Map, 2, "답변을 반영하고 있어요", MapSteps);
            Post(ApiRoutes.MapAnswer, new { session_id = AppConfig.SessionId, question_id = qid, text },
                _ => _user.ManualInput.text = "", $"답변: {text}", () => SendAnswer(text));
        }

        void ResetQuestion()
        {
            _questionId = null;
            _questionText = null;
            _questionRounds = 0;
            if (_warehouse.Map != null) _warehouse.ClearHighlight();
        }

        // ------------------------------------------------------------------ 지도 확인 (FR-09)

        void ConfirmMap()
        {
            if (string.IsNullOrEmpty(_mapVersion)) return;
            SetRackEditing(false);
            UIFactory.SetInteractable(_user.ConfirmButton, false);
            Post(ApiRoutes.MapConfirm, new { session_id = AppConfig.SessionId, map_version = _mapVersion }, r =>
            {
                _mapConfirmed = true;
                Log(LogLevel.Info, LogModule.Api, $"지도 v{_mapVersion} 확정");
                SetPhase(Phase.Confirmed);
                _user.Toast("창고를 확정했어요. 시뮬레이션 조건을 정해 주세요", ToastKind.Success, 3f);
                GoSimConfig();
            }, null, ConfirmMap);
        }

        // ------------------------------------------------------------------ SC-04 시나리오 실행

        void RunScenario()
        {
            if (!_mapConfirmed) { _user.Toast("창고를 먼저 확인해 주세요", ToastKind.Warning); return; }
            SetPhase(Phase.Simulating);
            ShowProcessing(Work.Sim, 3, "시뮬레이션 실행 중", SimSteps);
            var body = new
            {
                session_id = AppConfig.SessionId,
                map_version = _mapVersion,
                robots = _user.Robots.Value,
                inbound = _user.Inbound.Value,
                outbound = _user.Outbound.Value,
                spec_b_pct = _user.SpecB.Value,
                strategy = "optimized"
            };
            Post(ApiRoutes.Scenario, body, r =>
            {
                var id = (string)(r.Json as JObject)?["sim_id"];
                if (!string.IsNullOrEmpty(id)) Log(LogLevel.Info, LogModule.Sim, $"시뮬레이션 요청됨 (sim {id})");
                AdvanceProcessing(1);
            }, $"시뮬레이션 요청: 로봇 {body.robots}대, 입하 {body.inbound}, 출하 {body.outbound}", RunScenario);
        }

        void RequestCompare()
        {
            if (string.IsNullOrEmpty(_simId)) return;
            ShowProcessing(Work.Compare, 4, "기준 전략과 비교 중", CompareSteps);
            Post(ApiRoutes.Compare, new { session_id = AppConfig.SessionId, sim_id = _simId }, null,
                "기준 전략(무작위 보관·선착순·개별 최단 경로)과 비교 요청", RequestCompare);
        }

        void RequestAnalysis()
        {
            if (string.IsNullOrEmpty(_simId)) return;
            _playback.Pause();
            ShowProcessing(Work.Analysis, 4, "병목을 분석하고 있어요", AnalysisSteps);
            Post(ApiRoutes.Analyze, new { session_id = AppConfig.SessionId, sim_id = _simId }, null,
                "병목 분석 요청", RequestAnalysis);
        }

        // SC-08 롤링 재계획 (관리자 패널)
        void SendSimEvent(int addOrders, int robots)
        {
            int t = _playback.Timeline.CurrentStep;
            Post(ApiRoutes.SimEvent(_simId), new { session_id = AppConfig.SessionId, t, add_orders = addOrders, robots },
                null, $"재계획 요청: 스텝 {t}, 주문 +{addOrders}, 로봇 {robots}대");
        }

        // SC-10 개선안 선택 -> 승인 화면 -> 적용
        void GoApproval(Proposal p)
        {
            _user.ApprovalText.text = $"“{p.text}”";
            var effects = p.effects != null && p.effects.Count > 0 ? p.effects : DefaultEffects(p.type);
            _user.ApprovalEffects.text = string.Join("\n", effects.Select(e => "•  " + e));
            _user.ApprovalApply.onClick.RemoveAllListeners();
            _user.ApprovalApply.onClick.AddListener(() => ApproveProposal(p));
            _user.Show(UserScreen.Approval, 4, "개선안을 적용할까요?");
        }

        static List<string> DefaultEffects(string type)
        {
            switch (type)
            {
                case "dock_add": return new List<string> { "도크 앞 대기 줄 분산", "도크 왕복 거리 감소" };
                case "robot_count": return new List<string> { "통로 혼잡 완화" };
                case "storage": case "relocate": return new List<string> { "특정 구역 집중 완화", "로봇 경로 분산" };
                default: return new List<string> { "대기 감소 기대 (적용 후 수치로 확인)" };
            }
        }

        void ApproveProposal(Proposal p)
        {
            _beforeImprove = (_playback.Timeline.TotalSteps, _heatmap.TotalWait());
            _statsBefore = _heatmap.Stats != null && _heatmap.Stats.Count > 0 ? new List<CellStat>(_heatmap.Stats) : null;
            _statsAfter = null;
            _awaitingImprove = true;
            SetPhase(Phase.Simulating);
            ShowProcessing(Work.Sim, 4, "개선안을 적용하고 있어요", ImproveSteps);
            Post(ApiRoutes.ImproveApprove, new { session_id = AppConfig.SessionId, proposal_id = p.proposalId }, r =>
            {
                Log(LogLevel.Info, LogModule.Agent, $"개선안 적용: {p.text} → 재시뮬레이션");
                // 로봇 대수 개선안이면 설정 화면 값도 맞춘다 ("로봇 6대 → 4대" 형식, 가정)
                var m = Regex.Match(p.text ?? "", @"→\s*(\d+)\s*대");
                if (p.type == "robot_count" && m.Success) _user.Robots.Value = int.Parse(m.Groups[1].Value);
            }, null, () => ApproveProposal(p));
        }

        // SC-11 랙 편집
        void ToggleRackEdit()
        {
            SetRackEditing(!_rackEditor.Editing);
            if (_rackEditor.Editing) _user.Toast("컨트롤러로 랙을 잡아 다른 통로 칸에 놓으세요", ToastKind.Info, 5f);
        }

        void SetRackEditing(bool on)
        {
            if (_rackEditor.Editing != on) _rackEditor.SetEditing(on);
            UIFactory.SetButtonText(_user.RackEditButton, on ? "랙 옮기기: 켬" : "랙 옮기기: 끔");
            _user.SetToggle(_user.RackEditButton, on, UIFactory.Accent);
        }

        void OnRackMoved(string rackId, Vector2Int from, Vector2Int to)
        {
            SetRackEditing(false);   // 재검증 중 두 번째 이동 방지 (지도 버전이 바뀌기 전)
            _editPending = true;
            _mapConfirmed = false;
            SetPhase(Phase.Generating);
            ShowProcessing(Work.Map, 2, "배치를 다시 검증하고 있어요", MapSteps, 1);
            var moves = new[] { new { rack_id = rackId, from = new[] { from.x, from.y }, to = new[] { to.x, to.y } } };
            Post(ApiRoutes.MapEdit, new { session_id = AppConfig.SessionId, map_version = _mapVersion, moves },
                null, $"랙 {rackId} ({from.x},{from.y}) → ({to.x},{to.y}) 이동, 다시 검증");
        }

        // ------------------------------------------------------------------ WebSocket 이벤트

        void HandleWsMessage(string raw)
        {
            _rxCount++;
            _lastRx = DateTime.Now;
            ServerMessage msg;
            try { msg = ServerMessage.Parse(raw); }
            catch (Exception e) { Log(LogLevel.Error, LogModule.Ws, $"JSON 파싱 실패: {e.Message}", "PARSE_ERROR", null, raw); return; }

            switch (msg.Type)
            {
                case ServerMessage.Transcript: OnTranscript(msg); break;
                case ServerMessage.MapReady: OnMapReady(msg); break;
                case ServerMessage.Question: OnQuestion(msg); break;
                case ServerMessage.SimReady: OnSimReady(msg); break;
                case ServerMessage.Compare: OnCompare(msg); break;
                case ServerMessage.Analysis: OnAnalysis(msg); break;
                case ServerMessage.Error: OnServerError(msg); break;
                case ServerMessage.Status: OnStatus(msg); break;
                case ServerMessage.Scenario: OnScenario(msg); break;
                case ServerMessage.Report: OnReport(msg); break;
                default: Log(LogLevel.Warn, LogModule.Ws, $"알 수 없는 type: {msg.Type}", "UNKNOWN_TYPE", null, msg.Raw); break;
            }
        }

        void OnServerError(ServerMessage msg)
        {
            var code = msg.GetString("code");
            var text = msg.GetString("message");
            Log(LogLevel.Error, ModuleOfCode(code), text, code, ErrorCell(msg), msg.Raw);
            _agent.Finish(RunState.Error, $"{code} {text}", msg.Raw);
            if (code == "REPORT_ERROR") _showReportWhenReady = false;   // 오류 알림은 아래 기본 처리(토스트)
            switch (code)
            {
                case "STT_EMPTY":
                    // 보통 빈 transcript가 먼저 와서 이미 안내했지만, 오류만 오는 서버도 있으므로
                    if (_user.Current == UserScreen.Processing && _work == Work.Stt)
                    {
                        _work = Work.None;
                        _user.Toast("잘 못 들었어요. 다시 말하거나 직접 입력해 주세요", ToastKind.Warning);
                        if (_voiceTarget == VoiceTarget.Answer && !string.IsNullOrEmpty(_questionId)) GoQuestion(); else GoCreate();
                    }
                    return;
                case "QUESTION_LIMIT":
                    _work = Work.None;
                    ResetQuestion();
                    SetPhase(_warehouse.Map != null && !string.IsNullOrEmpty(_mapVersion) ? Phase.AwaitConfirm : Phase.Idle);
                    _user.Toast("질문이 여러 번 반복됐어요. 창고 설명을 처음부터 다시 해 주세요", ToastKind.Warning, 6f);
                    GoCreate();
                    return;
                default:
                    ShowError(string.IsNullOrEmpty(text) ? code : text, null);
                    return;
            }
        }

        /// <summary>
        /// Agent 진행 상태(status.node)를 체크리스트 단계로 바꾼다. 노드 이름은 서버마다 다를 수 있어
        /// 아는 낱말이 있으면 그 단계로, 모르면 한 칸씩 넘긴다.
        /// </summary>
        void OnStatus(ServerMessage msg)
        {
            var node = msg.GetString("node");
            var text = msg.GetString("message", node);
            Log(LogLevel.Debug, LogModule.Agent, $"[{node}] {text}", null, null, msg.Raw);
            _agent.Node(node, text, msg.GetString("tool", null), msg.Raw);
            if (_user.Current != UserScreen.Processing) return;
            string n = (node ?? "").ToLowerInvariant();
            int next = _user.ProcessingIndex + 1;
            switch (_work)
            {
                case Work.Stt:
                    if (Has(n, "stt", "음성", "인식")) next = 1;
                    break;
                case Work.Map:
                    if (Has(n, "해석", "요구", "interpret", "parse", "extract", "stt")) next = 1;
                    else if (Has(n, "생성", "generat", "build")) next = 2;
                    else if (Has(n, "검증", "valid")) next = 3;
                    break;
                case Work.Sim:
                    if (Has(n, "할당", "assign", "수정", "edit", "적용", "apply")) next = 1;
                    else if (Has(n, "시뮬레이션", "simul", "경로", "path", "plan")) next = Mathf.Max(1, _user.ProcessingIndex);
                    else if (Has(n, "로그", "log", "충돌", "collision", "검사")) next = 3;
                    if (Has(text ?? "", "충돌 0건")) _user.ProcDetail.text = "충돌 0건";
                    break;
                case Work.Analysis:
                    if (Has(n, "로그", "log", "집계")) next = 1;
                    else if (Has(n, "개선", "propos")) next = 2;
                    break;
                case Work.Compare:
                    if (Has(n, "비교", "compare", "baseline")) next = 1;
                    break;
                default:
                    return;
            }
            AdvanceProcessing(Mathf.Min(next, _user.ProcessingCount - 1));
        }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void OnMapReady(ServerMessage msg)
        {
            GridMap map;
            try { map = GridMap.FromToken(msg.Get("map")); }
            catch (Exception e) { ShowError("지도 데이터를 읽지 못했어요: " + e.Message, null); return; }

            if (map.schemaVersion != GridMap.SupportedSchemaVersion)
                Log(LogLevel.Warn, LogModule.Validator, $"스키마 버전 불일치: 서버 {map.schemaVersion}, VR {GridMap.SupportedSchemaVersion}", "SCHEMA_VERSION");

            _mapVersion = msg.GetString("map_version");
            _serverMapJson = msg.Get("map")?.ToString();
            _editPending = false;
            // 개선안 승인(SC-10)으로 서버가 바로 확정한 지도는 confirmed=true로 온다
            var confirmedToken = msg.Get("confirmed");
            _mapConfirmed = confirmedToken != null && confirmedToken.Type == JTokenType.Boolean && (bool)confirmedToken;
            ResetQuestion();
            ShowMap(map);
            ResetSimulation();

            var summary = SimParsers.Describe(msg.Get("summary"));
            Log(LogLevel.Info, LogModule.Agent, $"map_ready v{_mapVersion}: {map.width}×{map.height}, 랙 {map.CountCells(CellType.Rack)}칸. {summary}", null, null, msg.Raw);
            _agent.Finish(RunState.Completed, $"map_ready v{_mapVersion}", msg.Raw);
            var defaults = msg.GetStringList("defaults_applied");

            _user.SetConfirmSummary(MapChips(map));
            _user.ConfirmNote.text = defaults.Count > 0 ? $"말씀하지 않은 항목은 기본값으로 정했어요: {string.Join(", ", defaults)}" : "";  // SC-12
            ApplyScenarioDefaults(msg.Get("scenario_defaults"));

            if (_mapConfirmed)
            {
                SetPhase(Phase.Confirmed);
                if (_awaitingImprove || _loadingHistory) AdvanceProcessing(1);   // 개선안 적용, 기록 불러오기: 다음은 재생 준비
                else GoSimConfig();
            }
            else
            {
                SetPhase(Phase.AwaitConfirm);
                FinishProcessing(GoConfirm);
            }
        }

        /// <summary>S02 요약: 크기, 랙, 입하/출하 도크, 충전 구역.</summary>
        static List<(string, string)> MapChips(GridMap map)
        {
            float cs = map.cellSizeM;
            int rackCells = map.CountCells(CellType.Rack);
            int racks = map.racks != null ? map.racks.Count : 0;
            int charge = map.CountCells(CellType.Charge);
            var chips = new List<(string, string)>
            {
                ("크기", $"{map.width * cs:0.#} × {map.height * cs:0.#} m"),
                ("랙", racks > 0 ? $"{racks}개 · {rackCells}칸" : $"{rackCells}칸"),
                ("입하 도크", $"{map.CountCells(CellType.DockIn)}개"),
                ("출하 도크", $"{map.CountCells(CellType.DockOut)}개"),
                ("충전", charge > 0 ? $"{ChargeSide(map)} · {charge}칸" : "없음"),
            };
            return chips;
        }

        static string ChargeSide(GridMap map)
        {
            float sum = 0f;
            int n = 0;
            foreach (var c in map.cells)
                if (CellTypeNames.Parse(c.type) == CellType.Charge) { sum += c.x; n++; }
            if (n == 0 || map.width <= 0) return "";
            float u = sum / n / map.width;
            return u > 0.66f ? "오른쪽" : u < 0.33f ? "왼쪽" : "가운데";
        }

        void OnQuestion(ServerMessage msg)
        {
            // 질문에 지도가 같이 오면(검증 실패 지도) 먼저 그려서 문제 칸을 보이게 한다.
            var mapToken = msg.Get("map");
            if (mapToken != null && mapToken.Type == JTokenType.Object)
            {
                try { ShowMap(GridMap.FromToken(mapToken)); ResetSimulation(); } catch { }
            }
            _editPending = false;
            _questionId = msg.GetString("question_id");
            _questionText = msg.GetString("text");
            _questionRounds++;
            var cells = msg.GetCells("error_cells");
            if (_warehouse.Map != null) _warehouse.SetHighlight(cells);
            var codes = msg.GetStringList("errors");
            Log(LogLevel.Warn, LogModule.Validator, $"검증 실패 → Agent 질문 ({_questionRounds}/{AppConfig.MaxQuestionRounds}): {_questionText}",
                codes.Count > 0 ? string.Join(",", codes) : "VALIDATION", cells.Count > 0 ? new Vector2Int(cells[0].x, cells[0].y) : (Vector2Int?)null, msg.Raw);
            _agent.Finish(RunState.Question, "question: " + (codes.Count > 0 ? string.Join(",", codes) : _questionText), msg.Raw);

            _user.QuestionProblem.text = cells.Count > 0 ? $"<color={UIFactory.Hex(UIFactory.Danger)}>■</color>  문제 칸 {cells.Count}개를 창고에 빨간색으로 표시했어요" : "";
            _user.QuestionText.text = _questionText;
            _user.QuestionRound.text = $"질문 {_questionRounds} / {AppConfig.MaxQuestionRounds}";
            _user.ClearOptions();
            foreach (var (label, text) in ParseOptions(msg.Get("options")))
            {
                var t = text;
                _user.AddOption(label, () => SendAnswer(t));
            }
            _user.EndOptions();
            SetPhase(Phase.Question);
            FinishProcessing(GoQuestion, 0.7f, failed: true);
        }

        /// <summary>질문 선택지: [{label, text}] 또는 ["2m", "3m"] 둘 다 받는다 (최대 3개).</summary>
        static List<(string label, string text)> ParseOptions(JToken token)
        {
            var list = new List<(string, string)>();
            if (!(token is JArray arr)) return list;
            foreach (var item in arr)
            {
                if (list.Count >= 3) break;
                if (item.Type == JTokenType.String) { var s = item.ToString(); list.Add((s, s)); }
                else if (item is JObject o)
                {
                    var label = (string)o["label"] ?? (string)o["text"];
                    var text = (string)o["text"] ?? label;
                    if (!string.IsNullOrEmpty(label)) list.Add((label, text));
                }
            }
            return list;
        }

        void OnSimReady(ServerMessage msg)
        {
            var simId = msg.GetString("sim_id");
            int total = msg.GetInt("total_steps");
            var strategy = msg.GetString("strategy", "optimized");
            int replanFrom = msg.GetInt("replan_from", -1);

            if (replanFrom >= 0 && simId == _simId && _playback.Timeline.FrameCount > 0)
            {
                // SC-08: 변경 시점 이후만 새 결과로 교체
                _playback.Timeline.DropFrom(replanFrom);
                _playback.Timeline.TotalSteps = total;
                _framesComplete = false;
                Log(LogLevel.Info, LogModule.Sim, $"재계획 완료: 스텝 {replanFrom} 이후 갱신 (총 {total} 스텝, {msg.GetString("replan_ms", "?")}ms)", null, null, msg.Raw);
                _user.Toast($"스텝 {replanFrom} 이후 경로를 다시 계산했어요", ToastKind.Info);
                StartCoroutine(FetchFrames(simId, replanFrom, ++_fetchGeneration));
                StartCoroutine(FetchStats(simId));
                RequestReport(simId);   // 이벤트가 바뀌었으니 리포트도 다시
                return;
            }

            _simId = simId;
            _offline = false;
            if (msg.Get("scenario") is JObject hsc)                  // 기록 불러오기: 그때의 시나리오 값을 패널에
            {
                _user.Robots.Value = (int?)hsc["robots"] ?? _user.Robots.Value;
                _user.Inbound.Value = (int?)hsc["inbound"] ?? _user.Inbound.Value;
                _user.Outbound.Value = (int?)hsc["outbound"] ?? _user.Outbound.Value;
                _user.SpecB.Value = (int?)hsc["spec_b_pct"] ?? 0;
                _mapConfirmed = true;
            }
            _loadingHistory = false;
            if (!_awaitingImprove) { _statsBefore = null; _statsAfter = null; }
            _playback.Begin(total);
            _framesComplete = false;
            _warehouse.ClearMarkers();
            _proposals.Clear();
            _simSummary = SummaryLine(msg.Get("summary"));
            Log(LogLevel.Info, LogModule.Sim, $"sim_ready ({strategy}) 총 {total} 스텝. {SimParsers.Describe(msg.Get("summary"))}", null, null, msg.Raw);
            _agent.Finish(RunState.Completed, $"sim_ready {simId} · {total} 스텝", msg.Raw);
            _simStrategy = strategy;
            SetPhase(Phase.Ready);
            StartCoroutine(FetchFrames(simId, 0, ++_fetchGeneration));
            StartCoroutine(FetchStats(simId));
            if (_awaitingImprove) AdvanceProcessing(3);   // 통계를 받으면 비교 화면 (FetchStats)
            else FinishProcessing(GoPlayback);
            RequestReport(simId);   // 재생하는 동안 서버가 리포트(기준 전략 비교 포함)를 미리 계산
        }

        // ------------------------------------------------------------------ 권장 시나리오, 시나리오 음성

        /// <summary>지도 크기 기준 권장 시나리오를 설정 화면 값으로 (사용자가 다시 조정 가능).</summary>
        void ApplyScenarioDefaults(JToken token)
        {
            if (!(token is JObject o) || _awaitingImprove) return;
            _user.Robots.Value = (int?)o["robots"] ?? _user.Robots.Value;
            _user.Inbound.Value = (int?)o["inbound"] ?? _user.Inbound.Value;
            _user.Outbound.Value = (int?)o["outbound"] ?? _user.Outbound.Value;
            _user.SpecB.Value = (int?)o["spec_b_pct"] ?? 0;
            Log(LogLevel.Info, LogModule.Sim, $"권장 시나리오: 로봇 {_user.Robots.Value}대 ({(string)o["basis"]})");
        }

        /// <summary>시나리오 음성 해석 결과 -> 설정 화면 값. run 이면 바로 실행.</summary>
        void OnScenario(ServerMessage msg)
        {
            _user.Robots.Value = msg.GetInt("robots", _user.Robots.Value);
            _user.Inbound.Value = msg.GetInt("inbound", _user.Inbound.Value);
            _user.Outbound.Value = msg.GetInt("outbound", _user.Outbound.Value);
            _user.SpecB.Value = msg.GetInt("spec_b_pct", _user.SpecB.Value);
            var run = msg.Get("run");
            bool runNow = run != null && run.Type == JTokenType.Boolean && (bool)run;
            _voiceTarget = VoiceTarget.Describe;
            _work = Work.None;
            if (runNow && _mapConfirmed) { RunScenario(); return; }
            GoSimConfig();
            _user.Toast($"로봇 {_user.Robots.Value}대, 입하 {_user.Inbound.Value} · 출하 {_user.Outbound.Value}" +
                        (_user.SpecB.Value > 0 ? $", 1200 규격 {_user.SpecB.Value}%" : "") + "로 맞췄어요. -/+ 로 조정할 수 있어요",
                ToastKind.Success);
        }

        // ------------------------------------------------------------------ 최종 리포트

        void RequestReport(string simId)
        {
            if (_api == null || _offline || string.IsNullOrEmpty(simId)) return;
            if (_reportSimId == simId) { _lastReport = null; _reportShownFor = null; }
            StartCoroutine(_api.PostJson(ApiRoutes.Report, new { session_id = AppConfig.SessionId, sim_id = simId }, r =>
            {
                if (r.Ok) return;
                Log(LogLevel.Warn, LogModule.Api, $"POST /report 실패: {r.ErrorMessage}", r.TimedOut ? "TIMEOUT" : $"HTTP_{r.Status}", null, r.Text);
                // 사용자가 리포트를 기다리는 중이면 알린다 (예: /report 가 없는 예전 모의 서버 → 404)
                if (_showReportWhenReady && simId == _simId)
                {
                    _showReportWhenReady = false;
                    _user.Toast($"리포트를 만들지 못했어요 ({r.ErrorMessage})", ToastKind.Error, 6f, "다시 시도", OpenReport);
                }
            }));
        }

        int _reportWaitToken;

        /// <summary>리포트를 기다리는데 일정 시간 안에 WS report 가 오지 않으면 알리고 대기를 푼다.</summary>
        System.Collections.IEnumerator ReportTimeout(int token, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (token != _reportWaitToken || !_showReportWhenReady) yield break;
            _showReportWhenReady = false;
            Log(LogLevel.Warn, LogModule.Api, $"리포트 응답 없음 ({seconds:0}초)", "REPORT_TIMEOUT");
            _user.Toast("리포트 응답이 없어요. 서버 연결과 로그를 확인해 주세요", ToastKind.Warning, 6f, "다시 시도", OpenReport);
        }

        void OnReport(ServerMessage msg)
        {
            _lastReport = msg.Root;
            _reportSimId = msg.GetString("sim_id");
            if (_reportSimId != _simId) return;
            Log(LogLevel.Info, LogModule.Sim, "최종 리포트 준비됨");
            if (_showReportWhenReady) { _showReportWhenReady = false; ShowReport(); }
        }

        void OnPlaybackFinished()
        {
            if (_offline || string.IsNullOrEmpty(_simId) || _reportShownFor == _simId) return;
            if (_lastReport != null && _reportSimId == _simId) ShowReport();
            else _showReportWhenReady = true;   // 리포트가 도착하면 바로 표시
        }

        void OpenReport()
        {
            if (string.IsNullOrEmpty(_simId)) return;
            if (_lastReport != null && _reportSimId == _simId) { ShowReport(); return; }
            _showReportWhenReady = true;
            _user.Toast("최종 리포트를 만들고 있어요 (기준 전략 비교 포함)", ToastKind.Info);
            StartCoroutine(ReportTimeout(++_reportWaitToken, 40f));
            RequestReport(_simId);
        }

        /// <summary>visionOS 창처럼 사용자 정면 1.6m, 눈높이에 띄운다.</summary>
        static void PlaceInFront(Canvas canvas)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                var t = canvas.transform;
                t.SetParent(null, true);
                t.position = cam.transform.position + fwd * 1.6f + Vector3.down * 0.05f;
                t.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }
            XRSupport.RefreshRaycasters(canvas);
        }

        void ShowReport()
        {
            if (_lastReport == null) return;
            if (_history.Visible) _history.Hide();
            _report.Show(_lastReport, AppConfig.HttpBase);
            PlaceAtUserPanel(_report.Canvas);
            _user.Canvas.gameObject.SetActive(false);
            _reportShownFor = _reportSimId;
        }

        /// <summary>사용자 패널과 같은 자리·방향 (둘 다 아래 가장자리 기준)에 놓는다.</summary>
        void PlaceAtUserPanel(Canvas canvas)
        {
            var u = _user.Canvas.transform;
            var t = canvas.transform;
            t.SetParent(u.parent, false);
            t.position = u.position;
            t.rotation = u.rotation;
            XRSupport.RefreshRaycasters(canvas);
        }

        // ------------------------------------------------------------------ 시뮬레이션 기록 (SQLite)

        void OpenHistory()
        {
            if (_api == null || _offline) return;
            if (_report.Visible) _report.Hide();
            _history.ShowLoading();
            PlaceInFront(_history.Canvas);
            StartCoroutine(_api.Get(ApiRoutes.Sims + "?limit=30", r =>
            {
                if (!r.Ok) { _history.Hide(); ShowError("기록을 불러오지 못했어요: " + r.ErrorMessage, OpenHistory); return; }
                _history.Show((r.Json as JObject)?["sims"] as JArray, _simId, AppConfig.HttpBase);
            }));
        }

        void LoadHistory(string simId)
        {
            _loadingHistory = true;
            ShowProcessing(Work.Sim, 3, "기록을 불러오고 있어요", HistorySteps);
            Post(ApiRoutes.SimLoad(simId), new { session_id = AppConfig.SessionId }, _ => { },
                $"기록 {simId} 불러오기", () => LoadHistory(simId));
        }

        /// <summary>재생 화면 위 한 줄 요약 (서버 summary에서 아는 항목만).</summary>
        static string SummaryLine(JToken summary)
        {
            if (!(summary is JObject o)) return "";
            var parts = new List<string>();
            var done = o["완료 주문"] ?? o["orders_done"];
            if (done != null) parts.Add(done.ToString().Contains("/") ? $"완료 주문 {done}" : $"완료 주문 {done}건");
            var wait = o["대기 합계"] ?? o["wait_total"];
            if (wait != null) parts.Add($"대기 {wait}회");
            var unfinished = o["처리 못 함"];   // 서버가 주문을 다 처리하지 못했을 때 원인 문장 (docs/api.md)
            if (unfinished != null) parts.Add(unfinished.ToString());
            parts.Add("충돌 0건");
            return string.Join("  ·  ", parts);
        }

        IEnumerator FetchFrames(string simId, int from, int generation)
        {
            int total = _playback.Timeline.TotalSteps;
            bool first = from == 0;
            int failures = 0;
            while (from <= total && generation == _fetchGeneration && simId == _simId)
            {
                int to = Mathf.Min(from + AppConfig.FrameChunkSize - 1, total);
                ApiResult result = null;
                yield return _api.Get(ApiRoutes.SimFrames(simId, from, to), r => result = r);
                if (generation != _fetchGeneration) yield break;
                if (result == null || !result.Ok)
                {
                    if (++failures > 5) { ShowError("프레임을 받지 못했어요: " + result?.ErrorMessage, null); yield break; }
                    yield return new WaitForSeconds(1f);
                    continue;
                }
                failures = 0;
                List<SimFrame> frames;
                try { frames = SimParsers.ParseFrames(result.Text); }
                catch (Exception e) { ShowError("프레임 형식 오류: " + e.Message, null); yield break; }
                _playback.Timeline.AddFrames(frames);
                total = _playback.Timeline.TotalSteps;
                if (first)
                {
                    first = false;
                    _playback.ApplyPositions();
                    _playback.Play();   // 첫 구간을 받으면 바로 재생
                }
                from = to + 1;
                if (frames.Count == 0) break;
            }
            if (generation == _fetchGeneration)
            {
                _framesComplete = true;
                // total_steps가 마지막 t보다 크게 온 경우 받은 프레임 끝을 재생 끝으로 맞춘다
                var tl = _playback.Timeline;
                if (tl.LoadedUntil >= 0 && tl.LoadedUntil < tl.TotalSteps) tl.TotalSteps = tl.LoadedUntil;
            }
        }

        IEnumerator FetchStats(string simId)
        {
            ApiResult result = null;
            yield return _api.Get(ApiRoutes.SimStats(simId), r => result = r);
            List<CellStat> stats = null;
            if (result != null && result.Ok)
            {
                try { stats = SimParsers.ParseStats(result.Text); }
                catch (Exception e) { Debug.LogWarning("[stats] " + e.Message); }
            }
            if (stats != null) _heatmap.SetStats(stats);

            if (_awaitingImprove && _beforeImprove.HasValue)
            {
                _awaitingImprove = false;
                _statsAfter = stats;
                var b = _beforeImprove.Value;
                int afterSteps = _playback.Timeline.TotalSteps, afterWait = _heatmap.TotalWait();
                var rows = new List<(string, string, string, string, bool)>();
                if (stats != null)
                    rows.Add(("대기 합계", $"{b.wait}", $"{afterWait}", Pct(b.wait, afterWait), afterWait <= b.wait));
                rows.Add(("처리 스텝", $"{b.steps}", $"{afterSteps}", Pct(b.steps, afterSteps), afterSteps <= b.steps));
                _user.SetComparison("적용 전", "적용 후", rows, stats != null
                    ? "같은 주문으로 개선안을 적용해 다시 시뮬레이션했어요 (숫자가 작을수록 좋아요)"
                    : "혼잡도 기록을 받지 못해 대기 비교는 빠졌어요");
                _user.CmpHeatRow.SetActive(_statsBefore != null && _statsAfter != null);
                ShowStatsHeatmap(false);
                Log(LogLevel.Info, LogModule.Sim, $"개선 전→후: 스텝 {b.steps}→{afterSteps}, 대기 {b.wait}→{afterWait}");
                FinishProcessing(() => _user.Show(UserScreen.Comparison, 4, "개선 결과"));
            }
        }

        /// <summary>비교 화면에서 '적용 전' 히트맵을 보다가 나가면 현재 결과(적용 후)로 되돌린다.</summary>
        void RestoreAfterStats()
        {
            if (_statsAfter != null && _user.CmpHeatRow.activeSelf) _heatmap.SetStats(_statsAfter);
        }

        void ShowStatsHeatmap(bool before)
        {
            var s = before ? _statsBefore : _statsAfter;
            if (s == null) return;
            _heatmap.SetStats(s);
            _heatmap.SetVisible(true);
            UIFactory.SetButtonColor(_user.CmpBeforeHeat, before ? UIFactory.Highlight : UIFactory.Secondary);
            UIFactory.SetButtonColor(_user.CmpAfterHeat, before ? UIFactory.Secondary : UIFactory.Highlight);
        }

        static string Pct(float before, float after)
        {
            if (before == 0) return "-";
            float p = (before - after) * 100f / before;
            return p >= 0 ? $"↓ {p:0.#}%" : $"↑ {-p:0.#}%";
        }

        void OnCompare(ServerMessage msg)
        {
            var baseline = msg.Get("baseline") as JObject;
            var optimized = msg.Get("optimized") as JObject;
            // 한쪽이라도 주문을 다 처리하지 못하면 서버가 improvement_pct 를 null 로, 이유를 note 로 보낸다 (착시 방지)
            var pctToken = msg.Get("improvement_pct");
            bool hasPct = pctToken != null && pctToken.Type != JTokenType.Null;
            float pct = hasPct ? msg.GetFloat("improvement_pct") : 0f;
            string note = msg.GetString("note");
            Log(LogLevel.Info, LogModule.Sim, hasPct ? $"비교 결과: 개선율 {pct:0.0}%" : $"비교 결과: {note}", null, null, msg.Raw);
            _agent.Finish(RunState.Completed, hasPct ? $"compare {pct:0.0}%" : "compare n/a", msg.Raw);
            _lastCompare = msg.Root;
            var rows = new List<(string, string, string, string, bool)>();
            if (baseline != null && optimized != null)
            {
                foreach (var prop in baseline.Properties())
                {
                    if (rows.Count >= 3) break;
                    var other = optimized[prop.Name];
                    if (other == null) continue;
                    if (float.TryParse(prop.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                        float.TryParse(other.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                        rows.Add((prop.Name, $"{a:0.#}", $"{b:0.#}", Pct(a, b), b <= a));
                    else
                        rows.Add((prop.Name, prop.Value.ToString(), other.ToString(), "", true));
                }
            }
            _user.SetComparison("기준 전략", "최적화", rows,
                hasPct ? $"같은 창고·주문에서 기준 전략 대비 처리 스텝 {pct:0.#}% 개선"
                       : (string.IsNullOrEmpty(note) ? "주문을 다 처리하지 못해 개선율을 비교할 수 없어요" : note));
            _user.CmpHeatRow.SetActive(false);
            _statsBefore = null;
            _statsAfter = null;
            FinishProcessing(() => _user.Show(UserScreen.Comparison, 4, "전략 비교"));
        }

        void OnAnalysis(ServerMessage msg)
        {
            var bottlenecks = msg.Get("bottlenecks")?.ToObject<List<Bottleneck>>() ?? new List<Bottleneck>();
            _warehouse.SetBottleneckMarkers(bottlenecks);
            Log(LogLevel.Info, LogModule.Agent, "병목: " + string.Join(", ", bottlenecks.Take(5).Select(b => $"({b.x},{b.y}) 대기 {b.wait}")),
                "BOTTLENECK", bottlenecks.Count > 0 ? new Vector2Int(bottlenecks[0].x, bottlenecks[0].y) : (Vector2Int?)null, msg.Raw);
            _agent.Finish(RunState.Completed, $"analysis 병목 {bottlenecks.Count}곳", msg.Raw);

            if (bottlenecks.Count > 0)
            {
                var b0 = bottlenecks[0];
                _user.BottleneckTitle.text = "▲ 주요 병목 발견";
                _user.BottleneckTitle.color = UIFactory.Lighten(UIFactory.Warning, 0.2f);
                _user.BottleneckWhere.text = DescribePlace(b0.x, b0.y);
                _user.BottleneckDetail.text = $"위치 ({b0.x}, {b0.y})  ·  대기 {b0.wait}회\n빨간 기둥 {bottlenecks.Count}곳 표시";
            }
            else
            {
                _user.BottleneckTitle.text = "√ 큰 병목이 없어요";
                _user.BottleneckTitle.color = UIFactory.Lighten(UIFactory.Success, 0.3f);
                _user.BottleneckWhere.text = "";
                _user.BottleneckDetail.text = "";
            }
            _user.AgentExplain.text = msg.GetString("explanation");

            _user.ClearProposals();
            _proposals = msg.Get("proposals")?.ToObject<List<Proposal>>() ?? new List<Proposal>();
            foreach (var p in _proposals)
            {
                var prop = p;
                _user.AddProposal(p.text, () => GoApproval(prop));
            }
            _user.ProposalHint.text = _proposals.Count > 0 ? "고르면 적용 전에 한 번 더 확인해요" : "지금 구성으로 충분해요";
            if (!_heatmap.Visible) _heatmap.SetVisible(true);
            FinishProcessing(() => _user.Show(UserScreen.Analysis, 4, "병목 분석"));
        }

        /// <summary>병목 칸 이름: 가까운(2칸 이내) 도크, 충전 구역 기준, 없으면 통로.</summary>
        string DescribePlace(int x, int y)
        {
            var map = _warehouse.Map;
            if (map == null) return "통로";
            CellType? best = null;
            int bestD = int.MaxValue;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (!map.InBounds(x + dx, y + dy)) continue;
                var t = map.GetCell(x + dx, y + dy);
                if (t != CellType.DockIn && t != CellType.DockOut && t != CellType.Charge) continue;
                int d = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (d < bestD) { bestD = d; best = t; }
            }
            switch (best)
            {
                case CellType.DockIn: return "입하 도크 앞";
                case CellType.DockOut: return "출하 도크 앞";
                case CellType.Charge: return "충전 구역 앞";
                default:
                    bool nearRack = false;
                    foreach (var d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                        if (map.InBounds(x + d.x, y + d.y) && map.GetCell(x + d.x, y + d.y) == CellType.Rack) nearRack = true;
                    return nearRack ? "랙 사이 통로" : "중앙 통로";
            }
        }

        // ------------------------------------------------------------------ 표시

        void ShowMap(GridMap map)
        {
            if (_rackEditor.Editing) ToggleRackEdit();
            _warehouse.Build(map);
            ApplyViewMode();
        }

        void ResetSimulation()
        {
            _inventory.Clear();
            _simId = null;
            _fetchGeneration++;
            _playback.Begin(0);
            _heatmap.SetStats(null);
            _heatmap.SetVisible(false);
            _pathView.SetVisible(false);
            _warehouse.ClearMarkers();
            _user.ClearProposals();
        }

        void ToggleViewMode()
        {
            _miniature = !_miniature;
            if (_miniature)
            {
                SetLifeSizeVisuals(false, Vector3.zero, 0f);
                RealignToTable();
            }
            else
            {
                ApplyViewMode();
                PlacePanelsAroundViewer();
                if (_warehouse.Map == null) Log(LogLevel.Info, LogModule.Vr, "실물 보기: 창고를 만들거나 오프라인 재생을 누르면 발밑 아래에 1:1로 펼쳐집니다");
            }
        }

        /// <summary>실물 보기: 지금 보는 방향 기준 오른쪽 아래에 사용자(또는 관리자) 패널.</summary>
        void PlacePanelsAroundViewer()
        {
            var cam = Camera.main;
            if (cam == null) { PlacePanels(); return; }
            var user = _user.Canvas.transform;
            var dev = _dev.Canvas.transform;
            user.parent.SetParent(null, false);
            var fwd = cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            var rightDir = Vector3.Cross(Vector3.up, fwd);
            float floorY = _groundY ?? (Rig != null ? Rig.position.y : 0f);
            var basePos = cam.transform.position;
            basePos.y = floorY + panelBottom;
            float h = UserPanel.FullHeight * user.localScale.y;
            TableLayout.Place(user, basePos + fwd * 1.2f + rightDir * 0.55f, cam.transform.position, h);
            // 관리자 패널은 사용자 패널 대신 같은 방향, 조금 더 멀리 (1.6m)
            TableLayout.Place(dev, basePos + fwd * 1.6f + rightDir * 0.5f, cam.transform.position, DevPanel.Height * DevPanel.MetersPerPixel);
        }

        // ---------- 실물 보기 연출: 씬 바닥 숨김, 유리 바닥, 반투명 벽 ----------

        readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        GameObject _glassFloor;

        IEnumerable<GameObject> LifeSizeHideTargets()
        {
            if (hideInLifeSize != null && hideInLifeSize.Length > 0)
            {
                foreach (var g in hideInLifeSize) if (g != null) yield return g;
                yield break;
            }
            var floor = GameObject.Find("Floor");
            if (floor != null && !floor.transform.IsChildOf(_warehouse.transform)) yield return floor;
        }

        void SetLifeSizeVisuals(bool life, Vector3 mapCenterWorld, float mapSpan)
        {
            // 씬 바닥, 벽: 렌더러만 끄고 충돌체는 남긴다 (보이지 않는 안전 바닥과 함께 떨어지지 않음)
            foreach (var r in _hiddenRenderers) if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();
            if (life)
            {
                foreach (var g in LifeSizeHideTargets())
                    foreach (var r in g.GetComponentsInChildren<Renderer>())
                        if (r.enabled) { r.enabled = false; _hiddenRenderers.Add(r); }
            }

            // 발밑 유리 바닥 (서 있는 높이를 눈으로 알 수 있게)
            if (life && glassFloorAlpha > 0f)
            {
                if (_glassFloor == null)
                {
                    _glassFloor = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    _glassFloor.name = "RW_GlassFloor";
                    Destroy(_glassFloor.GetComponent<Collider>());
                    var mat = new Material(MaterialLibrary.Overlay) { name = "GlassFloor" };
                    _glassFloor.GetComponent<Renderer>().sharedMaterial = mat;
                }
                var gm = _glassFloor.GetComponent<Renderer>().sharedMaterial;
                MaterialLibrary.SetMaterialColor(gm, new Color(0.6f, 0.85f, 1f, glassFloorAlpha));
                float floorY = _groundY ?? 0f;
                _glassFloor.transform.position = new Vector3(mapCenterWorld.x, floorY + 0.003f, mapCenterWorld.z);
                _glassFloor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                _glassFloor.transform.localScale = new Vector3(mapSpan + 10f, mapSpan + 10f, 1f);
                _glassFloor.SetActive(true);
            }
            else if (_glassFloor != null)
            {
                _glassFloor.SetActive(false);
            }

            _warehouse.SetWallsTranslucent(life && translucentWallsInLifeSize);
        }

        /// <summary>미니어처: 테이블 위 축소 모형 / 실물: 바닥에 1:1, 사용자 앞 남쪽 가장자리에서 시작.</summary>
        void ApplyViewMode()
        {
            var map = _warehouse.Map;
            if (map == null) return;
            var t = _warehouse.transform;
            float cs = map.cellSizeM;
            var center = GridCoord.GridCenterLocal(map.width, map.height, cs);
            float mapW = map.width * cs, mapH = map.height * cs;
            bool hasTable = TableLayout.TryGetTop(table, out var top);
            var viewer = ViewerPosition();

            if (_miniature)
            {
                if (hasTable)
                {
                    // 테이블 윗면 중앙에, 테이블 방향에 맞춰, 윗면 크기에 맞는 축척으로
                    var q = TableLayout.ChooseMapRotation(top, mapW, mapH, viewer);
                    float s = TableLayout.FitScale(top, q, mapW, mapH, tableMargin);
                    t.rotation = q;
                    t.localScale = Vector3.one * s;
                    t.position = top.center + Vector3.up * 0.005f - q * (center * s);
                }
                else
                {
                    float s = miniatureSize / Mathf.Max(1f, Mathf.Max(mapW, mapH));
                    t.rotation = Quaternion.identity;
                    t.localScale = Vector3.one * s;
                    t.position = tableCenter - center * s;
                }
            }
            else
            {
                // 실물(관제 보기): 1:1 창고를 사용자 바닥보다 depth 만큼 아래에 두고 위에서 내려다본다.
                // 무인 창고라 사람이 창고 바닥에 내려가지 않는다는 전제. 사용자는 보이지 않는 바닥 위에 선다.
                t.localScale = Vector3.one;
                var cam = Camera.main;
                var fwd = cam != null ? cam.transform.forward : Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                var q = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                float floorY = _groundY ?? (Rig != null ? Rig.position.y : 0f);
                float depth = autoDepth ? Mathf.Max(lifeSizeDepth, _warehouse.TallestHeight + 0.3f) : lifeSizeDepth;
                var stand = cam != null ? cam.transform.position : (Rig != null ? Rig.position : Vector3.zero);
                // 사용자 바로 앞(0.5m)에서 창고 남쪽 가장자리가 시작되도록
                var anchor = new Vector3(stand.x, floorY - depth, stand.z) + q * Vector3.forward * 0.5f;
                t.rotation = q;
                t.position = anchor - q * new Vector3(center.x, 0f, -0.5f * cs);
                SetLifeSizeVisuals(true, t.position + q * center, Mathf.Max(mapW, mapH));
            }
            if (_miniature) SetLifeSizeVisuals(false, Vector3.zero, 0f);
            if (table != null) table.SetActive(_miniature);
        }

        // ------------------------------------------------------------------ 오프라인 재생 (시연 대비, 관리자 패널)

        void PlayOffline()
        {
            var mapAsset = Resources.Load<TextAsset>("Offline/w2_map");
            var framesAsset = Resources.Load<TextAsset>("Offline/s2_frames");
            var statsAsset = Resources.Load<TextAsset>("Offline/s2_stats");
            if (mapAsset == null || framesAsset == null)
            {
                Log(LogLevel.Error, LogModule.Vr, "오프라인 데이터가 없어요 (Resources/Offline)", "NO_OFFLINE_DATA");
                return;
            }
            _offline = true;
            _fetchGeneration++;
            _simId = null;
            _mapConfirmed = false;
            ResetQuestion();
            var mapRoot = JToken.Parse(mapAsset.text);
            var map = GridMap.FromToken(mapRoot["map"] ?? mapRoot);
            ShowMap(map);
            _user.SetConfirmSummary(MapChips(map));
            _heatmap.SetVisible(false);
            _pathView.SetVisible(false);
            var frames = SimParsers.ParseFrames(framesAsset.text);
            _playback.Begin(frames.Count > 0 ? frames[frames.Count - 1].t : 0);
            _playback.Timeline.AddFrames(frames);
            _framesComplete = true;
            if (statsAsset != null) _heatmap.SetStats(SimParsers.ParseStats(statsAsset.text));
            _playback.ApplyPositions();
            _playback.Play();
            _simSummary = "저장된 시연 데이터 (W2 + S2)";
            Log(LogLevel.Info, LogModule.Sim, $"오프라인 재생 W2 + S2: 로봇 {_playback.Timeline.RobotIds.Count}대, {_playback.Timeline.TotalSteps} 스텝");
            _simStrategy = "offline";
            _user.SetConnection(ConnectionView.Offline);
            SetPhase(Phase.Ready);
            GoPlayback();
        }

        // ------------------------------------------------------------------ 매 프레임

        void Update()
        {
            _ws.Poll(Time.time);
            _user.Tick(Time.unscaledDeltaTime, _recorder.RecordingSeconds);

            // XR 초기화가 늦게 끝나는 경우를 위해 레이캐스터 설정을 다시 맞춘다
            bool xr = XRSupport.XRActive;
            if (_lastXrActive != xr)
            {
                _lastXrActive = xr;
                _ui.RefreshRaycasters();
                if (_report?.Canvas != null) XRSupport.RefreshRaycasters(_report.Canvas);
                if (_history?.Canvas != null) XRSupport.RefreshRaycasters(_history.Canvas);
            }

            // 오른손 A 버튼: 누른 채 말하기 (창고 만들기·인식 결과·질문 화면). 녹음 화면에서 누르면 녹음 종료.
            bool a = XRSupport.RightButton(UnityEngine.XR.CommonUsages.primaryButton) && !_adminMode;
            if (a && !_prevA)
            {
                var sc = _user.Current;
                if (sc == UserScreen.Recording) StopVoice();
                else if (sc == UserScreen.Create || sc == UserScreen.SttResult || sc == UserScreen.Question || sc == UserScreen.SimConfig)
                {
                    var target = sc == UserScreen.SimConfig ? VoiceTarget.Scenario
                        : sc == UserScreen.Question || (sc == UserScreen.SttResult && _voiceTarget == VoiceTarget.Answer)
                        ? VoiceTarget.Answer : VoiceTarget.Describe;
                    StartVoice(target);
                    _recordingByA = _recorder.IsRecording;
                }
            }
            if (!a && _prevA && _recordingByA)
            {
                _recordingByA = false;
                StopVoice();
            }
            _prevA = a;

            // 관리자 메뉴 비밀 입력 (왼손 그립 + Y 3번) → 사용자 패널에 ≡ 버튼이 잠깐 나타남
            TickAdminGesture();

            // 오른손 B 버튼: 미니어처 보기면 테이블 앞으로 다시 맞춤, 실물 보기면 패널만 지금 위치 기준으로
            bool b = XRSupport.RightButton(UnityEngine.XR.CommonUsages.secondaryButton);
            if (b && !_prevB)
            {
                if (_miniature) RealignToTable(); else PlacePanelsAroundViewer();
            }
            _prevB = b;
            // 헤드셋 추적이 늦게 잡히면 그때 한 번 맞춘다
            if (!_aligned && alignUserToTable && table != null && Time.frameCount % 15 == 0)
            {
                var o = UserAligner.FindOrigin();
                if (o != null && UserAligner.HeadReady(o)) RealignToTable();
            }

            // 안전장치: 바닥 아래로 떨어지면 원래 높이로 되돌리고 다시 맞춘다
            if (_groundY.HasValue && Time.frameCount % 10 == 0)
            {
                var o = UserAligner.FindOrigin();
                if (o != null && o.transform.position.y < _groundY.Value - 2f)
                {
                    UserAligner.EnsureGround(o);
                    var p = o.transform.position;
                    UserAligner.Teleport(o, () => o.transform.position = new Vector3(p.x, _groundY.Value, p.z));
                    Debug.LogWarning("[RobotWarehouse] XR Origin이 바닥 아래로 떨어져 원래 높이로 되돌렸습니다");
                    _realignRequested = true;
                }
            }

            if (_realignRequested)
            {
                _realignRequested = false;
                if (_miniature) RealignToTable();
            }

            UpdatePlaybackUi();
            TickAdmin();
        }

        // ---------- 재생 UI 갱신 (값이 바뀔 때만 -> 캔버스 레이아웃 재계산 최소화) ----------

        int _uiStep = -1, _uiTotal = -1, _uiLoaded = -1;
        bool _uiBuffering, _uiPlaying, _uiPlayInit;
        float _fpsTimer;
        int _fpsFrames, _fps;

        void UpdatePlaybackUi()
        {
            // 프레임 수(FPS)는 0.5초마다 측정 - Quest 성능 확인용 (관리자 패널)
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            bool fpsChanged = false;
            if (_fpsTimer >= 0.5f)
            {
                int fps = Mathf.RoundToInt(_fpsFrames / _fpsTimer);
                fpsChanged = fps != _fps;
                _fps = fps;
                _fpsFrames = 0;
                _fpsTimer = 0f;
            }

            var tl = _playback.Timeline;
            int step = tl.CurrentStep;
            bool showing = _user.Current == UserScreen.Playback;
            // 슬라이더는 스텝이 바뀔 때만 움직인다 (매 프레임 값 변경은 UI를 계속 다시 그리게 함)
            if (showing && (step != _uiStep || tl.TotalSteps != _uiTotal))
            {
                _updatingSlider = true;
                _user.StepSlider.maxValue = Mathf.Max(1, tl.TotalSteps);
                _user.StepSlider.value = step;
                _updatingSlider = false;
            }
            bool changed = step != _uiStep || tl.TotalSteps != _uiTotal || tl.LoadedUntil != _uiLoaded || tl.IsBuffering != _uiBuffering;
            if (showing && changed)
            {
                string buffering = tl.IsBuffering ? "  ·  결과 받는 중" : "";
                var summary = string.IsNullOrEmpty(_simSummary) ? "" : "   |   " + _simSummary;
                _user.PlayStatus.text = $"스텝 {step} / {tl.TotalSteps}{buffering}{summary}";
            }
            if (changed || fpsChanged)
            {
            }
            if (showing && changed)
            {
                _uiStep = step;
                _uiTotal = tl.TotalSteps;
                _uiLoaded = tl.LoadedUntil;
                _uiBuffering = tl.IsBuffering;
            }
            if (tl.Playing != _uiPlaying || !_uiPlayInit)
            {
                UIFactory.SetButtonText(_user.PlayButton, tl.Playing ? "‖" : "▶");
                _uiPlaying = tl.Playing;
                _uiPlayInit = true;
            }
        }
    }
}
