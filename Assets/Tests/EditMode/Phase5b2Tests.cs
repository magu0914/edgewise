using System.IO;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.EditorTools;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Edgewise.Tests.EditMode
{
    /// <summary>5b-2: 対戦相手の立ち絵（12 人・重複なし・アトラス）、小さなカードの絵の割り当て、図鑑の未入手の表示（数字を伏せる）。</summary>
    public class Phase5b2Tests
    {
        static GameAssets Assets => AssetDatabase.LoadAssetAtPath<GameAssets>(ArtBuilder.GameAssetsPath);

        [Test]
        public void EveryOpponentHasItsOwnPortraitIcon()
        {
            var ids = CampaignData.Opponents.Select(o => o.Id).ToList();
            Assert.That(ids.Count, Is.EqualTo(12));
            Assert.That(CardArtTable.PortraitAssignments.Select(a => a.cardId), Is.EquivalentTo(ids), "12 opponents, one portrait each");
            var paths = CardArtTable.PortraitAssignments.Select(a => a.IconPath).ToList();
            Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Count), "no portrait icon is reused");
            var cardIcons = CardArtTable.Assignments.Select(a => a.IconPath).ToList();
            Assert.That(paths.Intersect(cardIcons), Is.Empty, "portraits do not reuse card icons");
            foreach (var a in CardArtTable.PortraitAssignments)
            {
                Assert.That(File.Exists(CardArtPipeline.SvgPath(a)), "SVG is copied into the repo: " + a.IconPath);
                Assert.That(File.Exists(CardArtPipeline.MaskPath(a)), "mask exists: " + a.IconPath);
                Assert.That(CardArtTable.License(a.author), Is.EqualTo("CC BY 3.0").Or.EqualTo("CC0"));
            }
        }

        [Test]
        public void PortraitsAreInTheAtlasWithConstellations()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<CardArtAtlas>(CardArtPipeline.AtlasAssetPath);
            var table = AssetDatabase.LoadAssetAtPath<CardArtTable>(CardArtPipeline.TablePath);
            Assert.That(atlas.atlas.width, Is.EqualTo(1024));
            Assert.That(atlas.atlas.height, Is.EqualTo(1024), "8×8 cells");
            var cells = new System.Collections.Generic.HashSet<int>();
            foreach (var o in CampaignData.Opponents)
            {
                string id = CardArtTable.PortraitId(o.Id);
                var e = atlas.Find(id);
                Assert.That(e, Is.Not.Null, id);
                Assert.That(table.Find(id), Is.Not.Null, id);
                Assert.That(cells.Add(e.cell), Is.True, "own cell: " + id);
                Assert.That(e.stars.Length, Is.EqualTo(CardArtBaker.StarCount(CardArtPipeline.StarLevel(id))), id);
                Assert.That(e.lines.Length, Is.GreaterThanOrEqualTo(e.stars.Length - 1), id);
            }
            // 奥の星域・主ほど星が多い
            Assert.That(atlas.Find("@gate-elder").stars.Length, Is.GreaterThan(atlas.Find("@harbor-apprentice").stars.Length));
            Assert.That(table.CreditLine("@library-twins"), Does.Contain("face-to-face by Delapouite"));
        }

        [Test]
        public void PortraitViewDrawsTheOpponentsSilhouetteAndKeepsTheCrown()
        {
            var a = Assets;
            var root = new GameObject("Root", typeof(RectTransform));
            try
            {
                foreach (var o in CampaignData.Opponents)
                {
                    var badge = PortraitView.Create(root.transform, "Badge", a);
                    badge.Show(o);
                    Assert.That(badge.ArtId, Is.EqualTo("@" + o.Id));
                    Assert.That(badge.CrownShown, Is.EqualTo(o.IsBoss), "crown on bosses only");
                    Assert.That(badge.InitialText, Is.EqualTo(""), "the placeholder initial is gone");
                    var big = PortraitView.Create(root.transform, "Big", a, true);
                    big.Show(o);
                    Assert.That(big.ArtId, Is.EqualTo("@" + o.Id));
                    Assert.That(big.Art.CurrentShape, Is.EqualTo(ArtGraphic.Shape.Soft));
                    Assert.That(big.Art.HasConstellation, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MiniCardsDrawTheCardArtButNotForUnknownOrEmptySlots()
        {
            var a = Assets;
            var root = new GameObject("Root", typeof(RectTransform));
            try
            {
                var mc = MiniCardView.Create(root.transform, "Mini", a, true);
                foreach (var def in CardCatalog.All)
                {
                    mc.Show(def, true);
                    Assert.That(mc.ArtId, Is.EqualTo(def.Id), "the card's own silhouette");
                    Assert.That(mc.IsUnknown, Is.False);
                }
                mc.ShowUnknown();
                Assert.That(mc.ArtId, Is.Null, "unseen cards stay a question mark");
                Assert.That(mc.IsUnknown, Is.True);
                mc.ShowEmpty();
                Assert.That(mc.ArtId, Is.Null, "an empty deck slot has no art");
                // 数字は絵の上に読める（ランクの文字はプレートより後＝手前に描かれる）
                mc.Show(CardCatalog.ById["leomane"]);
                var plate = mc.transform.Find("RankPlate");
                var rank = mc.transform.Find("RankTop");
                var art = mc.transform.Find("Art");
                Assert.That(art.GetSiblingIndex(), Is.LessThan(plate.GetSiblingIndex()));
                Assert.That(plate.GetSiblingIndex(), Is.LessThan(rank.GetSiblingIndex()));
                Assert.That(rank.GetComponent<TextMeshProUGUI>().text, Is.EqualTo("9"));
                Assert.That(a.uiArtMaterial, Is.Not.Null);
                Assert.That(a.uiArtMaterial.shader.name, Is.EqualTo("Edgewise/UIArt"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UnknownCardsHideTheirNumbersAndName()
        {
            var a = Assets;
            var card = Object.Instantiate(a.cardPrefab);
            try
            {
                var def = CardCatalog.ById["orionmane"];
                card.Bind(def, Player.P0, 0, false);
                var texts = card.GetComponentsInChildren<TextMeshPro>(true);
                var rank = texts.First(t => t.name == "RankTop");
                var name = texts.First(t => t.name == "Name");
                Assert.That(rank.gameObject.activeSelf, Is.True);
                card.SetCount(2);
                var count = texts.First(t => t.name == "Count");
                Assert.That(count.gameObject.activeSelf && count.text == "×2", Is.True, "owned count badge");
                card.SetUnknown(true);
                Assert.That(rank.gameObject.activeSelf, Is.False, "numbers are hidden");
                Assert.That(name.gameObject.activeSelf, Is.False, "the name is hidden");
                Assert.That(count.gameObject.activeSelf, Is.False);
                Assert.That(card.Unknown, Is.True);
                card.Bind(def, Player.P0, 0, false);
                Assert.That(card.Unknown, Is.False, "rebinding resets the unknown state");
                string shader = File.ReadAllText("Assets/Game/Art/Shaders/Card.shader");
                Assert.That(shader, Does.Contain("else if (_Unknown > 0.5) acc = unknownFace("), "the shader draws a dark silhouette");
            }
            finally
            {
                Object.DestroyImmediate(card.gameObject);
            }
        }

        [Test]
        public void CreditsListEveryPortrait()
        {
            string text = CreditsScreen.Compose();
            foreach (var a in CardArtTable.PortraitAssignments)
            {
                var o = CampaignData.OpponentById(a.cardId);
                Assert.That(text, Does.Contain(o.Title + " " + o.Name + " — " + a.icon + " (" + CardArtTable.AuthorDisplayName(a.author)));
            }
        }
    }
}
