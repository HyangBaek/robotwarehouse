using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace RobotWarehouse.EditorTools
{
    /// <summary>
    /// RobotWarehouse 메뉴
    ///   1. 서버 연결                        -> ServerConnectWindow
    ///   2. 창고 에셋 적용 (Unity Warehouse -> URP) -> WarehouseAssetConverter
    ///   3. Quest 빌드 설정 적용
    ///   4. 서버 실행 방법 보기 (Agent 서버 · 모의 서버)
    /// 시연 씬은 MainScene(빌드 첫 번째 씬)을 쓴다.
    /// </summary>
    public static class RobotWarehouseSetup
    {
        [MenuItem("RobotWarehouse/3. Quest 빌드 설정 적용", priority = 3)]
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

        [MenuItem("RobotWarehouse/4. 서버 실행 방법 보기", priority = 20)]
        public static void ShowServerHelp()
        {
            EditorUtility.DisplayDialog("서버 실행",
                "[Agent 서버 (실제 LLM·STT·경로 엔진)]\n" +
                "D:\\hyang\\robotwarehouse\\server 에서\n" +
                "pip install -r requirements.txt   (처음 한 번, .env.example → .env 복사)\n" +
                "ssh -N -L 11434:localhost:<Ollama 포트> <계정>@<GPU 서버>   (LLM 터널, 창 유지)\n" +
                "uvicorn api.main:app --host 0.0.0.0 --port 8000\n\n" +
                "[모의 서버 (LLM·STT 없이)]\n" +
                "D:\\hyang\\robotwarehouse\\mock_server 에서\n" +
                "uvicorn server:app --host 0.0.0.0 --port 8000\n\n" +
                "두 서버 모두 포트 8000이라 VR 쪽 설정은 같습니다 (1. 서버 연결 → 127.0.0.1 / 8000).\n" +
                "Quest는 USB로 꽂고 adb reverse tcp:8000 tcp:8000 을 실행하면 127.0.0.1 그대로 접속됩니다.", "확인");
        }
    }
}
