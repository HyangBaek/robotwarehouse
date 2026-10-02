using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RobotWarehouse.Warehouse;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RobotWarehouse.EditorTools
{
    /// <summary>
    /// RobotWarehouse > 2. 창고 에셋 적용 (Unity Warehouse → URP)
    ///
    /// 'Unity Warehouse Scene (HDRP)' 에셋은 HDRP 셰이더 그래프를 써서 이 프로젝트(URP)에서는 분홍색으로 보인다.
    /// 원본은 건드리지 않고, 필요한 프리팹(선반·로봇·상자·파레트)만 URP Lit 머티리얼로 바꾼 사본을 만든다.
    ///   - 머티리얼: .mat 파일을 직접 읽어 알베도·노멀 텍스처와 색을 URP Lit로 옮김 (HDRP 셰이더가 없어도 동작)
    ///   - 프리팹: 스크립트·충돌체·조명 제거, 그림자용 평면 제거, 피벗을 바닥 중앙으로 맞춤
    ///   - 결과: Assets/RobotWarehouse/AssetSkin/ 와 Resources/WarehouseSkin.asset (런타임이 자동 사용)
    /// </summary>
    public static class WarehouseAssetConverter
    {
        const string OutDir = "Assets/RobotWarehouse/AssetSkin";
        const string MatDir = OutDir + "/Materials";
        const string PrefabDir = OutDir + "/Prefabs";
        const string SkinPath = "Assets/RobotWarehouse/Resources/WarehouseSkin.asset";

        static readonly string[] BoxNames =
            { "Cardboard_A1", "Cardboard_A2", "Cardboard_B1", "Cardboard_B2", "Cardboard_C1", "Cardboard_C2" };

        /// <summary>Quest 2에서 선반 1개 기준 경고할 삼각형 수.</summary>
        const int ShelfTriangleWarn = 20000;

        [MenuItem("RobotWarehouse/2. 창고 에셋 적용 (Unity Warehouse → URP)", priority = 2)]
        public static void Apply()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                EditorUtility.DisplayDialog("창고 에셋 적용", "URP Lit 셰이더를 찾지 못했습니다. URP 프로젝트인지 확인하세요.", "확인");
                return;
            }

            var shelf = FindPrefab("Shelf");
            var robot = FindPrefab("Palletrobot");
            var pallet = FindPrefab("Pallet");
            var boxes = BoxNames.Select(FindPrefab).Where(p => p != null).ToArray();
            if (shelf == null && robot == null)
            {
                EditorUtility.DisplayDialog("창고 에셋 적용",
                    "Unity Warehouse 에셋의 Shelf / Palletrobot 프리팹을 찾지 못했습니다.\n" +
                    "Window > Package Manager > My Assets 에서 에셋을 Import 했는지 확인하세요.", "확인");
                return;
            }

            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(PrefabDir);
            var report = new StringBuilder();
            var cache = new Dictionary<Material, Material>();

            try
            {
                AssetDatabase.StartAssetEditing();
                // 실행 중 정적 배칭(StaticBatchingUtility.Combine)을 쓰려면 메시 Read/Write가 필요하다
                MakeReadable(shelf);
                foreach (var b in boxes) MakeReadable(b);
                if (pallet != null) MakeReadable(pallet);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var skin = LoadOrCreateSkin();
            Vector3 size;
            int tris;

            try
            {
                EditorUtility.DisplayProgressBar("창고 에셋 적용", "선반", 0.1f);
                if (shelf != null)
                {
                    skin.shelfPrefab = ConvertPrefab(shelf, "RW_Shelf", lit, cache, out size, out tris);
                    skin.shelfSize = size;
                    report.AppendLine($"선반: {Fmt(size)}  삼각형 {tris:N0}개");
                    if (tris > ShelfTriangleWarn)
                        report.AppendLine($"  ※ 선반 하나의 삼각형이 많습니다. Quest FPS가 낮으면 WarehouseSkin에서 useSkin을 끄거나 boxFill을 낮추세요.");
                }

                EditorUtility.DisplayProgressBar("창고 에셋 적용", "로봇", 0.4f);
                if (robot != null)
                {
                    skin.robotPrefab = ConvertPrefab(robot, "RW_Palletrobot", lit, cache, out size, out tris);
                    skin.robotSize = size;
                    report.AppendLine($"로봇: {Fmt(size)}  삼각형 {tris:N0}개");
                }

                EditorUtility.DisplayProgressBar("창고 에셋 적용", "상자", 0.6f);
                var convertedBoxes = new List<GameObject>();
                for (int i = 0; i < boxes.Length; i++)
                {
                    var b = ConvertPrefab(boxes[i], "RW_" + boxes[i].name, lit, cache, out size, out tris);
                    if (i == 0) skin.boxSize = size;
                    convertedBoxes.Add(b);
                }
                skin.boxPrefabs = convertedBoxes.ToArray();
                report.AppendLine($"상자: {convertedBoxes.Count}종  {Fmt(skin.boxSize)}");

                EditorUtility.DisplayProgressBar("창고 에셋 적용", "파레트", 0.8f);
                if (pallet != null)
                {
                    skin.palletPrefab = ConvertPrefab(pallet, "RW_Pallet", lit, cache, out size, out tris);
                    skin.palletSize = size;
                }

                skin.useSkin = true;
                EditorUtility.SetDirty(skin);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                report.AppendLine("오류: " + e.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            report.AppendLine($"머티리얼 {cache.Count}개를 URP Lit으로 변환했습니다.");
            report.AppendLine("Play 하면 랙·로봇이 에셋 모델로 바뀝니다. 되돌리려면 Resources/WarehouseSkin 의 useSkin을 끄세요.");
            Debug.Log("[RobotWarehouse] 창고 에셋 적용\n" + report);
            Selection.activeObject = skin;
            EditorUtility.DisplayDialog("창고 에셋 적용 완료", report.ToString(), "확인");
        }

        static string Fmt(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.00}×{1:0.00}×{2:0.00}m", v.x, v.y, v.z);

        // ------------------------------------------------------------------ 검색

        static GameObject FindPrefab(string name)
        {
            var candidates = AssetDatabase.FindAssets($"{name} t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == name && !p.StartsWith(OutDir))
                .OrderByDescending(p => p.IndexOf("Warehouse", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            return candidates.Count == 0 ? null : AssetDatabase.LoadAssetAtPath<GameObject>(candidates[0]);
        }

        static void MakeReadable(GameObject prefab)
        {
            if (prefab == null) return;
            var paths = new HashSet<string>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) paths.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));
            foreach (var path in paths)
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                }
            }
        }

        static WarehouseSkin LoadOrCreateSkin()
        {
            var skin = AssetDatabase.LoadAssetAtPath<WarehouseSkin>(SkinPath);
            if (skin != null) return skin;
            Directory.CreateDirectory(Path.GetDirectoryName(SkinPath));
            skin = ScriptableObject.CreateInstance<WarehouseSkin>();
            AssetDatabase.CreateAsset(skin, SkinPath);
            return skin;
        }

        // ------------------------------------------------------------------ 프리팹

        static GameObject ConvertPrefab(GameObject src, string outName, Shader lit,
            Dictionary<Material, Material> cache, out Vector3 size, out int triangles)
        {
            var inst = (GameObject)Object.Instantiate(src);   // 원본과 연결이 끊긴 사본
            inst.name = src.name;
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            GameObject root = null;
            try
            {
                KeepLowestLod(inst);
                StripComponents(inst);

                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    var kept = new List<Material>();
                    bool anyVisible = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || IsShadowMaterial(m)) { kept.Add(null); continue; }
                        kept.Add(ConvertMaterial(m, lit, cache));
                        anyVisible = true;
                    }
                    if (!anyVisible)
                    {
                        // 바닥 그림자용 평면 등 → 제거
                        var mf = r.GetComponent<MeshFilter>();
                        Object.DestroyImmediate(r);
                        if (mf != null) Object.DestroyImmediate(mf);
                        continue;
                    }
                    var fallback = kept.First(k => k != null);
                    r.sharedMaterials = kept.Select(k => k ?? fallback).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.Off;   // Quest 성능 (그림자 끔)
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

                var rends = inst.GetComponentsInChildren<Renderer>(true);
                var b = rends.Length > 0 ? rends[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (var r in rends) b.Encapsulate(r.bounds);
                size = b.size;
                triangles = 0;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) triangles += CountTriangles(mf.sharedMesh);

                // 피벗을 바닥 중앙으로: 빈 부모를 바닥 중앙에 두고 모델을 그 아래로
                root = new GameObject(outName);
                root.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
                inst.transform.SetParent(root.transform, true);
                root.transform.position = Vector3.zero;

                var path = $"{PrefabDir}/{outName}.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                else if (inst != null) Object.DestroyImmediate(inst);
            }
        }

        static int CountTriangles(Mesh m)
        {
            int n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += (int)(m.GetIndexCount(i) / 3);
            return n;
        }

        /// <summary>LOD 그룹이 있으면 가장 가벼운 LOD만 남긴다 (Quest 성능).</summary>
        static void KeepLowestLod(GameObject go)
        {
            foreach (var group in go.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                if (lods.Length > 1)
                {
                    var keep = new HashSet<Renderer>(lods[lods.Length - 1].renderers.Where(r => r != null));
                    foreach (var lod in lods)
                        foreach (var r in lod.renderers)
                            if (r != null && !keep.Contains(r)) Object.DestroyImmediate(r.gameObject);
                }
                Object.DestroyImmediate(group);
            }
        }

        /// <summary>렌더링에 필요한 컴포넌트만 남긴다 (스크립트·충돌체·조명·애니메이터 제거).</summary>
        static void StripComponents(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer)
                            continue;
                        try { Object.DestroyImmediate(c); } catch { /* 의존 컴포넌트는 다음 바퀴에 지운다 */ }
                    }
                }
            }
        }

        static bool IsShadowMaterial(Material m) =>
            m.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0;

        // ------------------------------------------------------------------ 머티리얼

        static readonly string[] AlbedoProps =
            { "_MainTex", "_BaseColorMap", "_BaseMap", "_Albedo", "_AlbedoMap", "_Albedo_Map", "_albedo", "_Base_Map", "_Diffuse", "_Color_Map" };

        /// <summary>HDRP 셰이더 그래프 머티리얼 → URP Lit. .mat 파일 텍스트에서 값을 읽는다.</summary>
        static Material ConvertMaterial(Material src, Shader lit, Dictionary<Material, Material> cache)
        {
            if (cache.TryGetValue(src, out var done)) return done;

            var srcPath = AssetDatabase.GetAssetPath(src);
            var props = ReadMaterialFile(srcPath);

            Texture albedo = null;
            foreach (var p in AlbedoProps)
                if (props.Textures.TryGetValue(p, out albedo) && albedo != null) break;
            if (albedo == null)
                albedo = props.Textures.Where(kv => kv.Key.IndexOf("albedo", StringComparison.OrdinalIgnoreCase) >= 0
                                                   || kv.Key.IndexOf("basecolor", StringComparison.OrdinalIgnoreCase) >= 0)
                                       .Select(kv => kv.Value).FirstOrDefault(t => t != null);

            Texture normal = props.Textures
                .Where(kv => kv.Key.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                             && !kv.Key.EndsWith("OS", StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .FirstOrDefault(t => t != null && IsNormalMap(t));

            var tint = Color.white;
            if (props.Colors.TryGetValue("_Color", out var c))
            {
                if (c.maxColorComponent > 1.5f) c = Color.white;          // HDR 강도값은 색이 아님
                if (albedo == null)
                {
                    // 텍스처 없는 단색 재질: 너무 어두우면 선형값으로 보고 감마로 바꿔 쓴다
                    tint = c.maxColorComponent < 0.2f ? c.gamma : c;
                }
                else if (c.maxColorComponent >= 0.2f)
                {
                    tint = c;
                }
            }
            tint.a = 1f;

            var outPath = $"{MatDir}/URP_{San(src.name)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(outPath);
            bool isNew = mat == null;
            if (isNew) mat = new Material(lit);
            mat.shader = lit;
            mat.name = "URP_" + src.name;
            mat.SetColor("_BaseColor", tint);
            mat.SetColor("_Color", tint);
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTexture("_MainTex", albedo);
                foreach (var tilingKey in new[] { "_Tiling", "_Albedo_Tiling" })
                {
                    if (props.Colors.TryGetValue(tilingKey, out var tv) && tv.r > 0f && tv.g > 0f)
                    {
                        mat.SetTextureScale("_BaseMap", new Vector2(tv.r, tv.g));
                        break;
                    }
                }
            }
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            bool metal = src.name.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0
                         || src.name.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0;
            mat.SetFloat("_Metallic", metal ? 0.6f : 0f);
            mat.SetFloat("_Smoothness", metal ? 0.45f : 0.25f);
            mat.enableInstancing = true;

            if (isNew) AssetDatabase.CreateAsset(mat, outPath);
            else EditorUtility.SetDirty(mat);
            cache[src] = mat;
            return mat;
        }

        static bool IsNormalMap(Texture t)
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
            return imp != null && imp.textureType == TextureImporterType.NormalMap;
        }

        static string San(string s) => Regex.Replace(s, @"[^\w\-]", "_");

        class MatProps
        {
            public readonly Dictionary<string, Texture> Textures = new Dictionary<string, Texture>();
            public readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();
        }

        static readonly Regex TexRe = new Regex(
            @"- (\w+):\s*\r?\n\s*m_Texture: \{fileID: (-?\d+), guid: ([0-9a-f]{32})", RegexOptions.Compiled);
        static readonly Regex ColRe = new Regex(
            @"- (\w+): \{r: ([-\d.eE+]+), g: ([-\d.eE+]+), b: ([-\d.eE+]+), a: ([-\d.eE+]+)\}", RegexOptions.Compiled);

        /// <summary>
        /// .mat 파일(YAML)을 직접 읽는다. 셰이더(HDRP)가 이 프로젝트에 없으면 Material API로는
        /// 텍스처·색을 읽을 수 없기 때문이다.
        /// </summary>
        static MatProps ReadMaterialFile(string path)
        {
            var result = new MatProps();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
            var text = File.ReadAllText(path);
            foreach (Match m in TexRe.Matches(text))
            {
                var texPath = AssetDatabase.GUIDToAssetPath(m.Groups[3].Value);
                if (string.IsNullOrEmpty(texPath)) continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                if (tex != null && !result.Textures.ContainsKey(m.Groups[1].Value))
                    result.Textures[m.Groups[1].Value] = tex;
            }
            foreach (Match m in ColRe.Matches(text))
            {
                float F(int i) => float.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
                result.Colors[m.Groups[1].Value] = new Color(F(2), F(3), F(4), F(5));
            }
            return result;
        }
    }
}
