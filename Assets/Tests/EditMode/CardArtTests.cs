using System.IO;
using System.Linq;
using Edgewise.Core.Data;
using Edgewise.EditorTools;
using Edgewise.Game.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Edgewise.Tests.EditMode
{
    /// <summary>Phase 4a: カードとアイコンの対応、SDF アトラスと星座のパイプラインの決定論。</summary>
    public class CardArtTests
    {
        [Test]
        public void EveryCardHasExactlyOneIconAndNoIconIsReused()
        {
            var ids = CardCatalog.All.Select(c => c.Id).ToList();
            var assigned = CardArtTable.Assignments.Select(a => a.cardId).ToList();
            Assert.That(assigned, Is.EquivalentTo(ids), "every card must have exactly one icon");
            var paths = CardArtTable.Assignments.Select(a => a.IconPath).ToList();
            Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Count), "icons must not be reused: "
                + string.Join(", ", paths.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key)));
            foreach (var a in CardArtTable.Assignments)
            {
                Assert.That(File.Exists(CardArtPipeline.SvgPath(a)), "SVG is copied into the repo: " + a.IconPath);
                Assert.That(File.Exists(CardArtPipeline.MaskPath(a)), "mask exists: " + a.IconPath);
                Assert.That(CardArtTable.License(a.author), Is.EqualTo("CC BY 3.0").Or.EqualTo("CC0"));
            }
        }

        [Test]
        public void GeneratedAssetsCoverEveryCard()
        {
            var table = AssetDatabase.LoadAssetAtPath<CardArtTable>(CardArtPipeline.TablePath);
            var atlas = AssetDatabase.LoadAssetAtPath<CardArtAtlas>(CardArtPipeline.AtlasAssetPath);
            Assert.That(table, Is.Not.Null);
            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.atlas, Is.Not.Null);
            Assert.That(atlas.atlas.format, Is.EqualTo(TextureFormat.R8), "the SDF atlas is a single-channel R8 texture");
            foreach (var c in CardCatalog.All)
            {
                var t = table.Find(c.Id);
                Assert.That(t, Is.Not.Null, c.Id);
                var e = atlas.Find(c.Id);
                Assert.That(e, Is.Not.Null, c.Id);
                Assert.That(e.stars.Length, Is.EqualTo(CardArtBaker.StarCount(c.Level)), c.Id + " stars");
                Assert.That(e.stars.Length, Is.InRange(7, CardArtAtlas.MaxStars));
                Assert.That(e.lines.Length, Is.InRange(e.stars.Length - 1, CardArtAtlas.MaxLines), c.Id + " lines");
                foreach (var s in e.stars) Assert.That(s.x >= 0 && s.x <= 1 && s.y >= 0 && s.y <= 1, c.Id + " star inside the cell");
            }
            // 5b-2: カード 50 枚＋対戦相手の立ち絵 12 人が、それぞれ別のセルに入る
            Assert.That(atlas.entries.Select(e => e.cell).Distinct().Count(), Is.EqualTo(CardCatalog.All.Count + Edgewise.Core.Campaign.CampaignData.Opponents.Count));
        }

        [Test]
        public void SameMaskGivesTheSameSdfAndStarsAsTheCommittedAtlas()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<CardArtAtlas>(CardArtPipeline.AtlasAssetPath);
            var png = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            png.LoadImage(File.ReadAllBytes(CardArtPipeline.AtlasPngPath));
            var px = png.GetPixels32();
            int w = png.width;
            Object.DestroyImmediate(png);
            int cs = CardArtBaker.Cell;

            foreach (string id in new[] { "glimmouse", "lanternmare", "ophiwyrm" })
            {
                var entry = CardArtTable.Assignments.First(a => a.cardId == id);
                int level = CardCatalog.ById[id].Level;
                var first = CardArtPipeline.BakeOne(entry, level);
                var second = CardArtPipeline.BakeOne(entry, level);
                Assert.That(second.cell.Bytes, Is.EqualTo(first.cell.Bytes), id + ": SDF is deterministic");
                Assert.That(second.stars.Stars, Is.EqualTo(first.stars.Stars), id + ": stars are deterministic");
                Assert.That(second.stars.Lines, Is.EqualTo(first.stars.Lines), id + ": lines are deterministic");

                // 保存済みのアトラス（PNG と CardArtAtlas）とも一致する
                var stored = atlas.Find(id);
                int col = stored.cell % CardArtPipeline.Columns, row = stored.cell / CardArtPipeline.Columns;
                for (int y = 0; y < cs; y++)
                    for (int x = 0; x < cs; x++)
                        Assert.That(px[(row * cs + y) * w + col * cs + x].r, Is.EqualTo(first.cell.Bytes[y * cs + x]), id + " pixel " + x + "," + y);
                Assert.That(stored.stars, Is.EqualTo(first.stars.Stars), id + ": stored stars");
                Assert.That(stored.lines, Is.EqualTo(first.stars.Lines), id + ": stored lines");
            }
        }

        [Test]
        public void StarsDependOnTheCardIdAndLevelBudget()
        {
            Assert.That(CardArtBaker.StarCount(1), Is.EqualTo(7));
            Assert.That(CardArtBaker.StarCount(10), Is.EqualTo(14));
            for (int lv = 2; lv <= 10; lv++) Assert.That(CardArtBaker.StarCount(lv), Is.GreaterThanOrEqualTo(CardArtBaker.StarCount(lv - 1)));
            // 同じ図形でもカード ID が違えば星の選び方が変わる（ID から決定論的に揺らぐ）
            var entry = CardArtTable.Assignments.First(a => a.cardId == "pegasion");
            var cell = CardArtPipeline.BakeOne(entry, 10).cell;
            var a1 = CardArtBaker.PickConstellation(cell.Distance, "pegasion", 10);
            var a2 = CardArtBaker.PickConstellation(cell.Distance, "pegasion", 10);
            var b = CardArtBaker.PickConstellation(cell.Distance, "someone-else", 10);
            Assert.That(a2.Stars, Is.EqualTo(a1.Stars));
            Assert.That(b.Stars, Is.Not.EqualTo(a1.Stars));
        }
    }
}
