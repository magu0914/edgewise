using System.Collections.Generic;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.EditorTools;
using Edgewise.Game.Match;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Edgewise.Tests.EditMode
{
    /// <summary>Phase 6（Resonance と Trait。DESIGN §3-4）の表示まわり: 演出ステップ・バッジ・文言・設定・星図巡りで使わないこと。</summary>
    public class Phase6Tests
    {
        static GameAssets Assets => AssetDatabase.LoadAssetAtPath<GameAssets>(ArtBuilder.GameAssetsPath);

        [Test]
        public void AGuardEventBecomesATraitStepInEventOrder()
        {
            var attacker = new CardDef("atk", "Atk", 1, new Sides(9, 1, 1, 1));
            var guard = new CardDef("grd", "Grd", 1, new Sides(1, 1, 1, 1), null, Tribe.Beast, TraitId.Guard);
            var cards = new Dictionary<string, CardDef> { { "atk", attacker } };
            var board = new PlacedCard?[9];
            board[1] = new PlacedCard(guard, Player.P1, 0);
            var state = GameState.FromParts(new RuleSet(traits: true), cards, board, new Element?[9], new[] { "atk" }, new string[0],
                Player.P0, Player.P0, 0, 0, 1u, Phase.Playing, 0);
            var result = GameEngine.ApplyMove(state, new Move(0, 4));
            var steps = PlaybackPlanner.Build(result.Events);
            Assert.That(PlaybackPlanner.Describe(steps), Is.EqualTo("Place p0 cell 4 atk r0 | Trait guard cell 1 | Turn p1"));
            var trait = steps.Single(s => s.Kind == StepKind.Trait);
            Assert.That(trait.Cell, Is.EqualTo(1));
            Assert.That(trait.Trait, Is.EqualTo(TraitId.Guard));
        }

        [Test]
        public void ThePlannerKeepsFlipsAndGuardsInTheEnginesOrder()
        {
            // Pierce で 2 枚取り、1 枚は Guard に防がれる（右上 → 右下 → 左下 → 左上の順）
            var p = new CardDef("p", "P", 1, new Sides(9, 9, 9, 9), null, null, TraitId.Pierce);
            var weak = new CardDef("w", "W", 1, new Sides(1, 1, 1, 1));
            var guard = new CardDef("g", "G", 1, new Sides(1, 1, 1, 1), null, null, TraitId.Guard);
            var cards = new Dictionary<string, CardDef> { { "p", p } };
            var board = new PlacedCard?[9];
            board[2] = new PlacedCard(weak, Player.P1, 0);
            board[8] = new PlacedCard(guard, Player.P1, 0);
            board[0] = new PlacedCard(weak, Player.P1, 0);
            var state = GameState.FromParts(new RuleSet(traits: true), cards, board, new Element?[9], new[] { "p" }, new string[0],
                Player.P0, Player.P0, 0, 0, 1u, Phase.Playing, 0);
            var steps = PlaybackPlanner.Build(GameEngine.ApplyMove(state, new Move(0, 4)).Events);
            Assert.That(PlaybackPlanner.Describe(steps), Is.EqualTo(
                "Place p0 cell 4 p r0 | FlipGroup [2:basic0] | Trait guard cell 8 | FlipGroup [0:basic0] | Turn p1"));
        }

        [Test]
        public void TheBadgeShowsOnlyWhenTraitsAreOnAndTheCardIsFaceUp()
        {
            var card = Object.Instantiate(Assets.cardPrefab);
            try
            {
                var leomane = CardCatalog.ById["leomane"];
                Assert.That(leomane.Trait, Is.EqualTo(TraitId.Pierce));
                card.Bind(leomane, Player.P0, 0, false);
                Assert.That(card.TraitBadgeVisible, Is.False, "off by default (Traits rule OFF)");
                Assert.That(card.TraitBadgeText, Is.Empty);
                card.SetTraitsEnabled(true);
                Assert.That(card.TraitBadgeVisible, Is.True);
                Assert.That(card.TraitBadgeText, Is.EqualTo("貫"));
                card.SetFaceDown(true);
                Assert.That(card.TraitBadgeVisible, Is.False, "hidden while face down");
                card.SetFaceDown(false);
                card.Bind(CardCatalog.ById["glimmouse"], Player.P0, 0, false);
                Assert.That(card.TraitBadgeVisible, Is.False, "a card without a Trait has no badge");
                card.SetTraitsEnabled(false);
                card.Bind(leomane, Player.P0, 0, false);
                Assert.That(card.TraitBadgeVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(card.gameObject);
            }
        }

        [Test]
        public void ResonanceShieldAndSpentGuardAreViewStatesThatBindResets()
        {
            var card = Object.Instantiate(Assets.cardPrefab);
            try
            {
                card.SetTraitsEnabled(true);
                card.Bind(CardCatalog.ById["auroraveil"], Player.P0, 0, false);
                Assert.That(card.TraitBadgeText, Is.EqualTo("守"));
                card.SetResonance(true);
                card.SetShield(0.7f);
                card.SetTraitUsed(true);
                card.ApplyNow();
                Assert.That(card.Resonating, Is.True);
                Assert.That(card.TraitUsed, Is.True);
                card.Bind(CardCatalog.ById["auroraveil"], Player.P0, 0, false);
                Assert.That(card.Resonating, Is.False);
                Assert.That(card.TraitUsed, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(card.gameObject);
            }
        }

        [Test]
        public void EveryTraitHasAGlyphAColorAndTextAndTheGlyphIsInTheFont()
        {
            var glyphs = new HashSet<string>();
            foreach (TraitId t in System.Enum.GetValues(typeof(TraitId)))
            {
                string g = Theme.TraitGlyph(t);
                Assert.That(g.Length, Is.EqualTo(1), t + " badge is one kanji");
                Assert.That(glyphs.Add(g), Is.True, "glyphs are distinct");
                Assert.That(UiText.ExtraGlyphs, Does.Contain(g), "baked into the font");
                Assert.That(UiText.TraitName(t), Is.Not.Empty);
                Assert.That(UiText.TraitDesc(t), Is.Not.Empty);
                Assert.That(Theme.TraitColor(t), Is.Not.EqualTo(Color.white));
                foreach (char c in UiText.TraitName(t) + UiText.TraitDesc(t)) Assert.That(c, Is.Not.EqualTo('\0'));
            }
            Assert.That(glyphs, Is.EquivalentTo(new[] { "守", "貫", "鼓", "錨" }));
        }

        [Test]
        public void TwelveOfTheTwentyHighestCardsHaveOneTraitEach()
        {
            var withTrait = CardCatalog.All.Where(c => c.Trait.HasValue).ToList();
            Assert.That(withTrait.Count, Is.EqualTo(12));
            Assert.That(withTrait.All(c => c.Level >= 7), Is.True);
            Assert.That(CardCatalog.All.Count(c => c.Level >= 7), Is.EqualTo(20));
            foreach (TraitId t in System.Enum.GetValues(typeof(TraitId))) Assert.That(withTrait.Count(c => c.Trait == t), Is.GreaterThan(0), t.ToString());
        }

        [Test]
        public void TheRuleListHasResonanceAndTraitsOffByDefault()
        {
            Assert.That(SetupScreen.RuleCount, Is.EqualTo(11));
            Assert.That(SetupScreen.RuleName(9), Is.EqualTo("Resonance"));
            Assert.That(SetupScreen.RuleName(10), Is.EqualTo("Traits"));
            Assert.That(SetupScreen.RuleName(SetupScreen.PivotIndex), Is.EqualTo("Pivot"));
            var s = new MatchSettings();
            Assert.That(s.resonance, Is.False);
            Assert.That(s.traits, Is.False);
            Assert.That(s.ToRuleSet().Resonance, Is.False);
            Assert.That(s.ToRuleSet().Traits, Is.False);
            s.resonance = true;
            s.traits = true;
            var r = s.ToRuleSet();
            Assert.That(r.Resonance && r.Traits, Is.True);
            Assert.That(r.ToBitString().Substring(9), Is.EqualTo("11"));
            // 保存した設定（古いセーブには 2 項目がない）を読んでも OFF のまま
            var old = new MatchSettings();
            JsonUtility.FromJsonOverwrite("{\"open\":true,\"same\":true,\"pivot\":true}", old);
            Assert.That(old.resonance || old.traits, Is.False);
        }

        [Test]
        public void TheRulesPanelExplainsResonanceAndEachTraitOnlyWhenOn()
        {
            string off = MenuPanel.DescribeRules(new RuleSet(open: true));
            Assert.That(off, Does.Not.Contain("Resonance").And.Not.Contain("Traits"));
            string res = MenuPanel.DescribeRules(new RuleSet(resonance: true));
            Assert.That(res, Does.Contain("Resonance").And.Contain(UiText.RuleResonanceDesc).And.Contain(UiText.ResonanceHeading));
            Assert.That(res, Does.Not.Contain("Traits"));
            string tr = MenuPanel.DescribeRules(new RuleSet(traits: true));
            Assert.That(tr, Does.Contain("Traits").And.Contain(UiText.TraitHeading));
            foreach (TraitId t in System.Enum.GetValues(typeof(TraitId)))
            {
                Assert.That(tr, Does.Contain(Theme.TraitGlyph(t)).And.Contain(UiText.TraitName(t)).And.Contain(UiText.TraitDesc(t)));
            }
        }

        [Test]
        public void TheDexExplainsTheTrait()
        {
            string text = CardDex.TraitInfoText(TraitId.Guard);
            Assert.That(text, Does.Contain("守").And.Contain(UiText.TraitGuardName).And.Contain(UiText.TraitGuardDesc).And.Contain(UiText.DexTraitNote));
        }

        [Test]
        public void TheHowToNoteMentionsTheNewRules()
        {
            Assert.That(UiText.HowToRulesNote, Does.Contain("Resonance").And.Contain("Traits").And.Contain("ルール確認"));
        }

        [Test]
        public void TheCampaignNeverUsesResonanceOrTraits()
        {
            foreach (var region in CampaignData.Regions)
            {
                Assert.That(region.Rules.Resonance || region.Rules.Traits, Is.False, region.Id);
            }
            foreach (var opponent in CampaignData.Opponents)
            {
                var rules = CampaignRules.MatchRules(opponent);
                Assert.That(rules.Resonance || rules.Traits, Is.False, opponent.Id);
            }
        }
    }
}
