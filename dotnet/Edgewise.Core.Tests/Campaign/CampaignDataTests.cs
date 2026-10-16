using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using NUnit.Framework;

namespace Edgewise.Core.Tests.Campaign
{
    [TestFixture]
    public class CampaignDataTests
    {
        static int Level(string id) => CardCatalog.ById[id].Level;

        [Test]
        public void HasFourRegionsWithThreeOpponentsAndABossEach()
        {
            Assert.That(CampaignData.Regions.Count, Is.EqualTo(4));
            for (int i = 0; i < CampaignData.Regions.Count; i++)
            {
                var r = CampaignData.Regions[i];
                Assert.That(r.Number, Is.EqualTo(i + 1));
                Assert.That(r.Opponents.Count, Is.EqualTo(3), r.Id);
                Assert.That(r.Opponents.Count(o => o.IsBoss), Is.EqualTo(1), r.Id);
                Assert.That(r.Boss.IsBoss, Is.True);
                foreach (var o in r.Opponents)
                {
                    Assert.That(o.RegionId, Is.EqualTo(r.Id));
                    Assert.That(o.DeckLevelCap, Is.EqualTo(r.DeckLevelCap + (o.IsBoss ? CampaignData.BossCapBonus : 0)), o.Id);
                    Assert.That(CampaignData.OpponentById(o.Id), Is.SameAs(o));
                }
            }
            Assert.That(CampaignData.Regions.Select(r => r.DeckLevelCap), Is.EqualTo(new[] { 15, 25, 35, 45 }));
            Assert.That(CampaignData.Opponents.Select(o => o.Id).Distinct().Count(), Is.EqualTo(12));
        }

        [Test]
        public void RegionRulesFollowTheDesign()
        {
            var r = CampaignData.Regions;
            Assert.That(r[0].Rules, Is.EqualTo(new RuleSet(open: true)));
            Assert.That(r[1].Rules, Is.EqualTo(new RuleSet(open: true, same: true, sameWall: true, plus: true)));
            Assert.That(r[2].Rules, Is.EqualTo(new RuleSet(open: false, elemental: true, same: true, plus: true, combo: true)));
            Assert.That(r[3].Rules.Pivot && r[3].Rules.SuddenDeath && r[3].Rules.Same && r[3].Rules.SameWall && r[3].Rules.Plus && r[3].Rules.Combo,
                Is.True);
            Assert.That(r.All(x => !x.Rules.Random && !x.Rules.Resonance && !x.Rules.Traits), Is.True);
        }

        [Test]
        public void StarterHasSevenLevelOneOrTwoCards()
        {
            Assert.That(CampaignData.StarterCards.Count, Is.EqualTo(7));
            Assert.That(CampaignData.StarterCards.Distinct().Count(), Is.EqualTo(7));
            Assert.That(CampaignData.StarterCards.All(id => Level(id) <= 2), Is.True);
        }

        [Test]
        public void EveryCardIsHeldBySomeOpponent()
        {
            var held = new HashSet<string>(CampaignData.Opponents.SelectMany(o => o.Cards));
            var missing = CardCatalog.All.Where(c => !held.Contains(c.Id)).Select(c => c.Id).ToList();
            Assert.That(missing, Is.Empty);
            Assert.That(held.All(CardCatalog.ById.ContainsKey), Is.True);
        }

        [Test]
        public void LevelTenCardsAreOnlyInTheLastRegionAndTheLastBossHasTwo()
        {
            foreach (var o in CampaignData.Opponents)
            {
                int lv10 = o.Cards.Count(id => Level(id) == 10);
                if (o.RegionId != "gate") Assert.That(lv10, Is.Zero, o.Id);
            }
            var elder = CampaignData.Regions[3].Boss;
            Assert.That(elder.Cards.Count(id => Level(id) == 10), Is.EqualTo(2));
        }

