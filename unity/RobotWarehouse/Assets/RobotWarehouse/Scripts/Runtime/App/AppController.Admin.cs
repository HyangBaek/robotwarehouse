using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using RobotWarehouse.Network;
using RobotWarehouse.Playback;
using RobotWarehouse.UI;
using RobotWarehouse.XR;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace RobotWarehouse.App
{
    /// <summary>
    /// 관리자·개발자 모드 (ui_재료 '관리자/디버그 UI 설계').
    /// 사용자 UI = 무엇을 해야 하나 / 관리자 UI = 지금 무슨 일이 일어나나 / 개발자 UI = 왜 그렇게 됐나.
    /// 화면 값은 0.25초마다 보이는 탭만 갱신한다 (Quest 프레임 유지). 관리자 모드에서는 사용자 패널을 숨긴다.
    /// 내부 상태를 직접 고치는 기능은 두지 않는다 (설계 29장: 읽기 중심, 재연결·시나리오·재생 제어만).
    /// </summary>
    public partial class AppController
    {
        readonly AdminLog _log = new AdminLog();
        readonly AgentTracker _agent = new AgentTracker();
        DebugOverlay _overlay;
        bool _adminMode;

        // 연결 통계
        int _rxCount, _txCount, _wsReconnects;
        DateTime? _lastRx, _lastTx;
        float? _pingMs;
        bool? _healthOk;
        string _healthText = "--";

        // 시뮬레이션 기록
        string _simStrategy = "";
        (int robots, int inbound, int outbound)? _lastScenario;
        JObject _lastCompare;

        // 관리자 화면 상태
        float _adminTimer;
        int _runIndex = -1, _nodeIndex = -1;
        int _robotPage, _logPage;
        LogLevel? _logLevel;
        LogModule? _logModule;
        bool _showRaw;
        string _lastAnswerText = "";
        List<LogEntry> _logView = new List<LogEntry>();
        int _logViewVersion = -1;
        string _logViewKey = "";
        LogEntry _logSelected;
        int _exampleIndex = -1, _presetIndex = -1;
        bool _updatingAdminSlider;
        readonly List<string> _robotRowIds = new List<string>();

        const int LogPageSize = 10;

        // 관리자 진입 비밀 입력
        public const int GestureTaps = 3;            // Y를 몇 번
        public const float GestureWindow = 2f;       // 몇 초 안에
        public const float SettingsVisibleSec = 15f; // ≡ 버튼이 보이는 시간
        readonly List<float> _gestureTaps = new List<float>();
        float _settingsUntil;

        // 관리자 PIN
        public const int PinMaxFails = 3;          // 연속으로 틀리면
        public const float PinLockSec = 30f;       // 이만큼 잠금
        public const float PinGraceSec = 300f;     // 맞힌 뒤 5분 동안은 다시 묻지 않음
        string _pinInput = "";
        int _pinFails;
        float _pinLockUntil, _pinOkUntil;

        // ================================================================== 로그·Agent 기록

        void Log(LogLevel level, LogModule module, string message, string code = null, Vector2Int? cell = null, string raw = null)
            => _log.Add(level, module, message, code, cell, raw);

        static LogModule ModuleOf(Work w) => w switch
        {
            Work.Sim => LogModule.Sim,
            Work.Compare => LogModule.Sim,
            Work.None => LogModule.Vr,
            _ => LogModule.Agent
        };

        static LogModule ModuleOfCode(string code)
        {
            switch (code)
            {
                case "SCHEMA_INVALID":
                case "UNREACHABLE":
                case "NO_DOCK_IN":
                case "NO_DOCK_OUT":
                case "SIZE_EXCEEDED":
                case "IMPROVE_INVALID": return LogModule.Validator;
                case "COLLISION": return LogModule.Sim;
                case "STT_EMPTY":
                case "QUESTION_LIMIT": return LogModule.Agent;
                default: return LogModule.Api;
            }
        }

        static Vector2Int? ErrorCell(ServerMessage msg)
        {
            var cells = msg.GetCells("error_cells");
            if (cells.Count > 0) return new Vector2Int(cells[0].x, cells[0].y);
            var x = msg.Get("x");
            var y = msg.Get("y");
            if (x != null && y != null && x.Type == JTokenType.Integer && y.Type == JTokenType.Integer) return new Vector2Int((int)x, (int)y);
            return null;
        }

        void BeginAgentRun(Work work, string title)
        {
            string kind, input;
            switch (work)
            {
                case Work.Stt: kind = "음성 인식"; input = "(음성)"; break;
                case Work.Map:
                    bool answer = !string.IsNullOrEmpty(_questionId);
                    kind = _editPending ? "랙 편집 재검증" : answer ? "답변 반영" : "창고 생성";
                    input = _editPending ? "랙 이동" : answer ? _lastAnswerText : _lastRequestText;
                    break;
                case Work.Sim:
                    kind = _awaitingImprove ? "개선안 재시뮬레이션" : "시뮬레이션";
                    input = $"로봇 {_user.Robots.Value}대 · 입하 {_user.Inbound.Value} · 출하 {_user.Outbound.Value}";
                    _lastScenario = (_user.Robots.Value, _user.Inbound.Value, _user.Outbound.Value);
                    break;
                case Work.Analysis: kind = "병목 분석"; input = $"sim {_simId}"; break;
                case Work.Compare: kind = "전략 비교"; input = $"sim {_simId}"; break;
                default: kind = title; input = "-"; break;
            }
            _agent.Begin(kind, string.IsNullOrEmpty(input) ? "-" : input);
            _runIndex = -1;
            _nodeIndex = -1;
        }

        // ================================================================== 만들기·연결

        void BuildAdmin()
        {
            _user.AdminButton.onClick.AddListener(RequestAdmin);
            _user.PinCancel.onClick.AddListener(() => { _pinInput = ""; _user.ClosePin(); });
            _user.PinConfirm.onClick.AddListener(SubmitPin);
            for (int i = 0; i < _user.PinKeys.Count; i++)
            {
                int k = i;
                _user.PinKeys[i].onClick.AddListener(() => PinKey(k));
            }
            _overlay = _warehouse.gameObject.AddComponent<DebugOverlay>();
            _overlay.playback = _playback;
            _overlay.warehouse = _warehouse;

            _dev.UserModeButton.onClick.AddListener(() =>
                _dev.Confirm("사용자 모드로 돌아갈까요?", "관리자 패널을 닫고 사용자 작업 패널을 다시 보여 줍니다.", "확인",
                    () => SetAdminMode(false), danger: false));
            _dev.OnTabChanged += _ => _adminTimer = 1f;   // 탭을 바꾸면 바로 갱신

            // Dashboard · Connection
            _dev.ViewAllLogsButton.onClick.AddListener(() => _dev.ShowTab(AdminTab.Logs));
            _dev.PingButton.onClick.AddListener(Ping);
            _dev.ReconnectButton.onClick.AddListener(Connect);

            // Agent
            _dev.PrevRunButton.onClick.AddListener(() => StepRun(-1));
            _dev.NextRunButton.onClick.AddListener(() => StepRun(1));
            for (int i = 0; i < _dev.GraphNodes.Count; i++)
            {
                int idx = i;
                _dev.GraphNodes[i].button.onClick.AddListener(() => { _nodeIndex = idx; _adminTimer = 1f; });
            }

            // Simulation
            _dev.SimPlay.onClick.AddListener(() => _playback.Play());
            _dev.SimPause.onClick.AddListener(() => _playback.Pause());
            _dev.SimStop.onClick.AddListener(() => _dev.Confirm("재생을 멈출까요?", "재생을 멈추고 처음(스텝 0)으로 되돌립니다. 결과는 지워지지 않아요.", "Stop",
                () => { _playback.Pause(); _playback.Timeline.Seek(0); Log(LogLevel.Info, LogModule.Sim, "관리자: 재생 정지"); }));
            for (int i = 0; i < _dev.SimSpeed.Length; i++)
            {
                float sp = _dev.Speeds[i];
                _dev.SimSpeed[i].onClick.AddListener(() => { _playback.SetSpeed(sp); _user.HighlightSpeed(sp); _adminTimer = 1f; });
            }
            _dev.SimSlider.onValueChanged.AddListener(v => { if (!_updatingAdminSlider) _playback.Timeline.Seek(v); });
            _dev.Robots.OnRowClick += r => { if (r < _robotRowIds.Count) SelectRobotAdmin(_robotRowIds[r], false); };
            _dev.RobotPrev.onClick.AddListener(() => { _robotPage = Mathf.Max(0, _robotPage - 1); _adminTimer = 1f; });
            _dev.RobotNext.onClick.AddListener(() => { _robotPage++; _adminTimer = 1f; });
            _dev.FocusRobotButton.onClick.AddListener(() =>
            {
                var id = _playback.SelectedRobot ?? (_playback.Timeline.RobotIds.Count > 0 ? _playback.Timeline.RobotIds[0] : null);
                if (id != null) SelectRobotAdmin(id, true);
            });
            _dev.Overlay["로봇 ID"].onClick.AddListener(() => _overlay.SetRobotIds(!_overlay.ShowRobotIds));
            _dev.Overlay["대기 표시"].onClick.AddListener(() => _overlay.SetWaiting(!_overlay.ShowWaiting));
            _dev.Overlay["격자"].onClick.AddListener(() => _overlay.SetGrid(!_overlay.ShowGrid));
            _dev.Overlay["경로"].onClick.AddListener(() =>
            {
                _pathView.SetVisible(!_pathView.Visible);
                if (_pathView.Visible && _playback.SelectedRobot == null) _playback.SelectNextRobot(1);
            });
            _dev.Overlay["히트맵"].onClick.AddListener(() => _heatmap.SetVisible(!_heatmap.Visible));
            _dev.Overlay["병목"].onClick.AddListener(() => _warehouse.SetMarkersVisible(!_warehouse.MarkersVisible));
            foreach (var b in _dev.Overlay.Values) b.onClick.AddListener(() => _adminTimer = 1f);

            // Logs
            foreach (var kv in _dev.LevelFilter)
            {
                var name = kv.Key;
                kv.Value.onClick.AddListener(() =>
                {
                    _logLevel = name == "ALL" ? (LogLevel?)null : (LogLevel)Enum.Parse(typeof(LogLevel), name, true);
                    _logPage = 0;
                    _adminTimer = 1f;
                });
            }
            foreach (var kv in _dev.ModuleFilter)
            {
                var name = kv.Key;
                kv.Value.onClick.AddListener(() =>
                {
                    _logModule = name switch
                    {
                        "AGENT" => LogModule.Agent, "API" => LogModule.Api, "WS" => LogModule.Ws,
                        "SIM" => LogModule.Sim, "VR" => LogModule.Vr, _ => (LogModule?)null
                    };
                    _logPage = 0;
                    _adminTimer = 1f;
                });
            }
            _dev.LogSearch.onValueChanged.AddListener(_ => { _logPage = 0; _adminTimer = 1f; });
            _dev.LogPrev.onClick.AddListener(() => { _logPage = Mathf.Max(0, _logPage - 1); _adminTimer = 1f; });
            _dev.LogNext.onClick.AddListener(() => { _logPage++; _adminTimer = 1f; });
            _dev.LogTable.OnRowClick += r =>
            {
                int i = _logPage * LogPageSize + r;
                if (i < _logView.Count) { _logSelected = _logView[i]; _adminTimer = 1f; }
            };
            _dev.LogRawToggle.onClick.AddListener(() => { _showRaw = !_showRaw; _adminTimer = 1f; });
            _dev.LogFocus.onClick.AddListener(() =>
            {
                if (_logSelected?.Cell != null) _overlay.Focus(_logSelected.Cell.Value);
            });
            _dev.LogExport.onClick.AddListener(() =>
            {
                try { Log(LogLevel.Info, LogModule.Vr, "로그 저장: " + _log.Export()); }
                catch (Exception e) { Log(LogLevel.Error, LogModule.Vr, "로그 저장 실패: " + e.Message); }
            });
            _dev.LogClear.onClick.AddListener(() => _dev.Confirm("로그를 지울까요?", $"보관 중인 로그 {_log.Entries.Count}줄을 모두 지웁니다. 필요하면 먼저 Export 하세요.", "Clear",
                () => { _log.Clear(); _logSelected = null; _logPage = 0; }));

            // Scenario
            for (int i = 0; i < _dev.ExampleButtons.Count; i++)
            {
                int idx = i;
                _dev.ExampleButtons[i].onClick.AddListener(() => { _exampleIndex = idx; _adminTimer = 1f; });
            }
            _dev.ExampleFill.onClick.AddListener(() =>
            {
                if (_exampleIndex < 0) return;
                SetAdminMode(false);
                GoManual(VoiceTarget.Describe, DevPanel.ExampleSentences[_exampleIndex].text);
            });
            _dev.ExampleGenerate.onClick.AddListener(() =>
            {
                if (_exampleIndex < 0) return;
                RequestMapFromText(DevPanel.ExampleSentences[_exampleIndex].text);
                _dev.ShowTab(AdminTab.Agent);
            });
            for (int i = 0; i < _dev.PresetButtons.Count; i++)
            {
                int idx = i;
                _dev.PresetButtons[i].onClick.AddListener(() => { _presetIndex = idx; _adminTimer = 1f; });
            }
            _dev.PresetApply.onClick.AddListener(() => ApplyPreset(false));
            _dev.PresetRun.onClick.AddListener(() => ApplyPreset(true));
            _dev.OfflineButton.onClick.AddListener(PlayOffline);
            _dev.ViewModeButton.onClick.AddListener(ToggleViewMode);
            _dev.AddOrdersButton.onClick.AddListener(() => SendSimEvent(10, _user.Robots.Value));
            _dev.ChangeRobotsButton.onClick.AddListener(() => SendSimEvent(0, _user.Robots.Value));
            _dev.CompareButton.onClick.AddListener(() => { RequestCompare(); _dev.ShowTab(AdminTab.Agent); });

            SetAdminMode(showDevPanel);
        }

        /// <summary>
        /// 관리자 진입은 사용자가 우연히 누르지 않도록 두 단계로 둔다.
        /// 1) 왼손 그립을 누른 채 Y를 2초 안에 3번 → 사용자 패널 머리글에 ≡ 버튼이 15초 동안 나타남
        /// 2) ≡ 버튼을 레이로 눌러야 관리자 모드 진입
        /// 그립 없이 Y만 누르거나, 3번이 2초를 넘기면 세지 않는다. 에디터에서는 F1로 바로 전환(개발 편의, 빌드에는 없음).
        /// </summary>
        void TickAdminGesture()
        {
            bool y = XRSupport.LeftButton(UnityEngine.XR.CommonUsages.secondaryButton);
            bool grip = XRSupport.LeftButton(UnityEngine.XR.CommonUsages.gripButton);
            if (y && !_prevY && !_adminMode)
            {
                if (!grip) _gestureTaps.Clear();
                else
                {
                    float now = Time.unscaledTime;
                    _gestureTaps.Add(now);
                    _gestureTaps.RemoveAll(t => now - t > GestureWindow);
                    if (_gestureTaps.Count >= GestureTaps)
                    {
                        _gestureTaps.Clear();
                        ShowSettingsButton();
                    }
                }
            }
            _prevY = y;
            TickPin();
#if UNITY_EDITOR
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) SetAdminMode(!_adminMode);
#endif
            if (_settingsUntil > 0f && Time.unscaledTime > _settingsUntil)
            {
                _settingsUntil = 0f;
                _user.SetSettingsVisible(false);
            }
        }

        /// <summary>≡ 버튼: PIN이 필요하면 PIN 화면, 아니면 바로 관리자 모드.</summary>
        void RequestAdmin()
        {
            var sc = _user.Current;
            if (sc == UserScreen.Processing || sc == UserScreen.Recording)
            {
                _user.Toast("지금 작업이 끝난 뒤 열 수 있어요", ToastKind.Warning);
                return;
            }
            _settingsUntil = 0f;
            _user.SetSettingsVisible(false);
            if (!AppConfig.AdminPinRequired || Time.unscaledTime < _pinOkUntil)
            {
                SetAdminMode(true);
                return;
            }
            _pinInput = "";
            UpdatePinView();
            _user.ShowPin();
        }

        void PinKey(int k)
        {
            if (Time.unscaledTime < _pinLockUntil) return;
            if (k == 10) _pinInput = "";
            else if (k == 11) { if (_pinInput.Length > 0) _pinInput = _pinInput.Substring(0, _pinInput.Length - 1); }
            else if (_pinInput.Length < AppConfig.AdminPinLength) _pinInput += k.ToString();
            _user.PinMessage.text = "";
            UpdatePinView();
            if (_pinInput.Length == AppConfig.AdminPinLength) SubmitPin();   // 자리 수를 채우면 바로 확인
        }

        void SubmitPin()
        {
            if (Time.unscaledTime < _pinLockUntil) return;
            if (_pinInput.Length < AppConfig.AdminPinLength)
            {
                _user.PinMessage.text = $"{AppConfig.AdminPinLength}자리를 모두 입력하세요";
                return;
            }
            if (AppConfig.CheckAdminPin(_pinInput))
            {
                _pinInput = "";
                _pinFails = 0;
                _pinOkUntil = Time.unscaledTime + PinGraceSec;
                Log(LogLevel.Info, LogModule.Vr, "관리자 PIN 확인 → 관리자 모드");
                _user.ClosePin();
                SetAdminMode(true);
                return;
            }
            _pinInput = "";
            _pinFails++;
            Log(LogLevel.Warn, LogModule.Vr, $"관리자 PIN 틀림 ({_pinFails}/{PinMaxFails})", "PIN_FAIL");
            if (_pinFails >= PinMaxFails)
            {
                _pinFails = 0;
                _pinLockUntil = Time.unscaledTime + PinLockSec;
                Log(LogLevel.Warn, LogModule.Vr, $"관리자 PIN {PinMaxFails}번 틀림 → {PinLockSec:0}초 잠금", "PIN_LOCK");
            }
            else _user.PinMessage.text = $"PIN이 맞지 않아요 (남은 시도 {PinMaxFails - _pinFails}번)";
            UpdatePinView(error: true);
        }

        void UpdatePinView(bool error = false)
        {
            _user.SetPinDots(AppConfig.AdminPinLength, _pinInput.Length, error);
            bool locked = Time.unscaledTime < _pinLockUntil;
            foreach (var b in _user.PinKeys) UIFactory.SetInteractable(b, !locked);
            UIFactory.SetInteractable(_user.PinConfirm, !locked);
        }

        /// <summary>PIN 잠금 남은 시간 표시 (매 프레임, PIN 화면일 때만).</summary>
        void TickPin()
        {
            if (_user.Current != UserScreen.Pin) return;
            float left = _pinLockUntil - Time.unscaledTime;
            if (left > 0f)
            {
                var msg = $"여러 번 틀려서 잠겼어요. {Mathf.CeilToInt(left)}초 뒤 다시 시도하세요";
                if (_user.PinMessage.text != msg) _user.PinMessage.text = msg;
                if (_user.PinConfirm.interactable) UpdatePinView(true);
            }
            else if (!_user.PinConfirm.interactable)
            {
                _user.PinMessage.text = "";
                UpdatePinView();
            }
        }

        void ShowSettingsButton()
        {
            _settingsUntil = Time.unscaledTime + SettingsVisibleSec;
            _user.SetSettingsVisible(true);
            _user.Toast("관리자 메뉴가 열렸어요. 오른쪽 위 ≡ 를 누르세요", ToastKind.Info, 4f);
            Log(LogLevel.Debug, LogModule.Vr, "관리자 진입 입력 확인 → ≡ 버튼 표시");
        }

        /// <summary>사용자 ↔ 관리자 모드. 두 패널을 동시에 보이지 않는다 (설계 31장).</summary>
        void SetAdminMode(bool on)
        {
            _adminMode = on;
            _dev.SetVisible(on);
            _user.Canvas.gameObject.SetActive(!on);
            // 관리자 모드에서 나오면 ≡ 버튼을 잠깐 남겨 두어 바로 다시 들어갈 수 있게
            if (!on && _dev != null && _user != null && Time.unscaledTime > 1f)
            {
                _settingsUntil = Time.unscaledTime + SettingsVisibleSec;
                _user.SetSettingsVisible(true);
            }
            if (on)
            {
                _adminTimer = 1f;
                Log(LogLevel.Debug, LogModule.Vr, "관리자 모드 진입");
            }
        }

        void StepRun(int dir)
        {
            int n = _agent.Runs.Count;
            if (n == 0) return;
            int cur = _runIndex < 0 ? n - 1 : _runIndex;
            cur = Mathf.Clamp(cur + dir, 0, n - 1);
            _runIndex = cur == n - 1 ? -1 : cur;
            _nodeIndex = -1;
            _adminTimer = 1f;
        }

        void SelectRobotAdmin(string id, bool focus)
        {
            _playback.SelectRobot(id);
            _pathView.SetVisible(true);
            if (focus && _playback.TryGetRobotCell(id, out var cell))
                _overlay.Focus(new Vector2Int(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.y)), 4f);
            _adminTimer = 1f;
        }

        void ApplyPreset(bool run)
        {
            if (_presetIndex < 0) return;
            var p = DevPanel.ScenarioPresets[_presetIndex];
            _user.Robots.Value = p.robots;
            _user.Inbound.Value = p.inbound;
            _user.Outbound.Value = p.outbound;
            Log(LogLevel.Info, LogModule.Sim, $"프리셋 {p.id} 적용: 로봇 {p.robots}대, 입하 {p.inbound}, 출하 {p.outbound} (창고 {p.map} 기준)");
            if (!run) return;
            if (!_mapConfirmed)
            {
                Log(LogLevel.Warn, LogModule.Sim, $"Run Scenario: 지도가 확정되지 않았어요. {p.map} 창고를 만들고 확인한 뒤 실행하세요");
                return;
            }
            RunScenario();
            _dev.ShowTab(AdminTab.Agent);
        }

        /// <summary>/health 응답 시간 측정 (Connection · Ping).</summary>
        void Ping()
        {
            if (_api == null) _api = new ApiClient(AppConfig.HttpBase);
            var sw = Stopwatch.StartNew();
            StartCoroutine(_api.Get(ApiRoutes.Health, r =>
            {
                sw.Stop();
                _healthOk = r.Ok;
                _pingMs = r.Ok ? (float)sw.Elapsed.TotalMilliseconds : (float?)null;
                _healthText = r.Ok ? $"OK  {r.Text?.Trim()}" : r.ErrorMessage;
                if (r.Ok) Log(LogLevel.Debug, LogModule.Api, $"GET /health {sw.Elapsed.TotalMilliseconds:0}ms");
                else Log(LogLevel.Error, LogModule.Api, $"서버 응답 없음 ({AppConfig.HttpBase}): {r.ErrorMessage}. 에디터 메뉴 RobotWarehouse > 1. 서버 연결에서 주소 확인",
                    r.TimedOut ? "TIMEOUT" : "HEALTH_FAIL");
                _adminTimer = 1f;
            }));
        }

        // ================================================================== 갱신

        void TickAdmin()
        {
            if (!_adminMode) return;
            _adminTimer += Time.unscaledDeltaTime;
            if (_adminTimer < 0.25f) return;
            _adminTimer = 0f;
            RefreshHeader();
            switch (_dev.Current)
            {
                case AdminTab.Dashboard: RefreshDashboard(); break;
                case AdminTab.Connection: RefreshConnection(); break;
                case AdminTab.Agent: RefreshAgent(); break;
                case AdminTab.Simulation: RefreshSimulation(); break;
                case AdminTab.Logs: RefreshLogs(); break;
                case AdminTab.Scenario: RefreshScenario(); break;
                case AdminTab.Performance: RefreshPerformance(); break;
            }
        }

        static void T(Text t, string v) => DevPanel.SetText(t, v);

        void RefreshHeader()
        {
            if (_offline) _dev.SetSystem(DevPanel.StatusBlue, "OFFLINE PLAYBACK");
            else if (Connected && _healthOk != false) _dev.SetSystem(DevPanel.StatusGreen, "SYSTEM ONLINE");
            else if (_ws.State == WsState.Reconnecting || _ws.State == WsState.Connecting || (Connected && _healthOk == false))
                _dev.SetSystem(DevPanel.StatusYellow, "SYSTEM DEGRADED");
            else _dev.SetSystem(DevPanel.StatusRed, "SYSTEM OFFLINE");
        }

        (Color color, string text) WsView() => _ws.State switch
        {
            WsState.Connected => (DevPanel.StatusGreen, "CONNECTED"),
            WsState.Connecting => (DevPanel.StatusBlue, "CONNECTING"),
            WsState.Reconnecting => (DevPanel.StatusYellow, "RECONNECTING"),
            _ => (DevPanel.StatusGray, "DISCONNECTED")
        };

        (Color color, string text) SimView()
        {
            var tl = _playback.Timeline;
            if (CurrentPhase == Phase.Simulating) return (DevPanel.StatusBlue, "COMPUTING");
            if (tl.FrameCount == 0) return (DevPanel.StatusGray, "IDLE");
            if (tl.IsBuffering) return (DevPanel.StatusYellow, "BUFFERING");
            if (tl.Playing) return (DevPanel.StatusBlue, "PLAYING");
            if (tl.Finished) return (DevPanel.StatusGreen, "COMPLETED");
            return (DevPanel.StatusGreen, "READY");
        }

        (Color color, string text) RunView(AgentRun run)
        {
            if (run == null) return (DevPanel.StatusGray, "IDLE");
            return run.State switch
            {
                RunState.Running => (DevPanel.StatusBlue, "RUNNING"),
                RunState.Completed => (DevPanel.StatusGreen, "COMPLETED"),
                RunState.Question => (DevPanel.StatusYellow, "QUESTION"),
                RunState.Error => (DevPanel.StatusRed, "ERROR"),
                _ => (DevPanel.StatusGray, "IDLE")
            };
        }

        string OrdersText() => _lastScenario.HasValue ? $"{_lastScenario.Value.inbound} / {_lastScenario.Value.outbound}" : "--";

        void RefreshDashboard()
        {
            var tiles = _dev.Tiles;
            if (_healthOk == null) tiles["SERVER"].Set(DevPanel.StatusGray, "--", AppConfig.HttpBase);
            else if (_healthOk.Value) tiles["SERVER"].Set(DevPanel.StatusGreen, "ONLINE", $"{AppConfig.HttpBase} · {_pingMs:0}ms");
            else tiles["SERVER"].Set(DevPanel.StatusRed, "ERROR", _healthText);
            var ws = WsView();
            tiles["WEBSOCKET"].Set(ws.color, ws.text, $"RX {_rxCount} · 마지막 {(_lastRx.HasValue ? _lastRx.Value.ToString("HH:mm:ss") : "--")}");
            var run = _agent.Current;
            var rv = RunView(run);
            tiles["AGENT"].Set(rv.color, run == null ? "IDLE" : rv.text == "COMPLETED" ? "READY" : rv.text, run == null ? "" : $"{run.Kind} · {run.Seconds:0.0}s");
            var sv = SimView();
            tiles["SIMULATOR"].Set(sv.color, sv.text, _playback.Timeline.FrameCount > 0 ? $"스텝 {_playback.Timeline.CurrentStep} / {_playback.Timeline.TotalSteps}" : "");
            if (_warehouse.Map == null) tiles["MAP"].Set(DevPanel.StatusGray, "NONE");
            else if (!string.IsNullOrEmpty(_questionId)) tiles["MAP"].Set(DevPanel.StatusYellow, "VALIDATION", "검증 실패 · 질문 중");
            else if (_mapConfirmed) tiles["MAP"].Set(DevPanel.StatusGreen, "CONFIRMED", $"v{_mapVersion} · {_warehouse.Map.width}×{_warehouse.Map.height}");
            else tiles["MAP"].Set(DevPanel.StatusYellow, _offline ? "OFFLINE" : "UNCONFIRMED", $"v{_mapVersion}");
            if (XRSupport.XRActive) tiles["QUEST"].Set(DevPanel.StatusGreen, "CONNECTED", $"{UnityEngine.XR.XRSettings.loadedDeviceName} · {_fps} fps");
            else tiles["QUEST"].Set(DevPanel.StatusGray, "EDITOR", $"XR 꺼짐 · {_fps} fps");

            var tl = _playback.Timeline;
            T(_dev.Session["Session"], AppConfig.SessionId);
            T(_dev.Session["Map"], _warehouse.Map == null ? "--" : $"v{_mapVersion}  {_warehouse.Map.width}×{_warehouse.Map.height}");
            T(_dev.Session["Simulation"], _offline ? "offline (W2+S2)" : _simId ?? "--");
            T(_dev.Session["Robots"], tl.RobotIds.Count > 0 ? tl.RobotIds.Count.ToString() : "--");
            T(_dev.Session["Orders"], OrdersText());
            T(_dev.Session["Steps"], tl.FrameCount > 0 ? $"{tl.CurrentStep} / {tl.TotalSteps}" : "--");
            T(_dev.Session["Collision"], tl.FrameCount > 0 ? "0 (서버 검증 통과분만 수신)" : "--");
            T(_dev.Session["Mode"], _miniature ? "미니어처" : "실물 1:1");

            var recent = _log.Query(null, null, null, false);
            for (int r = 0; r < _dev.RecentEvents.Rows; r++)
            {
                if (r < recent.Count)
                {
                    var e = recent[r];
                    _dev.RecentEvents.Set(r, null, e.Time.ToString("HH:mm:ss"), e.Level.ToString().ToUpper(), e.Message);
                    _dev.RecentEvents.SetCellColor(r, 1, DevPanel.LevelColor(e.Level));
                }
                else _dev.RecentEvents.Clear(r);
            }
        }

        void RefreshConnection()
        {
            var c = _dev.Conn;
            T(c["server.Host"], AppConfig.Host);
            T(c["server.Port"], AppConfig.Port.ToString());
            T(c["server.Protocol"], "HTTP / WebSocket");
            T(c["server.Session"], AppConfig.SessionId);
            var ws = WsView();
            T(c["server.Status"], ws.text);
            c["server.Status"].color = ws.color;
            T(c["server.Health"], _healthOk == null ? "--" : _healthText);
            c["server.Health"].color = _healthOk == false ? DevPanel.StatusRed : UIFactory.TextMain;
            T(c["server.Ping"], _pingMs.HasValue ? $"{_pingMs:0} ms" : "--");
            T(c["ws.Status"], ws.text);
            c["ws.Status"].color = ws.color;
            T(c["ws.URL"], ApiRoutes.WebSocket(AppConfig.SessionId));
            T(c["ws.Last RX"], DevPanel.Ago(_lastRx));
            T(c["ws.Last TX"], DevPanel.Ago(_lastTx) + "  (REST)");
            T(c["ws.RX msgs"], _rxCount.ToString("N0"));
            T(c["ws.TX reqs"], _txCount.ToString("N0"));
            T(c["ws.Reconnects"], _wsReconnects.ToString());
            T(c["quest.Device"], XRSupport.XRActive ? UnityEngine.XR.XRSettings.loadedDeviceName : "없음 (에디터)");
            T(c["quest.XR"], XRSupport.XRActive ? "ACTIVE" : "OFF");
            T(c["quest.FPS"], _fps.ToString());
            T(c["quest.View"], _miniature ? "미니어처" : "실물 1:1");
        }

        AgentRun ShownRun()
        {
            var runs = _agent.Runs;
            if (runs.Count == 0) return null;
            return _runIndex >= 0 && _runIndex < runs.Count ? runs[_runIndex] : runs[runs.Count - 1];
        }

        void RefreshAgent()
        {
            var run = ShownRun();
            var rv = RunView(run);
            T(_dev.Run["Run ID"], run == null ? "--" : $"{run.Id}  ({(_runIndex < 0 ? "최신" : $"{_runIndex + 1}/{_agent.Runs.Count}")})");
            T(_dev.Run["Kind"], run?.Kind ?? "--");
            T(_dev.Run["Status"], rv.text);
            _dev.Run["Status"].color = rv.color;
            T(_dev.Run["Duration"], run == null ? "--" : $"{run.Seconds:0.00} sec");
            T(_dev.Run["Result"], run?.Result ?? "--");
            T(_dev.AgentInput, run == null ? "아직 실행 기록이 없어요" : $"\"{run.Input}\"");
            UIFactory.SetInteractable(_dev.PrevRunButton, _agent.Runs.Count > 1 && (_runIndex != 0));
            UIFactory.SetInteractable(_dev.NextRunButton, _runIndex >= 0);

            // 노드 목록: 서버 status 노드 + 마지막 Response
            var nodes = new List<(string title, string sub, Color color, AgentNode node)>();
            if (run != null)
            {
                foreach (var n in run.Nodes)
                {
                    var (glyph, col, state) = n.State switch
                    {
                        NodeState.Completed => ("✓", DevPanel.StatusGreen, $"{n.Seconds:0.00} sec"),
                        NodeState.Running => ("●", DevPanel.StatusBlue, $"RUNNING  {n.Seconds:0.0}s"),
                        NodeState.Error => ("×", DevPanel.StatusRed, "ERROR"),
                        _ => ("○", DevPanel.StatusGray, "WAITING")
                    };
                    nodes.Add(($"{glyph}  {n.Name}", state, col, n));
                }
                var resp = run.State switch
                {
                    RunState.Running => ("○  Response", "WAITING", DevPanel.StatusGray),
                    RunState.Error => ("×  Response", run.Result ?? "ERROR", DevPanel.StatusRed),
                    RunState.Question => ("✓  Response", "question → 사용자", DevPanel.StatusYellow),
                    _ => ("✓  Response", run.Result ?? "", DevPanel.StatusGreen)
                };
                nodes.Add((resp.Item1, resp.Item2, resp.Item3, null));
            }
            int start = Mathf.Max(0, nodes.Count - _dev.GraphNodes.Count);
            for (int i = 0; i < _dev.GraphNodes.Count; i++)
            {
                var g = _dev.GraphNodes[i];
                int k = start + i;
                bool on = k < nodes.Count;
                g.button.gameObject.SetActive(on);
                if (!on) continue;
                T(g.title, nodes[k].title);
                T(g.sub, nodes[k].sub);
                g.dot.color = nodes[k].color;
                bool sel = _nodeIndex < 0 ? k == nodes.Count - 1 : i == _nodeIndex;
                UIFactory.SetButtonColor(g.button, sel ? new Color(0.25f, 0.5f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.07f));
            }

            // 노드 상세
            int selK = _nodeIndex < 0 ? nodes.Count - 1 : start + _nodeIndex;
            if (selK >= 0 && selK < nodes.Count)
            {
                var sel = nodes[selK];
                var n = sel.node;
                T(_dev.NodeDetail["Node"], n?.Name ?? "Response");
                T(_dev.NodeDetail["Status"], sel.sub);
                _dev.NodeDetail["Status"].color = sel.color;
                T(_dev.NodeDetail["Duration"], n != null ? $"{n.Seconds:0.00} sec" : run != null ? $"전체 {run.Seconds:0.00} sec" : "--");
                T(_dev.NodeDetail["Tool"], string.IsNullOrEmpty(n?.Tool) ? "--" : n.Tool);
                T(_dev.NodeDetail["Message"], n?.Message ?? run?.Result ?? "--");
                var raw = n != null ? n.Raw : run?.ResultRaw;
                T(_dev.NodeRaw, string.IsNullOrEmpty(raw) ? "" : "RAW  " + Trim(raw, 360));
            }
            else
            {
                foreach (var t in _dev.NodeDetail.Values) T(t, "--");
                T(_dev.NodeRaw, "");
            }

            var timeline = _agent.Timeline(_dev.ToolTimeline.Rows);
            for (int r = 0; r < _dev.ToolTimeline.Rows; r++)
            {
                if (r >= timeline.Count) { _dev.ToolTimeline.Clear(r); continue; }
                var (rn, node) = timeline[r];
                var st = node.State switch
                {
                    NodeState.Completed => "✓ 완료", NodeState.Running => "● 실행 중", NodeState.Error => "× 오류", _ => "○ 대기"
                };
                _dev.ToolTimeline.Set(r, null, node.Start.ToString("HH:mm:ss"),
                    string.IsNullOrEmpty(node.Tool) ? node.Name : $"{node.Name} · {node.Tool}",
                    st, node.State == NodeState.Running ? "running" : $"{node.Seconds:0.00}s");
                _dev.ToolTimeline.SetCellColor(r, 2, node.State switch
                {
                    NodeState.Completed => DevPanel.StatusGreen, NodeState.Running => DevPanel.StatusBlue,
                    NodeState.Error => DevPanel.StatusRed, _ => DevPanel.StatusGray
                });
            }
        }

        static string Trim(string s, int max)
        {
            s = s.Replace("\n", " ").Replace("\r", "");
            return s.Length <= max ? s : s.Substring(0, max) + " …";
        }

        void RefreshSimulation()
        {
            var tl = _playback.Timeline;
            T(_dev.SimSession["Map"], _warehouse.Map == null ? "--" : $"v{_mapVersion}{(_mapConfirmed ? " (확정)" : "")}");
            T(_dev.SimSession["Simulation"], _offline ? "offline W2+S2" : _simId ?? "--");
            T(_dev.SimSession["Robots"], tl.RobotIds.Count > 0 ? tl.RobotIds.Count.ToString() : "--");
            T(_dev.SimSession["Orders"], OrdersText());
            T(_dev.SimSession["Strategy"], string.IsNullOrEmpty(_simStrategy) ? "--" : _simStrategy);

            var sv = SimView();
            T(_dev.SimStatus["State"], sv.text);
            _dev.SimStatus["State"].color = sv.color;
            float pct = tl.TotalSteps > 0 ? tl.CurrentStep * 100f / tl.TotalSteps : 0f;
            T(_dev.SimStatus["Step"], tl.FrameCount > 0 ? $"{tl.CurrentStep} / {tl.TotalSteps}  ({pct:0}%)" : "--");
            int waiting = 0;
            foreach (var id in tl.RobotIds)
                if (tl.TryGetRobot(id, out _, out var st) && IsWaitState(st)) waiting++;
            T(_dev.SimStatus["Waiting now"], tl.FrameCount > 0 ? $"{waiting}대" : "--");
            T(_dev.SimStatus["Collision"], tl.FrameCount > 0 ? "0" : "--");
            T(_dev.SimStatus["Runtime"], tl.FrameCount > 0 ? $"{tl.Time / Mathf.Max(0.01f, tl.StepsPerSecond):0.0} s (1x 기준)" : "--");

            DevPanel.Toggle(_dev.Overlay["로봇 ID"], _overlay.ShowRobotIds, UIFactory.Accent);
            DevPanel.Toggle(_dev.Overlay["대기 표시"], _overlay.ShowWaiting, UIFactory.Danger);
            DevPanel.Toggle(_dev.Overlay["경로"], _pathView.Visible, UIFactory.Accent);
            DevPanel.Toggle(_dev.Overlay["격자"], _overlay.ShowGrid, UIFactory.Accent);
            DevPanel.Toggle(_dev.Overlay["히트맵"], _heatmap.Visible, UIFactory.Highlight);
            DevPanel.Toggle(_dev.Overlay["병목"], _warehouse.MarkersVisible, UIFactory.Danger);

            DevPanel.Toggle(_dev.SimPlay, tl.Playing, UIFactory.Primary);
            for (int i = 0; i < _dev.SimSpeed.Length; i++)
                DevPanel.Toggle(_dev.SimSpeed[i], Mathf.Approximately(tl.Speed, _dev.Speeds[i]), UIFactory.Primary);
            _updatingAdminSlider = true;
            _dev.SimSlider.maxValue = Mathf.Max(1, tl.TotalSteps);
            _dev.SimSlider.value = tl.CurrentStep;
            _updatingAdminSlider = false;
            T(_dev.SimPercent, $"{pct:0}%");

            // 로봇 표
            var ids = tl.RobotIds;
            int pages = Mathf.Max(1, Mathf.CeilToInt(ids.Count / (float)_dev.Robots.Rows));
            _robotPage = Mathf.Clamp(_robotPage, 0, pages - 1);
            T(_dev.RobotPage, $"{_robotPage + 1} / {pages}");
            _robotRowIds.Clear();
            for (int r = 0; r < _dev.Robots.Rows; r++)
            {
                int i = _robotPage * _dev.Robots.Rows + r;
                if (i >= ids.Count) { _dev.Robots.Clear(r); continue; }
                var id = ids[i];
                _robotRowIds.Add(id);
                string state = "--", task = "--", pos = "--";
                if (tl.TryGetFrame(tl.CurrentStep, out var f))
                    foreach (var rs in f.robots)
                        if (rs.id == id) { state = rs.state; task = string.IsNullOrEmpty(rs.taskId) ? "--" : rs.taskId; pos = $"{rs.x}, {rs.y}"; break; }
                bool sel = _playback.SelectedRobot == id;
                _dev.Robots.Set(r, null, id, (state ?? "--").ToUpper(), task, pos, _overlay.WaitCount(id, tl.CurrentStep).ToString(),
                    sel && _pathView.Visible ? "● ON" : "OFF");
                _dev.Robots.SetCellColor(r, 1, RobotPlayback.StateColor(state));
                if (sel && _pathView.Visible) _dev.Robots.SetCellColor(r, 5, UIFactory.Lighten(UIFactory.Accent, 0.3f));
            }
            int selRow = _playback.SelectedRobot == null ? -1 : _robotRowIds.IndexOf(_playback.SelectedRobot);
            _dev.Robots.Select(selRow);
        }

        static bool IsWaitState(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "wait":
                case "waiting":
                case "blocked": return true;
                default: return false;
            }
        }

        void RefreshLogs()
        {
            string search = _dev.LogSearch.text?.Trim();
            string key = $"{_logLevel}|{_logModule}|{search}";
            if (_log.Version != _logViewVersion || key != _logViewKey)
            {
                _logView = _log.Query(_logLevel, _logModule, search, _logLevel == LogLevel.Debug);
                _logViewVersion = _log.Version;
                _logViewKey = key;
            }
            int pages = Mathf.Max(1, Mathf.CeilToInt(_logView.Count / (float)LogPageSize));
            _logPage = Mathf.Clamp(_logPage, 0, pages - 1);
            T(_dev.LogPage, $"{_logPage + 1} / {pages}  ({_logView.Count}줄)");
            int selRow = -1;
            for (int r = 0; r < _dev.LogTable.Rows; r++)
            {
                int i = _logPage * LogPageSize + r;
                if (i >= _logView.Count) { _dev.LogTable.Clear(r); continue; }
                var e = _logView[i];
                if (e == _logSelected) selRow = r;
                var msg = e.Code != null ? $"[{e.Code}] {e.Message}" : e.Message;
                _dev.LogTable.Set(r, null, e.Time.ToString("HH:mm:ss"), e.Level.ToString().ToUpper(), e.Module.ToString().ToUpper(), msg);
                _dev.LogTable.SetCellColor(r, 1, DevPanel.LevelColor(e.Level));
                if (e.Level == LogLevel.Error) _dev.LogTable.SetCellColor(r, 3, UIFactory.Lighten(DevPanel.StatusRed, 0.35f));
            }
            _dev.LogTable.Select(selRow);

            foreach (var kv in _dev.LevelFilter)
                DevPanel.Toggle(kv.Value, kv.Key == "ALL" ? _logLevel == null : _logLevel?.ToString().ToUpper() == kv.Key, UIFactory.Primary);
            foreach (var kv in _dev.ModuleFilter)
                DevPanel.Toggle(kv.Value, kv.Key == "ALL" ? _logModule == null : _logModule?.ToString().ToUpper() == kv.Key, UIFactory.Primary);

            var s = _logSelected;
            T(_dev.LogDetail["Time"], s == null ? "--" : s.Time.ToString("HH:mm:ss.fff"));
            T(_dev.LogDetail["Level"], s == null ? "--" : s.Level.ToString().ToUpper());
            _dev.LogDetail["Level"].color = s == null ? UIFactory.TextMain : DevPanel.LevelColor(s.Level);
            T(_dev.LogDetail["Module"], s == null ? "--" : s.Module.ToString().ToUpper());
            T(_dev.LogDetail["Code"], s?.Code ?? "--");
            T(_dev.LogDetail["Position"], s?.Cell != null ? $"({s.Cell.Value.x}, {s.Cell.Value.y})" : "--");
            T(_dev.LogDetail["Message"], s == null ? "줄을 누르면 자세히 보여요" : s.Message);
            T(_dev.LogRaw, s == null || !_showRaw ? "" : string.IsNullOrEmpty(s.Raw) ? "(원본 JSON 없음)" : Trim(s.Raw, 500));
            UIFactory.SetInteractable(_dev.LogFocus, s?.Cell != null && _warehouse.Map != null);
            UIFactory.SetInteractable(_dev.LogRawToggle, s != null);
            DevPanel.Toggle(_dev.LogRawToggle, _showRaw, UIFactory.Accent);
        }

        void RefreshScenario()
        {
            for (int i = 0; i < _dev.ExampleButtons.Count; i++)
                DevPanel.Toggle(_dev.ExampleButtons[i], i == _exampleIndex, UIFactory.Primary);
            T(_dev.ExampleText, _exampleIndex < 0 ? "예시를 고르세요" : $"\"{DevPanel.ExampleSentences[_exampleIndex].text}\"");
            UIFactory.SetInteractable(_dev.ExampleFill, _exampleIndex >= 0);
            UIFactory.SetInteractable(_dev.ExampleGenerate, _exampleIndex >= 0 && Connected && CurrentPhase != Phase.Generating);

            for (int i = 0; i < _dev.PresetButtons.Count; i++)
                DevPanel.Toggle(_dev.PresetButtons[i], i == _presetIndex, UIFactory.Primary);
            if (_presetIndex >= 0)
            {
                var p = DevPanel.ScenarioPresets[_presetIndex];
                T(_dev.PresetText, $"{p.id}: 창고 {p.map} · 로봇 {p.robots}대 · 입하 {p.inbound} + 출하 {p.outbound}");
            }
            else T(_dev.PresetText, "프리셋을 고르세요");
            UIFactory.SetInteractable(_dev.PresetApply, _presetIndex >= 0);
            UIFactory.SetInteractable(_dev.PresetRun, _presetIndex >= 0 && Connected && _mapConfirmed && CurrentPhase != Phase.Simulating);

            bool hasSim = !string.IsNullOrEmpty(_simId) && !_offline && Connected;
            bool busy = CurrentPhase == Phase.Generating || CurrentPhase == Phase.Simulating;
            UIFactory.SetInteractable(_dev.AddOrdersButton, hasSim && !busy);
            UIFactory.SetInteractable(_dev.ChangeRobotsButton, hasSim && !busy);
            UIFactory.SetInteractable(_dev.CompareButton, hasSim && !busy);
            UIFactory.SetButtonText(_dev.ViewModeButton, _miniature ? "보기: 미니어처" : "보기: 실물 1:1");

            var b = _lastCompare?["baseline"] as JObject;
            var o = _lastCompare?["optimized"] as JObject;
            var keys = b?.Properties().Select(p => p.Name).Where(k => o?[k] != null).Take(_dev.CompareTable.Rows).ToList() ?? new List<string>();
            for (int r = 0; r < _dev.CompareTable.Rows; r++)
            {
                if (r < keys.Count) _dev.CompareTable.Set(r, null, keys[r], b[keys[r]].ToString(), o[keys[r]].ToString());
                else _dev.CompareTable.Clear(r);
            }
            if (_lastCompare != null)
            {
                // improvement_pct 가 null 이면(비교 불가) JValue 를 float 로 바꾸다 예외가 나므로 형식을 먼저 확인
                var pv = _lastCompare["improvement_pct"];
                bool hasPct = pv != null && pv.Type != JTokenType.Null;
                float pct = hasPct ? (float)pv : 0f;
                T(_dev.CompareSummary, hasPct ? $"Improvement  {pct:0.0}%" : $"Improvement  n/a ({(string)_lastCompare["note"] ?? "incomplete"})");
                _dev.CompareSummary.color = hasPct && pct >= 0 ? UIFactory.Lighten(DevPanel.StatusGreen, 0.2f) : DevPanel.StatusYellow;
            }
        }

        void RefreshPerformance()
        {
            var p = _dev.Perf;
            T(p["app.FPS"], _fps.ToString());
            T(p["app.Frame time"], _fps > 0 ? $"{1000f / _fps:0.0} ms" : "--");
            T(p["app.Memory"], $"{UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f):0} MB");
            T(p["app.Robots drawn"], _playback.Timeline.RobotIds.Count.ToString());
            T(p["app.Draw mode"], (_miniature ? "미니어처" : "실물 1:1") + (_warehouse.Skinned ? " · 에셋 랙" : " · 기본 도형"));
            T(p["net.Ping"], _pingMs.HasValue ? $"{_pingMs:0} ms" : "--");
            T(p["net.RX msgs"], _rxCount.ToString("N0"));
            T(p["net.TX reqs"], _txCount.ToString("N0"));
            T(p["net.Last RX"], DevPanel.Ago(_lastRx));
            var tl = _playback.Timeline;
            T(p["sim.Steps / sec"], tl.FrameCount > 0 ? $"{tl.StepsPerSecond * tl.Speed:0.#}" : "--");
            T(p["sim.Frames loaded"], tl.FrameCount > 0 ? $"{tl.LoadedUntil + 1} / {tl.TotalSteps + 1}" : "--");
            T(p["sim.Buffering"], tl.FrameCount > 0 ? (tl.IsBuffering ? "예" : "아니오") : "--");
            T(p["sim.Inventory events"], _inventory.EventCount.ToString());
        }
    }
}
