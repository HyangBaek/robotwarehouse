using System;
using RobotWarehouse.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// 코드로 World Space UI를 만든다 (프리팹 없이 씬 하나로 동작).
    /// 색·버튼 규격은 ui_재료 '사용자 UI 개선 설계' 기준:
    ///  - 파랑 = 주요 행동, 초록 = 완료·확인, 주황 = 주의, 빨강 = 오류, 회색 = 보조·취소
    ///  - 버튼 최소 0.25m × 0.12m (캔버스 1px = 1mm → 250 × 120px), 서로 다른 행동 버튼 간격 0.08m
    ///  - 패널 모서리 R 0.03m, 반투명 유리 배경
    /// </summary>
    public static class UIFactory
    {
        // ---------------------------------------------------------------- 색 (설계서 HSL 값)
        public static readonly Color Primary = Hsl(210, 1f, 0.50f);     // 실행
        public static readonly Color Success = Hsl(120, 1f, 0.35f);     // 완료·확인
        public static readonly Color Warning = Hsl(30, 1f, 0.50f);      // 다시 확인
        public static readonly Color Danger = Hsl(0, 0.85f, 0.50f);     // 오류
        /// <summary>보조·취소. 설계 L=60%는 흰 글자 대비가 낮아 버튼 배경은 L=40%로 둔다.</summary>
        public static readonly Color Secondary = Hsl(215, 0.08f, 0.40f);
        public static readonly Color Accent = Hsl(185, 0.9f, 0.38f);    // 켜진 토글 (경로 등)
        public static readonly Color Highlight = Hsl(50, 0.95f, 0.45f); // 켜진 토글 (히트맵)

        public static readonly Color PanelBg = new Color(0.07f, 0.09f, 0.12f, 0.86f);
        public static readonly Color SectionBg = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color TextMain = new Color(0.96f, 0.97f, 0.98f);
        public static readonly Color TextDim = new Color(0.70f, 0.74f, 0.80f);
        public static readonly Color TextMuted = new Color(0.50f, 0.54f, 0.60f);
        public static readonly Color InputBg = new Color(0.96f, 0.97f, 0.99f);
        public static readonly Color DisabledBg = new Color(0.30f, 0.32f, 0.35f, 0.55f);

        // 예전 이름 (관리자 패널 등에서 사용)
        public static Color ButtonBg => Primary;
        public static Color ButtonAlt => Secondary;
        public static Color ButtonWarn => Warning;

        // ---------------------------------------------------------------- 글자·버튼 크기 (px = mm)
        public const int FontHuge = 48;
        public const int FontLarge = 38;
        public const int FontTitle = 34;
        public const int FontBody = 28;
        public const int FontSmall = 24;

        /// <summary>
        /// 버튼 높이 0.096m. 설계서 최소값(0.12m)으로는 1.2m 패널에 내용이 다 들어가지 않아 실행 화면 확인 후 줄였다.
        /// 레이 포인터로 누르기에는 충분한 크기 (Quest 실기 확인 필요).
        /// </summary>
        public const float ButtonHeight = 96f;
        public const float ButtonMinWidth = 250f;   // 0.25m
        public const float ActionGap = 80f;         // 0.08m
        public const float PanelRadius = 30f;       // 0.03m
        public const float ButtonRadius = 18f;

        public static Color Hsl(float h, float s, float l)
        {
            h = Mathf.Repeat(h, 360f) / 360f;
            if (s <= 0f) return new Color(l, l, l);
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            return new Color(Hue(p, q, h + 1f / 3f), Hue(p, q, h), Hue(p, q, h - 1f / 3f));
        }

        static float Hue(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        public static Color Lighten(Color c, float k) => Color.Lerp(c, Color.white, k);
        public static Color Darken(Color c, float k) => Color.Lerp(c, Color.black, k);
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ---------------------------------------------------------------- 둥근 모서리·원 스프라이트 (런타임 생성)
        const int SpriteSize = 64;
        const int SpriteBorder = 28;
        static Sprite _rounded, _circle, _ring;

        /// <summary>9-slice 둥근 사각형. Image.pixelsPerUnitMultiplier로 반지름을 바꾼다.</summary>
        public static Sprite RoundedSprite => _rounded != null ? _rounded : (_rounded = MakeSprite(Shape.Rounded));
        public static Sprite CircleSprite => _circle != null ? _circle : (_circle = MakeSprite(Shape.Circle));
        public static Sprite RingSprite => _ring != null ? _ring : (_ring = MakeSprite(Shape.Ring));

        enum Shape { Rounded, Circle, Ring }

        static Sprite MakeSprite(Shape shape)
        {
            int n = SpriteSize;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "RW_UI_" + shape,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[n * n];
            float r = shape == Shape.Rounded ? SpriteBorder : n * 0.5f - 1f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d;   // 가장자리까지 부호 거리 (안쪽 음수)
                if (shape == Shape.Rounded)
                {
                    // 둥근 사각형 부호 거리 함수
                    float half = n * 0.5f;
                    float qx = Mathf.Abs(fx - half) - (half - r), qy = Mathf.Abs(fy - half) - (half - r);
                    d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                }
                else
                {
                    d = Vector2.Distance(new Vector2(fx, fy), new Vector2(n * 0.5f, n * 0.5f)) - r;
                    if (shape == Shape.Ring) d = Mathf.Abs(d + 3.5f) - 3.5f;   // 두께 7px 고리
                }
                float a = Mathf.Clamp01(0.5f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var border = shape == Shape.Rounded ? new Vector4(SpriteBorder, SpriteBorder, SpriteBorder, SpriteBorder) : Vector4.zero;
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>Image를 둥근 사각형으로 (radius: 캔버스 px).</summary>
        public static void MakeRounded(Image img, float radius)
        {
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = SpriteBorder / Mathf.Max(1f, radius);
        }

        // ---------------------------------------------------------------- 캔버스·레이아웃
        public static Canvas CreateWorldCanvas(string name, Vector2 sizePx, Transform parent, float metersPerPixel = 0.001f,
            int padding = 24, float spacing = 14f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            scaler.referencePixelsPerUnit = 100f;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * metersPerPixel;

            var bg = go.AddComponent<Image>();
            bg.color = PanelBg;
            MakeRounded(bg, PanelRadius);

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
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
            var img = go.AddComponent<Image>();
            img.color = SectionBg;
            MakeRounded(img, 16f);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 16);
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            if (!string.IsNullOrEmpty(title))
            {
                var t = Label(go.transform, title, FontSmall, TextDim);
                t.fontStyle = FontStyle.Bold;
            }
            return (RectTransform)go.transform;
        }

        /// <summary>세로로 쌓는 빈 묶음 (배경 없음).</summary>
        public static RectTransform Column(Transform parent, float spacing = 12f, TextAnchor align = TextAnchor.UpperCenter,
            string name = "Column")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = align;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return (RectTransform)go.transform;
        }

        public static RectTransform Row(Transform parent, float height = 64f, float spacing = 10f, bool expandWidth = true)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = expandWidth;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleHeight = 0;   // 줄은 높이 고정 (안의 빈 칸 때문에 세로로 늘어나지 않게)
            return (RectTransform)go.transform;
        }

        /// <summary>남는 공간을 채우는 빈 칸 (가운데 정렬·버튼 벌리기용).</summary>
        public static LayoutElement Spacer(Transform parent, float minSize = 0f, float flexible = 1f)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            // 부모 방향으로만 늘어난다. 가로 줄의 빈 칸이 세로로도 늘어나면 줄 전체가 커져 화면 밖으로 밀림
            bool horizontal = parent.GetComponent<HorizontalLayoutGroup>() != null;
            if (horizontal) { le.minWidth = minSize; le.flexibleWidth = flexible; le.flexibleHeight = 0; }
            else { le.minHeight = minSize; le.flexibleHeight = flexible; le.flexibleWidth = 0; }
            return le;
        }

        public static Image Divider(Transform parent, float thickness = 2f)
        {
            var go = new GameObject("Divider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = DividerColor;
            img.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = thickness;
            le.preferredHeight = thickness;
            return img;
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
            t.lineSpacing = 1.1f;
            t.text = text;
            return t;
        }

        /// <summary>LayoutElement 가져오기 (Unity 객체는 ?? 로 null 검사가 안 되므로 명시적으로)</summary>
        static LayoutElement LayoutOf(Component c)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            return le;
        }

        public static void SetWidth(Component c, float width)
        {
            var le = LayoutOf(c);
            le.preferredWidth = width;
            le.minWidth = width;
            le.flexibleWidth = 0;
        }

        public static void SetHeight(Component c, float height)
        {
            var le = LayoutOf(c);
            le.preferredHeight = height;
            le.minHeight = height;
        }

        // ---------------------------------------------------------------- 버튼 (Normal / Hover / Pressed / Disabled)

        /// <summary>
        /// 버튼. 색은 Image 색이 아니라 ColorBlock으로 지정해 네 상태가 같은 규칙으로 바뀐다.
        /// width ≤ 0이면 줄 안에서 늘어난다. height 기본 120px(0.12m).
        /// </summary>
        public static Button Button(Transform parent, string label, UnityAction onClick, Color? color = null, float width = -1f,
            float height = ButtonHeight, int fontSize = FontBody)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            MakeRounded(img, ButtonRadius);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ApplyColors(btn, color ?? Primary);
            if (onClick != null) btn.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            if (width > 0) { le.preferredWidth = width; le.minWidth = width; le.flexibleWidth = 0; }
            else { le.minWidth = Mathf.Min(ButtonMinWidth, 160f); le.flexibleWidth = 1; }

            var t = Label(go.transform, label, fontSize, Color.white, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform, 10, 4);
            return btn;
        }

        static void ApplyColors(Button b, Color c)
        {
            var cb = b.colors;
            cb.normalColor = c;
            cb.highlightedColor = Lighten(c, 0.18f);   // 포인터가 올라감
            cb.pressedColor = Darken(c, 0.28f);        // 누름
            cb.selectedColor = c;
            cb.disabledColor = DisabledBg;             // 이 단계에서 쓸 수 없음
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static void SetButtonColor(Button b, Color c)
        {
            if (b == null) return;
            ApplyColors(b, c);
            if (b.targetGraphic != null)
                b.targetGraphic.CrossFadeColor(b.interactable ? c : DisabledBg, 0f, true, true);
        }

        /// <summary>쓸 수 없는 버튼은 글자도 흐리게.</summary>
        public static void SetInteractable(Button b, bool on)
        {
            if (b == null) return;
            b.interactable = on;
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = on ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }

        public static InputField Input(Transform parent, string placeholder, float height = 64f, bool multiline = false,
            InputField.ContentType contentType = InputField.ContentType.Standard, int fontSize = FontBody)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = InputBg;
            MakeRounded(img, 14f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1;

            var anchor = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            var text = Label(go.transform, "", fontSize, new Color(0.08f, 0.09f, 0.11f), anchor);
            text.supportRichText = false;
            Stretch(text.rectTransform, 20, 14);
            var ph = Label(go.transform, placeholder, fontSize, new Color(0.45f, 0.47f, 0.52f), anchor);
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform, 20, 14);

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

            float track = Mathf.Max(10f, height * 0.18f);
            var bg = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(1f, 1f, 1f, 0.18f);
            MakeRounded(bg, track * 0.5f);
            Stretch(bg.rectTransform, 0, (height - track) * 0.5f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            Stretch((RectTransform)fillArea.transform, 0, (height - track) * 0.5f);
            var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.transform.SetParent(fillArea.transform, false);
            fill.color = Primary;
            MakeRounded(fill, track * 0.5f);
            Stretch(fill.rectTransform);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            Stretch((RectTransform)handleArea.transform, height * 0.3f, 0);
            var handle = new GameObject("Handle", typeof(RectTransform)).AddComponent<Image>();
            handle.transform.SetParent(handleArea.transform, false);
            handle.sprite = CircleSprite;
            handle.color = Color.white;
            handle.rectTransform.sizeDelta = new Vector2(height * 0.6f, -height * 0.4f);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            var cb = slider.colors;
            cb.highlightedColor = Lighten(Primary, 0.5f);
            cb.pressedColor = Lighten(Primary, 0.2f);
            slider.colors = cb;
            return slider;
        }

        public static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        /// <summary>원 배지 (단계 번호·체크 표시). 글자는 배지 안 가운데.</summary>
        public static (Image bg, Text text) Badge(Transform parent, float size, string text, Color color, bool ring = false,
            int fontSize = FontSmall)
        {
            var go = new GameObject("Badge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = ring ? RingSprite : CircleSprite;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = true;
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = size;
            le.minHeight = le.preferredHeight = size;
            le.flexibleWidth = 0;
            var t = Label(go.transform, text, fontSize, Color.white, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            return (img, t);
        }

        /// <summary>
        /// 라벨 + [-] 큰 숫자·단위 [+]. 가상 키보드 없이 숫자를 바꾸는 VR용 입력 (S03).
        /// </summary>
        public static Stepper Stepper(Transform parent, string label, string unit, int min, int max, int step, int value,
            float height = 104f)
        {
            var row = Row(parent, height, 16, false);
            var l = Label(row, label, FontBody, TextMain);
            l.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var s = new Stepper { Min = min, Max = max, Step = step, Unit = unit };
            s.Minus = Button(row, "-", () => s.Value -= s.Step, Secondary, 130, height, FontLarge);
            s.ValueText = Label(row, "", FontLarge, TextMain, TextAnchor.MiddleCenter);
            s.ValueText.fontStyle = FontStyle.Bold;
            SetWidth(s.ValueText, 210);
            s.Plus = Button(row, "+", () => s.Value += s.Step, Secondary, 130, height, FontLarge);
            s.Value = value;
            return s;
        }

        /// <summary>예전 형식 (단위 없음).</summary>
        public static Stepper Stepper(Transform parent, string label, int min, int max, int step, int value) =>
            Stepper(parent, label, "", min, max, step, value, 64f);
    }

    public class Stepper
    {
        public int Min, Max, Step;
        public string Unit = "";
        public Text ValueText;
        public Button Minus, Plus;
        public event Action<int> OnChanged;
        int _value;

        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(Min, Math.Min(Max, value));
                if (ValueText != null) ValueText.text = _value + Unit;
                if (Minus != null) UIFactory.SetInteractable(Minus, _value > Min);
                if (Plus != null) UIFactory.SetInteractable(Plus, _value < Max);
                OnChanged?.Invoke(_value);
            }
        }

        public bool Interactable
        {
            set
            {
                if (Minus != null) UIFactory.SetInteractable(Minus, value && _value > Min);
                if (Plus != null) UIFactory.SetInteractable(Plus, value && _value < Max);
            }
        }
    }
}
