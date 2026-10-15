using Edgewise.Core;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.Controls;
using Edgewise.Game.Match;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// Main.unity を毎回作り直す（冪等。同じパスに保存するので GUID は変わらない）。
    /// 画面の部品は各 View の Build() が組み立て、参照は Wire() でつなぐ。手でシーンを編集しないこと（次の実行で上書きされる）。
    /// </summary>
    public static class SceneBuilder
    {
        public static void BuildMain(string path, GameAssets assets, VolumeProfile volume)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- カメラ・ポストプロセス ----
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Theme.Night;
            cam.orthographic = false;
            cam.fieldOfView = TableLayout.FieldOfView;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<AudioListener>();

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = volume;

            // ---- ゲーム（ワールド空間） ----
            var game = new GameObject("Game");
            var stars = Quad("Starfield", game.transform, assets, assets.starfieldMaterial, -1000);

            var table = new GameObject("Table");
            table.transform.SetParent(game.transform, false);
            var board = new GameObject("Board").AddComponent<BoardView>();
            board.transform.SetParent(table.transform, false);
            board.Build(assets);
            var hand0 = new GameObject("Hand0").AddComponent<HandView>();
            hand0.transform.SetParent(table.transform, false);
            hand0.owner = Player.P0;
            var hand1 = new GameObject("Hand1").AddComponent<HandView>();
            hand1.transform.SetParent(table.transform, false);
            hand1.owner = Player.P1;
            var cards = new GameObject("Cards").transform;
            cards.SetParent(table.transform, false);

            var layout = game.AddComponent<TableLayout>();
            layout.Wire(cam, board, hand0, hand1, stars.transform);
            var player = game.AddComponent<PlayerController>();
            var match = game.AddComponent<MatchController>();
            var fps = game.AddComponent<FpsMeter>();
            var sfx = game.AddComponent<SfxBank>();
            sfx.Wire(assets.juice);
            var juiceFx = game.AddComponent<JuiceFx>();
            juiceFx.Wire(assets.juice, sfx, layout);
            var flow = game.AddComponent<AppFlow>();

            // ---- UI ----
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 1f;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.AddComponent<ResponsiveCanvas>();

            var title = MakeScreen<TitleScreen>("Title", canvasGo.transform);
            title.Build(assets);
            var setup = MakeScreen<SetupScreen>("Setup", canvasGo.transform);
            setup.Build(assets);
            var hud = MakeScreen<HudView>("Hud", canvasGo.transform);
            hud.Build(assets);
            var banner = MakeScreen<BannerView>("Banner", canvasGo.transform);
            banner.Build(assets);
            var result = MakeScreen<ResultScreen>("Result", canvasGo.transform);
            result.Build(assets);
            var menu = MakeScreen<MenuPanel>("Menu", canvasGo.transform);
            menu.Build(assets);
            var settings = MakeScreen<SettingsPanel>("Settings", canvasGo.transform);
            settings.Build(assets);
            settings.gameObject.SetActive(false);
            setup.gameObject.SetActive(false);
            hud.gameObject.SetActive(false);
            result.gameObject.SetActive(false);
            menu.gameObject.SetActive(false);

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            var module = esGo.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();

            match.Wire(assets, board, hand0, hand1, hud, banner, layout, player, cards, juiceFx);
            flow.Wire(assets, table, title, setup, hud, menu, result, banner, match, fps, settings);
            table.SetActive(false);

            foreach (var c in new Component[] { layout, match, flow, board, hud, title, setup, result, menu, banner, settings, sfx, juiceFx })
                EditorUtility.SetDirty(c);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Edgewise] Built " + path);
        }

        static T MakeScreen<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        static MeshRenderer Quad(string name, Transform parent, GameAssets assets, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = assets.quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.sortingOrder = order;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
    }
}
