using System;
using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using NUnit.Framework;

namespace Edgewise.Core.Tests.Campaign
{
    [TestFixture]
    public class CampaignRulesTests
    {
        static Dictionary<string, int> Col(params string[] ids)
        {
            var d = new Dictionary<string, int>();
            foreach (var id in ids) d[id] = d.TryGetValue(id, out int n) ? n + 1 : 1;
            return d;
        }

        static Region Harbor => CampaignData.Regions[0];
        static Opponent Apprentice => CampaignData.OpponentById("harbor-apprentice");
        static Opponent HarborBoss => CampaignData.Regions[0].Boss;

        // ------------------------------------------------------------------
        // 開始
        // ------------------------------------------------------------------

        [Test]
        public void NewGameStartsWithTheStarterCardsAndOnlyTheFirstRegion()
        {
            var s = CampaignRules.NewGame(42);
            Assert.That(s.Seed, Is.EqualTo(42));
            Assert.That(s.ClassicMode, Is.False);
            Assert.That(s.TotalCards, Is.EqualTo(7));
            Assert.That(CampaignData.StarterCards.All(id => s.Count(id) == 1), Is.True);
            Assert.That(s.UnlockedRegions, Is.EqualTo(new[] { "harbor" }));
            Assert.That(CampaignRules.CanChallenge(s, Apprentice), Is.True);
            Assert.That(CampaignRules.CanChallenge(s, CampaignData.Regions[1].Opponents[0]), Is.False);
            Assert.That(CampaignRules.ValidateDeck(s, Harbor, CampaignRules.AutoDeck(s, Harbor)), Is.Empty);
        }

        // ------------------------------------------------------------------
        // デッキの検証
        // ------------------------------------------------------------------

        [Test]
        public void ValidDeckHasNoIssues()
        {
            var col = Col("glimmouse", "dustwisp", "coglet", "emberkit", "scalefry", "pyxmoth");
            Assert.That(CampaignRules.ValidateDeck(col, new[] { "glimmouse", "dustwisp", "coglet", "emberkit", "scalefry" }, 5), Is.Empty);
        }

        [Test]
        public void DeckValidationReportsEachProblem()
        {
            var col = Col("glimmouse", "dustwisp", "coglet", "emberkit", "scalefry", "lynxlight", "leomane");
            Assert.That(CampaignRules.ValidateDeck(col, new[] { "glimmouse", "dustwisp", "coglet", "emberkit" }, 15),
                Is.EqualTo(new[] { new DeckIssue(DeckProblem.WrongCount) }));
            Assert.That(CampaignRules.ValidateDeck(col, new[] { "glimmouse", "dustwisp", "coglet", "emberkit", "scalefry", "lynxlight" }, 15),
                Is.EqualTo(new[] { new DeckIssue(DeckProblem.WrongCount) }));
            Assert.That(CampaignRules.ValidateDeck(col, new[] { "glimmouse", "glimmouse", "coglet", "emberkit", "scalefry" }, 15),
                Is.EqualTo(new[] { new DeckIssue(DeckProblem.Duplicate, "glimmouse") }));
            Assert.That(CampaignRules.ValidateDeck(col, new[] { "glimmouse", "nosuchcard", "coglet", "emberkit", "scalefry" }, 15),
                Is.EqualTo(new[] { new DeckIssue(DeckProblem.UnknownCard, "nosuchcard") }));
            Assert.That(CampaignRules.ValidateDeck(col, new[] { "glimmouse", "pyxmoth", "coglet", "emberkit", "scalefry" }, 15),
                Is.EqualTo(new[] { new DeckIssue(DeckProblem.NotOwned, "pyxmoth") }));
            // 合計 Lv: 1+1+1+3+7 = 13
            var deck = new[] { "glimmouse", "dustwisp", "coglet", "lynxlight", "leomane" };
            Assert.That(CampaignRules.TotalLevel(deck), Is.EqualTo(13));
            Assert.That(CampaignRules.ValidateDeck(col, deck, 13), Is.Empty, "the cap is inclusive");
            Assert.That(CampaignRules.ValidateDeck(col, deck, 12), Is.EqualTo(new[] { new DeckIssue(DeckProblem.OverLevelCap) }));
            Assert.That(CampaignRules.ValidateDeck(col, null, 15), Is.EqualTo(new[] { new DeckIssue(DeckProblem.WrongCount) }));
            Assert.That(CampaignRules.ValidateDeck(null, deck, 15).Count(i => i.Problem == DeckProblem.NotOwned), Is.EqualTo(5));
            // 0 枚は持っていない扱い
            var zero = new Dictionary<string, int>(col) { ["leomane"] = 0 };
            Assert.That(CampaignRules.ValidateDeck(zero, deck, 15), Is.EqualTo(new[] { new DeckIssue(DeckProblem.NotOwned, "leomane") }));
        }

