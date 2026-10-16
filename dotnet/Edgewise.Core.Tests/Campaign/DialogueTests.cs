using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.Campaign;
using NUnit.Framework;

namespace Edgewise.Core.Tests.Campaign
{
    /// <summary>
    /// 5c: 台詞の指針（DESIGN §12-7-1）の機械的に確かめられる部分。
    /// 全場面の有無・1 行の長さ・ルールの手ほどきのキーワード・セレストが「連鎖」と言わないこと・再戦の判定・エンディングの文。
    /// </summary>
    [TestFixture]
    public class DialogueTests
    {
        /// <summary>1 行の上限（全角で数える。目安は 30 字、上限は 34 字）。</summary>
        const double MaxWidth = 34;

        /// <summary>表示幅（全角 1、ASCII は半角として 0.5）。</summary>
        static double Width(string line) => line.Sum(c => c < 0x7f ? 0.5 : 1.0);

        static IEnumerable<(string scene, IReadOnlyList<string> lines)> Scenes(Opponent o)
        {
            yield return ("intro", o.Lines.Intro);
            yield return ("rematch", o.Lines.Rematch);
            yield return ("playerWins", o.Lines.PlayerWins);
            yield return ("playerLoses", o.Lines.PlayerLoses);
            yield return ("give", o.Lines.GiveCard);
            yield return ("take", o.Lines.TakeCard);
            if (o.IsBoss) yield return ("regionClear", o.Lines.RegionClear);
        }

        [Test]
        public void EveryOpponentHasEverySceneWithOneToThreeLines()
        {
            foreach (var o in CampaignData.Opponents)
            {
                foreach (var (scene, lines) in Scenes(o))
                {
                    Assert.That(lines.Count, Is.InRange(1, 3), o.Id + " " + scene);
                    Assert.That(lines.All(l => !string.IsNullOrWhiteSpace(l)), Is.True, o.Id + " " + scene);
                }
                Assert.That(o.Lines.Rematch.Count, Is.InRange(1, 2), o.Id + " rematch is short (1-2 lines)");
                Assert.That(o.Lines.Rematch, Is.Not.EqualTo(o.Lines.Intro), o.Id);
                if (!o.IsBoss) Assert.That(o.Lines.RegionClear, Is.Empty, o.Id);
            }
        }

        [Test]
        public void EveryLineFitsTheBand()
        {
            foreach (var o in CampaignData.Opponents)
            {
                foreach (var line in o.Lines.AllLines())
                {
                    Assert.That(Width(line), Is.LessThanOrEqualTo(MaxWidth), o.Id + ": " + line);
                    Assert.That(line, Does.Not.Contain("\n"), o.Id + ": one line each");
                }
            }
        }

        /// <summary>担当のルールのキーワードが、初回の登場台詞に入っている（ゆるい判定: どれか 1 つ）。</summary>
        [TestCase("harbor-apprentice", "大きい")]
        [TestCase("harbor-apprentice", "ヒント")]
        [TestCase("harbor-keeper", "角", "端")]
        [TestCase("harbor-master", "引き分け", "決着")]
        [TestCase("library-scribe", "同じ数")]
        [TestCase("library-scribe", "壁")]
        [TestCase("library-twins", "和", "足し")]
        [TestCase("library-curator", "Same")]
        [TestCase("library-curator", "Plus")]
        [TestCase("observatory-forecaster", "属性")]
        [TestCase("observatory-seer", "手札", "伏せ")]
        [TestCase("observatory-director", "連鎖", "Combo")]
        [TestCase("gate-traveler", "回")]
        [TestCase("gate-knight", "守")]
        public void FirstIntroTouchesTheRuleItTeaches(string opponentId, params string[] anyOf)
        {
            var o = CampaignData.OpponentById(opponentId);
            var intro = string.Join("\n", o.Lines.Intro);
            Assert.That(anyOf.Any(k => intro.Contains(k)), Is.True, opponentId + " intro should mention one of: " + string.Join(", ", anyOf));
        }

        [Test]
        public void TheCuratorNeverTalksAboutChainsBecauseTheLibraryHasNoCombo()
        {
            var library = CampaignData.RegionById("library");
            Assert.That(library.Rules.Combo, Is.False, "the premise: no Combo in the library");
            var curator = CampaignData.OpponentById("library-curator");
            foreach (var line in curator.Lines.AllLines())
            {
                Assert.That(line, Does.Not.Contain("連鎖"), line);
                Assert.That(line, Does.Not.Contain("Combo"), line);
                Assert.That(line, Does.Not.Contain("連な"), line);
            }
            Assert.That(curator.PersonalityLabel, Does.Not.Contain("連鎖"));
        }

