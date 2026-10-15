using System.IO;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// 50 枚のカードを実際のプレハブとシェーダーで描いて 1 枚の画像に並べる（コンタクトシート。DESIGN §7-1 の監査用）。
    /// 各カードの下にカード名とアイコン名（作者）を入れる。GPU が要るので -nographics を付けずに batchmode で実行する:
    ///   tools/RunUnity.ps1 -Graphics -LogName sheet.log -Extra '-executeMethod','Edgewise.EditorTools.CardArtContactSheet.Render'
    /// 出力: Logs/shots/4a/contact_sheet.png（1 枚 192px 幅、10 列 × 5 行）。
    /// </summary>
    public static class CardArtContactSheet
    {
        public const string OutPath = "Logs/shots/4a/contact_sheet.png";
        const int Columns = 10;
        const float PixelsPerUnit = 192f;
        const float CellW = 1.34f;
        const float CellH = 2.02f;

        [MenuItem("Edgewise/Card Art/Contact Sheet")]
        public static void Render()
        {
            // 新しいシーンを先に作る（シーンを切り替えると読み込み済みのアセットが解放されるため、参照はその後で読む）
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(ArtBuilder.GameAssetsPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtBuilder.CardPrefabPath);
            if (assets == null || prefab == null || assets.cardArt == null || assets.cardArtTable == null)
                throw new System.Exception("Run ProjectSetup first (GameAssets / card prefab / card art missing)");
            int n = CardCatalog.All.Count;
            int rows = (n + Columns - 1) / Columns;
            int width = Mathf.RoundToInt(Columns * CellW * PixelsPerUnit);
            int height = Mathf.RoundToInt(rows * CellH * PixelsPerUnit);

            var camGo = new GameObject("SheetCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = rows * CellH * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.045f, 0.09f, 1f);
            cam.transform.position = new Vector3(Columns * CellW * 0.5f, -rows * CellH * 0.5f, -10f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;

            for (int i = 0; i < n; i++)
            {
                var def = CardCatalog.All[i];
                int col = i % Columns, row = i / Columns;
                float cx = (col + 0.5f) * CellW;
                float top = -row * CellH;
                var view = Object.Instantiate(prefab).GetComponent<CardView>();
                view.name = def.Id;
                view.transform.position = new Vector3(cx, top - 0.12f - CardView.QuadSize.y * 0.5f, 0f);
                view.Bind(def, Player.P0, 0, false);
                view.SetOrder(1);
                view.ApplyNow();

                var entry = assets.cardArtTable.Find(def.Id);
                var label = new GameObject("Label " + def.Id).AddComponent<TextMeshPro>();
                label.font = assets.font;
                label.fontSize = 1.05f;
                label.alignment = TextAlignmentOptions.Top;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.rectTransform.sizeDelta = new Vector2(CellW, 0.4f);
                label.transform.position = new Vector3(cx, top - 0.12f - CardView.QuadSize.y - 0.03f - 0.2f, 0f);
                label.color = new Color(0.92f, 0.94f, 1f);
                label.text = "Lv" + def.Level + " " + def.Name + "\n<size=82%><color=#F5C86B>" + (entry != null ? entry.icon + " (" + entry.author + ")" : "?") + "</color></size>";
                label.ForceMeshUpdate();
            }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllBytes(OutPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Debug.Log("[Edgewise] Contact sheet: " + Path.GetFullPath(OutPath) + " " + width + "x" + height);
        }
    }
}