        // ------------------------------------------------------------------
        // おまかせ
        // ------------------------------------------------------------------

        static string[] BruteForceAutoDeck(IReadOnlyDictionary<string, int> col, RuleSet rules, int cap)
        {
            var items = CardCatalog.All.Where(c => col.TryGetValue(c.Id, out int n) && n > 0).ToList();
            string[] best = null;
            int bestScore = -1, bestLevel = 0;
            int m = items.Count;
            for (int a = 0; a < m; a++)
                for (int b = a + 1; b < m; b++)
                    for (int c = b + 1; c < m; c++)
                        for (int d = c + 1; d < m; d++)
                            for (int e = d + 1; e < m; e++)
                            {
                                var set = new[] { items[a], items[b], items[c], items[d], items[e] };
                                int lv = set.Sum(x => x.Level);
                                if (lv > cap) continue;
                                int sc = set.Sum(x => CampaignRules.CardScore(x, rules));
                                if (sc > bestScore || (sc == bestScore && lv < bestLevel))
                                {
                                    bestScore = sc;
                                    bestLevel = lv;
                                    best = set.Select(x => x.Id).ToArray();
                                }
                            }
            return best;
        }

        [Test]
        public void AutoDeckFindsTheBestScoringDeckWithinTheCap()
        {
            var r = new TestRandom(0xa070);
            for (int t = 0; t < 120; t++)
            {
                int size = 4 + r.NextInt(12);
                var ids = new List<string>();
                while (ids.Count < size)
                {
                    var id = r.Pick(CardCatalog.All).Id;
                    if (!ids.Contains(id)) ids.Add(id);
                }
                var col = Col(ids.ToArray());
                var rules = CampaignData.Regions[r.NextInt(4)].Rules;
                int cap = 5 + r.NextInt(46);
                var auto = CampaignRules.AutoDeck(col, rules, cap);
                var brute = BruteForceAutoDeck(col, rules, cap);
                if (brute == null)
                {
                    Assert.That(auto, Is.Null);
                    continue;
                }
                Assert.That(auto, Is.Not.Null);
                Assert.That(CampaignRules.ValidateDeck(col, auto, cap), Is.Empty);
                int Score(string[] d) => d.Sum(id => CampaignRules.CardScore(CardCatalog.ById[id], rules));
                Assert.That(Score(auto), Is.EqualTo(Score(brute)));
                Assert.That(CampaignRules.TotalLevel(auto), Is.EqualTo(CampaignRules.TotalLevel(brute)));
                Assert.That(CampaignRules.AutoDeck(col, rules, cap), Is.EqualTo(auto), "deterministic");
            }
        }

        [Test]
        public void AutoDeckPrefersRuleFriendlyCards()
        {
            // Elemental では属性つきのカードに加点（同じ辺の合計なら属性つきが選ばれる）
            var plain = CardCatalog.ById["glimmouse"]; // 1.4.1.5 属性なし
            var fire = CardCatalog.ById["emberkit"];  // 4.2.1.5 火
            Assert.That(CampaignRules.CardScore(fire, new RuleSet(elemental: true)) - CampaignRules.CardScore(fire, RuleSet.None), Is.EqualTo(12));
            Assert.That(CampaignRules.CardScore(plain, new RuleSet(elemental: true)), Is.EqualTo(CampaignRules.CardScore(plain, RuleSet.None)));
            var ace = CardCatalog.ById["cygnwraith"];  // 10.3.8.6
            Assert.That(CampaignRules.CardScore(ace, new RuleSet(same: true, sameWall: true)) - CampaignRules.CardScore(ace, RuleSet.None),
                Is.EqualTo(6));
            Assert.That(CampaignRules.AutoDeck(Col("glimmouse", "dustwisp", "coglet", "emberkit"), RuleSet.None, 50), Is.Null, "fewer than 5 kinds");
            Assert.That(CampaignRules.AutoDeck(Col("leomane", "draconyx", "stormgear", "auroraveil", "forgecolossus"), RuleSet.None, 34), Is.Null,
                "over the cap");
        }

