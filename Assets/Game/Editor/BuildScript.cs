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
    /// - BuildWebGL: GitHub Pages 用。出力先は Builds/WebGL（Main と Debug シーン）。圧縮は Player 設定のまま（Gzip ＋ Decompression Fallback ON。
    ///   GitHub Pages は Content-Encoding を付けられないので、ローダーが JS で解凍する）。
    /// - BuildWebGLUnityroom: unityroom 用（8a。docs/PUBLISH.md）。出力先は Builds/unityroom-build/edgewise。unityroom の説明どおり
    ///   Development Build オフ・Compression Format Gzip・Decompression Fallback オフでビルドし、終わったら Player 設定を元に戻す
    ///   （ProjectSettings に差分を残さず、GitHub Pages 用の設定は変えない）。
    /// 失敗したら終了コード 1 で Editor を終了する。
    /// </summary>
    public static class BuildScript
    {
        public const string WebGLOutput = "Builds/WebGL";
        /// <summary>unityroom 用の出力先。ファイル名はフォルダ名から付く（edgewise.loader.js / .data.gz / .framework.js.gz / .wasm.gz）。</summary>
        public const string UnityroomOutput = "Builds/unityroom-build/edgewise";

        [MenuItem("Edgewise/Build WebGL")]
        public static void BuildWebGL()
        {
            Finish(Build(WebGLOutput, "WebGL"));
        }

        /// <summary>unityroom 用: Gzip・Decompression Fallback オフ・Development Build オフ。設定はビルド後に元へ戻す。</summary>
        [MenuItem("Edgewise/Build WebGL (unityroom)")]
        public static void BuildWebGLUnityroom()
        {
            var format = PlayerSettings.WebGL.compressionFormat;
            bool fallback = PlayerSettings.WebGL.decompressionFallback;
            bool dev = EditorUserBuildSettings.development;
            bool ok;
            try
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = false;
                EditorUserBuildSettings.development = false;
                ok = Build(UnityroomOutput, "unityroom");
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = format;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                EditorUserBuildSettings.development = dev;
                AssetDatabase.SaveAssets();
            }
            Finish(ok);
        }

        static bool Build(string output, string label)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            if (!File.Exists(ProjectSetup.MainScenePath) || !File.Exists(ProjectSetup.DebugScenePath)) ProjectSetup.Run();

            // Main（ゲーム本体）を先頭に、Debug（CPU 対 CPU の計測。URL に ?bench を付けると開く）も含める
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScenePath, ProjectSetup.DebugScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log("[Edgewise] WebGL build (" + label + ") " + summary.result + ": " + summary.totalSize / 1024 + " KiB, "
                + summary.totalTime.TotalSeconds.ToString("0.0") + " s, errors " + summary.totalErrors + ", compression "
                + PlayerSettings.WebGL.compressionFormat + ", fallback " + PlayerSettings.WebGL.decompressionFallback + ", output " + Path.GetFullPath(output));
            return summary.result == BuildResult.Succeeded;
        }

        static void Finish(bool ok)
        {
            if (ok) return;
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw new Exception("WebGL build failed");
        }
    }
}