        [Test]
        public void EachOpponentHoldsSixToEightDistinctCardsAndCanBuildADeckWithinItsCap()
        {
            foreach (var o in CampaignData.Opponents)
            {
                Assert.That(o.Cards.Count, Is.InRange(6, 8), o.Id);
                Assert.That(o.Cards.Distinct().Count(), Is.EqualTo(o.Cards.Count), o.Id);
                var lowest = o.Cards.Select(Level).OrderBy(x => x).Take(5).Sum();
                Assert.That(lowest, Is.LessThanOrEqualTo(o.DeckLevelCap), o.Id);
                for (int seed = 0; seed < 20; seed++)
                {
                    var deck = CampaignRules.ChooseOpponentDeck(CampaignRules.NewGame(), o, seed);
                    Assert.That(deck, Is.Not.Null, o.Id);
                    Assert.That(deck.Length, Is.EqualTo(5));
                    Assert.That(deck.Distinct().Count(), Is.EqualTo(5));
                    Assert.That(deck.All(o.Cards.Contains), Is.True);
                    Assert.That(CampaignRules.TotalLevel(deck), Is.LessThanOrEqualTo(o.DeckLevelCap), o.Id);
                }
            }
        }

        [Test]
        public void OpponentsHaveProfilesAndDialogue()
        {
            foreach (var o in CampaignData.Opponents)
            {
                Assert.That(o.Name, Is.Not.Empty);
                Assert.That(o.Title, Is.Not.Empty);
                Assert.That(o.Profile, Is.Not.Null);
                foreach (var lines in new[] { o.Lines.Intro, o.Lines.PlayerWins, o.Lines.PlayerLoses, o.Lines.GiveCard, o.Lines.TakeCard })
                {
                    Assert.That(lines.Count, Is.InRange(1, 3), o.Id);
                    Assert.That(lines.All(l => !string.IsNullOrWhiteSpace(l)), Is.True, o.Id);
                }
                if (o.IsBoss) Assert.That(o.Lines.RegionClear.Count, Is.InRange(1, 3), o.Id);
                else Assert.That(o.Lines.RegionClear, Is.Empty, o.Id);
            }
        }

        [Test]
        public void DialogueAvoidsFinalFantasyProperNouns()
        {
            // 権利の扱い（DESIGN §0）。原作を連想させる代表的な固有名詞が台詞・名前に入っていないこと
            var banned = new[]
            {
                "チョコボ", "モーグリ", "サボテンダー", "トンベリ", "クリスタル", "トリプル", "トライアド", "スコール", "リノア", "ガーデン",
                "SeeD", "シド", "バハムート", "イフリート", "シヴァ", "ファイナル", "Triple", "Triad",
                "ガーネット", "ピアとピオ", "ジン", "パロム", "ポロム", "ティナ", "クラウド", "ティーダ", "ユウナ", "ライトニング", "オーディン", "リヴァイアサン", "ラムウ", "ポーション", "エーテル",
            };
            foreach (var o in CampaignData.Opponents)
            {
                var text = string.Join("\n", o.Lines.AllLines()) + o.Name + o.Title;
                foreach (var w in banned) Assert.That(text, Does.Not.Contain(w), o.Id);
            }
        }

        [Test]
        public void StrengthAndPersonalityMatchTheDesignTable()
        {
            var expected = new Dictionary<string, string>
            {
                ["harbor-apprentice"] = "Easy", ["harbor-keeper"] = "Easy+", ["harbor-master"] = "Normal",
                ["library-scribe"] = "Normal", ["library-twins"] = "Normal", ["library-curator"] = "Normal+",
                ["observatory-forecaster"] = "Normal+", ["observatory-seer"] = "Hard-", ["observatory-director"] = "Hard",
                ["gate-traveler"] = "Hard-", ["gate-knight"] = "Hard", ["gate-elder"] = "Hard",
            };
            foreach (var o in CampaignData.Opponents)
            {
                Assert.That(o.StrengthLabel, Is.EqualTo(expected[o.Id]), o.Id);
                Assert.That(o.Profile.Name, Is.EqualTo(o.StrengthLabel), o.Id);
            }
            // 冷静・最強は性格なし（純粋な Hard）
            Assert.That(CampaignData.OpponentById("observatory-director").Profile.Personality, Is.Null);
            Assert.That(CampaignData.OpponentById("gate-elder").Profile.Personality, Is.Null);
            Assert.That(CampaignData.OpponentById("library-scribe").Profile.Personality.Same, Is.GreaterThan(0));
            Assert.That(CampaignData.OpponentById("library-twins").Profile.Personality.Plus, Is.GreaterThan(0));
            Assert.That(CampaignData.OpponentById("gate-traveler").Profile.Personality.Pivot, Is.GreaterThan(0));
            Assert.That(CampaignData.OpponentById("observatory-seer").Profile.Personality.Volatility, Is.GreaterThan(0));
            Assert.That(CampaignData.OpponentById("gate-knight").Profile.PersonalityAsTieBreak, Is.True);
        }
    }
}
