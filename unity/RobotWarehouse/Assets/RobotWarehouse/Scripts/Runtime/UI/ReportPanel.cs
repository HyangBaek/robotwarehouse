using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using RobotWarehouse.XR;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// 최종 시뮬레이션 리포트 창 (서버 WS "report"). visionOS 창 모티브:
    /// 유리 창 + 상단 세그먼트 탭(요약, 로봇 효율, 주문, 비교) + 둥근 카드 + 알약 칩.
    /// 숫자는 모두 서버(tools/report.py)가 계산한 값을 그대로 보여 준다.
    /// </summary>
    public class ReportPanel
    {
        public Canvas Canvas;
        public bool Visible => Canvas != null && Canvas.gameObject.activeSelf;
        public event Action OnClosed;

        static readonly string[] TabNames = { "요약", "로봇 효율", "주문", "비교" };
        static readonly Color ColLoaded = UIFactory.Orange, ColEmpty = UIFactory.Primary, ColHandling = UIFactory.Yellow,
            ColWait = UIFactory.Danger, ColIdle = new Color(1f, 1f, 1f, 0.22f), ColBase = new Color(1f, 1f, 1f, 0.40f);
        static readonly Color TabOn = new Color(1f, 1f, 1f, 0.26f), TabOff = new Color(1f, 1f, 1f, 0.04f);

        Text _title, _subtitle, _footer;
        RectTransform _pills;
        readonly Button[] _tabs = new Button[TabNames.Length];
        readonly RectTransform[] _pages = new RectTransform[TabNames.Length];
        Texture2D _chartTex;

        public void Build(Transform parent)
        {
            Canvas = UIFactory.CreateWorldCanvas("Panel_Report", new Vector2(1600, 1180), parent);
            var root = Canvas.transform;

            // 머리글: 제목, 부제 | 정보 알약 | 닫기
            var head = HRow(root, 96, 16);
            var titles = VCol(head, 4);
            titles.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            _title = UIFactory.Label(titles, "시뮬레이션 리포트", UIFactory.FontTitle + 12, UIFactory.TextMain);
            _title.fontStyle = FontStyle.Bold;
            _subtitle = UIFactory.Label(titles, "", UIFactory.FontSmall, UIFactory.TextDim);
            _pills = HRow(head, 52, 10);
            _pills.GetComponent<LayoutElement>().flexibleWidth = 0;
            var close = UIFactory.Button(head, "닫기", Hide, UIFactory.ButtonAlt, 140);
            close.GetComponent<LayoutElement>().preferredHeight = 60;

            // 세그먼트 탭 (visionOS 세그먼트 컨트롤)
            var segWrap = HRow(root, 72, 0);
            var seg = new GameObject("Segments", typeof(RectTransform));
            seg.transform.SetParent(segWrap, false);
            UIFactory.Round(seg.AddComponent<Image>(), 36f).color = new Color(1f, 1f, 1f, 0.08f);
            var segLayout = seg.AddComponent<HorizontalLayoutGroup>();
            segLayout.padding = new RectOffset(6, 6, 6, 6);
            segLayout.spacing = 4;
            segLayout.childControlWidth = true; segLayout.childControlHeight = true;
            segLayout.childForceExpandWidth = true; segLayout.childForceExpandHeight = true;
            var segLe = seg.AddComponent<LayoutElement>();
            segLe.preferredWidth = 880; segLe.flexibleWidth = 0;
            Spacer(segWrap);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                _tabs[i] = UIFactory.Button(seg.transform, TabNames[i], () => SelectTab(idx), TabOff);
                _tabs[i].GetComponent<LayoutElement>().preferredHeight = 60;
            }

            // 페이지 영역
            var body = new GameObject("Pages", typeof(RectTransform));
            body.transform.SetParent(root, false);
            var ble = body.AddComponent<LayoutElement>();
            ble.preferredHeight = 860; ble.minHeight = 860; ble.flexibleHeight = 1;
            for (int i = 0; i < _pages.Length; i++)
            {
                var pg = VCol(body.transform, 16);
                UIFactory.Stretch(pg);
                _pages[i] = pg;
            }
            _footer = UIFactory.Label(root, "", UIFactory.FontSmall - 2, new Color(1f, 1f, 1f, 0.42f));

            XRSupport.ConfigureCanvas(Canvas);
            SelectTab(0);
            Canvas.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (Canvas == null) return;
            Canvas.gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        void SelectTab(int idx)
        {
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].gameObject.SetActive(i == idx);
                UIFactory.SetButtonColor(_tabs[i], i == idx ? TabOn : TabOff);
            }
        }

        // ------------------------------------------------------------------ 표시

        public void Show(JObject r, string serverBase = null)
        {
            var sc = r["scenario"] as JObject;
            var k = r["kpis"] as JObject;
            var o = r["orders"] as JObject;
            _subtitle.text = (string)r["map_summary"] ?? "";
            Clear(_pills);
            Pill(_pills, $"로봇 {Num(sc?["robots"])}대");
            Pill(_pills, $"입하 {Num(sc?["inbound"])} · 출하 {Num(sc?["outbound"])}");
            Pill(_pills, (string)r["sim_id"] ?? "");

            var cmp = new Dictionary<string, JObject>();
            if (r["comparison"] is JArray ca)
                foreach (var row in ca) if (row is JObject jo) cmp[(string)jo["key"]] = jo;

            BuildSummary(_pages[0], k, o, cmp, r["insights"] as JArray);
            BuildRobots(_pages[1], r["robots"] as JArray);
            BuildOrders(_pages[2], o, r["timeline"] as JArray, (r["baseline"] as JObject)?["timeline"] as JArray);
            BuildCompare(_pages[3], r["comparison"] as JArray);

            var url = (string)r["html_url"];
            _footer.text = (string)r["time_note"] +
                           (string.IsNullOrEmpty(url) ? "" : $"   ·   노트북 브라우저 리포트: {(serverBase ?? "")}{url}");
            SelectTab(0);
            Canvas.gameObject.SetActive(true);
        }

        void BuildSummary(RectTransform page, JObject k, JObject o, Dictionary<string, JObject> cmp, JArray insights)
        {
            Clear(page);
            var grid = new GameObject("Tiles", typeof(RectTransform));
            grid.transform.SetParent(page, false);
            var gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(371, 178);
            gl.spacing = new Vector2(16, 16);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            grid.AddComponent<LayoutElement>().preferredHeight = 178 * 2 + 16;

            Tile(grid.transform, "총 처리 시간", Num(k?["total_steps"]), "스텝", "기준 전략 대비", cmp, "total_steps");
            Tile(grid.transform, "처리량", Num(k?["throughput_per_100"]), "건/100스텝", "", cmp, "throughput_per_100");
            Tile(grid.transform, "주문 완료", $"{Num(o?["done"])}/{Num(o?["total"])}", "", $"{Num(o?["completion_pct"])}%", cmp, null);
            Tile(grid.transform, "주문당 평균 시간", Num(o?["lead_avg"]), "스텝", $"P90 {Num(o?["lead_p90"])}", cmp, "lead_avg");
            Tile(grid.transform, "로봇 가동률", Num(k?["utilization_pct"]), "%", $"적재 작업 {Num(k?["productive_pct"])}%", cmp, "utilization_pct");
            Tile(grid.transform, "대기 · 유휴", $"{Num(k?["wait_pct"])} · {Num(k?["idle_pct"])}", "%", "로봇 평균", cmp, null);
            Tile(grid.transform, "총 이동 거리", Num(k?["distance"]), "칸", $"주문당 {Num(k?["distance_per_order"])}칸", cmp, null);
            int coll = (int?)k?["collisions"] ?? 0;
            var t = Tile(grid.transform, "충돌", coll.ToString(), "건", $"재계획 평균 {Num(k?["replan_avg_ms"])}ms", cmp, null);
            Chip(t, coll == 0 ? "안전" : "확인 필요", coll == 0, true);

            var card = UIFactory.Card(page);
            var h = UIFactory.Label(card, "핵심 인사이트", UIFactory.FontBody, UIFactory.TextMain);
            h.fontStyle = FontStyle.Bold;
            if (insights != null)
                foreach (var it in insights)
                {
                    var level = (string)it["level"];
                    var col = level == "good" ? UIFactory.Green : level == "warn" ? UIFactory.Orange : UIFactory.Cyan;
                    UIFactory.Label(card, $"<color=#{ColorUtility.ToHtmlStringRGB(col)}>●</color>  {(string)it["text"]}",
                        UIFactory.FontSmall + 2, UIFactory.TextMain);
                }
        }

        void BuildRobots(RectTransform page, JArray robots)
        {
            Clear(page);
            var card = UIFactory.Card(page, UIFactory.RadiusCard, null, 20, 8);
            var head = HRow(card, 40, 14);
            HeaderCell(head, "로봇", 90, TextAnchor.MiddleLeft);
            HeaderCell(head, "작업 구성 (시뮬레이션 전체 시간 대비)", -1, TextAnchor.MiddleLeft);
            HeaderCell(head, "가동률", 120, TextAnchor.MiddleRight);
            HeaderCell(head, "주문", 90, TextAnchor.MiddleRight);
            int n = 0;
            if (robots != null)
                foreach (var rb in robots)
                {
                    if (++n > 16) break;   // 패널 높이 기준 최대 16대
                    var row = HRow(card, 40, 14);
                    var id = UIFactory.Label(row, (string)rb["id"], UIFactory.FontSmall + 2, UIFactory.TextMain);
                    id.fontStyle = FontStyle.Bold;
                    Fixed(id.gameObject, 90);
                    StackedBar(row, new[]
                    {
                        ((float?)rb["move_loaded"] ?? 0f, ColLoaded), ((float?)rb["move_empty"] ?? 0f, ColEmpty),
                        ((float?)rb["handling"] ?? 0f, ColHandling), ((float?)rb["wait"] ?? 0f, ColWait),
                        ((float?)rb["idle"] ?? 0f, ColIdle),
                    });
                    var u = UIFactory.Label(row, $"{Num(rb["utilization_pct"])}%", UIFactory.FontSmall + 2, UIFactory.TextMain, TextAnchor.MiddleRight);
                    Fixed(u.gameObject, 120);
                    var od = UIFactory.Label(row, $"{Num(rb["orders"])}건", UIFactory.FontSmall + 2, UIFactory.TextDim, TextAnchor.MiddleRight);
                    Fixed(od.gameObject, 90);
                }
            UIFactory.Label(card, Legend(("적재 이동", ColLoaded), ("빈 이동", ColEmpty), ("적재·하역", ColHandling),
                ("대기", ColWait), ("유휴", ColIdle)) + "      가동률 = 이동·적재·하역 시간 비율", UIFactory.FontSmall - 2, UIFactory.TextDim);
        }

        void BuildOrders(RectTransform page, JObject o, JArray timeline, JArray baseTimeline)
        {
            Clear(page);
            var top = HRow(page, 470, 16);
            // 누적 완료 곡선
            var chartCard = UIFactory.Card(top, UIFactory.RadiusCard, null, 18, 8);
            chartCard.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1.5f;
            UIFactory.Label(chartCard, "누적 완료 주문", UIFactory.FontSmall, UIFactory.TextDim);
            var raw = new GameObject("Chart", typeof(RectTransform)).AddComponent<RawImage>();
            raw.transform.SetParent(chartCard, false);
            raw.raycastTarget = false;
            raw.gameObject.AddComponent<LayoutElement>().preferredHeight = 330;
            float tMax = 1, yMax = 1;
            var opt = Points(timeline, ref tMax, ref yMax);
            var bas = Points(baseTimeline, ref tMax, ref yMax);
            if (_chartTex != null) UnityEngine.Object.Destroy(_chartTex);
            _chartTex = ChartTexture.CumulativeLines(900, 360, opt, bas, tMax, yMax, ColEmpty, ColBase);
            raw.texture = _chartTex;
            UIFactory.Label(chartCard, Legend(("최적화", ColEmpty), ("기준 전략", ColBase)) +
                $"      0 ~ {tMax:0} 스텝 · 0 ~ {yMax:0} 건", UIFactory.FontSmall - 2, UIFactory.TextDim);

            // 처리 시간 통계 타일
            var stats = new GameObject("Stats", typeof(RectTransform));
            stats.transform.SetParent(top, false);
            var sg = stats.AddComponent<GridLayoutGroup>();
            sg.cellSize = new Vector2(250, 142);
            sg.spacing = new Vector2(14, 14);
            sg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            sg.constraintCount = 2;
            var sle = stats.AddComponent<LayoutElement>();
            sle.preferredWidth = 514; sle.flexibleWidth = 0;
            SmallTile(stats.transform, "평균", Num(o?["lead_avg"]), "스텝");
            SmallTile(stats.transform, "중앙값", Num(o?["lead_median"]), "스텝");
            SmallTile(stats.transform, "P90", Num(o?["lead_p90"]), "스텝");
            SmallTile(stats.transform, "최대", Num(o?["lead_max"]), "스텝");
            SmallTile(stats.transform, "배정 대기", Num(o?["queue_avg"]), "스텝");
            SmallTile(stats.transform, "수행 시간", Num(o?["service_avg"]), "스텝");

            // 처리 시간 분포
            var hist = UIFactory.Card(page, UIFactory.RadiusCard, null, 18, 6);
            UIFactory.Label(hist, "주문 처리 시간 분포 (도착 → 완료, 스텝)", UIFactory.FontSmall, UIFactory.TextDim);
            var bars = HRow(hist, 300, 12);
            var bins = o?["histogram"] as JArray;
            int cmax = 1;
            if (bins != null) foreach (var b in bins) cmax = Math.Max(cmax, (int?)b["count"] ?? 0);
            if (bins != null)
                foreach (var b in bins)
                {
                    int c = (int?)b["count"] ?? 0;
                    var col = VCol(bars, 4);
                    var vl = col.GetComponent<VerticalLayoutGroup>();
                    vl.childAlignment = TextAnchor.LowerCenter;
                    col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                    UIFactory.Label(col, c.ToString(), UIFactory.FontSmall - 2, UIFactory.TextMain, TextAnchor.MiddleCenter);
                    var bar = new GameObject("Bar", typeof(RectTransform)).AddComponent<Image>();
                    bar.transform.SetParent(col, false);
                    UIFactory.Round(bar, 12f).color = ColEmpty;
                    bar.gameObject.AddComponent<LayoutElement>().preferredHeight = Mathf.Max(6f, 210f * c / cmax);
                    UIFactory.Label(col, $"{Num(b["from"])}~{Num(b["to"])}", UIFactory.FontSmall - 4, UIFactory.TextDim, TextAnchor.MiddleCenter);
                }
        }

        void BuildCompare(RectTransform page, JArray rows)
        {
            Clear(page);
            var card = UIFactory.Card(page, UIFactory.RadiusCard, null, 22, 6);
            if (rows == null || rows.Count == 0)
            {
                UIFactory.Label(card, "비교 결과가 없습니다", UIFactory.FontBody, UIFactory.TextDim);
                return;
            }
            foreach (var row in rows)
            {
                var line = HRow(card, 96, 18);
                var name = UIFactory.Label(line, (string)row["label"], UIFactory.FontSmall + 2, UIFactory.TextDim);
                Fixed(name.gameObject, 380);
                var barsCol = VCol(line, 8);
                barsCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                float b = Mathf.Abs((float?)row["baseline"] ?? 0f), o = Mathf.Abs((float?)row["optimized"] ?? 0f);
                float mx = Mathf.Max(1e-6f, Mathf.Max(b, o));
                ValueBar(barsCol, b / mx, ColBase, Num(row["baseline"]));
                ValueBar(barsCol, o / mx, ColEmpty, Num(row["optimized"]));
                var chipHolder = HRow(line, 96, 0);
                Fixed(chipHolder.gameObject, 150);
                var better = (bool?)row["better"] ?? false;
                Chip(chipHolder, (string)row["change_text"] ?? "-", better, false);
            }
            UIFactory.Label(card, Legend(("기준 전략 (무작위 보관 · 선착순 · 개별 최단 경로)", ColBase),
                ("최적화 (점수 보관 · 헝가리안 · 협력 경로 + LNS)", ColEmpty)), UIFactory.FontSmall - 2, UIFactory.TextDim);
        }

        // ------------------------------------------------------------------ 구성 요소

        RectTransform Tile(Transform parent, string label, string value, string unit, string sub,
            Dictionary<string, JObject> cmp, string cmpKey)
        {
            var card = UIFactory.Card(parent, 26f, null, 16, 4);
            UIFactory.Label(card, label, UIFactory.FontSmall, UIFactory.TextDim);
            var v = UIFactory.Label(card, $"{value}<size=24><color=#FFFFFF99> {unit}</color></size>", 50, UIFactory.TextMain);
            v.fontStyle = FontStyle.Bold;
            var subRow = HRow(card, 34, 8);
            subRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            if (cmpKey != null && cmp.TryGetValue(cmpKey, out var c))
                Chip(subRow, (string)c["change_text"] ?? "-", (bool?)c["better"] ?? false, false);
            if (!string.IsNullOrEmpty(sub))
            {
                var s = UIFactory.Label(subRow, sub, UIFactory.FontSmall - 4, new Color(1f, 1f, 1f, 0.45f));
                s.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            return subRow;
        }

        void SmallTile(Transform parent, string label, string value, string unit)
        {
            var card = UIFactory.Card(parent, 24f, null, 14, 2);
            UIFactory.Label(card, label, UIFactory.FontSmall - 2, UIFactory.TextDim);
            var v = UIFactory.Label(card, $"{value}<size=22><color=#FFFFFF99> {unit}</color></size>", 42, UIFactory.TextMain);
            v.fontStyle = FontStyle.Bold;
        }

        /// <summary>알약 칩. good=초록, 아니면 빨강.</summary>
        static void Chip(Transform parent, string text, bool good, bool prepend)
        {
            var go = new GameObject("Chip", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (prepend) go.transform.SetAsFirstSibling();
            var col = good ? UIFactory.Green : new Color(1f, 0.41f, 0.38f, 1f);
            UIFactory.Round(go.AddComponent<Image>(), 16f).color = new Color(col.r, col.g, col.b, 0.20f);
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(14, 14, 2, 2);
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 32; le.flexibleWidth = 0;
            var t = UIFactory.Label(go.transform, text, UIFactory.FontSmall - 4, col, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        static void Pill(Transform parent, string text)
        {
            var go = new GameObject("Pill", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UIFactory.Round(go.AddComponent<Image>(), 26f).color = new Color(1f, 1f, 1f, 0.08f);
            UIFactory.AddRim(go.transform, 26f, new Color(1f, 1f, 1f, 0.14f));
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(20, 20, 4, 4);
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            go.AddComponent<LayoutElement>().flexibleWidth = 0;
            var t = UIFactory.Label(go.transform, text, UIFactory.FontSmall - 2, UIFactory.TextDim, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>비율대로 나눈 가로 막대 (둥근 트랙 안에서 잘림).</summary>
        static void StackedBar(Transform parent, (float value, Color color)[] parts)
        {
            // 행 높이(40) 가운데에 두께 20인 막대를 둔다
            var holder = new GameObject("BarHolder", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var vl = holder.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            holder.AddComponent<LayoutElement>().flexibleWidth = 1;

            var track = new GameObject("StackedBar", typeof(RectTransform));
            track.transform.SetParent(holder.transform, false);
            var timg = track.AddComponent<Image>();
            UIFactory.Round(timg, 10f).color = new Color(1f, 1f, 1f, 0.06f);
            track.AddComponent<Mask>().showMaskGraphic = true;
            var le = track.AddComponent<LayoutElement>();
            le.preferredHeight = 20; le.minHeight = 20;
            var hl = track.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            foreach (var (value, color) in parts)
            {
                if (value <= 0f) continue;
                var seg = new GameObject("Seg", typeof(RectTransform)).AddComponent<Image>();
                seg.transform.SetParent(track.transform, false);
                seg.color = color;
                seg.raycastTarget = false;
                var sle = seg.gameObject.AddComponent<LayoutElement>();
                sle.flexibleWidth = value;     // 여유 폭을 값 비율로 나눔
                sle.minWidth = 0; sle.preferredWidth = 0;
            }
        }

        static void ValueBar(Transform parent, float ratio, Color color, string valueText)
        {
            var track = new GameObject("ValueBar", typeof(RectTransform));
            track.transform.SetParent(parent, false);
            UIFactory.Round(track.AddComponent<Image>(), 13f).color = new Color(1f, 1f, 1f, 0.05f);
            track.AddComponent<LayoutElement>().preferredHeight = 28;
            var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.transform.SetParent(track.transform, false);
            UIFactory.Round(fill, 13f).color = color;
            fill.raycastTarget = false;
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(Mathf.Clamp(ratio, 0.03f, 1f), 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = UIFactory.Label(track.transform, valueText, UIFactory.FontSmall - 4, UIFactory.TextMain, TextAnchor.MiddleRight);
            UIFactory.Stretch(t.rectTransform, 12, 0);
        }

        static void HeaderCell(Transform parent, string text, float width, TextAnchor anchor)
        {
            var t = UIFactory.Label(parent, text, UIFactory.FontSmall - 4, new Color(1f, 1f, 1f, 0.45f), anchor);
            if (width > 0) Fixed(t.gameObject, width);
            else t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static string Legend(params (string name, Color color)[] items)
        {
            var parts = new List<string>();
            foreach (var (name, color) in items)
                parts.Add($"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>●</color> {name}");
            return string.Join("    ", parts);
        }

        static List<Vector2> Points(JArray tl, ref float tMax, ref float yMax)
        {
            var pts = new List<Vector2>();
            if (tl == null) return pts;
            pts.Add(Vector2.zero);
            foreach (var p in tl)
            {
                float t = (float?)p["t"] ?? 0f, y = (float?)p["done"] ?? 0f;
                pts.Add(new Vector2(t, y));
                tMax = Mathf.Max(tMax, t);
                yMax = Mathf.Max(yMax, y);
            }
            return pts;
        }

        // ------------------------------------------------------------------ 레이아웃 도우미

        static RectTransform HRow(Transform parent, float height, float spacing)
        {
            var go = new GameObject("HRow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = spacing;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            return (RectTransform)go.transform;
        }

        static RectTransform VCol(Transform parent, float spacing)
        {
            var go = new GameObject("VCol", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.spacing = spacing;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        static void Spacer(Transform parent)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static void Fixed(GameObject go, float width)
        {
            if (!go.TryGetComponent<LayoutElement>(out var le)) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.flexibleWidth = 0;
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        static string Num(JToken v)
        {
            if (v == null || v.Type == JTokenType.Null) return "-";
            if (v.Type == JTokenType.Integer) return ((long)v).ToString("#,0", CultureInfo.InvariantCulture);
            if (v.Type == JTokenType.Float)
            {
                double d = (double)v;
                return Math.Abs(d - Math.Round(d)) < 1e-9 ? ((long)Math.Round(d)).ToString("#,0", CultureInfo.InvariantCulture)
                    : d.ToString("#,0.#", CultureInfo.InvariantCulture);
            }
            return v.ToString();
        }
    }

    /// <summary>리포트용 차트를 텍스처에 그린다 (UI 정점 수를 늘리지 않음, 한 번만 생성).</summary>
    public static class ChartTexture
    {
        public static Texture2D CumulativeLines(int w, int h, List<Vector2> main, List<Vector2> other,
            float tMax, float yMax, Color mainColor, Color otherColor)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ReportChart" };
            var px = new Color32[w * h];
            const int L = 8, R = 8, T = 10, B = 8;
            float sx(float t) => L + (w - L - R) * t / Mathf.Max(1f, tMax);
            float sy(float y) => B + (h - T - B) * y / Mathf.Max(1f, yMax);

            for (int i = 0; i <= 4; i++)      // 가로 눈금선
            {
                int y = Mathf.RoundToInt(sy(yMax * i / 4f));
                for (int x = L; x < w - R; x++) Blend(px, w, h, x, y, new Color(1f, 1f, 1f, 0.10f));
            }
            if (main.Count > 1)                // 최적화 곡선 아래 그라데이션
            {
                for (int x = L; x < w - R; x++)
                {
                    float t = (x - L) / (float)(w - L - R) * Mathf.Max(1f, tMax);
                    if (t > main[main.Count - 1].x) break;
                    int top = Mathf.RoundToInt(sy(Interp(main, t)));
                    for (int y = B; y <= top; y++)
                    {
                        float a = 0.32f * (y - B) / Mathf.Max(1f, top - B);
                        Blend(px, w, h, x, y, new Color(mainColor.r, mainColor.g, mainColor.b, a));
                    }
                }
            }
            DrawPolyline(px, w, h, other, sx, sy, otherColor, 2.5f);
            DrawPolyline(px, w, h, main, sx, sy, mainColor, 3.5f);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float Interp(List<Vector2> pts, float t)
        {
            for (int i = 1; i < pts.Count; i++)
                if (t <= pts[i].x)
                {
                    float a = pts[i - 1].x, b = pts[i].x;
                    return Mathf.Lerp(pts[i - 1].y, pts[i].y, b > a ? (t - a) / (b - a) : 1f);
                }
            return pts[pts.Count - 1].y;
        }

        static void DrawPolyline(Color32[] px, int w, int h, List<Vector2> pts, Func<float, float> sx, Func<float, float> sy,
            Color color, float width)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                var a = new Vector2(sx(pts[i - 1].x), sy(pts[i - 1].y));
                var b = new Vector2(sx(pts[i].x), sy(pts[i].y));
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - width), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + width);
                int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - width), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + width);
                var ab = b - a;
                float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t);
                    float cov = Mathf.Clamp01(width * 0.5f - d + 0.5f);   // 경계 안티앨리어싱
                    if (cov > 0f) Blend(px, w, h, x, y, new Color(color.r, color.g, color.b, color.a * cov), true);
                }
            }
        }

        static void Blend(Color32[] px, int w, int h, int x, int y, Color c, bool max = false)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            Color dst = px[i];
            if (max && dst.a >= c.a && Mathf.Abs(dst.r - c.r) < 0.02f) return;   // 같은 선이 겹칠 때 진해지지 않게
            float a = c.a + dst.a * (1f - c.a);
            if (a <= 0f) return;
            var rgb = (new Color(c.r, c.g, c.b) * c.a + new Color(dst.r, dst.g, dst.b) * dst.a * (1f - c.a)) / a;
            px[i] = new Color(rgb.r, rgb.g, rgb.b, a);
        }
    }
}
