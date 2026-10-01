using System.IO;
using System.Linq;
using RobotWarehouse.App;
using RobotWarehouse.Core;
using RobotWarehouse.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RobotWarehouse.EditorTools
{
    /// <summary>
    /// 메뉴 한 번으로 시연 씬과 Quest 빌드 설정을 만든다.
    ///   RobotWarehouse > 1. 시연 씬 만들기
    ///   RobotWarehouse > 2. Quest 빌드 설정 적용
    /// </summary>
    public static class RobotWarehouseSetup
    {
        const string Root = "Assets/RobotWarehouse";
        const string MatDir = Root + "/Materials";
        const string SceneDir = Root + "/Scenes";
        const string ScenePath = SceneDir + "/RobotWarehouse.unity";
        const string FontPath = Root + "/Resources/Fonts/NanumGothic.ttf";

        static readonly string[] XrOriginPrefabNames =
        {
            "Complete XR Origin Set Up Variant",
            "Complete XR Origin Set Up",
            "XR Origin (XR Rig)",
        };

        [MenuItem("RobotWarehouse/1. 시연 씬 만들기", priority = 1)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(SceneDir);

            var block = CreateMaterial("RW_Block", LitShader(), Color.white, true);
            var overlay = CreateMaterial("RW_Overlay", Shader.Find("Sprites/Default"), Color.white, false);
            var floorMat = CreateMaterial("RW_Floor", LitShader(), new Color(0.42f, 0.44f, 0.47f), true);
            var tableMat = CreateMaterial("RW_Table", LitShader(), new Color(0.23f, 0.25f, 0.29f), true);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 조명
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;   // Quest 성능 (NFR-01)
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.6f);

            // 바닥·테이블
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            table.transform.position = new Vector3(0f, 0.38f, 1.1f);
            table.transform.localScale = new Vector3(1.7f, 0.76f, 1.0f);
            table.GetComponent<Renderer>().sharedMaterial = tableMat;

            // XR Origin (VR 템플릿 프리팹)
            var rig = InstantiateXrOrigin();
            var cam = Camera.main;

            // 앱
            var app = new GameObject("RobotWarehouseApp");
            var lib = app.AddComponent<MaterialLibrary>();
            lib.block = block;
            lib.overlay = overlay;
            lib.font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            var controller = app.AddComponent<AppController>();
            controller.tableCenter = new Vector3(0f, 0.8f, 1.1f);
            controller.table = table;

            if (rig != null)
            {
                var desk = rig.AddComponent<DesktopRigController>();
                desk.head = cam != null ? cam.transform : null;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildFirst(ScenePath);
            Selection.activeGameObject = app;
            Debug.Log($"[RobotWarehouse] 씬 생성 완료: {ScenePath} (XR Origin: {(rig != null ? rig.name : "없음 → 기본 카메라")})");
        }

        static Shader LitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        }

        static Material CreateMaterial(string name, Shader shader, Color color, bool instancing)
        {
            var path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            mat.enableInstancing = instancing;
            MaterialLibrary.SetMaterialColor(mat, color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static GameObject InstantiateXrOrigin()
        {
            foreach (var name in XrOriginPrefabNames)
            {
                var guid = AssetDatabase.FindAssets($"\"{name}\" t:Prefab")
                    .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
                if (guid == null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = Vector3.zero;
                return go;
            }
            // 프리팹을 못 찾으면 에디터 확인용 카메라만 둔다
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 1.6f, 0f);
            var rigGo = new GameObject("DesktopRig");
            camGo.transform.SetParent(rigGo.transform, true);
            return rigGo;
        }

        static void AddSceneToBuildFirst(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("RobotWarehouse/2. Quest 빌드 설정 적용", priority = 2)]
        public static void ApplyQuestSettings()
        {
            // 같은 Wi-Fi의 서버 노트북에 http:// 로 접속하기 위해 필요
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.forceInternetPermission = true;
            // Quest 2 설치 호환: 템플릿 기본값(34)보다 낮춘다. Meta 권장 최소 32 [검증 필요]
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.productName = "RobotWarehouse";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.teama.robotwarehouse");
            AssetDatabase.SaveAssets();
            Debug.Log("[RobotWarehouse] Quest 빌드 설정 적용: HTTP 허용, 인터넷 권한, minSdk 32, IL2CPP ARM64");
        }

        [MenuItem("RobotWarehouse/3. 모의 서버 실행 방법 보기", priority = 20)]
        public static void ShowServerHelp()
        {
            EditorUtility.DisplayDialog("모의 서버",
                "D:\\hyang\\robotwarehouse\\mock_server 에서\n\n" +
                "pip install -r requirements.txt\nuvicorn server:app --host 0.0.0.0 --port 8000\n\n" +
                "에디터에서는 서버 IP 127.0.0.1, Quest에서는 노트북의 Wi-Fi IP를 입력합니다.", "확인");
        }
    }
}