        // ------------------------------------------------------------------
        // 相手のデッキ
        // ------------------------------------------------------------------

        [Test]
        public void OpponentDeckIsDeterministicWithinTheCapAndVariesWithTheSeed()
        {
            foreach (var o in CampaignData.Opponents)
            {
                var s = CampaignRules.NewGame(7);
                var decks = new HashSet<string>();
                for (int seed = 0; seed < 40; seed++)
                {
                    var a = CampaignRules.ChooseOpponentDeck(s, o, seed);
                    var b = CampaignRules.ChooseOpponentDeck(s, o, seed);
                    Assert.That(b, Is.EqualTo(a));
                    Assert.That(CampaignRules.TotalLevel(a), Is.LessThanOrEqualTo(o.DeckLevelCap));
                    decks.Add(string.Join(",", a));
                }
                Assert.That(decks.Count, Is.GreaterThan(1), o.Id + " should vary its deck");
            }
        }

        [Test]
        public void OpponentDeckSlackBoundsHowWeakTheDeckCanBe()
        {
            var pool = new[] { "glimmouse", "dustwisp", "pyxmoth", "lynxlight", "velahound", "lupusfang", "aquilaburst" };
            for (int seed = 0; seed < 50; seed++)
            {
                // 上限 20 で組める最大の合計 Lv は 6+5+4+3+2 = 20
                var tight = CampaignRules.ChooseOpponentDeck(pool, 20, 0, seed);
                Assert.That(CampaignRules.TotalLevel(tight), Is.EqualTo(20));
                var loose = CampaignRules.ChooseOpponentDeck(pool, 20, 3, seed);
                Assert.That(CampaignRules.TotalLevel(loose), Is.InRange(17, 20));
            }
            Assert.That(CampaignRules.ChooseOpponentDeck(pool, 4, 3, 1), Is.Null);
            Assert.That(CampaignRules.ChooseOpponentDeck(new[] { "glimmouse", "dustwisp" }, 50, 3, 1), Is.Null);
        }

        [Test]
        public void MatchSeedChangesWithEachGamePlayed()
        {
            var s = CampaignRules.NewGame(3);
            long a = CampaignRules.MatchSeed(s, Apprentice);
            Assert.That(CampaignRules.MatchSeed(s, Apprentice), Is.EqualTo(a));
            Assert.That(CampaignRules.MatchSeed(s, HarborBoss), Is.Not.EqualTo(a));
            Assert.That(CampaignRules.MatchSeed(CampaignRules.NewGame(4), Apprentice), Is.Not.EqualTo(a));
            var deck = CampaignRules.ChooseOpponentDeck(s, Apprentice, a);
            var after = CampaignRules.ApplyMatch(s, Apprentice, CampaignRules.AutoDeck(s, Harbor), deck,
                new MatchOutcome(MatchResultKind.Draw, false), null).State;
            Assert.That(CampaignRules.MatchSeed(after, Apprentice), Is.Not.EqualTo(a));
        }

        [Test]
        public void MatchConfigUsesTheRegionRulesAndGamePivotTokens()
        {
            var gate = CampaignData.Regions[3];
            var cfg = CampaignRules.MatchConfig(gate.Opponents[0], new[] { "leomane", "draconyx", "stormgear", "auroraveil", "forgecolossus" },
                new[] { "leomane", "draconyx", "cygnwraith", "taurocrag", "gravelith" }, 9);
            Assert.That(cfg.Rules, Is.EqualTo(gate.Rules));
            Assert.That((cfg.PivotTokensFirst, cfg.PivotTokensSecond), Is.EqualTo(GameConstants.MatchPivotTokens(gate.Rules)));
            // 両者が同じカードを使ってもエンジンは対局できる
            var state = GameEngine.CreateGame(cfg);
            Assert.That(state.Phase, Is.EqualTo(Phase.Playing));
        }

