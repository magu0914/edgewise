using System;
using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.AI;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using NUnit.Framework;

namespace Edgewise.Core.Tests.AI
{
    /// <summary>AI の性格（DESIGN §12-5）と、星図巡り用の決定化（§12-6）のテスト。</summary>
    [TestFixture]
    public class PersonalityTests
    {
        static readonly AiProfile SmallNormal = AiProfile.Normal.WithMaxNodes(8000);

        // ------------------------------------------------------------------
        // 性格なし ＝ 従来どおり
        // ------------------------------------------------------------------

        [Test]
        public void NeutralPersonalityChangesNothing()
        {
            var profiles = new[] { AiProfile.Easy, SmallNormal, AiProfile.Hard.WithMaxNodes(20000) };
            for (int i = 0; i < 150; i++)
            {
                var r = new TestRandom(0x7e57 + i);
                var s = AiH.RandomPosition(r, r.NextInt(8));
                if (s == null) continue;
                foreach (var p in profiles)
                {
                    foreach (var tie in new[] { false, true })
                    {
                        var a = new SearchSession(s, s.Turn, p, i, AiH.Pool);
                        var b = new SearchSession(s, s.Turn, p.WithPersonality(AiPersonality.Neutral, tie), i, AiH.Pool);
                        Assert.That(b.Run(), Is.EqualTo(a.Run()));
                        Assert.That(b.NodesSearched, Is.EqualTo(a.NodesSearched));
                        Assert.That(b.GetEvaluations().Select(e => e.Value), Is.EqualTo(a.GetEvaluations().Select(e => e.Value)));
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 性格が手に表れる
        // ------------------------------------------------------------------

        static bool TriggersRule(GameState s, Move m, RuleHeading heading) =>
            GameEngine.ApplyMove(s, m).Events.OfType<RuleEvent>().Any(e => heading == RuleHeading.Plus ? e.Rule == RuleHeading.Plus : e.Rule != RuleHeading.Plus);

        static bool OnMatchingElement(GameState s, Move m)
        {
            var e = s.CellElements[m.Cell];
            return e.HasValue && s.Cards[s.GetHand(s.Turn)[m.HandIndex]].Element == e.Value;
        }

        static bool IsCorner(Move m) => m.Cell == 0 || m.Cell == 2 || m.Cell == 6 || m.Cell == 8;

        /// <summary>
        /// 性格のある／ない AI が、その性格が好む手（predicate）を選んだ割合。好む手が少なくとも 1 つあり、全部ではない局面だけを数える。
        /// </summary>
        static (double Base, double Persona, int Positions) Rates(AiProfile baseProfile, AiPersonality personality, Func<TestRandom, GameState> make,
            Func<GameState, Move, bool> predicate, int tries, long seed)
        {
            int n = 0, hitBase = 0, hitPersona = 0;
            var persona = baseProfile.WithPersonality(personality, false);
            for (int i = 0; i < tries; i++)
            {
                var r = new TestRandom(seed + i);
                var s = make(r);
                if (s == null) continue;
                var legal = GameEngine.LegalMoves(s);
                int good = legal.Count(m => predicate(s, m));
                if (good == 0 || good == legal.Count) continue;
                n++;
                if (predicate(s, SearchSession.ChooseMove(s, s.Turn, baseProfile, i, AiH.Pool))) hitBase++;
                if (predicate(s, SearchSession.ChooseMove(s, s.Turn, persona, i, AiH.Pool))) hitPersona++;
            }
            return (n == 0 ? 0 : (double)hitBase / n, n == 0 ? 0 : (double)hitPersona / n, n);
        }

        static void Report(string name, (double Base, double Persona, int Positions) x) =>
            TestContext.Out.WriteLine("[personality] " + name + ": base " + (100 * x.Base).ToString("0.0") + "% -> persona "
                + (100 * x.Persona).ToString("0.0") + "% (" + x.Positions + " positions)");

        static GameState Position(TestRandom r, bool same = false, bool plus = false, bool elemental = false, bool pivot = false)
        {
            var rules = new RuleSet(open: true, same: same, sameWall: same, plus: plus, elemental: elemental, pivot: pivot);
            var state = GameEngine.CreateGame(H.RandomConfig(r, rules));
            int plies = 1 + r.NextInt(5);
            for (int i = 0; i < plies; i++) state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
            return state.Phase == Phase.Playing ? state : null;
        }

        [Test]
        public void SameHunterPicksSameMovesMoreOften()
        {
            var x = Rates(SmallNormal, CampaignPersonalities.SameHunter, r => Position(r, same: true),
                (s, m) => TriggersRule(s, m, RuleHeading.Same), 2000, 0x5a3e);
            Report("same-hunter (Normal, Same+Same Wall)", x);
            Assert.That(x.Positions, Is.GreaterThan(100));
            Assert.That(x.Persona, Is.GreaterThan(x.Base + 0.15));
        }

        [Test]
        public void PlusHunterPicksPlusMovesMoreOften()
        {
            var x = Rates(SmallNormal, CampaignPersonalities.PlusHunter, r => Position(r, plus: true),
                (s, m) => TriggersRule(s, m, RuleHeading.Plus), 500, 0x9105);
            Report("plus-hunter (Normal, Plus)", x);
            Assert.That(x.Positions, Is.GreaterThan(100));
            Assert.That(x.Persona, Is.GreaterThan(x.Base + 0.15));
        }

        [Test]
        public void CornerGuardPrefersCorners()
        {
            var x = Rates(AiProfile.Easy, CampaignPersonalities.CornerGuard, r => Position(r), (s, m) => IsCorner(m), 400, 0xc0e7);
            Report("corner-guard (Easy, Basic)", x);
            Assert.That(x.Persona, Is.GreaterThan(x.Base + 0.15));
        }

        [Test]
        public void ElementReaderPrefersMatchingCells()
        {
            var x = Rates(SmallNormal, CampaignPersonalities.ElementReader, r => Position(r, elemental: true), OnMatchingElement, 500, 0xe1e3);
            Report("element-reader (Normal, Elemental)", x);
            Assert.That(x.Positions, Is.GreaterThan(80));
            Assert.That(x.Persona, Is.GreaterThan(x.Base + 0.15));
        }

        [Test]
        public void EarlyPivotRotatesMoreOften()
        {
            var x = Rates(SmallNormal, CampaignPersonalities.EarlyPivot, r => Position(r, pivot: true), (s, m) => m.Rotation != 0, 300, 0x9170);
            Report("early-pivot (Normal, Pivot)", x);
            Assert.That(x.Persona, Is.GreaterThan(x.Base + 0.15));
        }

        [Test]
        public void GreedyTakesMoreCardsRightAway()
        {
            int Flips(GameState s, Move m) => GameEngine.ApplyMove(s, m).Events.OfType<FlipEvent>().Count();
            long baseFlips = 0, personaFlips = 0;
            int n = 0;
            var persona = AiProfile.Easy.WithPersonality(CampaignPersonalities.Greedy);
            for (int i = 0; i < 400; i++)
            {
                var s = Position(new TestRandom(0x63ee + i));
                if (s == null) continue;
                n++;
                baseFlips += Flips(s, SearchSession.ChooseMove(s, s.Turn, AiProfile.Easy, i, AiH.Pool));
                personaFlips += Flips(s, SearchSession.ChooseMove(s, s.Turn, persona, i, AiH.Pool));
            }
            TestContext.Out.WriteLine("[personality] greedy (Easy, Basic): flips per move " + ((double)baseFlips / n).ToString("0.00") + " -> "
                + ((double)personaFlips / n).ToString("0.00") + " (" + n + " positions)");
            Assert.That(personaFlips, Is.GreaterThan(baseFlips));
        }

        [Test]
        public void FickleVariesMoreWithTheSeed()
        {
            // 揺らぎ: 同じ局面で seed を変えたときに選ぶ手の種類が増える
            var hm = CampaignAi.HardMinus.WithMaxNodes(6000);
            var fickle = hm.WithPersonality(CampaignPersonalities.Fickle);
            int distinctBase = 0, distinctFickle = 0;
            for (int i = 0; i < 40; i++)
            {
                var s = Position(new TestRandom(0xf1c + i));
                if (s == null) continue;
                distinctBase += Enumerable.Range(0, 8).Select(seed => SearchSession.ChooseMove(s, s.Turn, hm, seed, AiH.Pool)).Distinct().Count();
                distinctFickle += Enumerable.Range(0, 8).Select(seed => SearchSession.ChooseMove(s, s.Turn, fickle, seed, AiH.Pool)).Distinct().Count();
            }
            TestContext.Out.WriteLine("[personality] fickle (Hard-): distinct moves over 8 seeds, summed " + distinctBase + " -> " + distinctFickle);
            Assert.That(distinctFickle, Is.GreaterThan(distinctBase));
        }

        [Test]
        public void HardOnlyUsesThePersonalityBetweenEquallyGoodMoves()
        {
            // Hard（PersonalityAsTieBreak）は性格があっても読み切った値が最善の手しか選ばない。それでも同点の手の間では好みが出る
            var hard = AiProfile.Hard.WithMaxNodes(200000);
            var guardian = hard.WithPersonality(CampaignPersonalities.Guardian);
            Assert.That(guardian.PersonalityAsTieBreak, Is.True);
            int differs = 0, checkedPositions = 0;
            for (int i = 0; i < 120; i++)
            {
                var r = new TestRandom(0x6a2d + i);
                var s = AiH.RandomPosition(r, 4 + r.NextInt(3), open: true, suddenDeath: false);
                if (s == null) continue;
                var evals = SearchSession.AnalyzeMoves(s, s.Turn, 2000000, null, i);
                if (!evals.All(e => e.Solved)) continue;
                int best = evals.Max(e => e.Value);
                if (best < 0) continue; // 負けの局面では TieBreak が「相手が間違えやすい負け方」を選ぶので値は最善とは限らない（従来どおり）
                var m = SearchSession.ChooseMove(s, s.Turn, guardian, i);
                var plain = SearchSession.ChooseMove(s, s.Turn, hard, i);
                Assert.That(evals.First(e => e.Move == m).Value, Is.EqualTo(best), StateHash.Serialize(s));
                Assert.That(evals.First(e => e.Move == plain).Value, Is.EqualTo(best));
                if (m != plain) differs++;
                checkedPositions++;
            }
            TestContext.Out.WriteLine("[personality] guardian (Hard, tie-break only): " + differs + "/" + checkedPositions
                + " solved positions chose a different move of the same value");
            Assert.That(checkedPositions, Is.GreaterThan(50));
            Assert.That(differs, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------
        // 決定論
        // ------------------------------------------------------------------

        static readonly AiProfile[] PersonaProfiles =
        {
            AiProfile.Easy.WithPersonality(CampaignPersonalities.Greedy),
            CampaignAi.EasyPlus.WithPersonality(CampaignPersonalities.CornerGuard),
            SmallNormal.WithPersonality(CampaignPersonalities.SameHunter),
            CampaignAi.NormalPlus.WithMaxNodes(10000).WithPersonality(CampaignPersonalities.ChainSeeker),
            CampaignAi.HardMinus.WithMaxNodes(15000).WithPersonality(CampaignPersonalities.Fickle),
            CampaignAi.HardMinus.WithMaxNodes(15000).WithPersonality(CampaignPersonalities.EarlyPivot),
            AiProfile.Hard.WithMaxNodes(20000).WithPersonality(CampaignPersonalities.Guardian),
        };

        [Test]
        public void PersonalitiesStayDeterministicRegardlessOfSlicing()
        {
            for (int i = 0; i < 70; i++)
            {
                var r = new TestRandom(0xde70 + i);
                var s = AiH.RandomPosition(r, r.NextInt(7));
                if (s == null) continue;
                var p = PersonaProfiles[i % PersonaProfiles.Length];
                foreach (var model in new[] { CardPoolModel.Distinct, CardPoolModel.Shared })
                {
                    Move? first = null;
                    long nodes = 0;
                    foreach (int slice in new[] { 1 << 30, 1, 53, 4000 })
                    {
                        var session = new SearchSession(H.Clone(s), s.Turn, p, 42 + i, AiH.Pool, null, model);
                        while (!session.Step(slice)) { }
                        Assert.That(GameEngine.IsLegalMove(s, session.BestMove), Is.True);
                        if (first == null)
                        {
                            first = session.BestMove;
                            nodes = session.NodesSearched;
                        }
                        Assert.That(session.BestMove, Is.EqualTo(first.Value), p.Name + " slice " + slice);
                        Assert.That(session.NodesSearched, Is.EqualTo(nodes));
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 星図巡りの決定化（同じカードを両者が持ちうる）とズル禁止
        // ------------------------------------------------------------------

        /// <summary>両者のデッキが重なる対局（星図巡りのように同じカードを両者が持つ）を、ランダムに plies 手進めた局面。</summary>
        static GameState SharedPosition(TestRandom r, int plies, RuleSet rules)
        {
            var all = CardCatalog.All;
            var deck0 = new List<string>();
            while (deck0.Count < 5)
            {
                var id = r.Pick(all).Id;
                if (!deck0.Contains(id)) deck0.Add(id);
            }
            var deck1 = new List<string>();
            int shared = 1 + r.NextInt(4);
            for (int i = 0; i < shared; i++) deck1.Add(deck0[i]);
            while (deck1.Count < 5)
            {
                var id = r.Pick(all).Id;
                if (!deck1.Contains(id)) deck1.Add(id);
            }
            var state = GameEngine.CreateGame(new GameConfig(rules, deck0, deck1, r.NextUInt32(), CardCatalog.ById, 1, 2));
            for (int i = 0; i < plies && state.Phase == Phase.Playing; i++)
                state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
            return state.Phase == Phase.Playing && state.SuddenDeathRound == 0 ? state : null;
        }

        /// <summary>
        /// 相手の手札だけを、星図巡りの候補（全カードから相手が盤に置いたカードを除いたもの。自分の手札のカードも含む）から引き直した局面。
        /// 少なくとも 1 枚は自分の手札と同じカードにする（重複カードありの条件）。
        /// </summary>
        static GameState WithOtherSharedOpponentHand(GameState s, Player viewer, TestRandom r)
        {
            var opp = viewer.Opponent();
            var placedByOpp = s.Board.Where(pc => pc.HasValue && pc.Value.PlacedBy == opp).Select(pc => pc.Value.CardId).ToList();
            var candidates = CardCatalog.All.Select(c => c.Id).Where(id => !placedByOpp.Contains(id)).ToList();
            int count = s.GetHand(opp).Count;
            var old = s.GetHand(opp).ToArray();
            string[] hand;
            for (int attempt = 0; ; attempt++)
            {
                var list = new List<string>();
                if (attempt < 20) list.Add(r.Pick(s.GetHand(viewer).ToArray()));
                while (list.Count < count)
                {
                    var id = r.Pick(candidates);
                    if (!list.Contains(id)) list.Add(id);
                }
                hand = list.ToArray();
                if (!hand.SequenceEqual(old)) break;
            }

            var cards = new Dictionary<string, CardDef>();
            foreach (var pc in s.Board)
            {
                if (pc.HasValue) cards[pc.Value.CardId] = pc.Value.Card;
            }
            foreach (var id in s.GetHand(viewer)) cards[id] = s.Cards[id];
            foreach (var id in hand) cards[id] = CardCatalog.ById[id];
            var hand0 = viewer == Player.P0 ? s.GetHand(Player.P0).ToArray() : hand;
            var hand1 = viewer == Player.P1 ? s.GetHand(Player.P1).ToArray() : hand;
            return GameState.FromParts(s.Rules, cards, s.Board.ToArray(), s.CellElements.ToArray(), hand0, hand1, s.Turn, s.FirstPlayer,
                s.GetPivotTokens(Player.P0), s.GetPivotTokens(Player.P1), s.RngState, s.Phase, s.SuddenDeathRound);
        }

        [Test]
        public void SharedModelChoosesTheSameMoveWhateverTheOpponentReallyHoldsEvenWithDuplicateCards()
        {
            var profiles = PersonaProfiles.Concat(new[] { AiProfile.Easy, SmallNormal, AiProfile.Hard.WithMaxNodes(20000) }).ToArray();
            var rulesList = CampaignData.Regions.Select(x => x.Rules.Open ? new RuleSet(false, false, x.Rules.Same, x.Rules.SameWall, x.Rules.Plus,
                x.Rules.Combo, x.Rules.Elemental, x.Rules.SuddenDeath, x.Rules.Pivot) : x.Rules).ToArray();
            int cases = 0, duplicateCases = 0;
            for (int i = 0; i < 400; i++)
            {
                var r = new TestRandom(0x5ead + i);
                var s = SharedPosition(r, r.NextInt(8), rulesList[i % rulesList.Length]);
                if (s == null || s.GetHand(s.Turn.Opponent()).Count == 0) continue;
                var me = s.Turn;
                var other = WithOtherSharedOpponentHand(s, me, r);
                if (s.GetHand(me).Intersect(s.GetHand(me.Opponent())).Any() || other.GetHand(me).Intersect(other.GetHand(me.Opponent())).Any())
                    duplicateCases++;
                var p = profiles[i % profiles.Length];
                // 候補は全カード、または相手の公開された持ちカード（見えている情報）
                IReadOnlyList<CardDef> pool = i % 2 == 0 ? CardCatalog.All : CampaignData.Opponents[i % 12].Cards.Select(id => CardCatalog.ById[id])
                    .Concat(s.GetHand(me).Select(id => CardCatalog.ById[id])).ToList();
                var a = new SearchSession(s, me, p, i, pool, null, CardPoolModel.Shared);
                var b = new SearchSession(other, me, p, i, pool, null, CardPoolModel.Shared);
                Assert.That(b.Run(), Is.EqualTo(a.Run()), p.Name + " peeked at the hidden hand in " + StateHash.Serialize(s));
                Assert.That(b.NodesSearched, Is.EqualTo(a.NodesSearched));
                Assert.That(b.GetEvaluations().Select(e => e.Value), Is.EqualTo(a.GetEvaluations().Select(e => e.Value)));
                var ha = SearchSession.AnalyzeMoves(s, me, 5000, pool, i, null, CardPoolModel.Shared);
                var hb = SearchSession.AnalyzeMoves(other, me, 5000, pool, i, null, CardPoolModel.Shared);
                Assert.That(hb.Select(e => e.Value), Is.EqualTo(ha.Select(e => e.Value)));
                cases++;
            }
            TestContext.Out.WriteLine("[determinization] shared no-peek: " + cases + " positions, " + duplicateCases + " with a card held by both");
            Assert.That(cases, Is.GreaterThan(300));
            Assert.That(duplicateCases, Is.GreaterThan(250));
        }

        [Test]
        public void SharedCandidatesExcludeOnlyCardsTheOpponentPlaced()
        {
            for (int i = 0; i < 200; i++)
            {
                var r = new TestRandom(0xca9d + i);
                var s = SharedPosition(r, 1 + r.NextInt(6), new RuleSet(same: true, plus: true, combo: true));
                if (s == null) continue;
                var me = s.Turn;
                var opp = me.Opponent();
                var shared = SearchSession.UnseenCardsShared(s, me, CardCatalog.All).Select(c => c.Id).ToList();
                var oppPlaced = s.Board.Where(pc => pc.HasValue && pc.Value.PlacedBy == opp).Select(pc => pc.Value.CardId).ToList();
                var mePlaced = s.Board.Where(pc => pc.HasValue && pc.Value.PlacedBy == me).Select(pc => pc.Value.CardId).ToList();
                Assert.That(shared.Count, Is.EqualTo(50 - oppPlaced.Count));
                Assert.That(shared.Intersect(oppPlaced), Is.Empty);
                // 自分の手札・自分が置いたカードは候補に残る（相手も同じカードを持ちうる）
                foreach (var id in s.GetHand(me).Concat(mePlaced).Where(id => !oppPlaced.Contains(id))) Assert.That(shared, Does.Contain(id));
                // 従来（Distinct）は自分の手札と盤上のカードをすべて除く
                var distinct = SearchSession.UnseenCards(s, me, CardCatalog.All).Select(c => c.Id).ToList();
                Assert.That(distinct.Intersect(s.GetHand(me)), Is.Empty);
                Assert.That(distinct.Intersect(mePlaced), Is.Empty);
            }
        }

        [Test]
        public void PlacedByIsKeptThroughFlipsAndIsNotPartOfTheStateIdentity()
        {
            for (int g = 0; g < 100; g++)
            {
                var r = new TestRandom(0x91ac + g);
                var state = H.NewRandomGame(r, suddenDeath: false);
                var placer = new Player?[9];
                while (state.Phase == Phase.Playing)
                {
                    var move = r.Pick(GameEngine.LegalMoves(state));
                    var slow = GameEngine.ApplyMove(state, move).State;
                    var fast = GameEngine.ApplyMoveFast(state, move);
                    placer[move.Cell] = state.Turn;
                    for (int c = 0; c < 9; c++)
                    {
                        if (!slow.Board[c].HasValue) continue;
                        Assert.That(slow.Board[c].Value.PlacedBy, Is.EqualTo(placer[c].Value));
                        Assert.That(fast.Board[c].Value.PlacedBy, Is.EqualTo(placer[c].Value));
                    }
                    state = slow;
                }
            }
            var card = CardCatalog.All[0];
            Assert.That(new PlacedCard(card, Player.P0, 0, Player.P1), Is.EqualTo(new PlacedCard(card, Player.P0, 0)));
            Assert.That(new PlacedCard(card, Player.P1, 2).PlacedBy, Is.EqualTo(Player.P1));
            Assert.That(new PlacedCard(card, Player.P1, 2, Player.P0).WithOwner(Player.P0).PlacedBy, Is.EqualTo(Player.P0));
        }
    }
}
