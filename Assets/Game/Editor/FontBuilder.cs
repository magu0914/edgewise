using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Edgewise.Core.Data;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// TextMeshPro の準備と日本語フォントアセットの生成（冪等）。
    /// - TMP Essential Resources が無ければ取り込む（パッケージ同梱の unitypackage）。
    /// - Noto Sans JP（Google Fonts の OFL 版、Assets/Fonts/）から、使う文字だけを焼いた静的な SDF フォントアセットを作る。
    ///   WebGL に元の TTF（5.7 MB）を含めないため。使う文字 = UiText の全文字列＋ASCII＋カード名。
    ///   文字の集合が前回と同じなら作り直さない（.charset.txt に記録）。
    /// </summary>
    public static class FontBuilder
    {
        public const string SourceFont = "Assets/Fonts/NotoSansJP-Regular.ttf";
        public const string FontAssetPath = "Assets/Fonts/NotoSansJP-Regular SDF.asset";
        public const string OutlineMaterialPath = "Assets/Fonts/NotoSansJP-Regular SDF Outline.mat";
        public const string CharsetPath = "Assets/Fonts/NotoSansJP-Regular SDF.charset.txt";
        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>
        /// TMP Essential Resources のうち使わないもの（英字フォント LiberationSans と絵文字スプライト）を消す。
        /// Resources フォルダにあるとビルドに必ず含まれ、WebGL のサイズが約 1.7 MB 増えるため。
        /// </summary>
        public static void TrimTmpEssentials()
        {
            string[] unused =
            {
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset",
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset",
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Drop Shadow.mat",
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Outline.mat",
                "Assets/TextMesh Pro/Resources/Sprite Assets/EmojiOne.asset",
                "Assets/TextMesh Pro/Sprites/EmojiOne.png",
                "Assets/TextMesh Pro/Sprites/EmojiOne.json",
                "Assets/TextMesh Pro/Fonts/LiberationSans.ttf",
            };
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var sprite = so.FindProperty("m_defaultSpriteAsset");
                if (sprite != null) sprite.objectReferenceValue = null;
                var fallback = so.FindProperty("m_fallbackFontAssets");
                if (fallback != null && fallback.isArray) fallback.ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            foreach (var p in unused)
            {
                if (File.Exists(p)) AssetDatabase.DeleteAsset(p);
            }
        }

        public static void EnsureTmpEssentials()
        {
            if (File.Exists(TmpSettingsPath)) return;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string root = info != null ? info.resolvedPath : Path.GetFullPath("Packages/com.unity.ugui");
            string pkg = Path.Combine(root, "Package Resources", "TMP Essential Resources.unitypackage");
            if (!File.Exists(pkg)) throw new FileNotFoundException("TMP Essential Resources not found", pkg);
            // AssetDatabase.ImportPackage は非同期で、batchmode の -quit では取り込みが終わらないので、
            // unitypackage（tar.gz）を自分で展開して同期的に取り込む
            int files = ExtractUnityPackage(pkg);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Edgewise] Imported TMP Essential Resources (" + files + " files) from " + pkg);
        }

        /// <summary>unitypackage（各アセットが &lt;guid&gt;/asset・asset.meta・pathname の tar.gz）をプロジェクトに展開する。</summary>
        static int ExtractUnityPackage(string path)
        {
            var entries = new Dictionary<string, Dictionary<string, byte[]>>();
            using (var fs = File.OpenRead(path))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
            {
                var header = new byte[512];
                string longName = null;
                while (true)
                {
                    if (ReadFull(gz, header, 512) < 512) break;
                    bool empty = true;
                    for (int i = 0; i < 512; i++)
                    {
                        if (header[i] != 0)
                        {
                            empty = false;
                            break;
                        }
                    }
                    if (empty) break;
                    string name = CString(header, 0, 100);
                    string prefix = CString(header, 345, 155);
                    if (prefix.Length > 0) name = prefix + "/" + name;
                    long size = Convert.ToInt64(CString(header, 124, 12).Trim().Trim('\0').PadLeft(1, '0'), 8);
                    char type = (char)header[156];
                    var data = new byte[size];
                    ReadFull(gz, data, (int)size);
                    int pad = (int)((512 - size % 512) % 512);
                    if (pad > 0) ReadFull(gz, new byte[pad], pad);
                    if (type == 'L')
                    {
                        longName = Encoding.UTF8.GetString(data).TrimEnd('\0');
                        continue;
                    }
                    if (longName != null)
                    {
                        name = longName;
                        longName = null;
                    }
                    if (type != '0' && type != '\0') continue;
                    name = name.TrimStart('.', '/');
                    int slash = name.IndexOf('/');
                    if (slash <= 0) continue;
                    string guid = name.Substring(0, slash);
                    string file = name.Substring(slash + 1);
                    if (!entries.TryGetValue(guid, out var d)) entries[guid] = d = new Dictionary<string, byte[]>();
                    d[file] = data;
                }
            }
            int count = 0;
            foreach (var kv in entries)
            {
                if (!kv.Value.TryGetValue("pathname", out var pn)) continue;
                string target = Encoding.UTF8.GetString(pn).Split('\n')[0].Trim();
                if (!target.StartsWith("Assets/")) continue;
                if (kv.Value.TryGetValue("asset", out var asset))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, asset);
                    count++;
                }
                else Directory.CreateDirectory(target);
                if (kv.Value.TryGetValue("asset.meta", out var meta)) File.WriteAllBytes(target + ".meta", meta);
            }
            return count;
        }

        static int ReadFull(Stream s, byte[] buf, int n)
        {
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) break;
                off += r;
            }
            return off;
        }

        static string CString(byte[] b, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, offset, end - offset);
        }

        /// <summary>フォントに入れる文字（重複なし・コード順）。</summary>
        public static string Charset()
        {
            var set = new SortedSet<char>();
            for (char c = (char)0x20; c <= (char)0x7e; c++) set.Add(c);
            foreach (var f in typeof(UiText).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType == typeof(string) && f.GetValue(null) is string s)
                    foreach (char c in s) set.Add(c);
            }
            foreach (var card in CardCatalog.All)
                foreach (char c in card.Name) set.Add(c);
            foreach (char c in Theme.MarkP0 + Theme.MarkP1 + "★☆−") set.Add(c);
            set.Remove('\n');
            set.Remove('\r');
            return new string(set.ToArray());
        }

        public static (TMP_FontAsset Font, Material Outline) Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFont);
            if (font == null) throw new FileNotFoundException("Noto Sans JP not found. Put the Google Fonts OFL version at " + SourceFont);

            string chars = Charset();
            string hash = Hash(chars + "|v4");
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            bool upToDate = existing != null && File.Exists(CharsetPath) && File.ReadAllText(CharsetPath).StartsWith(hash);
            TMP_FontAsset fa = existing;
            if (!upToDate)
            {
                if (existing != null) AssetDatabase.DeleteAsset(FontAssetPath);
                // まず 1024×1024（40pt で焼く）。入りきらなければ大きくする
                fa = Create(font, chars, 1024, 1024, 40, 5);
                if (fa == null) fa = Create(font, chars, 2048, 1024, 56, 6);
                if (fa == null) fa = Create(font, chars, 2048, 2048, 56, 6);
                if (fa == null) throw new Exception("could not pack the font atlas");
                File.WriteAllText(CharsetPath, hash + "\n" + chars.Length + " characters\n" + chars + "\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(CharsetPath);
                Debug.Log("[Edgewise] Created " + FontAssetPath + " (" + chars.Length + " characters, atlas " + fa.atlasWidth + "x" + fa.atlasHeight + ")");
            }

            var outline = BuildOutlineMaterial(fa);
            SetTmpDefaults(fa);
            return (fa, outline);
        }

        static TMP_FontAsset Create(Font font, string chars, int w, int h, int pointSize, int padding)
        {
            var fa = TMP_FontAsset.CreateFontAsset(font, pointSize, padding, GlyphRenderMode.SDFAA, w, h, AtlasPopulationMode.Dynamic, false);
            fa.name = Path.GetFileNameWithoutExtension(FontAssetPath);
            fa.TryAddCharacters(chars, out string missing);
            int expected = chars.Length - (missing?.Length ?? 0);
            if (fa.characterTable.Count < expected)
            {
                Debug.LogWarning("[Edgewise] atlas " + w + "x" + h + " too small: " + fa.characterTable.Count + "/" + expected);
                UnityEngine.Object.DestroyImmediate(fa);
                return null;
            }
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[Edgewise] Characters not in Noto Sans JP: " + missing);

            AssetDatabase.CreateAsset(fa, FontAssetPath);
            var tex = fa.atlasTextures[0];
            tex.name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(tex, fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            // 静的にする: 元のフォントファイルへの参照を外し、焼いた文字だけをビルドに含める
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        static Material BuildOutlineMaterial(TMP_FontAsset fa)
        {
            var shader = Shader.Find("TextMeshPro/Distance Field");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (mat == null)
            {
                mat = new Material(fa.material);
                AssetDatabase.CreateAsset(mat, OutlineMaterialPath);
            }
            else
            {
                mat.CopyPropertiesFromMaterial(fa.material);
            }
            mat.shader = shader;
            mat.name = Path.GetFileNameWithoutExtension(OutlineMaterialPath);
            mat.SetTexture(ShaderUtilities.ID_MainTex, fa.atlasTextures[0]);
            mat.SetFloat(ShaderUtilities.ID_TextureWidth, fa.atlasWidth);
            mat.SetFloat(ShaderUtilities.ID_TextureHeight, fa.atlasHeight);
            mat.SetFloat(ShaderUtilities.ID_GradientScale, fa.atlasPadding + 1);
            mat.SetFloat(ShaderUtilities.ID_WeightNormal, fa.normalStyle);
            mat.SetFloat(ShaderUtilities.ID_WeightBold, fa.boldStyle);
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.02f, 0.02f, 0.06f, 1f));
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.12f);
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetTmpDefaults(TMP_FontAsset fa)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var p = so.FindProperty("m_defaultFontAsset");
            if (p != null) p.objectReferenceValue = fa;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