        [Test]
        public void BossMatchesAddSuddenDeathAndRegularMatchesDoNot()
        {
            var deck = new[] { "glimmouse", "dustwisp", "coglet", "scalefry", "pyxmoth" };
            foreach (var region in CampaignData.Regions)
            {
                foreach (var o in region.Opponents)
                {
                    var cfg = CampaignRules.MatchConfig(o, deck, deck, 1);
                    Assert.That(CampaignRules.MatchRules(o), Is.EqualTo(cfg.Rules));
                    Assert.That(cfg.Rules.SuddenDeath, Is.EqualTo(o.IsBoss || region.Rules.SuddenDeath), o.Id);
                    // Sudden Death 以外は星域のルールのまま
                    var expected = new RuleSet(region.Rules.Open, region.Rules.Random, region.Rules.Same, region.Rules.SameWall, region.Rules.Plus,
                        region.Rules.Combo, region.Rules.Elemental, cfg.Rules.SuddenDeath, region.Rules.Pivot);
                    Assert.That(cfg.Rules, Is.EqualTo(expected), o.Id);
                    Assert.That((cfg.PivotTokensFirst, cfg.PivotTokensSecond), Is.EqualTo(GameConstants.MatchPivotTokens(cfg.Rules)), o.Id);
                    if (!o.IsBoss) Assert.That(cfg.Rules, Is.EqualTo(region.Rules), o.Id);
                }
            }
            // 星域 1〜3 の主は SD が足され、星域 4 の主はもともと SD（Pivot トークンは 1/1）
            Assert.That(CampaignRules.MatchRules(CampaignData.Regions[0].Boss).SuddenDeath, Is.True);
            Assert.That(CampaignData.Regions[0].Rules.SuddenDeath, Is.False);
            var elder = CampaignRules.MatchConfig(CampaignData.Regions[3].Boss, deck, deck, 1);
            Assert.That((elder.PivotTokensFirst, elder.PivotTokensSecond), Is.EqualTo((1, 1)));
        }

        // ------------------------------------------------------------------
        // 判定と報酬
        // ------------------------------------------------------------------

        static GameState Ended(int p0Board, int p1Board, int p0Hand, int p1Hand)
        {
            var c = CardCatalog.ById["gearowl"];
            var board = new PlacedCard?[9];
            for (int i = 0; i < 9; i++) board[i] = new PlacedCard(c, i < p0Board ? Player.P0 : Player.P1, 0);
            var cards = new Dictionary<string, CardDef> { ["gearowl"] = c };
            return GameState.FromParts(RuleSet.None, cards, board, new Element?[9], Enumerable.Repeat("gearowl", p0Hand).ToArray(),
                Enumerable.Repeat("gearowl", p1Hand).ToArray(), Player.P0, Player.P0, 0, 0, 1, Phase.Ended, 0);
        }

        [Test]
        public void JudgeFindsWinsPerfectWinsLossesAndDraws()
        {
            var win = CampaignRules.Judge(Ended(6, 3, 0, 1), Player.P0);
            Assert.That((win.Kind, win.Perfect, win.RewardCount), Is.EqualTo((MatchResultKind.Win, false, 1)));
            var perfect = CampaignRules.Judge(Ended(9, 0, 0, 1), Player.P0);
            Assert.That((perfect.Kind, perfect.Perfect, perfect.RewardCount), Is.EqualTo((MatchResultKind.Win, true, 2)));
            var loss = CampaignRules.Judge(Ended(9, 0, 0, 1), Player.P1);
            Assert.That((loss.Kind, loss.Perfect, loss.RewardCount), Is.EqualTo((MatchResultKind.Loss, false, 0)));
            var draw = CampaignRules.Judge(Ended(5, 4, 0, 1), Player.P0);
            Assert.That((draw.Kind, draw.RewardCount), Is.EqualTo((MatchResultKind.Draw, 0)));
            var p1Perfect = CampaignRules.Judge(Ended(0, 9, 1, 0), Player.P1);
            Assert.That(p1Perfect.Perfect, Is.True);
            Assert.Throws<ArgumentException>(() => CampaignRules.Judge(Ended(5, 4, 0, 1).WithPhase(Phase.Playing), Player.P0));
        }

