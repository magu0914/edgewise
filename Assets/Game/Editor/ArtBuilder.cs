using System.IO;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// 見た目のアセットを生成・更新する（冪等）: マテリアル、uGUI の角丸スプライト（生成テクスチャ）、
    /// JuiceSettings（無ければ作る。調整済みの値は上書きしない）、Bloom の Volume Profile、GameAssets、カードのプレハブ。
    /// 外部の画像素材は使わない（絵はシェーダーと生成テクスチャ）。
    /// </summary>
    public static class ArtBuilder
    {
        public const string ArtDir = "Assets/Game/Art";
        public const string MaterialsDir = ArtDir + "/Materials";
        public const string GeneratedDir = ArtDir + "/Generated";
        public const string SettingsDir = "Assets/Game/Settings";
        public const string PrefabsDir = "Assets/Game/Prefabs";

        public const string RoundRectPath = GeneratedDir + "/RoundRect.png";
        public const string JuicePath = SettingsDir + "/JuiceSettings.asset";
        public const string BgmLibraryPath = SettingsDir + "/BgmLibrary.asset";
        public const string VolumePath = SettingsDir + "/PostProcess.asset";
        public const string GameAssetsPath = SettingsDir + "/GameAssets.asset";
        public const string CardPrefabPath = PrefabsDir + "/Card.prefab";

        public static GameAssets Build(TMP_FontAsset font, Material fontOutline)
        {
            Folder("Assets/Game", "Art");
            Folder(ArtDir, "Materials");
            Folder(ArtDir, "Generated");
            Folder("Assets/Game", "Settings");
            Folder("Assets/Game", "Prefabs");

            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(assets, GameAssetsPath);
            }
            assets.cardMaterial = EnsureMaterial("Card", "Edgewise/Card");
            assets.shapeMaterial = EnsureMaterial("Shape", "Edgewise/Shape");
            assets.boardMaterial = EnsureMaterial("Board", "Edgewise/Board");
            assets.starfieldMaterial = EnsureMaterial("Starfield", "Edgewise/Starfield");
            assets.font = font;
            assets.fontOutline = fontOutline;
            assets.roundRect = EnsureRoundRect();
            assets.juice = EnsureJuice();
            assets.bgm = EnsureBgmLibrary();
            assets.quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();

            assets.cardPrefab = BuildCardPrefab(assets);
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            return assets;
        }

        static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static Material EnsureMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception("Shader not found: " + shaderName);
            string path = MaterialsDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>角丸の 9 スライス用スプライト（白、64×64、角の半径 18px）をコードで描いて PNG にする。</summary>
        static Sprite EnsureRoundRect()
        {
            if (!File.Exists(RoundRectPath))
            {
                const int size = 64;
                const float r = 18f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                        float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) - r;
                        float a = Mathf.Clamp01(0.5f - d);
                        px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(RoundRectPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(RoundRectPath, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(RoundRectPath);
            bool changed = importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != new Vector4(20, 20, 20, 20)
                || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed;
            if (changed)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = new Vector4(20, 20, 20, 20);
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(RoundRectPath);
        }

        static JuiceSettings EnsureJuice()
        {
            var j = AssetDatabase.LoadAssetAtPath<JuiceSettings>(JuicePath);
            if (j != null) return j;
            j = ScriptableObject.CreateInstance<JuiceSettings>();
            AssetDatabase.CreateAsset(j, JuicePath);
            Debug.Log("[Edgewise] Created " + JuicePath);
            return j;
        }

        /// <summary>BGM の割り当て（無ければ空で作る。割り当て済みの曲は上書きしない）。曲は Assets/Audio/Bgm/ に置く（docs/AUDIO.md）。</summary>
        static BgmLibrary EnsureBgmLibrary()
        {
            Folder("Assets", "Audio");
            Folder("Assets/Audio", "Bgm");
            var lib = AssetDatabase.LoadAssetAtPath<BgmLibrary>(BgmLibraryPath);
            if (lib != null) return lib;
            lib = ScriptableObject.CreateInstance<BgmLibrary>();
            AssetDatabase.CreateAsset(lib, BgmLibraryPath);
            Debug.Log("[Edgewise] Created " + BgmLibraryPath);
            return lib;
        }

        /// <summary>Bloom（光の演出を引き立てる）と弱いビネット。WebGL で重ければ強さを下げる。</summary>
        public static VolumeProfile EnsureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
                bloom.name = "Bloom";
                AssetDatabase.AddObjectToAsset(bloom, profile);
                bloom.threshold.Override(1.0f);
                bloom.intensity.Override(0.9f);
                bloom.scatter.Override(0.6f);
                bloom.highQualityFiltering.Override(false);
            }
            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>(true);
                vignette.name = "Vignette";
                AssetDatabase.AddObjectToAsset(vignette, profile);
                vignette.intensity.Override(0.28f);
                vignette.smoothness.Override(0.5f);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static CardView BuildCardPrefab(GameAssets assets)
        {
            var go = new GameObject("Card");
            var view = go.AddComponent<CardView>();
            view.Build(assets);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, CardPrefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<CardView>();
        }
    }
}
