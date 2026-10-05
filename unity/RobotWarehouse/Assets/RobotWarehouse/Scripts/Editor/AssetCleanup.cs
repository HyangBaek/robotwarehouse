using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RobotWarehouse.EditorTools
{
    /// <summary>
    /// 안 쓰는 외부 에셋 정리 (메뉴: RobotWarehouse > 에셋 정리).
    /// 1) 빌드 씬과 Assets/RobotWarehouse 가 실제로 쓰는 파일을 찾아 AssetSkin/Source 로 옮긴다.
    ///    Unity 안에서 옮기므로 GUID가 그대로라 선반·로봇·박스·파레트 연결이 끊기지 않는다.
    /// 2) 옮긴 뒤 다시 검사해 정리 대상 폴더를 가리키는 연결이 하나도 없을 때만 그 폴더들을 지운다.
    /// </summary>
    public static class AssetCleanup
    {
        /// <summary>정리 대상 (통째로 지울 폴더). 이미 지운 폴더는 건너뛴다.</summary>
        static readonly string[] Targets =
        {
            "Assets/UnityWarehouseSceneHDRP",
            "Assets/SciFi Warehouse Kit",
            "Assets/_Recovery",
            "Assets/M.SKILL",
            "Assets/3D Laboratory Environment with Appratus",
            "Assets/Megapoly.Art",
        };

        const string KeepDir = "Assets/RobotWarehouse/AssetSkin/Source";
        const string Menu = "RobotWarehouse/에셋 정리/";

        [MenuItem(Menu + "1. 미리 보기 (아무것도 바꾸지 않음)")]
        static void Preview()
        {
            var plan = MakePlan();
            var msg = plan.Describe();
            Debug.Log("[에셋 정리 미리 보기]\n" + msg);
            EditorUtility.DisplayDialog("에셋 정리 미리 보기", msg + "\n\n자세한 목록은 Console 창에 있어요.", "확인");
        }

        [MenuItem(Menu + "2. 정리 실행 (쓰는 파일 옮기고 나머지 삭제)")]
        static void Execute()
        {
            var plan = MakePlan();
            if (plan.ExistingTargets.Count == 0)
            {
                EditorUtility.DisplayDialog("에셋 정리", "정리할 폴더가 없어요. 이미 정리된 상태입니다.", "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog("에셋 정리 실행",
                    plan.Describe() + "\n\n진행하기 전에 git 커밋이나 프로젝트 백업을 해 두세요. 진행할까요?",
                    "정리 실행", "취소"))
                return;

            // 열린 씬에 저장 안 한 변경이 있으면 먼저 저장 여부를 묻는다
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                AssetDatabase.StartAssetEditing();
                EnsureFolder(KeepDir);
                foreach (var src in plan.Used)
                {
                    var dst = AssetDatabase.GenerateUniqueAssetPath(KeepDir + "/" + Path.GetFileName(src));
                    var err = AssetDatabase.MoveAsset(src, dst);
                    if (!string.IsNullOrEmpty(err))
                    {
                        Fail($"파일을 옮기지 못해 중단했어요 (아무것도 지우지 않음).\n{src}\n{err}");
                        return;
                    }
                    Debug.Log($"[에셋 정리] 옮김: {src} -> {dst}");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // 안전 확인: 옮긴 뒤에도 정리 대상 폴더를 가리키는 연결이 남아 있으면 지우지 않는다
            var left = FindUsedInTargets();
            if (left.Count > 0)
            {
                Fail("옮긴 뒤에도 아직 쓰는 파일이 정리 대상 폴더에 남아 있어 삭제를 멈췄어요.\n" + string.Join("\n", left.Take(10)));
                return;
            }

            long freed = 0;
            var failed = new List<string>();
            foreach (var t in plan.ExistingTargets)
            {
                freed += FolderBytes(t);
                if (!AssetDatabase.DeleteAsset(t)) failed.Add(t);
                else Debug.Log($"[에셋 정리] 삭제: {t}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var done = $"정리했어요.\n옮긴 파일 {plan.Used.Count}개 -> {KeepDir}\n지운 폴더 {plan.ExistingTargets.Count - failed.Count}개, 약 {freed / 1048576f:0} MB 줄었어요.";
            if (failed.Count > 0) done += "\n\n지우지 못한 폴더 (Unity를 닫고 탐색기에서 지워 주세요):\n" + string.Join("\n", failed);
            done += "\n\nPlay를 눌러 선반·로봇·박스·파레트가 그대로 보이는지 확인해 주세요.";
            Debug.Log("[에셋 정리] " + done);
            EditorUtility.DisplayDialog("에셋 정리 완료", done, "확인");
        }

        // ------------------------------------------------------------------ 계획

        class Plan
        {
            public List<string> Used = new List<string>();
            public List<string> ExistingTargets = new List<string>();
            public long UsedBytes, TargetBytes;

            public string Describe()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"남길 파일 (쓰는 것): {Used.Count}개, 약 {UsedBytes / 1048576f:0} MB -> {KeepDir} 로 옮김");
                foreach (var u in Used) sb.AppendLine("  · " + u);
                sb.AppendLine();
                sb.AppendLine($"지울 폴더: {ExistingTargets.Count}개, 약 {(TargetBytes - UsedBytes) / 1048576f:0} MB 줄어듦");
                foreach (var t in ExistingTargets) sb.AppendLine("  · " + t);
                return sb.ToString().TrimEnd();
            }
        }

        static Plan MakePlan()
        {
            var plan = new Plan
            {
                Used = FindUsedInTargets(),
                ExistingTargets = Targets.Where(AssetDatabase.IsValidFolder).ToList(),
            };
            plan.UsedBytes = plan.Used.Sum(FileBytes);
            plan.TargetBytes = plan.ExistingTargets.Sum(FolderBytes);
            return plan;
        }

        /// <summary>빌드에 켜진 씬과 Assets/RobotWarehouse 의 모든 에셋이 (간접 포함) 쓰는 파일 중 정리 대상 폴더 안에 있는 것.</summary>
        static List<string> FindUsedInTargets()
        {
            var roots = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            roots.AddRange(AssetDatabase.FindAssets("", new[] { "Assets/RobotWarehouse" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p)));
            var deps = AssetDatabase.GetDependencies(roots.Distinct().ToArray(), true);
            return deps.Where(InTarget).Distinct().OrderBy(p => p).ToList();
        }

        static bool InTarget(string path) =>
            Targets.Any(t => path.StartsWith(t + "/", System.StringComparison.Ordinal));

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static long FileBytes(string assetPath)
        {
            var f = new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
            return f.Exists ? f.Length : 0;
        }

        static long FolderBytes(string assetPath)
        {
            var d = new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
            return d.Exists ? d.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }

        static void Fail(string msg)
        {
            Debug.LogError("[에셋 정리] " + msg);
            EditorUtility.DisplayDialog("에셋 정리 중단", msg, "확인");
        }
    }
}