        [Test]
        public void WinningAddsThePickedCardsAndRecordsTheMatch()
        {
            var s = CampaignRules.NewGame();
            var mine = CampaignRules.AutoDeck(s, Harbor);
            var theirs = new[] { "glimmouse", "coglet", "emberkit", "bolthare", "frostnewt" };
            var res = CampaignRules.ApplyMatch(s, Apprentice, mine, theirs, new MatchOutcome(MatchResultKind.Win, false), new[] { "bolthare" });
            var n = res.State;
            Assert.That(n.Count("bolthare"), Is.EqualTo(1));
            Assert.That(n.TotalCards, Is.EqualTo(8));
            Assert.That(n.Record(Apprentice.Id).Wins, Is.EqualTo(1));
            Assert.That(n.Record(Apprentice.Id).Seen, Is.EquivalentTo(theirs));
            Assert.That(n.LastDecks["harbor"], Is.EqualTo(mine));
            Assert.That(res.TakenCard, Is.Null);
            Assert.That(res.UnlockedRegion, Is.Null);
            Assert.That(s.TotalCards, Is.EqualTo(7), "the input state is unchanged");

            // 同じカードを 2 枚目として受け取れる（枚数を数える）
            var again = CampaignRules.ApplyMatch(n, Apprentice, mine, theirs, new MatchOutcome(MatchResultKind.Win, true),
                new[] { "glimmouse", "bolthare" }).State;
            Assert.That(again.Count("glimmouse"), Is.EqualTo(2));
            Assert.That(again.Count("bolthare"), Is.EqualTo(2));

            // 見たカードは「？」でなくなる
            var visible = CampaignRules.VisibleOpponentCards(n, Apprentice);
            Assert.That(visible.Length, Is.EqualTo(Apprentice.Cards.Count));
            for (int i = 0; i < visible.Length; i++)
                Assert.That(visible[i], Is.EqualTo(theirs.Contains(Apprentice.Cards[i]) ? Apprentice.Cards[i] : null));
            Assert.That(CampaignRules.VisibleOpponentCards(s, Apprentice).All(x => x == null), Is.True);
        }

        [Test]
        public void InvalidPicksAreRejected()
        {
            var s = CampaignRules.NewGame();
            var mine = CampaignRules.AutoDeck(s, Harbor);
            var theirs = new[] { "glimmouse", "coglet", "emberkit", "bolthare", "frostnewt" };
            var win = new MatchOutcome(MatchResultKind.Win, false);
            Assert.Throws<ArgumentException>(() => CampaignRules.ApplyMatch(s, Apprentice, mine, theirs, win, new[] { "leomane" }));
            Assert.Throws<ArgumentException>(() => CampaignRules.ApplyMatch(s, Apprentice, mine, theirs, win, new[] { "coglet", "emberkit" }));
            Assert.Throws<ArgumentException>(() =>
                CampaignRules.ApplyMatch(s, Apprentice, mine, theirs, new MatchOutcome(MatchResultKind.Win, true), new[] { "coglet", "coglet" }));
            Assert.Throws<ArgumentException>(() =>
                CampaignRules.ApplyMatch(s, Apprentice, mine, theirs, new MatchOutcome(MatchResultKind.Loss, false), new[] { "coglet" }));
        }

        [Test]
        public void LosingCostsNothingByDefault()
        {
            var s = CampaignRules.NewGame();
            var mine = CampaignRules.AutoDeck(s, Harbor);
            var res = CampaignRules.ApplyMatch(s, HarborBoss, mine, CampaignRules.ChooseOpponentDeck(s, HarborBoss, 1),
                new MatchOutcome(MatchResultKind.Loss, false), null);
            Assert.That(res.State.Collection, Is.EquivalentTo(s.Collection));
            Assert.That(res.TakenCard, Is.Null);
            Assert.That(res.State.Record(HarborBoss.Id).Losses, Is.EqualTo(1));
            var draw = CampaignRules.ApplyMatch(res.State, HarborBoss, mine, CampaignRules.ChooseOpponentDeck(s, HarborBoss, 2),
                new MatchOutcome(MatchResultKind.Draw, false), null).State;
            Assert.That(draw.Record(HarborBoss.Id).Draws, Is.EqualTo(1));
            Assert.That(draw.Collection, Is.EquivalentTo(s.Collection));
        }

