using System;
using System.IO;
using System.Reflection;
using Edgewise.Game.Debugging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// プロジェクトの初期設定（冪等）。batchmode から呼べる:
    ///   Unity -batchmode -quit -projectPath . -executeMethod Edgewise.EditorTools.ProjectSetup.Run
    /// - URP の 2D Renderer と Pipeline Asset を作り、Graphics / Quality 設定に割り当てる
    /// - Player 設定（製品名・Linear・WebGL の解凍フォールバック）
    /// - デバッグ表示つきの Main シーンを作り、Build Settings に登録する
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsDir = "Assets/Settings";
        public const string RendererPath = SettingsDir + "/Renderer2D.asset";
        public const string PipelinePath = SettingsDir + "/URP-2D.asset";
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Edgewise/Project Setup")]
        public static void Run()
        {
            EnsureFolder("Assets", "Settings");
            EnsureFolder("Assets", "Scenes");

            var pipeline = EnsurePipelineAsset();
            AssignPipeline(pipeline);
            ConfigurePlayer();
            EnsureMainScene();
            RegisterScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Edgewise] Project setup done.");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static UniversalRenderPipelineAsset EnsurePipelineAsset()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (renderer == null)
            {
                renderer = CreateRenderer2DData(RendererPath);
                Debug.Log("[Edgewise] Created " + RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                Debug.Log("[Edgewise] Created " + PipelinePath);
            }
            return pipeline;
        }

        /// <summary>
        /// URP の「Create > Rendering > URP 2D Renderer」と同じ手順で 2D Renderer Data を作る。
        /// その処理（Renderer2DMenus.CreateRendererAsset）は internal なのでリフレクションで呼び、見つからなければ同等の手順で作る。
        /// </summary>
        static Renderer2DData CreateRenderer2DData(string path)
        {
            var menus = Type.GetType("UnityEditor.Rendering.Universal.Renderer2DMenus, Unity.RenderPipelines.Universal.Editor");
            var create = menus?.GetMethod("CreateRendererAsset", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (create != null)
            {
                var data = create.Invoke(null, new object[] { path, RendererType._2DRenderer, false, "Renderer" }) as Renderer2DData;
                if (data != null) return data;
            }

            Debug.LogWarning("[Edgewise] Renderer2DMenus.CreateRendererAsset not found; creating Renderer2DData directly.");
            var rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
            var getDefaultPost = typeof(PostProcessData).GetMethod("GetDefaultPostProcessData", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var postProperty = typeof(Renderer2DData).GetProperty("postProcessData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (getDefaultPost != null && postProperty != null) postProperty.SetValue(rendererData, getDefaultPost.Invoke(null, null));
            AssetDatabase.CreateAsset(rendererData, path);
            ResourceReloader.ReloadAllNullIn(rendererData, "Packages/com.unity.render-pipelines.universal");
            return rendererData;
        }

        static void AssignPipeline(RenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Edgewise";
            PlayerSettings.companyName = "magu";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // GitHub Pages / unityroom など Content-Encoding を付けられないホストでも動くように、JS 側での解凍を許可する
            PlayerSettings.WebGL.decompressionFallback = true;
        }

        static void EnsureMainScene()
        {
            if (File.Exists(MainScenePath)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.13f);
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            var debugGo = new GameObject("Debug Board");
            debugGo.AddComponent<DebugBoardView>();

            EditorSceneManager.SaveScene(scene, MainScenePath);
            Debug.Log("[Edgewise] Created " + MainScenePath);
        }

        static void RegisterScenes()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
        }
    }
}
