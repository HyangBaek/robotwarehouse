using System;
using RobotWarehouse.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// 코드로 World Space UI를 만든다. 프리팹 없이 씬 하나로 동작하게 하려는 목적이다.
    /// 글자 크기는 Quest 2에서 1m 거리 기준으로 읽히도록 26px 이상(TC-VR-19).
    /// </summary>
    public static class UIFactory
    {
        public static readonly Color PanelBg = new Color(0.08f, 0.10f, 0.13f, 0.92f);
        public static readonly Color SectionBg = new Color(0.14f, 0.17f, 0.21f, 1f);
        public static readonly Color ButtonBg = new Color(0.20f, 0.42f, 0.85f, 1f);
        public static readonly Color ButtonAlt = new Color(0.28f, 0.31f, 0.36f, 1f);
        public static readonly Color ButtonWarn = new Color(0.85f, 0.35f, 0.20f, 1f);
        public static readonly Color TextMain = new Color(0.94f, 0.95f, 0.97f);
        public static readonly Color TextDim = new Color(0.68f, 0.72f, 0.78f);
        public static readonly Color InputBg = new Color(0.95f, 0.96f, 0.98f);

        public const int FontBody = 28;
        public const int FontSmall = 24;
        public const int FontTitle = 34;

        public static Canvas CreateWorldCanvas(string name, Vector2 sizePx, Transform parent, float metersPerPixel = 0.001f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * metersPerPixel;

            var bg = go.AddComponent<Image>();
            bg.color = PanelBg;

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 14;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return canvas;
        }

        public static RectTransform Section(Transform parent, string title)
        {
            var go = new GameObject("Section_" + title, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = SectionBg;
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 16);
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            if (!string.IsNullOrEmpty(title))
            {
                var t = Label(go.transform, title, FontTitle, TextMain);
                t.fontStyle = FontStyle.Bold;
            }
            return (RectTransform)go.transform;
        }

        public static RectTransform Row(Transform parent, float height = 64f, float spacing = 10f)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return (RectTransform)go.transform;
        }

        public static Text Label(Transform parent, string text, int size = FontBody, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = MaterialLibrary.UIFont;
            t.fontSize = size;
            t.color = color ?? TextMain;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, Color? color = null, float width = -1f)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color ?? ButtonBg;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 56;
            le.preferredHeight = 64;
            if (width > 0) { le.preferredWidth = width; le.flexibleWidth = 0; }
            else le.flexibleWidth = 1;

            var t = Label(go.transform, label, FontBody, Color.white, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            return btn;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static void SetButtonColor(Button b, Color c)
        {
            if (b.targetGraphic != null) b.targetGraphic.color = c;
        }

        public static InputField Input(Transform parent, string placeholder, float height = 64f, bool multiline = false,
            InputField.ContentType contentType = InputField.ContentType.Standard)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = InputBg;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1;

            var text = Label(go.transform, "", FontBody, Color.black, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            text.supportRichText = false;
            Stretch(text.rectTransform, 12, 6);
            var ph = Label(go.transform, placeholder, FontBody, new Color(0.45f, 0.45f, 0.5f), multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform, 12, 6);

            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = text;
            input.placeholder = ph;
            input.contentType = contentType;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.shouldHideMobileInput = false;
            return input;
        }

        public static Slider Slider(Transform parent, float height = 40f)
        {
            var go = new GameObject("Slider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1;

            var bg = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = ButtonAlt;
            Stretch(bg.rectTransform, 0, height * 0.3f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            Stretch((RectTransform)fillArea.transform, 10, height * 0.3f);
            var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.transform.SetParent(fillArea.transform, false);
            fill.color = ButtonBg;
            Stretch(fill.rectTransform);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            Stretch((RectTransform)handleArea.transform, 10, 0);
            var handle = new GameObject("Handle", typeof(RectTransform)).AddComponent<Image>();
            handle.transform.SetParent(handleArea.transform, false);
            handle.color = Color.white;
            handle.rectTransform.sizeDelta = new Vector2(24, 0);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            return slider;
        }

        public static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        /// <summary>- 값 + 스테퍼. 가상 키보드 없이 숫자를 바꾸기 위한 VR용 입력.</summary>
        public static Stepper Stepper(Transform parent, string label, int min, int max, int step, int value)
        {
            var row = Row(parent, 64);
            var l = Label(row, label, FontBody);
            l.gameObject.AddComponent<LayoutElement>().preferredWidth = 220;
            var s = new Stepper { Min = min, Max = max, Step = step };
            Button(row, "-", () => s.Value -= s.Step, ButtonAlt, 90);
            s.ValueText = Label(row, "", FontTitle, TextMain, TextAnchor.MiddleCenter);
            s.ValueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 140;
            Button(row, "+", () => s.Value += s.Step, ButtonAlt, 90);
            s.Value = value;
            return s;
        }
    }

    public class Stepper
    {
        public int Min, Max, Step;
        public Text ValueText;
        public event Action<int> OnChanged;
        int _value;

        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(Min, Math.Min(Max, value));
                if (ValueText != null) ValueText.text = _value.ToString();
                OnChanged?.Invoke(_value);
            }
        }
    }
}