        [Test]
        public void ClassicModeLossGivesTheHighestLevelCardToTheOpponentAndWinningTakesItBack()
        {
            var s = CampaignRules.NewGame(classicMode: true)
                .WithCollection(Col("glimmouse", "dustwisp", "coglet", "scalefry", "pyxmoth", "rivetcrab", "mirelamp", "leomane"));
            var mine = new[] { "glimmouse", "pyxmoth", "leomane", "coglet", "rivetcrab" };
            var theirs = CampaignRules.ChooseOpponentDeck(s, HarborBoss, 5);
            var res = CampaignRules.ApplyMatch(s, HarborBoss, mine, theirs, new MatchOutcome(MatchResultKind.Loss, false), null);
            Assert.That(res.TakenCard, Is.EqualTo("leomane"));
            Assert.That(res.State.Count("leomane"), Is.Zero);
            Assert.That(res.State.Record(HarborBoss.Id).Captured["leomane"], Is.EqualTo(1));
            Assert.That(HarborBoss.Cards, Does.Not.Contain("leomane"));
            // 取ったカードは相手の持ちカードに加わり、デッキに入りうる
            Assert.That(CampaignRules.OpponentPool(res.State, HarborBoss), Does.Contain("leomane"));

            // 勝って取り返す
            var deckWithIt = new[] { "leomane", "bolthare", "cinderdrake", "velahound", "clockwyrm" };
            var back = CampaignRules.ApplyMatch(res.State, HarborBoss, new[] { "glimmouse", "pyxmoth", "dustwisp", "coglet", "rivetcrab" },
                deckWithIt, new MatchOutcome(MatchResultKind.Win, false), new[] { "leomane" }).State;
            Assert.That(back.Count("leomane"), Is.EqualTo(1));
            Assert.That(back.Record(HarborBoss.Id).Captured.ContainsKey("leomane"), Is.False);
            Assert.That(CampaignRules.OpponentPool(back, HarborBoss), Does.Not.Contain("leomane"));
        }

        [Test]
        public void ClassicModeTieBreaksBySideTotalThenDeckOrder()
        {
            var col = Col("glimmouse", "dustwisp", "coglet", "emberkit", "scalefry", "pyxmoth", "rivetcrab");
            // Lv2 の pyxmoth (13) と rivetcrab (14) → 辺の合計が大きい rivetcrab
            Assert.That(CampaignRules.ClassicTake(col, new[] { "glimmouse", "pyxmoth", "rivetcrab", "coglet", "dustwisp" }, out _),
                Is.EqualTo("rivetcrab"));
            // 同じ Lv・同じ合計なら前のもの: coglet 2.3.3.4 = 12、emberkit 4.2.1.5 = 12
            Assert.That(CampaignRules.ClassicTake(col, new[] { "emberkit", "coglet", "glimmouse", "dustwisp", "scalefry" }, out _),
                Is.EqualTo("scalefry"), "scalefry 6.1.2.4 = 13 is the largest Lv1");
            Assert.That(CampaignRules.ClassicTake(col, new[] { "emberkit", "coglet", "glimmouse" }, out _), Is.EqualTo("emberkit"));
        }

