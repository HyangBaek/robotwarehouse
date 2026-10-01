using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// 입력 → REST 요청 → WebSocket 이벤트 → 창고·로봇·히트맵·패널 갱신.
    /// </summary>
    public class AppController : MonoBehaviour
    {
        public enum Phase { Idle, Generating, AwaitConfirm, Question, Confirmed, Simulating, Ready }
        enum VoiceTarget { Describe, Answer }

        [Header("배치")]
        [Tooltip("미니어처 창고를 놓을 위치 (XR Origin 기준이 아니라 월드 좌표)")]
        public Vector3 tableCenter = new Vector3(0f, 0.8f, 1.1f);
        [Tooltip("미니어처 모드에서 창고 긴 변 길이(m)")]
        public float miniatureSize = 1.4f;
        [Tooltip("미니어처 모드에서만 보이는 테이블 (선택)")]
        public GameObject table;
        public Vector3 leftPanelOffset = new Vector3(-1.05f, 1.35f, 0.9f);
        public Vector3 rightPanelOffset = new Vector3(1.05f, 1.35f, 0.9f);

        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public WarehouseRenderer Warehouse => _warehouse;
        public RobotPlayback Playback => _playback;

        WarehouseRenderer _warehouse;
        RobotPlayback _playback;
        PathLineView _pathView;
        HeatmapLayer _heatmap;
        RackGrabEditor _rackEditor;
        VoiceRecorder _recorder;
        AudioSource _audio;
        MainPanels _ui;
        ApiClient _api;
        readonly WsClient _ws = new WsClient();

        string _mapVersion;
        bool _mapConfirmed;
        string _simId;
        string _questionId;
        int _questionRounds;
        bool _offline;
        bool _miniature = true;
        bool _updatingSlider;
        int _fetchGeneration;
        bool _framesComplete;
        bool _prevA;
        bool? _lastXrActive;
        VoiceTarget _voiceTarget;
        Action _retryAction;

        // 개선안 전·후 비교 (SC-10)
        bool _awaitingImprove;
        (int steps, int wait)? _beforeImprove;

        // ------------------------------------------------------------------ 초기화

        void Start()
        {
            EnsureEventSystem();
            BuildWorld();
            BuildUI();
            _ws.ReconnectInterval = AppConfig.ReconnectIntervalSec;
            _ws.OnMessage += HandleWsMessage;
            _ws.OnStateChanged += HandleWsState;
            SetPhase(Phase.Idle);
            _ui.Log("서버 IP를 입력하고 연결하세요. 서버 없이 보려면 '오프라인 재생'.");
        }

        void OnDestroy() => _ws.Dispose();

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
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
            _rackEditor = root.AddComponent<RackGrabEditor>();
            _rackEditor.warehouse = _warehouse;
            _rackEditor.OnRackMoved += OnRackMoved;
            _rackEditor.OnRejected += msg => _ui.Log("<color=#FFB060>" + msg + "</color>");

            _recorder = gameObject.AddComponent<VoiceRecorder>();
            _recorder.OnRecorded += OnVoiceRecorded;
            _audio = gameObject.AddComponent<AudioSource>();
            _playback.OnFinished += () => _ui.Log($"재생 완료: 총 {_playback.Timeline.TotalSteps} 스텝");
        }

        void BuildUI()
        {
            _ui = new MainPanels();
            // 패널은 XR Origin을 따라다니게 한다 (이동·텔레포트해도 손 닿는 곳에 있음)
            var cam = Camera.main;
            Transform rig = cam != null ? cam.transform.root : null;
            var panelRoot = new GameObject("Panels").transform;
            panelRoot.SetParent(rig, false);
            _ui.Build(panelRoot);
            PlacePanel(_ui.Left.transform, leftPanelOffset);
            PlacePanel(_ui.Right.transform, rightPanelOffset);

            _ui.HostInput.text = AppConfig.Host;
            _ui.PortInput.text = AppConfig.Port.ToString();

            _ui.ConnectButton.onClick.AddListener(Connect);
            _ui.OfflineButton.onClick.AddListener(PlayOffline);
            _ui.ViewModeButton.onClick.AddListener(ToggleViewMode);
            _ui.GenerateButton.onClick.AddListener(GenerateFromText);
            _ui.RecordButton.OnPress += () => BeginVoice(VoiceTarget.Describe);
            _ui.RecordButton.OnRelease += EndVoice;
            _ui.AnswerRecordButton.OnPress += () => BeginVoice(VoiceTarget.Answer);
            _ui.AnswerRecordButton.OnRelease += EndVoice;
            _ui.AnswerButton.onClick.AddListener(SendAnswerText);
            _ui.ConfirmButton.onClick.AddListener(ConfirmMap);
            _ui.RetryButton.onClick.AddListener(Retry);
            _ui.EditRacksButton.onClick.AddListener(ToggleRackEdit);

            _ui.RunButton.onClick.AddListener(RunScenario);
            _ui.CompareButton.onClick.AddListener(RequestCompare);
            _ui.PlayButton.onClick.AddListener(() => _playback.TogglePlay());
            for (int i = 0; i < _ui.SpeedButtons.Length; i++)
            {
                float s = _ui.Speeds[i];
                _ui.SpeedButtons[i].onClick.AddListener(() => { _playback.SetSpeed(s); _ui.HighlightSpeed(s); });
            }
            _ui.HighlightSpeed(1f);
            _ui.StepSlider.onValueChanged.AddListener(v => { if (!_updatingSlider) _playback.Timeline.Seek(v); });
            _ui.HeatmapButton.onClick.AddListener(() =>
            {
                _heatmap.SetVisible(!_heatmap.Visible);
                _ui.SetToggleText(_ui.HeatmapButton, "히트맵", _heatmap.Visible);
            });
            _ui.MetricButton.onClick.AddListener(() =>
            {
                var m = _heatmap.Metric == HeatmapMetric.Wait ? HeatmapMetric.Pass : HeatmapMetric.Wait;
                _heatmap.SetMetric(m);
                UIFactory.SetButtonText(_ui.MetricButton, m == HeatmapMetric.Wait ? "지표: 대기" : "지표: 통과");
            });
            _ui.PathButton.onClick.AddListener(() =>
            {
                _pathView.SetVisible(!_pathView.Visible);
                if (_pathView.Visible && _playback.SelectedRobot == null) _playback.SelectNextRobot(1);
                _ui.SetToggleText(_ui.PathButton, "경로 선", _pathView.Visible);
            });
            _ui.PrevRobotButton.onClick.AddListener(() => _playback.SelectNextRobot(-1));
            _ui.NextRobotButton.onClick.AddListener(() => _playback.SelectNextRobot(1));
            _ui.AddOrdersButton.onClick.AddListener(() => SendSimEvent(10, _ui.Robots.Value));
            _ui.ChangeRobotsButton.onClick.AddListener(() => SendSimEvent(0, _ui.Robots.Value));
            _ui.AnalyzeButton.onClick.AddListener(RequestAnalysis);
        }

        void PlacePanel(Transform panel, Vector3 offset)
        {
            panel.localPosition = offset;
            var flat = new Vector3(offset.x, 0f, offset.z);
            panel.localRotation = Quaternion.LookRotation(flat.sqrMagnitude > 0.001f ? flat : Vector3.forward, Vector3.up);
        }

        // ------------------------------------------------------------------ 상태

        void SetPhase(Phase p)
        {
            CurrentPhase = p;
            bool connected = _ws.State == WsState.Connected;
            bool busy = p == Phase.Generating || p == Phase.Simulating;
            _ui.GenerateButton.interactable = connected && !busy;
            _ui.RecordButton.Interactable = connected && !busy;
            _ui.ConfirmButton.interactable = p == Phase.AwaitConfirm && !_offline;
            // FR-09: 확인 전에는 시뮬레이션 버튼 비활성
            _ui.RunButton.interactable = connected && _mapConfirmed && !busy;
            bool hasSim = !string.IsNullOrEmpty(_simId) && !_offline && connected;
            _ui.CompareButton.interactable = hasSim && !busy;
            _ui.AnalyzeButton.interactable = hasSim && !busy;
            _ui.AddOrdersButton.interactable = hasSim && !busy;
            _ui.ChangeRobotsButton.interactable = hasSim && !busy;
            _ui.EditRacksButton.interactable = _warehouse.Map != null && !_offline && connected && !busy;
            _ui.AnswerRow.SetActive(p == Phase.Question);
            _ui.ScenarioStatus.text = _mapConfirmed
                ? (p == Phase.Simulating ? "경로 계산 중…" : $"지도 v{_mapVersion} 확정됨. 실행할 수 있어요")
                : "지도 확인 후 실행할 수 있어요";
        }

        // ------------------------------------------------------------------ 연결 (TC-VR-11)

        void Connect()
        {
            _offline = false;
            AppConfig.Host = _ui.HostInput.text;
            if (int.TryParse(_ui.PortInput.text, out var port)) AppConfig.Port = port;
            _api = new ApiClient(AppConfig.HttpBase);
            _ui.ConnectionStatus.text = $"연결 중… {AppConfig.HttpBase}";
            _ws.Connect(AppConfig.WsBase + ApiRoutes.WebSocket(AppConfig.SessionId));
            StartCoroutine(_api.Get(ApiRoutes.Health, r =>
            {
                if (!r.Ok) _ui.Log($"<color=#FF8080>서버 응답 없음: {r.ErrorMessage}</color>");
            }));
        }

        void HandleWsState(WsState s)
        {
            switch (s)
            {
                case WsState.Connected:
                    _ui.ConnectionStatus.text = $"<color=#7CFC9A>연결됨</color>  {AppConfig.HttpBase}  세션 {AppConfig.SessionId}";
                    // EX-02: 재연결 뒤 마지막 sim_id 프레임 이어 받기
                    if (!string.IsNullOrEmpty(_simId) && !_framesComplete && !_offline)
                        StartCoroutine(FetchFrames(_simId, _playback.Timeline.LoadedUntil + 1, ++_fetchGeneration));
                    break;
                case WsState.Connecting:
                    _ui.ConnectionStatus.text = "연결 중…";
                    break;
                case WsState.Reconnecting:
                    _ui.ConnectionStatus.text = "<color=#FFB060>서버 재연결 중…</color> (5초 간격)";
                    break;
                default:
                    _ui.ConnectionStatus.text = "연결 안 됨";
                    break;
            }
            SetPhase(CurrentPhase);
        }

        // ------------------------------------------------------------------ 요청 공통

        void Post(string path, object body, Action<ApiResult> onOk, string busyText = null, Action retry = null)
        {
            if (_api == null) { _ui.Log("<color=#FF8080>먼저 서버에 연결하세요</color>"); return; }
            _ui.RetryButton.gameObject.SetActive(false);
            if (busyText != null) _ui.Log(busyText);
            StartCoroutine(_api.PostJson(path, body, r =>
            {
                if (r.Ok) onOk?.Invoke(r);
                else ShowError(r.ErrorMessage, retry);
            }));
        }

        void ShowError(string message, Action retry)
        {
            _ui.Log($"<color=#FF8080>{message}</color>");
            _retryAction = retry;
            _ui.RetryButton.gameObject.SetActive(retry != null);
            if (CurrentPhase == Phase.Generating) SetPhase(_warehouse.Map != null ? Phase.AwaitConfirm : Phase.Idle);
            else if (CurrentPhase == Phase.Simulating) SetPhase(Phase.Confirmed);
        }

        void Retry()
        {
            _ui.RetryButton.gameObject.SetActive(false);
            _retryAction?.Invoke();
        }

        // ------------------------------------------------------------------ SC-01 텍스트로 창고 구성

        void GenerateFromText()
        {
            var text = _ui.DescribeInput.text?.Trim();
            if (string.IsNullOrEmpty(text)) { _ui.Log("창고 구조를 입력하거나 예시 버튼(W1~W6)을 누르세요"); return; }
            RequestMapFromText(text);
        }

        void RequestMapFromText(string text)
        {
            ResetQuestion();
            _mapConfirmed = false;
            SetPhase(Phase.Generating);
            Post(ApiRoutes.MapText, new { session_id = AppConfig.SessionId, text }, _ => { },
                $"창고 생성 중… \"{text}\"", () => RequestMapFromText(text));
        }

        // ------------------------------------------------------------------ SC-02 음성 입력

        void BeginVoice(VoiceTarget target)
        {
            if (_api == null) { _ui.Log("먼저 서버에 연결하세요"); return; }
            _voiceTarget = target;
            _recorder.Begin();
            if (_recorder.IsRecording) _ui.TranscriptText.text = "<color=#FF8080>● 녹음 중… 버튼을 놓으면 전송</color>";
        }

        void EndVoice()
        {
            if (!_recorder.IsRecording) return;
            _recorder.End();
        }

        void OnVoiceRecorded(byte[] wav, string error)
        {
            if (wav == null)
            {
                _ui.TranscriptText.text = error;   // EX-03
                return;
            }
            _ui.TranscriptText.text = $"음성 전송 중… ({wav.Length / 1024} KB)";
            bool answer = _voiceTarget == VoiceTarget.Answer && !string.IsNullOrEmpty(_questionId);
            if (!answer) { _mapConfirmed = false; ResetQuestion(); SetPhase(Phase.Generating); }
            StartCoroutine(_api.PostAudio(ApiRoutes.MapVoice, AppConfig.SessionId, wav, r =>
            {
                if (!r.Ok) ShowError(r.ErrorMessage, null);
            }, answer ? _questionId : null));
        }

        // ------------------------------------------------------------------ SC-03 수정 질문 답변

        void SendAnswerText()
        {
            var text = _ui.AnswerInput.text?.Trim();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_questionId)) return;
            var qid = _questionId;
            SetPhase(Phase.Generating);
            Post(ApiRoutes.MapAnswer, new { session_id = AppConfig.SessionId, question_id = qid, text },
                _ => _ui.AnswerInput.text = "", $"답변: {text}", SendAnswerText);
        }

        void ResetQuestion()
        {
            _questionId = null;
            _questionRounds = 0;
            if (_warehouse.Map != null) _warehouse.ClearHighlight();
        }

        // ------------------------------------------------------------------ 지도 확인 (FR-09)

        void ConfirmMap()
        {
            if (string.IsNullOrEmpty(_mapVersion)) return;
            Post(ApiRoutes.MapConfirm, new { session_id = AppConfig.SessionId, map_version = _mapVersion }, r =>
            {
                _mapConfirmed = true;
                _ui.Log($"지도 v{_mapVersion} 확정. 시나리오를 입력하고 실행하세요");
                SetPhase(Phase.Confirmed);
            }, null, ConfirmMap);
        }

        // ------------------------------------------------------------------ SC-04 시나리오 실행

        void RunScenario()
        {
            if (!_mapConfirmed) { _ui.Log("지도를 먼저 확인하세요"); return; }
            SetPhase(Phase.Simulating);
            var body = new
            {
                session_id = AppConfig.SessionId,
                map_version = _mapVersion,
                robots = _ui.Robots.Value,
                inbound = _ui.Inbound.Value,
                outbound = _ui.Outbound.Value,
                strategy = "optimized"
            };
            Post(ApiRoutes.Scenario, body, r =>
            {
                var id = (string)(r.Json as JObject)?["sim_id"];
                if (!string.IsNullOrEmpty(id)) _ui.Log($"시뮬레이션 요청됨 (sim {id})");
            }, $"경로 계산 중… 로봇 {body.robots}대, 입하 {body.inbound}, 출하 {body.outbound}", RunScenario);
        }

        void RequestCompare()
        {
            Post(ApiRoutes.Compare, new { session_id = AppConfig.SessionId, sim_id = _simId }, null,
                "기준 전략(무작위 보관·선착순·개별 최단 경로)과 비교 중…", RequestCompare);
        }

        void RequestAnalysis()
        {
            Post(ApiRoutes.Analyze, new { session_id = AppConfig.SessionId, sim_id = _simId }, null,
                "로그 분석 중…", RequestAnalysis);
        }

        // SC-08 롤링 재계획
        void SendSimEvent(int addOrders, int robots)
        {
            int t = _playback.Timeline.CurrentStep;
            Post(ApiRoutes.SimEvent(_simId), new { session_id = AppConfig.SessionId, t, add_orders = addOrders, robots },
                null, $"재계획 요청: 스텝 {t}, 주문 +{addOrders}, 로봇 {robots}대");
        }

        // SC-10 개선안 승인
        void ApproveProposal(Proposal p)
        {
            _beforeImprove = (_playback.Timeline.TotalSteps, _heatmap.TotalWait());
            _awaitingImprove = true;
            Post(ApiRoutes.ImproveApprove, new { session_id = AppConfig.SessionId, proposal_id = p.proposalId }, r =>
            {
                _ui.Log($"개선안 적용: {p.text} → 재시뮬레이션");
            }, null, () => ApproveProposal(p));
        }

        // SC-11 랙 편집
        void ToggleRackEdit()
        {
            _rackEditor.SetEditing(!_rackEditor.Editing);
            _ui.SetToggleText(_ui.EditRacksButton, "랙 편집", _rackEditor.Editing);
            if (_rackEditor.Editing) _ui.Log("그립/트리거로 랙을 잡아 다른 통로 칸에 놓으세요");
        }

        void OnRackMoved(string rackId, Vector2Int from, Vector2Int to)
        {
            _mapConfirmed = false;
            SetPhase(Phase.Generating);
            var moves = new[] { new { rack_id = rackId, from = new[] { from.x, from.y }, to = new[] { to.x, to.y } } };
            Post(ApiRoutes.MapEdit, new { session_id = AppConfig.SessionId, map_version = _mapVersion, moves },
                null, $"랙 {rackId} ({from.x},{from.y}) → ({to.x},{to.y}) 이동, 다시 검증 중…");
        }

        // ------------------------------------------------------------------ WebSocket 이벤트

        void HandleWsMessage(string raw)
        {
            ServerMessage msg;
            try { msg = ServerMessage.Parse(raw); }
            catch (Exception e) { Debug.LogWarning($"[WS] JSON 파싱 실패: {e.Message}"); return; }

            var audio = msg.GetString("audio_url");
            if (!string.IsNullOrEmpty(audio)) PlayTts(audio);

            switch (msg.Type)
            {
                case ServerMessage.Transcript: OnTranscript(msg); break;
                case ServerMessage.MapReady: OnMapReady(msg); break;
                case ServerMessage.Question: OnQuestion(msg); break;
                case ServerMessage.SimReady: OnSimReady(msg); break;
                case ServerMessage.Compare: OnCompare(msg); break;
                case ServerMessage.Analysis: OnAnalysis(msg); break;
                case ServerMessage.Error:
                    ShowError($"{msg.GetString("code")} {msg.GetString("message")}".Trim(), null);
                    break;
                case ServerMessage.Status:
                    _ui.Log($"<color=#9EC9FF>· {msg.GetString("message", msg.GetString("node"))}</color>");
                    break;
                case ServerMessage.Tts: break;
                default: Debug.Log($"[WS] 알 수 없는 type: {msg.Type}"); break;
            }
        }

        void OnTranscript(ServerMessage msg)
        {
            var text = msg.GetString("text");
            if (string.IsNullOrWhiteSpace(text))
            {
                _ui.TranscriptText.text = "잘 못 들었어요. 다시 말하거나 입력해 주세요";   // EX-03
                SetPhase(_warehouse.Map != null ? Phase.AwaitConfirm : Phase.Idle);
                return;
            }
            _ui.TranscriptText.text = $"인식: \"{text}\"";
            if (CurrentPhase != Phase.Question) _ui.DescribeInput.text = text;
        }

        void OnMapReady(ServerMessage msg)
        {
            GridMap map;
            try { map = GridMap.FromToken(msg.Get("map")); }
            catch (Exception e) { ShowError("지도 데이터를 읽지 못했어요: " + e.Message, null); return; }

            if (map.schemaVersion != GridMap.SupportedSchemaVersion)
                _ui.Log($"<color=#FFB060>스키마 버전 불일치: 서버 {map.schemaVersion}, VR {GridMap.SupportedSchemaVersion}</color>");

            _mapVersion = msg.GetString("map_version");
            // 개선안 승인(SC-10)으로 서버가 바로 확정한 지도는 confirmed=true로 온다
            var confirmedToken = msg.Get("confirmed");
            _mapConfirmed = confirmedToken != null && confirmedToken.Type == JTokenType.Boolean && (bool)confirmedToken;
            ResetQuestion();
            ShowMap(map);
            ResetSimulation();

            var summary = SimParsers.Describe(msg.Get("summary"));
            _ui.Log($"<b>지도 v{_mapVersion} 생성</b> {map.width}×{map.height}, 랙 {map.CountCells(CellType.Rack)}칸, " +
                    $"입하 {map.CountCells(CellType.DockIn)} · 출하 {map.CountCells(CellType.DockOut)}");
            if (!string.IsNullOrEmpty(summary)) _ui.Log(summary);
            var defaults = msg.GetStringList("defaults_applied");
            if (defaults.Count > 0) _ui.Log($"<color=#FFD37A>기본값 적용: {string.Join(", ", defaults)}</color>");  // SC-12
            if (_mapConfirmed)
            {
                SetPhase(Phase.Confirmed);
            }
            else
            {
                _ui.Log("배치를 둘러본 뒤 '지도 확인'을 누르세요");
                SetPhase(Phase.AwaitConfirm);
            }
        }

        void OnQuestion(ServerMessage msg)
        {
            // 질문에 지도가 같이 오면(검증 실패 지도) 먼저 그려서 문제 칸을 보이게 한다.
            var mapToken = msg.Get("map");
            if (mapToken != null && mapToken.Type == JTokenType.Object)
            {
                try { ShowMap(GridMap.FromToken(mapToken)); } catch { }
            }
            _questionId = msg.GetString("question_id");
            _questionRounds++;
            var cells = msg.GetCells("error_cells");
            if (_warehouse.Map != null) _warehouse.SetHighlight(cells);
            _ui.Log($"<color=#FFD37A><b>Agent 질문 ({_questionRounds}/{AppConfig.MaxQuestionRounds})</b> {msg.GetString("text")}</color>");
            if (cells.Count > 0) _ui.Log($"문제 칸 {cells.Count}개를 빨간색으로 표시했어요");
            _ui.AnswerInput.text = "";
            SetPhase(Phase.Question);
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
                _ui.Log($"재계획 완료: 스텝 {replanFrom} 이후 갱신 (총 {total} 스텝, {msg.GetString("replan_ms", "?")}ms)");
                StartCoroutine(FetchFrames(simId, replanFrom, ++_fetchGeneration));
                StartCoroutine(FetchStats(simId));
                return;
            }

            _simId = simId;
            _offline = false;
            _playback.Begin(total);
            _framesComplete = false;
            _warehouse.ClearMarkers();
            _ui.ClearProposals();
            _ui.AnalysisText.text = "";
            _ui.Log($"<b>시뮬레이션 완료</b> ({strategy}) 총 {total} 스텝. {SimParsers.Describe(msg.Get("summary"))}");
            SetPhase(Phase.Ready);
            StartCoroutine(FetchFrames(simId, 0, ++_fetchGeneration));
            StartCoroutine(FetchStats(simId));
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
            if (result == null || !result.Ok) yield break;
            try { _heatmap.SetStats(SimParsers.ParseStats(result.Text)); }
            catch (Exception e) { Debug.LogWarning("[stats] " + e.Message); yield break; }

            if (_awaitingImprove && _beforeImprove.HasValue)
            {
                _awaitingImprove = false;
                var b = _beforeImprove.Value;
                int afterSteps = _playback.Timeline.TotalSteps, afterWait = _heatmap.TotalWait();
                _ui.AnalysisText.text =
                    $"<b>적용 전 → 후</b>\n처리 스텝 {b.steps} → {afterSteps} ({Pct(b.steps, afterSteps)})\n" +
                    $"대기 합계 {b.wait} → {afterWait} ({Pct(b.wait, afterWait)})";
            }
        }

        static string Pct(int before, int after)
        {
            if (before == 0) return "0%";
            float p = (before - after) * 100f / before;
            return p >= 0 ? $"{p:0.0}% 개선" : $"{-p:0.0}% 증가";
        }

        void OnCompare(ServerMessage msg)
        {
            var baseline = SimParsers.Describe(msg.Get("baseline"));
            var optimized = SimParsers.Describe(msg.Get("optimized"));
            float pct = msg.GetFloat("improvement_pct");
            _ui.AnalysisText.text = $"<b>기준 전략 vs 최적화</b>\n기준: {baseline}\n최적화: {optimized}\n개선율: <b>{pct:0.0}%</b>";
            _ui.Log($"비교 결과: 개선율 {pct:0.0}%");
        }

        void OnAnalysis(ServerMessage msg)
        {
            var bottlenecks = msg.Get("bottlenecks")?.ToObject<List<Bottleneck>>() ?? new List<Bottleneck>();
            _warehouse.SetBottleneckMarkers(bottlenecks);
            var top = string.Join(", ", bottlenecks.Take(5).Select(b => $"({b.x},{b.y}) 대기 {b.wait}"));
            _ui.AnalysisText.text = $"<b>병목</b> {top}\n{msg.GetString("explanation")}";

            _ui.ClearProposals();
            var proposals = msg.Get("proposals")?.ToObject<List<Proposal>>() ?? new List<Proposal>();
            foreach (var p in proposals)
            {
                var prop = p;
                var b = UIFactory.Button(_ui.ProposalContainer, $"적용: {p.text}", () => ApproveProposal(prop), new Color(0.2f, 0.55f, 0.4f));
                b.GetComponentInChildren<Text>().fontSize = UIFactory.FontSmall;
                _ui.ProposalButtons.Add(b);
            }
            if (!_heatmap.Visible) { _heatmap.SetVisible(true); _ui.SetToggleText(_ui.HeatmapButton, "히트맵", true); }
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
            _simId = null;
            _fetchGeneration++;
            _playback.Begin(0);
            _heatmap.SetStats(null);
            _warehouse.ClearMarkers();
            _ui.ClearProposals();
            _ui.AnalysisText.text = "";
        }

        void ToggleViewMode()
        {
            _miniature = !_miniature;
            UIFactory.SetButtonText(_ui.ViewModeButton, _miniature ? "보기: 미니어처" : "보기: 실물 1:1");
            ApplyViewMode();
        }

        /// <summary>미니어처: 테이블 위 축소 모형 / 실물: 바닥에 1:1, 사용자 앞 남쪽 가장자리에서 시작.</summary>
        void ApplyViewMode()
        {
            var map = _warehouse.Map;
            if (map == null) return;
            var t = _warehouse.transform;
            var center = GridCoord.GridCenterLocal(map.width, map.height, map.cellSizeM);
            if (_miniature)
            {
                float longest = Mathf.Max(map.width, map.height) * map.cellSizeM;
                float s = miniatureSize / Mathf.Max(1f, longest);
                t.localScale = Vector3.one * s;
                t.position = tableCenter - center * s;
            }
            else
            {
                t.localScale = Vector3.one;
                t.position = new Vector3(-center.x, 0f, 1.5f);
            }
            t.rotation = Quaternion.identity;
            if (table != null) table.SetActive(_miniature);
        }

        void PlayTts(string url)
        {
            if (_api == null) return;
            StartCoroutine(_api.GetAudioClip(url, clip => { if (clip != null) _audio.PlayOneShot(clip); }));
        }

        // ------------------------------------------------------------------ 오프라인 재생 (시연 대비)

        void PlayOffline()
        {
            var mapAsset = Resources.Load<TextAsset>("Offline/w2_map");
            var framesAsset = Resources.Load<TextAsset>("Offline/s2_frames");
            var statsAsset = Resources.Load<TextAsset>("Offline/s2_stats");
            if (mapAsset == null || framesAsset == null)
            {
                _ui.Log("<color=#FF8080>오프라인 데이터가 없어요 (Resources/Offline)</color>");
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
            var frames = SimParsers.ParseFrames(framesAsset.text);
            _playback.Begin(frames.Count > 0 ? frames[frames.Count - 1].t : 0);
            _playback.Timeline.AddFrames(frames);
            _framesComplete = true;
            if (statsAsset != null) _heatmap.SetStats(SimParsers.ParseStats(statsAsset.text));
            _playback.ApplyPositions();
            _playback.Play();
            _ui.Log($"<b>오프라인 재생</b> W2 + S2: 로봇 {_playback.Timeline.RobotIds.Count}대, {_playback.Timeline.TotalSteps} 스텝");
            SetPhase(Phase.Ready);
        }

        // ------------------------------------------------------------------ 매 프레임

        void Update()
        {
            _ws.Poll(Time.time);

            // XR 초기화가 늦게 끝나는 경우를 위해 레이캐스터 설정을 다시 맞춘다
            bool xr = XRSupport.XRActive;
            if (_lastXrActive != xr)
            {
                _lastXrActive = xr;
                XRSupport.RefreshRaycasters(_ui.Left);
                XRSupport.RefreshRaycasters(_ui.Right);
            }

            // 오른손 A 버튼: 누르고 말하기 (질문 중이면 답변으로)
            bool a = XRSupport.RightButton(UnityEngine.XR.CommonUsages.primaryButton);
            if (a && !_prevA) BeginVoice(CurrentPhase == Phase.Question ? VoiceTarget.Answer : VoiceTarget.Describe);
            if (!a && _prevA) EndVoice();
            _prevA = a;

            var tl = _playback.Timeline;
            _updatingSlider = true;
            _ui.StepSlider.maxValue = Mathf.Max(1, tl.TotalSteps);
            _ui.StepSlider.value = tl.Time;
            _updatingSlider = false;
            string buffering = tl.IsBuffering ? "  (프레임 받는 중)" : "";
            _ui.StepText.text = $"스텝 {tl.CurrentStep} / {tl.TotalSteps}   받은 프레임 {tl.LoadedUntil + 1}{buffering}";
            UIFactory.SetButtonText(_ui.PlayButton, tl.Playing ? "일시정지" : "재생");
            _ui.SelectedRobotText.text = _playback.SelectedRobot ?? MainPanels.WaitSelectRobot;
        }
    }
}
