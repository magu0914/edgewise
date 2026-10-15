using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Edgewise.Core.Data;
using Edgewise.Game.View;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// カード絵の変換パイプライン（DESIGN §7-1）。冪等で、同じ入力なら同じ出力（PNG・アセットのバイト列）になる。
    ///   0. CardArtTable.Assignments（正本）を Assets/Game/Settings/CardArtTable.asset に書き出す
    ///   1. 採用した SVG を素材フォルダ（リポジトリ外）から Assets/Art/Icons/Source/&lt;作者&gt;/ にコピーする（無ければリポジトリ内のものを使う）
    ///   2. SVG → 512px のマスク PNG（tools/rasterize_icons.mjs。ヘッドレス Chrome）。マスクが無いとき、または -rasterize のときだけ
    ///   3. マスク → 128px の SDF（CardArtBaker）→ 1 枚のアトラス（R8、8×7 セル）＋星座の点と線 → CardArtAtlas.asset
    /// batchmode:
    ///   tools/RunUnity.ps1 -LogName art.log -Extra '-executeMethod','Edgewise.EditorTools.CardArtPipeline.Run'            （2 はマスクが無いときだけ）
    ///   tools/RunUnity.ps1 -LogName art.log -Extra '-executeMethod','Edgewise.EditorTools.CardArtPipeline.Run','-rasterize' （マスクも作り直す）
    ///   tools/RunUnity.ps1 -LogName art.log -Extra '-executeMethod','Edgewise.EditorTools.CardArtPipeline.VerifyRasterize' （作り直したマスクが同じか確かめる）
    /// </summary>
    public static class CardArtPipeline
    {
        public const string IconRoot = "Assets/Art/Icons";
        public const string SourceDir = IconRoot + "/Source";
        public const string MaskDir = IconRoot + "/Masks";
        public const string GeneratedDir = IconRoot + "/Generated";
        public const string AtlasPngPath = GeneratedDir + "/CardArtAtlas.png";
        public const string AtlasAssetPath = GeneratedDir + "/CardArtAtlas.asset";
        public const string TablePath = "Assets/Game/Settings/CardArtTable.asset";

        /// <summary>game-icons.net の全アイコンを展開した場所（リポジトリ外）。無ければリポジトリ内の Source を使う。</summary>
        public const string ExternalSource = "D:/Projects/tripletriad-art-source/extracted/icons-master";

        public const int Columns = 8;
        public const int Rows = 7;
        const int RasterPort = 9336;

        [MenuItem("Edgewise/Card Art/Rebuild")]
        public static void Run()
        {
            bool force = Environment.GetCommandLineArgs().Contains("-rasterize");
            Build(force);
        }

        public static (CardArtAtlas atlas, CardArtTable table) Build(bool forceRasterize = false)
        {
            EnsureFolder(SourceDir);
            EnsureFolder(MaskDir);
            EnsureFolder(GeneratedDir);
            var table = WriteTable();
            CopySources(table);
            bool missing = table.entries.Any(e => !File.Exists(MaskPath(e)));
            if (forceRasterize || missing) Rasterize(table, MaskDir);
            var atlas = Bake(table);
            Debug.Log("[Edgewise] Card art: " + table.entries.Count + " cards, atlas " + atlas.atlas.width + "x" + atlas.atlas.height
                + ", sha1 " + Sha1(File.ReadAllBytes(AtlasPngPath)).Substring(0, 12));
            return (atlas, table);
        }

        /// <summary>マスクを一時フォルダに作り直し、リポジトリのマスクとバイト単位で一致するか確かめる（ラスタライズの決定論の確認）。</summary>
        public static void VerifyRasterize()
        {
            var table = WriteTable();
            string tmp = Path.Combine("Temp", "CardArtVerify");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Rasterize(table, tmp);
            int same = 0, diff = 0;
            foreach (var e in table.entries)
            {
                var a = File.ReadAllBytes(MaskPath(e));
                var b = File.ReadAllBytes(Path.Combine(tmp, e.author, e.icon + ".png"));
                if (a.SequenceEqual(b)) same++;
                else
                {
                    diff++;
                    Debug.LogWarning("[Edgewise] Mask differs: " + e.IconPath);
                }
            }
            Debug.Log("[Edgewise] Rasterize verify: " + same + " identical, " + diff + " different");
            if (diff > 0 && Application.isBatchMode) EditorApplication.Exit(1);
        }

        public static string SvgPath(CardArtTable.Entry e) => SourceDir + "/" + e.author + "/" + e.icon + ".svg";

        public static string MaskPath(CardArtTable.Entry e) => MaskDir + "/" + e.author + "/" + e.icon + ".png";

        // ------------------------------------------------------------------

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static CardArtTable WriteTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<CardArtTable>(TablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<CardArtTable>();
                AssetDatabase.CreateAsset(table, TablePath);
            }
            // カード定義の順に並べる（アトラスのセル番号もこの順）
            var byId = CardArtTable.Assignments.ToDictionary(a => a.cardId);
            var list = new List<CardArtTable.Entry>();
            foreach (var c in CardCatalog.All)
            {
                if (!byId.TryGetValue(c.Id, out var a)) throw new Exception("CardArtTable: no icon for " + c.Id);
                list.Add(new CardArtTable.Entry(a.cardId, a.author, a.icon, a.scale, a.offset.x, a.offset.y, a.flipX));
            }
            table.entries = list;
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            return table;
        }

        static void CopySources(CardArtTable table)
        {
            foreach (var e in table.entries)
            {
                string dst = SvgPath(e);
                string src = Path.Combine(ExternalSource, e.author, e.icon + ".svg");
                if (File.Exists(src))
                {
                    var bytes = File.ReadAllBytes(src);
                    if (!File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(bytes))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.WriteAllBytes(dst, bytes);
                    }
                }
                else if (!File.Exists(dst))
                {
                    throw new FileNotFoundException("SVG not found (neither " + src + " nor " + dst + ")");
                }
            }
            // 採用しなくなった SVG・マスクは消す（リポジトリに残さない）
            Prune(SourceDir, ".svg", table);
            Prune(MaskDir, ".png", table);
            AssetDatabase.Refresh();
        }

        static void Prune(string dir, string ext, CardArtTable table)
        {
            if (!Directory.Exists(dir)) return;
            var keep = new HashSet<string>(table.entries.Select(e => (e.author + "/" + e.icon + ext).ToLowerInvariant()));
            foreach (var f in Directory.GetFiles(dir, "*" + ext, SearchOption.AllDirectories))
            {
                string rel = f.Replace('\\', '/').Substring(dir.Length + 1).ToLowerInvariant();
                if (keep.Contains(rel)) continue;
                AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
                Debug.Log("[Edgewise] Removed unused " + f);
            }
        }

        /// <summary>tools/rasterize_icons.mjs（ヘッドレス Chrome）で SVG → マスク PNG。</summary>
        static void Rasterize(CardArtTable table, string outDir)
        {
            string list = Path.GetFullPath(Path.Combine("Temp", "cardart_icons.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(list));
            File.WriteAllLines(list, table.entries.Select(e => e.IconPath));
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "tools/rasterize_icons.mjs --src \"" + SourceDir + "\" --list \"" + list + "\" --out \"" + outDir + "\" --port " + RasterPort,
                WorkingDirectory = Path.GetFullPath("."),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                Debug.Log("[Edgewise] rasterize_icons: exit " + p.ExitCode + " " + stdout.Trim() + " " + stderr.Trim());
                if (p.ExitCode != 0) throw new Exception("rasterize_icons.mjs failed: " + stderr);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>マスク PNG を読む（インポート設定に左右されないよう、ファイルのバイト列から）。</summary>
        public static CardArtBaker.Mask LoadMask(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path))) throw new Exception("cannot read " + path);
                return CardArtBaker.MaskFromPixels(tex.GetPixels32(), tex.width, tex.height);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>1 枚分の SDF と星座（テストからも呼ぶ）。</summary>
        public static (CardArtBaker.BakedCell cell, CardArtBaker.Constellation stars) BakeOne(CardArtTable.Entry e, int level)
        {
            var mask = LoadMask(MaskPath(e));
            var cell = CardArtBaker.BakeCell(mask, e.scale, e.offset.x, e.offset.y, e.flipX);
            var stars = CardArtBaker.PickConstellation(cell.Distance, e.cardId, level);
            return (cell, stars);
        }

        static CardArtAtlas Bake(CardArtTable table)
        {
            int cs = CardArtBaker.Cell;
            int w = Columns * cs, h = Rows * cs;
            if (table.entries.Count > Columns * Rows) throw new Exception("atlas is too small for " + table.entries.Count + " cards");
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);
            var entries = new List<CardArtAtlas.Entry>();
            for (int i = 0; i < table.entries.Count; i++)
            {
                var e = table.entries[i];
                var def = CardCatalog.ById[e.cardId];
                var (cell, stars) = BakeOne(e, def.Level);
                int col = i % Columns, row = i / Columns;
                for (int y = 0; y < cs; y++)
                {
                    for (int x = 0; x < cs; x++)
                    {
                        byte v = cell.Bytes[y * cs + x];
                        px[(row * cs + y) * w + col * cs + x] = new Color32(v, v, v, 255);
                    }
                }
                entries.Add(new CardArtAtlas.Entry
                {
                    cardId = e.cardId,
                    cell = i,
                    uvRect = new Vector4((float)cs / w, (float)cs / h, (float)(col * cs) / w, (float)(row * cs) / h),
                    stars = stars.Stars,
                    lines = stars.Lines,
                });
            }

            Directory.CreateDirectory(GeneratedDir);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            bool changed = !File.Exists(AtlasPngPath) || !File.ReadAllBytes(AtlasPngPath).SequenceEqual(png);
            if (changed)
            {
                File.WriteAllBytes(AtlasPngPath, png);
                AssetDatabase.ImportAsset(AtlasPngPath, ImportAssetOptions.ForceSynchronousImport);
            }
            ConfigureAtlasImporter();

            var atlas = AssetDatabase.LoadAssetAtPath<CardArtAtlas>(AtlasAssetPath);
            if (atlas == null)
            {
                atlas = ScriptableObject.CreateInstance<CardArtAtlas>();
                AssetDatabase.CreateAsset(atlas, AtlasAssetPath);
            }
            atlas.atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPngPath);
            atlas.cellSize = cs;
            atlas.columns = Columns;
            atlas.rows = Rows;
            atlas.spread = CardArtBaker.Spread;
            atlas.entries = entries;
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            return atlas;
        }

        /// <summary>R8・リニア・ミップマップあり・クランプ・無圧縮（SDF は圧縮すると輪郭が崩れる）。</summary>
        static void ConfigureAtlasImporter()
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(AtlasPngPath);
            bool dirty = imp.textureType != TextureImporterType.SingleChannel || imp.sRGBTexture || !imp.mipmapEnabled
                || imp.wrapMode != TextureWrapMode.Clamp || imp.textureCompression != TextureImporterCompression.Uncompressed
                || imp.npotScale != TextureImporterNPOTScale.None || imp.maxTextureSize != 1024;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            if (settings.singleChannelComponent != TextureImporterSingleChannelComponent.Red) dirty = true;
            if (!dirty) return;
            imp.textureType = TextureImporterType.SingleChannel;
            imp.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            imp.SetTextureSettings(settings);
            imp.sRGBTexture = false;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.anisoLevel = 1;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        static string Sha1(byte[] data)
        {
            using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
    }
}