        [Test]
        public void RulesAreOnlyMentionedWhereTheyExist()
        {
            // 星域 1・2 の相手は、まだ出てこないルール（Combo・属性・Pivot）を語らない
            foreach (var o in CampaignData.Regions[0].Opponents.Concat(CampaignData.Regions[1].Opponents))
            {
                var intro = string.Join("\n", o.Lines.Intro.Concat(o.Lines.Rematch));
                foreach (var w in new[] { "連鎖", "Combo", "属性", "Pivot" }) Assert.That(intro, Does.Not.Contain(w), o.Id);
            }
        }

        [Test]
        public void RematchIsDecidedByTheSavedRecord()
        {
            var mira = CampaignData.OpponentById("harbor-apprentice");
            var keeper = CampaignData.OpponentById("harbor-keeper");
            var s = CampaignRules.NewGame(3);
            Assert.That(CampaignRules.IsRematch(s, mira), Is.False, "first meeting");
            Assert.That(CampaignRules.IntroLines(s, mira), Is.EqualTo(mira.Lines.Intro));

            // 引き分け・負け・勝ちのどれでも、1 度対戦すれば再戦
            foreach (var kind in new[] { MatchResultKind.Draw, MatchResultKind.Loss, MatchResultKind.Win })
            {
                var deck = CampaignRules.AutoDeck(s, CampaignData.Regions[0]);
                var theirs = CampaignRules.ChooseOpponentDeck(s, mira, 1);
                var after = CampaignRules.ApplyMatch(s, mira, deck, theirs, new MatchOutcome(kind, false), null).State;
                Assert.That(CampaignRules.IsRematch(after, mira), Is.True, kind.ToString());
                Assert.That(CampaignRules.IntroLines(after, mira), Is.EqualTo(mira.Lines.Rematch), kind.ToString());
                Assert.That(CampaignRules.IsRematch(after, keeper), Is.False, "other opponents are still first meetings");
            }

            // セーブを通しても同じ（形式は変えていない: 勝敗数から判定する）
            var saved = CampaignRules.NewGame(3).WithRecord(mira.Id, new OpponentRecord(0, 1, 0, null, null));
            var loaded = CampaignSave.Load(CampaignSave.ToJson(saved));
            Assert.That(loaded.Status, Is.EqualTo(SaveLoadStatus.Ok));
            Assert.That(CampaignRules.IsRematch(loaded.State, mira), Is.True);
            Assert.That(CampaignRules.IsRematch(null, mira), Is.False);
        }

        [Test]
        public void EndingHasThreeToFourShortLines()
        {
            Assert.That(CampaignData.Ending.Count, Is.InRange(3, 4));
            foreach (var line in CampaignData.Ending)
            {
                Assert.That(string.IsNullOrWhiteSpace(line), Is.False);
                Assert.That(Width(line), Is.LessThanOrEqualTo(MaxWidth), line);
            }
            // 星図を巡り終えたことを告げる
            Assert.That(string.Join("", CampaignData.Ending), Does.Contain("星図"));
        }

        [Test]
        public void OnlyTheFirstWinOverTheLastBossCompletesTheJourney()
        {
            var lastBoss = CampaignData.Regions[CampaignData.Regions.Count - 1].Boss;
            var all = CampaignRules.NewGame(3).WithUnlocked(CampaignData.Regions.Select(r => r.Id));
            var theirs = CampaignRules.ChooseOpponentDeck(all, lastBoss, 1);
            var win = new MatchOutcome(MatchResultKind.Win, false);

            var lost = CampaignRules.ApplyMatch(all, lastBoss, null, theirs, new MatchOutcome(MatchResultKind.Loss, false), null);
            Assert.That(lost.CompletedJourney, Is.False);

            var first = CampaignRules.ApplyMatch(lost.State, lastBoss, null, theirs, win, null);
            Assert.That(first.ClearedRegion, Is.True);
            Assert.That(first.CompletedJourney, Is.True, "the first win over the last boss");

            var second = CampaignRules.ApplyMatch(first.State, lastBoss, null, theirs, win, null);
            Assert.That(second.CompletedJourney, Is.False, "not again on the second win");

            // ほかの主の初勝利はエンディングにならない
            foreach (var r in CampaignData.Regions.Take(CampaignData.Regions.Count - 1))
            {
                var app = CampaignRules.ApplyMatch(all, r.Boss, null, CampaignRules.ChooseOpponentDeck(all, r.Boss, 1), win, null);
                Assert.That(app.ClearedRegion, Is.True, r.Id);
                Assert.That(app.CompletedJourney, Is.False, r.Id);
            }
        }
    }
}
