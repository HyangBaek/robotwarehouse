using System;
using Newtonsoft.Json.Linq;
using RobotWarehouse.XR;
using UnityEngine;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// 시뮬레이션 기록 창 (서버 GET /sims, SQLite 저장분). visionOS 창 스타일.
    /// 항목을 고르면 그 시뮬레이션의 지도와 로봇 동선을 다시 불러와 재생한다 (POST /sim/{id}/load).
    /// </summary>
    public class HistoryPanel
    {
        public Canvas Canvas;
        public bool Visible => Canvas != null && Canvas.gameObject.activeSelf;
        /// <summary>불러오기를 누른 시뮬레이션 ID</summary>
        public event Action<string> OnLoad;
        public event Action OnRefresh;

        const int MaxRows = 10;
        RectTransform _list;
        Text _subtitle, _footer;

        public void Build(Transform parent)
        {
            Canvas = UIFactory.CreateWorldCanvas("Panel_History", new Vector2(1400, 1100), parent);
            var root = Canvas.transform;

            var head = UIFactory.Row(root, 96);
            var titles = new GameObject("Titles", typeof(RectTransform));
            titles.transform.SetParent(head, false);
            var tl = titles.AddComponent<VerticalLayoutGroup>();
            tl.childControlWidth = true; tl.childControlHeight = true;
            tl.childForceExpandWidth = true; tl.childForceExpandHeight = false;
            tl.spacing = 4;
            titles.AddComponent<LayoutElement>().flexibleWidth = 1;
            var title = UIFactory.Label(titles.transform, "시뮬레이션 기록", UIFactory.FontTitle + 12, UIFactory.TextMain);
            title.fontStyle = FontStyle.Bold;
            _subtitle = UIFactory.Label(titles.transform, "", UIFactory.FontSmall, UIFactory.TextDim);
            UIFactory.Button(head, "새로고침", () => OnRefresh?.Invoke(), UIFactory.ButtonAlt, 170);
            UIFactory.Button(head, "닫기", Hide, UIFactory.ButtonAlt, 140);

            _list = UIFactory.Card(root, UIFactory.RadiusCard, null, 18, 8);
            var le = _list.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 860; le.flexibleHeight = 1;
            _footer = UIFactory.Label(root, "", UIFactory.FontSmall - 2, new Color(1f, 1f, 1f, 0.42f));

            XRSupport.ConfigureCanvas(Canvas);
            Canvas.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (Canvas != null) Canvas.gameObject.SetActive(false);
        }

        public void ShowLoading()
        {
            Clear();
            UIFactory.Label(_list, "기록을 불러오는 중…", UIFactory.FontBody, UIFactory.TextDim);
            Canvas.gameObject.SetActive(true);
        }

        /// <summary>sims = 서버 /sims 응답의 "sims" 배열 (최신순).</summary>
        public void Show(JArray sims, string currentSimId, string serverBase)
        {
            Clear();
            int n = sims?.Count ?? 0;
            _subtitle.text = $"저장된 시뮬레이션 {n}건 중 최근 {Math.Min(n, MaxRows)}건 · 고르면 지도와 로봇 동선을 다시 재생";
            if (n == 0) UIFactory.Label(_list, "아직 저장된 시뮬레이션이 없습니다", UIFactory.FontBody, UIFactory.TextDim);
            for (int i = 0; i < Math.Min(n, MaxRows); i++)
            {
                var s = sims[i];
                var id = (string)s["sim_id"];
                var row = UIFactory.Row(_list, 74, 16);
                var info = UIFactory.Label(row,
                    $"<b>{id}</b>{(id == currentSimId ? "  <color=#64D2FF>보는 중</color>" : "")}   " +
                    $"<color=#FFFFFF99>{(string)s["created_at"]}</color>\n" +
                    $"<size=22><color=#FFFFFF99>로봇 {(int?)s["robots"]}대 · 입하 {(int?)s["inbound"]} · 출하 {(int?)s["outbound"]}" +
                    $"{((int?)s["spec_b_pct"] > 0 ? $" · 1200 규격 {(int?)s["spec_b_pct"]}%" : "")} · " +
                    $"{(int?)s["total_steps"]} 스텝 · 주문 {(int?)s["orders_done"]}/{(int?)s["orders_total"]}</color></size>",
                    UIFactory.FontSmall + 2, UIFactory.TextMain);
                info.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                var btn = UIFactory.Button(row, "불러오기", () => { Hide(); OnLoad?.Invoke(id); },
                    id == currentSimId ? UIFactory.Secondary : UIFactory.Primary, 190, 64);
                btn.GetComponent<LayoutElement>().preferredHeight = 60;
            }
            _footer.text = $"노트북 브라우저에서 전체 기록 · 로봇 동선 지도 · CSV: {serverBase}/sims.html";
            Canvas.gameObject.SetActive(true);
        }

        void Clear()
        {
            for (int i = _list.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_list.GetChild(i).gameObject);
        }
    }
}