        [Test]
        public void ClassicModeNeverLeavesFewerThanFiveKindsOfCards()
        {
            var five = Col("glimmouse", "dustwisp", "coglet", "scalefry", "lynxlight");
            var s = CampaignRules.NewGame(classicMode: true).WithCollection(five);
            var mine = new[] { "glimmouse", "dustwisp", "coglet", "scalefry", "lynxlight" };
            var res = CampaignRules.ApplyMatch(s, HarborBoss, mine, CampaignRules.ChooseOpponentDeck(s, HarborBoss, 1),
                new MatchOutcome(MatchResultKind.Loss, false), null);
            Assert.That(res.TakenCard, Is.Null);
            Assert.That(res.ProtectedCard, Is.EqualTo("lynxlight"));
            Assert.That(res.State.Collection, Is.EquivalentTo(five));
            Assert.That(res.State.Record(HarborBoss.Id).Losses, Is.EqualTo(1));

            // 2 枚持っているカードなら、取られても 5 種類は残る
            var withCopy = new Dictionary<string, int>(five) { ["lynxlight"] = 2 };
            var s2 = s.WithCollection(withCopy);
            var res2 = CampaignRules.ApplyMatch(s2, HarborBoss, mine, CampaignRules.ChooseOpponentDeck(s2, HarborBoss, 1),
                new MatchOutcome(MatchResultKind.Loss, false), null);
            Assert.That(res2.TakenCard, Is.EqualTo("lynxlight"));
            Assert.That(res2.State.Count("lynxlight"), Is.EqualTo(1));

            // 6 種類なら 1 枚取られて 5 種類
            var six = new Dictionary<string, int>(five) { ["pyxmoth"] = 1 };
            var res3 = CampaignRules.ApplyMatch(s.WithCollection(six), HarborBoss, mine, CampaignRules.ChooseOpponentDeck(s, HarborBoss, 1),
                new MatchOutcome(MatchResultKind.Loss, false), null);
            Assert.That(res3.TakenCard, Is.EqualTo("lynxlight"));
            Assert.That(res3.State.Collection.Count, Is.EqualTo(5));
        }

        // ------------------------------------------------------------------
        // 解放
        // ------------------------------------------------------------------

        [Test]
        public void BeatingABossUnlocksTheNextRegionOnce()
        {
            var s = CampaignRules.NewGame();
            var mine = CampaignRules.AutoDeck(s, Harbor);
            var theirs = CampaignRules.ChooseOpponentDeck(s, HarborBoss, 1);

            // 主以外に勝っても開かない
            var afterRegular = CampaignRules.ApplyMatch(s, Apprentice, mine, CampaignRules.ChooseOpponentDeck(s, Apprentice, 1),
                new MatchOutcome(MatchResultKind.Win, false), null);
            Assert.That(afterRegular.UnlockedRegion, Is.Null);
            Assert.That(afterRegular.State.UnlockedRegions, Is.EqualTo(new[] { "harbor" }));

            // 主に負けても開かない
            var lost = CampaignRules.ApplyMatch(s, HarborBoss, mine, theirs, new MatchOutcome(MatchResultKind.Loss, false), null);
            Assert.That(lost.State.UnlockedRegions, Is.EqualTo(new[] { "harbor" }));

            var won = CampaignRules.ApplyMatch(lost.State, HarborBoss, mine, theirs, new MatchOutcome(MatchResultKind.Win, false),
                new[] { theirs[0] });
            Assert.That(won.UnlockedRegion?.Id, Is.EqualTo("library"));
            Assert.That(won.ClearedRegion, Is.True);
            Assert.That(won.State.UnlockedRegions, Is.EqualTo(new[] { "harbor", "library" }));
            Assert.That(CampaignRules.CanChallenge(won.State, CampaignData.Regions[1].Boss), Is.True);

            var again = CampaignRules.ApplyMatch(won.State, HarborBoss, mine, theirs, new MatchOutcome(MatchResultKind.Win, false), null);
            Assert.That(again.UnlockedRegion, Is.Null);
            Assert.That(again.ClearedRegion, Is.False);
            Assert.That(again.State.UnlockedRegions, Is.EqualTo(new[] { "harbor", "library" }));

            // 最後の主に勝っても次はない
            var lastBoss = CampaignData.Regions[3].Boss;
            var all = s.WithUnlocked(CampaignData.Regions.Select(r => r.Id));
            var fin = CampaignRules.ApplyMatch(all, lastBoss, null, CampaignRules.ChooseOpponentDeck(all, lastBoss, 1),
                new MatchOutcome(MatchResultKind.Win, false), null);
            Assert.That(fin.UnlockedRegion, Is.Null);
            Assert.That(fin.ClearedRegion, Is.True);
        }
    }
}
