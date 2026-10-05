using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>관리자 패널 탭 (관리자, 디버그 UI 설계 4장, 37장 1차 구현 + 시연 제어).</summary>
    public enum AdminTab { Dashboard, Connection, Agent, Simulation, Logs, Scenario, Performance }

    /// <summary>
    /// 관리자, 개발자 패널 (ui_재료 '관리자/디버그 UI 설계').
    /// 1600 x 900px (16:9) 캔버스를 0.8mm/px로 -> 1.28m x 0.72m.
    /// 구조: Header(80) / 왼쪽 Navigation(280) + 오른쪽 Content. 사용자 패널과 동시에 보이지 않는다 (모드 전환).
    /// 값 채우기, 버튼 동작은 AppController(Admin 부분)가 한다. 이 클래스는 화면 구성만 맡는다.
    /// </summary>
    public class DevPanel
    {
        public const float Width = 1600f, Height = 900f, MetersPerPixel = 0.0008f;
        public const int FTitle = 34, FSection = 24, FBody = 22, FSmall = 19;
        public const float BtnH = 56f;

        public static readonly (string id, string text)[] ExampleSentences =
        {
            ("W1", "랙 4줄, 통로 폭 3m, 입하 도크 1개, 출하 도크 1개로 만들어줘"),
            ("W2", "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개, 충전 구역은 오른쪽 벽 쪽에"),
            ("W3", "랙 6줄을 좌우 두 구역으로 나누고 가운데 큰 통로는 북쪽 방향 일방통행"),
            ("W4", "랙 10줄을 통로 없이 붙여서 배치해줘"),
            ("W5", "랙 6줄, 통로 폭 3m"),
            ("W6", "적당한 크기로 창고 하나 만들어줘"),
        };

        /// <summary>시나리오 프리셋 (Scenario Manager): 이름, 창고, 로봇, 입하, 출하</summary>
        public static readonly (string id, string map, int robots, int inbound, int outbound)[] ScenarioPresets =
        {
            ("S1", "W1", 2, 10, 10),
            ("S2", "W2", 4, 25, 25),
            ("S2-H", "W2", 8, 25, 25),
            ("S3", "W3", 4, 25, 25),
        };

        public static readonly Color StatusGreen = UIFactory.Hsl(130, 0.65f, 0.48f);
        public static readonly Color StatusYellow = UIFactory.Hsl(45, 0.95f, 0.52f);
        public static readonly Color StatusRed = UIFactory.Hsl(0, 0.85f, 0.55f);
        public static readonly Color StatusBlue = UIFactory.Hsl(210, 0.95f, 0.58f);
        public static readonly Color StatusGray = new Color(0.45f, 0.48f, 0.53f);
        static readonly Color NavBg = new Color(0f, 0f, 0f, 0.22f);
        static readonly Color CardBg = new Color(1f, 1f, 1f, 0.05f);

        public Canvas Canvas;
        public AdminTab Current { get; private set; }
        public event Action<AdminTab> OnTabChanged;
        public bool Visible => Canvas != null && Canvas.gameObject.activeSelf;

        // Header
        public Text SystemStatus;
        Image _systemDot;

        // Navigation
        readonly Dictionary<AdminTab, Button> _nav = new Dictionary<AdminTab, Button>();
        readonly Dictionary<AdminTab, GameObject> _pages = new Dictionary<AdminTab, GameObject>();
        public Button UserModeButton;

        // Dashboard
        public readonly Dictionary<string, StatusTile> Tiles = new Dictionary<string, StatusTile>();
        public readonly Dictionary<string, Text> Session = new Dictionary<string, Text>();
        public UITable RecentEvents;
        public Button ViewAllLogsButton;

        // Connection
        public readonly Dictionary<string, Text> Conn = new Dictionary<string, Text>();
        public Button PingButton, ReconnectButton;

        // Agent
        public readonly Dictionary<string, Text> Run = new Dictionary<string, Text>();
        public Text AgentInput;
        public Button PrevRunButton, NextRunButton;
        public readonly List<(Button button, Image dot, Text title, Text sub)> GraphNodes = new List<(Button, Image, Text, Text)>();
        public readonly Dictionary<string, Text> NodeDetail = new Dictionary<string, Text>();
        public Text NodeRaw;
        public UITable ToolTimeline;

        // Simulation
        public readonly Dictionary<string, Text> SimSession = new Dictionary<string, Text>();
        public readonly Dictionary<string, Text> SimStatus = new Dictionary<string, Text>();
        public Button SimPlay, SimPause, SimStop;
        public Button[] SimSpeed;
        public readonly float[] Speeds = { 1f, 2f, 4f };
        public Slider SimSlider;
        public Text SimPercent;
        public UITable Robots;
        public Button RobotPrev, RobotNext, FocusRobotButton;
        public Text RobotPage;
        public readonly Dictionary<string, Button> Overlay = new Dictionary<string, Button>();
        public static readonly string[] OverlayNames = { "로봇 ID", "대기 표시", "경로", "격자", "히트맵", "병목" };

        // Logs
        public readonly Dictionary<string, Button> LevelFilter = new Dictionary<string, Button>();
        public readonly Dictionary<string, Button> ModuleFilter = new Dictionary<string, Button>();
        public InputField LogSearch;
        public Button LogExport, LogClear, LogPrev, LogNext, LogFocus, LogRawToggle;
        public Text LogPage;
        public UITable LogTable;
        public readonly Dictionary<string, Text> LogDetail = new Dictionary<string, Text>();
        public Text LogRaw;

        // Scenario
        public readonly List<Button> ExampleButtons = new List<Button>();
        public Text ExampleText;
        public Button ExampleFill, ExampleGenerate;
        public readonly List<Button> PresetButtons = new List<Button>();
        public Text PresetText;
        public Button PresetApply, PresetRun, OfflineButton, AddOrdersButton, ChangeRobotsButton, ViewModeButton, CompareButton;
        public UITable CompareTable;
        public Text CompareSummary;

        // Performance
        public readonly Dictionary<string, Text> Perf = new Dictionary<string, Text>();

        // Modal
        GameObject _modal;
        Text _modalTitle, _modalBody;
        Button _modalOk;
        Action _modalAction;

        // ================================================================== 만들기

        public void Build(Transform parent)
        {
            Canvas = UIFactory.CreateWorldCanvas("AdminPanel", new Vector2(Width, Height), parent, MetersPerPixel, 0, 0);
            ((RectTransform)Canvas.transform).pivot = new Vector2(0.5f, 0f);
            Canvas.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.94f);
            BuildHeader(Canvas.transform);
            UIFactory.Divider(Canvas.transform);

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(Canvas.transform, false);
            body.AddComponent<LayoutElement>().flexibleHeight = 1;
            var hl = body.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;

            BuildNav(body.transform);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(body.transform, false);
            content.AddComponent<LayoutElement>().flexibleWidth = 1;
            var ct = (RectTransform)content.transform;

            BuildDashboard(Page(ct, AdminTab.Dashboard));
            BuildConnection(Page(ct, AdminTab.Connection));
            BuildAgent(Page(ct, AdminTab.Agent));
            BuildSimulation(Page(ct, AdminTab.Simulation));
            BuildLogs(Page(ct, AdminTab.Logs));
            BuildScenario(Page(ct, AdminTab.Scenario));
            BuildPerformance(Page(ct, AdminTab.Performance));
            BuildModal();
            ShowTab(AdminTab.Dashboard);
        }

        void BuildHeader(Transform root)
        {
            var row = UIFactory.Row(root, 80, 18, false);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(28, 28, 0, 0);
            var t = UIFactory.Label(row, "ROBOT WAREHOUSE", FTitle, UIFactory.TextMain);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.SetWidth(t, 340);
            var sub = UIFactory.Label(row, "ADMIN / DEBUG", FSection, UIFactory.Lighten(UIFactory.Warning, 0.2f));
            sub.fontStyle = FontStyle.Bold;
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.SetWidth(sub, 220);
            UIFactory.Spacer(row);
            (_systemDot, _) = UIFactory.Badge(row, 22, "", StatusGray);
            SystemStatus = UIFactory.Label(row, "SYSTEM OFFLINE", FSection, UIFactory.TextMain);
            SystemStatus.fontStyle = FontStyle.Bold;
            SystemStatus.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.SetWidth(SystemStatus, 260);
        }

        static readonly (AdminTab tab, string label, string hint)[] NavItems =
        {
            (AdminTab.Dashboard, "Dashboard", "시스템 상태"),
            (AdminTab.Connection, "Connection", "서버·WS·Quest"),
            (AdminTab.Agent, "Agent", "실행 흐름"),
            (AdminTab.Simulation, "Simulation", "재생·로봇"),
            (AdminTab.Logs, "Logs", "이벤트 기록"),
            (AdminTab.Scenario, "Scenario", "시연 데이터"),
            (AdminTab.Performance, "Performance", "FPS·네트워크"),
        };

        void BuildNav(Transform body)
        {
            var nav = UIFactory.Column(body, 8, TextAnchor.UpperCenter, "Navigation");
            var vl = nav.GetComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(16, 16, 18, 18);
            var le = nav.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 280;
            var bg = nav.gameObject.AddComponent<Image>();
            bg.color = NavBg;
            foreach (var (tab, label, hint) in NavItems)
            {
                var t = tab;
                var b = UIFactory.Button(nav, "", () => ShowTab(t), UIFactory.Secondary, -1, 72, FBody);
                var txt = b.GetComponentInChildren<Text>();
                txt.alignment = TextAnchor.MiddleLeft;
                txt.text = $"{label}\n<size={FSmall - 2}><color={UIFactory.Hex(UIFactory.TextDim)}>{hint}</color></size>";
                txt.rectTransform.offsetMin = new Vector2(22, 4);
                _nav[tab] = b;
            }
            UIFactory.Spacer(nav);
            UserModeButton = UIFactory.Button(nav, "←  사용자 모드", null, new Color(1f, 1f, 1f, 0.12f), -1, 64, FBody);
            UIFactory.Label(nav, "진입: 왼손 그립 + Y 3번 → ≡", FSmall - 3, UIFactory.TextMuted, TextAnchor.MiddleCenter);
        }

        RectTransform Page(RectTransform content, AdminTab tab)
        {
            var go = new GameObject("Page_" + tab, typeof(RectTransform));
            go.transform.SetParent(content, false);
            UIFactory.Stretch((RectTransform)go.transform);
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(24, 24, 20, 20);
            vl.spacing = 14;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            var title = UIFactory.Label(go.transform, tab.ToString(), FTitle - 4, UIFactory.TextMain);
            title.fontStyle = FontStyle.Bold;
            UIFactory.SetHeight(title, 40);
            _pages[tab] = go;
            go.SetActive(false);
            return (RectTransform)go.transform;
        }

        public void ShowTab(AdminTab tab)
        {
            foreach (var kv in _pages) kv.Value.SetActive(kv.Key == tab);
            foreach (var kv in _nav) UIFactory.SetButtonColor(kv.Value, kv.Key == tab ? UIFactory.Primary : new Color(1f, 1f, 1f, 0.06f));
            bool changed = Current != tab;
            Current = tab;
            if (changed) OnTabChanged?.Invoke(tab);
        }

        // ---------------------------------------------------------------- 공통 조각

        /// <summary>제목 있는 카드. width <= 0이면 남는 폭을 나눠 가진다.</summary>
        public static RectTransform Card(Transform parent, string title, float width = -1f, float height = -1f)
        {
            var go = new GameObject("Card_" + title, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = CardBg;
            UIFactory.MakeRounded(img, 14);
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(18, 18, 14, 16);
            vl.spacing = 8;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            var le = go.AddComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = le.minWidth = width; le.flexibleWidth = 0; } else le.flexibleWidth = 1;
            if (height > 0) { le.preferredHeight = le.minHeight = height; } else le.flexibleHeight = 1;
            if (!string.IsNullOrEmpty(title))
            {
                var t = UIFactory.Label(go.transform, title, FSmall, UIFactory.TextDim);
                t.fontStyle = FontStyle.Bold;
            }
            return (RectTransform)go.transform;
        }

        /// <summary>가로로 카드를 늘어놓는 줄. height <= 0이면 남는 높이를 차지.</summary>
        static RectTransform CardRow(Transform parent, float height, float spacing = 14f)
        {
            var row = UIFactory.Row(parent, Mathf.Max(0, height), spacing, false);
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandHeight = true;
            hl.childAlignment = TextAnchor.UpperLeft;
            var le = row.GetComponent<LayoutElement>();
            if (height <= 0) { le.minHeight = 0; le.preferredHeight = 0; le.flexibleHeight = 1; }
            return row;
        }

        /// <summary>이름, 값 한 줄. 값 Text를 돌려준다.</summary>
        public static Text KV(Transform parent, string label, float labelWidth = 170f)
        {
            var row = UIFactory.Row(parent, 32, 10, false);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var l = UIFactory.Label(row, label, FBody - 2, UIFactory.TextDim);
            UIFactory.SetWidth(l, labelWidth);
            var v = UIFactory.Label(row, "--", FBody, UIFactory.TextMain);
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            v.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            return v;
        }

        public static Button Small(Transform parent, string label, Color? color = null, float width = -1f)
            => UIFactory.Button(parent, label, null, color ?? UIFactory.Secondary, width, BtnH, FBody - 2);

        public class StatusTile
        {
            public Image Dot;
            public Text State, Detail;

            public void Set(Color color, string state, string detail = "")
            {
                Dot.color = color;
                State.text = state;
                State.color = color == StatusGray ? UIFactory.TextDim : UIFactory.Lighten(color, 0.25f);
                Detail.text = detail;
            }
        }

        static StatusTile Tile(Transform parent, string name)
        {
            var go = new GameObject("Tile_" + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = CardBg;
            UIFactory.MakeRounded(img, 14);
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(18, 14, 12, 12);
            vl.spacing = 4;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            var n = UIFactory.Label(go.transform, name, FSmall, UIFactory.TextDim);
            n.fontStyle = FontStyle.Bold;
            var r = UIFactory.Row(go.transform, 36, 10, false);
            r.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var tile = new StatusTile();
            (tile.Dot, _) = UIFactory.Badge(r, 20, "", StatusGray);
            tile.State = UIFactory.Label(r, "--", FSection, UIFactory.TextMain);
            tile.State.fontStyle = FontStyle.Bold;
            tile.State.horizontalOverflow = HorizontalWrapMode.Overflow;
            tile.State.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            tile.Detail = UIFactory.Label(go.transform, "", FSmall, UIFactory.TextDim);
            tile.Detail.horizontalOverflow = HorizontalWrapMode.Overflow;
            return tile;
        }

        // ---------------------------------------------------------------- Dashboard

        void BuildDashboard(RectTransform p)
        {
            var grid = new GameObject("Tiles", typeof(RectTransform));
            grid.transform.SetParent(p, false);
            var gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(412, 118);
            gl.spacing = new Vector2(14, 14);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;
            UIFactory.SetHeight(grid.transform as RectTransform, 250);
            foreach (var name in new[] { "SERVER", "WEBSOCKET", "AGENT", "SIMULATOR", "MAP", "QUEST" })
                Tiles[name] = Tile(grid.transform, name);

            var row = CardRow(p, 0);
            var ses = Card(row, "CURRENT SESSION", 430);
            foreach (var k in new[] { "Session", "Map", "Simulation", "Robots", "Orders", "Steps", "Collision", "Mode" })
                Session[k] = KV(ses, k, 150);
            var ev = Card(row, "RECENT EVENTS");
            RecentEvents = UITable.Create(ev, new[] { "TIME", "LEVEL", "MESSAGE" }, new[] { 120f, 90f, -1f }, 6, 38, false);
            var r = UIFactory.Row(ev, BtnH, 0, false);
            UIFactory.Spacer(r);
            ViewAllLogsButton = Small(r, "View All Logs  →", null, 260);
        }

        // ---------------------------------------------------------------- Connection

        void BuildConnection(RectTransform p)
        {
            var row = CardRow(p, 0);
            var s = Card(row, "SERVER");
            foreach (var k in new[] { "Host", "Port", "Protocol", "Session", "Status", "Health", "Ping" })
                Conn["server." + k] = KV(s, k, 120);
            UIFactory.Spacer(s);
            var b = UIFactory.Row(s, BtnH, 12);
            PingButton = Small(b, "Ping");
            ReconnectButton = Small(b, "Reconnect", UIFactory.Primary);

            var w = Card(row, "WEBSOCKET");
            foreach (var k in new[] { "Status", "URL", "Last RX", "Last TX", "RX msgs", "TX reqs", "Reconnects" })
                Conn["ws." + k] = KV(w, k, 140);

            var q = Card(row, "QUEST 2");
            foreach (var k in new[] { "Device", "XR", "FPS", "View" })
                Conn["quest." + k] = KV(q, k, 100);
        }

        // ---------------------------------------------------------------- Agent

        void BuildAgent(RectTransform p)
        {
            var row = CardRow(p, 0);

            var info = Card(row, "AGENT RUN", 330);
            foreach (var k in new[] { "Run ID", "Kind", "Status", "Duration", "Result" })
                Run[k] = KV(info, k, 120);
            var inTitle = UIFactory.Label(info, "INPUT", FSmall, UIFactory.TextDim);
            inTitle.fontStyle = FontStyle.Bold;
            AgentInput = UIFactory.Label(info, "", FBody - 2, UIFactory.TextMain, TextAnchor.UpperLeft);
            AgentInput.verticalOverflow = VerticalWrapMode.Truncate;
            var ale = AgentInput.gameObject.AddComponent<LayoutElement>();
            ale.preferredHeight = 120;
            ale.flexibleHeight = 1;
            var nb = UIFactory.Row(info, BtnH, 10);
            PrevRunButton = Small(nb, "◀ 이전 실행");
            NextRunButton = Small(nb, "다음 ▶");

            var graph = Card(row, "AGENT GRAPH", 330);
            for (int i = 0; i < 8; i++)
            {
                var b = UIFactory.Button(graph, "", null, new Color(1f, 1f, 1f, 0.07f), -1, 62, FBody);
                var old = b.GetComponentInChildren<Text>();
                old.gameObject.SetActive(false);   // 기본 글자 대신 점 + 두 줄 글자
                UnityEngine.Object.Destroy(old.gameObject);
                var hl = b.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.padding = new RectOffset(14, 10, 6, 6);
                hl.spacing = 12;
                hl.childAlignment = TextAnchor.MiddleLeft;
                hl.childControlWidth = hl.childControlHeight = true;
                hl.childForceExpandWidth = false;
                hl.childForceExpandHeight = false;
                var (dot, _) = UIFactory.Badge(b.transform, 18, "", StatusGray);
                var col = UIFactory.Column(b.transform, 0, TextAnchor.MiddleLeft, "Text");
                col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                var title = UIFactory.Label(col, "", FBody - 1, UIFactory.TextMain);
                title.fontStyle = FontStyle.Bold;
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                var sub = UIFactory.Label(col, "", FSmall - 2, UIFactory.TextDim);
                sub.horizontalOverflow = HorizontalWrapMode.Overflow;
                GraphNodes.Add((b, dot, title, sub));
            }

            var right = UIFactory.Column(row, 14, TextAnchor.UpperLeft, "Right");
            right.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var det = Card(right, "NODE DETAIL", -1, 300);
            foreach (var k in new[] { "Node", "Status", "Duration", "Tool", "Message" })
                NodeDetail[k] = KV(det, k, 110);
            NodeDetail["Message"].horizontalOverflow = HorizontalWrapMode.Wrap;
            NodeRaw = UIFactory.Label(det, "", FSmall - 2, UIFactory.Lighten(UIFactory.Accent, 0.4f), TextAnchor.UpperLeft);
            NodeRaw.verticalOverflow = VerticalWrapMode.Truncate;
            var rle = NodeRaw.gameObject.AddComponent<LayoutElement>();
            rle.preferredHeight = 60;
            rle.flexibleHeight = 1;

            var tl = Card(right, "TOOL CALL TIMELINE");
            ToolTimeline = UITable.Create(tl, new[] { "TIME", "NODE / TOOL", "STATUS", "DURATION" }, new[] { 110f, -1f, 110f, 110f }, 7, 34, false);
        }

        // ---------------------------------------------------------------- Simulation

        void BuildSimulation(RectTransform p)
        {
            var top = CardRow(p, 260);
            var ses = Card(top, "SESSION", 360);
            foreach (var k in new[] { "Map", "Simulation", "Robots", "Orders", "Strategy" })
                SimSession[k] = KV(ses, k, 130);
            var st = Card(top, "SIMULATION STATUS", 360);
            foreach (var k in new[] { "State", "Step", "Waiting now", "Collision", "Runtime" })
                SimStatus[k] = KV(st, k, 150);
            var ov = Card(top, "DEBUG OVERLAY");
            var og = new GameObject("Toggles", typeof(RectTransform));
            og.transform.SetParent(ov, false);
            var gl = og.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(176, 52);
            gl.spacing = new Vector2(10, 10);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 2;
            foreach (var name in OverlayNames)
                Overlay[name] = UIFactory.Button(og.transform, name, null, UIFactory.Secondary, 176, 52, FBody - 3);

            var ctl = Card(p, "SIMULATION CONTROL", -1, 120);
            var cr = UIFactory.Row(ctl, BtnH, 10, false);
            SimPlay = Small(cr, "▶ Play", UIFactory.Primary, 130);
            SimPause = Small(cr, "‖ Pause", null, 130);
            SimStop = Small(cr, "■ Stop", UIFactory.Danger, 120);
            UIFactory.Spacer(cr, 14, 0);
            SimSpeed = new Button[Speeds.Length];
            for (int i = 0; i < Speeds.Length; i++) SimSpeed[i] = Small(cr, $"{Speeds[i]:0}x", null, 70);
            UIFactory.Spacer(cr, 14, 0);
            SimSlider = UIFactory.Slider(cr, 44);
            SimPercent = UIFactory.Label(cr, "0%", FBody, UIFactory.TextMain, TextAnchor.MiddleRight);
            UIFactory.SetWidth(SimPercent, 80);

            var rob = Card(p, "ROBOTS  (줄을 누르면 선택 · 경로 표시)");
            Robots = UITable.Create(rob, new[] { "ID", "STATE", "TASK", "POS", "WAIT", "PATH" },
                new[] { 110f, 170f, -1f, 140f, 110f, 110f }, 4, 36, true);
            var pr = UIFactory.Row(rob, BtnH, 10, false);
            RobotPrev = Small(pr, "◀", null, 70);
            RobotPage = UIFactory.Label(pr, "", FSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            UIFactory.SetWidth(RobotPage, 110);
            RobotNext = Small(pr, "▶", null, 70);
            UIFactory.Spacer(pr);
            FocusRobotButton = Small(pr, "Focus Robot", UIFactory.Primary, 220);
        }

        // ---------------------------------------------------------------- Logs

        void BuildLogs(RectTransform p)
        {
            var f = UIFactory.Row(p, BtnH, 8, false);
            foreach (var name in new[] { "ALL", "ERROR", "WARN", "INFO", "DEBUG" })
                LevelFilter[name] = Small(f, name, null, name == "ALL" ? 80 : 104);
            UIFactory.Spacer(f, 18, 0);
            foreach (var name in new[] { "ALL", "AGENT", "API", "WS", "SIM", "VR" })
                ModuleFilter[name] = Small(f, name, null, name == "AGENT" ? 104 : 76);
            UIFactory.Spacer(f);

            var g = UIFactory.Row(p, BtnH, 10, false);
            LogSearch = UIFactory.Input(g, "검색 (예: UNREACHABLE)", BtnH, false, InputField.ContentType.Standard, FBody - 2);
            LogSearch.GetComponent<LayoutElement>().flexibleWidth = 1;
            LogExport = Small(g, "Export", null, 130);
            LogClear = Small(g, "Clear", UIFactory.Danger, 120);

            var row = CardRow(p, 0);
            var t = Card(row, "LOG VIEWER");
            LogTable = UITable.Create(t, new[] { "TIME", "LEVEL", "MODULE", "MESSAGE" }, new[] { 120f, 90f, 110f, -1f }, 10, 36, true);
            var pr = UIFactory.Row(t, BtnH, 10, false);
            LogPrev = Small(pr, "◀ 최근", null, 120);
            LogPage = UIFactory.Label(pr, "", FSmall, UIFactory.TextDim, TextAnchor.MiddleCenter);
            UIFactory.SetWidth(LogPage, 160);
            LogNext = Small(pr, "이전 ▶", null, 120);

            var d = Card(row, "LOG DETAIL", 380);
            foreach (var k in new[] { "Time", "Level", "Module", "Code", "Position" })
                LogDetail[k] = KV(d, k, 110);
            var mt = UIFactory.Label(d, "MESSAGE", FSmall, UIFactory.TextDim);
            mt.fontStyle = FontStyle.Bold;
            LogDetail["Message"] = UIFactory.Label(d, "", FBody - 2, UIFactory.TextMain, TextAnchor.UpperLeft);
            LogRaw = UIFactory.Label(d, "", FSmall - 2, UIFactory.Lighten(UIFactory.Accent, 0.4f), TextAnchor.UpperLeft);
            LogRaw.verticalOverflow = VerticalWrapMode.Truncate;
            var rle = LogRaw.gameObject.AddComponent<LayoutElement>();
            rle.preferredHeight = 40;
            rle.flexibleHeight = 1;
            var b = UIFactory.Row(d, BtnH, 10);
            LogRawToggle = Small(b, "Raw JSON");
            LogFocus = Small(b, "Focus in 3D", UIFactory.Primary);
        }

        // ---------------------------------------------------------------- Scenario (시연 데이터)

        void BuildScenario(RectTransform p)
        {
            var row = CardRow(p, 0);
            var left = UIFactory.Column(row, 14, TextAnchor.UpperLeft, "Left");
            left.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            var fx = Card(left, "WAREHOUSE FIXTURE  (예시 문장)", -1, 252);
            var wr = UIFactory.Row(fx, BtnH, 8, false);
            foreach (var (id, _) in ExampleSentences) ExampleButtons.Add(Small(wr, id, null, 96));
            ExampleText = UIFactory.Label(fx, "예시를 고르세요", FBody - 2, UIFactory.TextMain);
            UIFactory.SetHeight(ExampleText, 56);
            var fr = UIFactory.Row(fx, BtnH, 10);
            ExampleFill = Small(fr, "사용자 입력 칸에 채우기");
            ExampleGenerate = Small(fr, "바로 생성", UIFactory.Primary);

            var sc = Card(left, "SCENARIO MANAGER", -1, 252);
            var sr = UIFactory.Row(sc, BtnH, 8, false);
            foreach (var s in ScenarioPresets) PresetButtons.Add(Small(sr, s.id, null, 130));
            PresetText = UIFactory.Label(sc, "프리셋을 고르세요", FBody - 2, UIFactory.TextMain);
            UIFactory.SetHeight(PresetText, 56);
            var pr = UIFactory.Row(sc, BtnH, 10);
            PresetApply = Small(pr, "설정 화면에 적용");
            PresetRun = Small(pr, "Run Scenario", UIFactory.Primary);

            var demo = Card(left, "DEMO · 재계획");
            var dr = UIFactory.Row(demo, BtnH, 10);
            OfflineButton = Small(dr, "오프라인 재생 (W2+S2)");
            ViewModeButton = Small(dr, "보기: 미니어처");
            var dr2 = UIFactory.Row(demo, BtnH, 10);
            AddOrdersButton = Small(dr2, "주문 +10 (재계획)");
            ChangeRobotsButton = Small(dr2, "로봇 수 적용 (재계획)");

            var cmp = Card(row, "STRATEGY COMPARISON", 520);
            CompareTable = UITable.Create(cmp, new[] { "", "BASELINE", "OPTIMIZED" }, new[] { -1f, 140f, 140f }, 4, 40, false);
            CompareSummary = UIFactory.Label(cmp, "아직 비교하지 않았어요", FSection, UIFactory.TextMain, TextAnchor.MiddleCenter);
            UIFactory.SetHeight(CompareSummary, 70);
            UIFactory.Spacer(cmp);
            CompareButton = Small(cmp, "Run Baseline vs Optimized", UIFactory.Primary);
        }

        // ---------------------------------------------------------------- Performance

        void BuildPerformance(RectTransform p)
        {
            var row = CardRow(p, 0);
            var q = Card(row, "QUEST 2 / APP");
            foreach (var k in new[] { "FPS", "Frame time", "Memory", "Robots drawn", "Draw mode" })
                Perf["app." + k] = KV(q, k, 170);
            var n = Card(row, "NETWORK");
            foreach (var k in new[] { "Ping", "RX msgs", "TX reqs", "Last RX" })
                Perf["net." + k] = KV(n, k, 130);
            var s = Card(row, "SIMULATION");
            foreach (var k in new[] { "Steps / sec", "Frames loaded", "Buffering", "Inventory events" })
                Perf["sim." + k] = KV(s, k, 200);
            UIFactory.Label(p, "측정할 수 없는 값은 -- 로 표시합니다 (CPU·GPU 사용률은 Quest 개발자 도구 OVR Metrics Tool로 확인).",
                FSmall, UIFactory.TextDim);
        }

        // ---------------------------------------------------------------- Modal (위험 버튼 확인)

        void BuildModal()
        {
            _modal = new GameObject("Modal", typeof(RectTransform));
            _modal.transform.SetParent(Canvas.transform, false);
            _modal.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.Stretch((RectTransform)_modal.transform);
            var cv = _modal.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 10;
            _modal.AddComponent<GraphicRaycaster>();
            _modal.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(_modal.transform, false);
            var rt = (RectTransform)box.transform;
            rt.sizeDelta = new Vector2(620, 300);
            var img = box.AddComponent<Image>();
            img.color = new Color(0.12f, 0.14f, 0.18f, 1f);
            UIFactory.MakeRounded(img, 20);
            var vl = box.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(32, 32, 28, 28);
            vl.spacing = 16;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            _modalTitle = UIFactory.Label(box.transform, "", FTitle - 4, UIFactory.TextMain);
            _modalTitle.fontStyle = FontStyle.Bold;
            _modalBody = UIFactory.Label(box.transform, "", FBody, UIFactory.TextDim);
            _modalBody.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var r = UIFactory.Row(box.transform, BtnH + 8, 0, false);
            UIFactory.Button(r, "취소", HideModal, UIFactory.Secondary, 180, BtnH + 8, FBody);
            UIFactory.Spacer(r);
            _modalOk = UIFactory.Button(r, "확인", () => { var a = _modalAction; HideModal(); a?.Invoke(); }, UIFactory.Danger, 220, BtnH + 8, FBody);
            _modal.SetActive(false);
        }

        /// <summary>되돌리기 어려운 명령(Stop, Clear, 모드 전환) 확인 창 (설계 29장).</summary>
        public void Confirm(string title, string body, string okLabel, Action onOk, bool danger = true)
        {
            _modalTitle.text = title;
            _modalBody.text = body;
            UIFactory.SetButtonText(_modalOk, okLabel);
            UIFactory.SetButtonColor(_modalOk, danger ? UIFactory.Danger : UIFactory.Primary);
            _modalAction = onOk;
            _modal.SetActive(true);
        }

        public void HideModal()
        {
            _modalAction = null;
            if (_modal != null) _modal.SetActive(false);
        }

        // ================================================================== 공통 갱신

        public void SetSystem(Color color, string text)
        {
            _systemDot.color = color;
            if (SystemStatus.text != text) SystemStatus.text = text;
        }

        public void SetVisible(bool v)
        {
            if (Canvas != null) Canvas.gameObject.SetActive(v);
            if (!v) HideModal();
        }

        public static void Toggle(Button b, bool on, Color onColor) => UIFactory.SetButtonColor(b, on ? onColor : UIFactory.Secondary);

        public static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }

        public static string Ago(DateTime? t)
        {
            if (!t.HasValue) return "--";
            var s = (DateTime.Now - t.Value).TotalSeconds;
            return $"{t.Value:HH:mm:ss}  ({(s < 60 ? $"{s:0}초 전" : $"{s / 60:0}분 전")})";
        }

        public static Color LevelColor(LogLevel l) => l switch
        {
            LogLevel.Error => StatusRed,
            LogLevel.Warn => StatusYellow,
            LogLevel.Debug => UIFactory.TextMuted,
            _ => UIFactory.TextMain
        };
    }

    /// <summary>
    /// 간단한 표 (고정 행 수, 값만 바꿔 씀 -> 매번 오브젝트를 만들지 않음).
    /// 열 너비 <= 0이면 남는 폭. clickable이면 줄을 눌러 OnRowClick.
    /// </summary>
    public class UITable
    {
        public RectTransform Root;
        public Text[,] Cells;
        public Image[] RowBg;
        public int Rows, Cols;
        public event Action<int> OnRowClick;
        int _selected = -1;

        static readonly Color HeaderBg = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color Alt = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color Even = new Color(1f, 1f, 1f, 0.015f);   // 완전 투명이면 포인터 강조색이 곱해져도 안 보임
        static readonly Color Sel = new Color(0.25f, 0.5f, 1f, 0.32f);

        public static UITable Create(Transform parent, string[] headers, float[] widths, int rows, float rowHeight, bool clickable)
        {
            var t = new UITable { Rows = rows, Cols = headers.Length };
            t.Root = UIFactory.Column(parent, 0, TextAnchor.UpperLeft, "Table");
            t.Cells = new Text[rows, headers.Length];
            t.RowBg = new Image[rows];

            var head = MakeRow(t.Root, rowHeight, HeaderBg);
            for (int c = 0; c < headers.Length; c++)
            {
                var h = Cell(head.transform, headers[c], widths[c]);
                h.color = UIFactory.TextDim;
                h.fontStyle = FontStyle.Bold;
                h.fontSize = DevPanel.FSmall;
            }
            for (int r = 0; r < rows; r++)
            {
                var row = MakeRow(t.Root, rowHeight, r % 2 == 1 ? Alt : Even);
                t.RowBg[r] = row;
                for (int c = 0; c < headers.Length; c++) t.Cells[r, c] = Cell(row.transform, "", widths[c]);
                if (clickable)
                {
                    int idx = r;
                    var b = row.gameObject.AddComponent<Button>();
                    b.targetGraphic = row;
                    var cb = b.colors;
                    cb.highlightedColor = new Color(1.4f, 1.5f, 2f, 6f);
                    cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                    b.colors = cb;
                    b.onClick.AddListener(() => t.OnRowClick?.Invoke(idx));
                }
            }
            return t;
        }

        static Image MakeRow(Transform parent, float h, Color bg)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(10, 10, 0, 0);
            hl.spacing = 10;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = h;
            le.flexibleHeight = 0;
            return img;
        }

        static Text Cell(Transform row, string text, float width)
        {
            var t = UIFactory.Label(row, text, DevPanel.FBody - 3, UIFactory.TextMain);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var le = t.gameObject.AddComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = le.minWidth = width; le.flexibleWidth = 0; }
            else { le.preferredWidth = 1; le.flexibleWidth = 1; }
            return t;
        }

        public void Set(int r, Color? color, params string[] values)
        {
            for (int c = 0; c < Cols; c++)
            {
                var t = Cells[r, c];
                var v = c < values.Length ? values[c] ?? "" : "";
                if (t.text != v) t.text = v;
                var col = color ?? UIFactory.TextMain;
                if (t.color != col) t.color = col;
            }
        }

        public void SetCellColor(int r, int c, Color color)
        {
            if (Cells[r, c].color != color) Cells[r, c].color = color;
        }

        public void Clear(int r) => Set(r, null);

        public void Select(int r)
        {
            if (_selected == r) return;
            _selected = r;
            for (int i = 0; i < Rows; i++)
                RowBg[i].color = i == r ? Sel : i % 2 == 1 ? Alt : Even;
        }
    }
}
