using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InfraTown.Tests.Build
{
    /// <summary>
    /// 빌드 테스트용 Windows 플레이어 빌드 (Tools/run-build-test.ps1이 -executeMethod로 부름).
    /// INFRATOWN_BUILD_TEST 정의를 이 빌드에만 넣어 BuildPlaytestRunner를 포함시킨다.
    /// 프로젝트의 Build Settings(씬 목록)는 바꾸지 않는다.
    /// </summary>
    public static class BuildTestBuilder
    {
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            string outDir = Arg(args, "-buildTestOut") ?? "Logs/BuildTest/Player";
            string scenesArg = Arg(args, "-buildTestScenes") ?? "Assets/Scenes/1park2.unity";
            var scenes = scenesArg.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

            foreach (var scene in scenes)
            {
                if (!File.Exists(scene))
                {
                    Debug.LogError($"[BuildTest] 씬이 없음: {scene}");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "InfraTown.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "INFRATOWN_BUILD_TEST" },
            };

            Debug.Log($"[BuildTest] 빌드 시작: {string.Join(", ", scenes)} → {options.locationPathName}");
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildTest] 빌드 결과: {summary.result}, {summary.totalSize / (1024f * 1024f):F1}MB, {summary.totalTime.TotalSeconds:F0}초, 에러 {summary.totalErrors}");
            File.WriteAllText(Path.Combine(outDir, "build-result.txt"),
                $"{summary.result}\t{summary.totalTime.TotalSeconds:F0}\t{summary.totalErrors}\t{string.Join(";", scenes)}");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
