using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// batchmode からのビルド。例:
    ///   Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod Edgewise.EditorTools.BuildScript.BuildWebGL -logFile build.log
    /// 出力先は Builds/WebGL（Main と Debug シーン）。失敗したら終了コード 1 で Editor を終了する。
    /// </summary>
    public static class BuildScript
    {
        public const string WebGLOutput = "Builds/WebGL";

        [MenuItem("Edgewise/Build WebGL")]
        public static void BuildWebGL()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            if (!File.Exists(ProjectSetup.MainScenePath) || !File.Exists(ProjectSetup.DebugScenePath)) ProjectSetup.Run();

            // Main（ゲーム本体）を先頭に、Debug（CPU 対 CPU の計測。URL に ?bench を付けると開く）も含める
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScenePath, ProjectSetup.DebugScenePath },
                locationPathName = WebGLOutput,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log("[Edgewise] WebGL build " + summary.result + ": " + summary.totalSize / 1024 + " KiB, "
                + summary.totalTime.TotalSeconds.ToString("0.0") + " s, errors " + summary.totalErrors + ", output " + Path.GetFullPath(WebGLOutput));

            if (summary.result != BuildResult.Succeeded)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw new Exception("WebGL build failed: " + summary.result);
            }
        }
    }
}
