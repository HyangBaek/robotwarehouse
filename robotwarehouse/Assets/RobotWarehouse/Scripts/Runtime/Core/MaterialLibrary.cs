using System.Collections.Generic;
using UnityEngine;

namespace RobotWarehouse.Core
{
    /// <summary>
    /// 머티리얼 참조 모음. 씬 빌더(에디터)가 URP Lit 머티리얼을 만들어 넣어
    /// 빌드에서 셰이더가 빠지지 않게 한다. 비어 있으면 항상 포함되는 Sprites/Default로 대체한다.
    /// 블록 색은 색마다 머티리얼 하나를 캐시해 공유한다 (SRP Batcher가 묶을 수 있게).
    /// </summary>
    public class MaterialLibrary : MonoBehaviour
    {
        public static MaterialLibrary Instance { get; private set; }

        [Tooltip("랙·벽·로봇용 Lit 머티리얼 (URP Lit)")] public Material block;
        [Tooltip("바닥·히트맵·강조·경로 선용 Unlit 투명 머티리얼 (Sprites/Default)")] public Material overlay;
        [Tooltip("한글 UI 폰트")] public Font font;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        static readonly Dictionary<Color, Material> _blockCache = new Dictionary<Color, Material>();
        static Material _fallback;

        void Awake() { Instance = this; }

        static MaterialLibrary Find()
        {
            if (Instance == null) Instance = Object.FindFirstObjectByType<MaterialLibrary>();
            return Instance;
        }

        static Material Fallback
        {
            get
            {
                if (_fallback == null) _fallback = new Material(Shader.Find("Sprites/Default"));
                return _fallback;
            }
        }

        public static Material Block => Find() != null && Instance.block != null ? Instance.block : Fallback;
        public static Material Overlay => Find() != null && Instance.overlay != null ? Instance.overlay : Fallback;
        public static Material Line => Overlay;

        /// <summary>색별로 공유되는 블록 머티리얼.</summary>
        public static Material BlockColored(Color c)
        {
            if (_blockCache.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(Block) { name = $"Block_{ColorUtility.ToHtmlStringRGB(c)}", enableInstancing = true };
            SetMaterialColor(m, c);
            _blockCache[c] = m;
            return m;
        }

        public static Font UIFont
        {
            get
            {
                if (Find() != null && Instance.font != null) return Instance.font;
                var f = Resources.Load<Font>("Fonts/NanumGothic");
                if (f != null) return f;
                try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                return f;
            }
        }

        public static void SetTexture(Material m, Texture tex)
        {
            if (m.HasProperty(MainTexId)) m.SetTexture(MainTexId, tex);
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, tex);
        }

        public static void SetMaterialColor(Material m, Color c)
        {
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
        }
    }
}
