using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.AI;
using NUnit.Framework;

namespace Edgewise.Core.Tests.AI
{
    [TestFixture]
    public class SearchSessionTests
    {
        static readonly AiProfile[] TinyProfiles =
        {
            AiProfile.Easy.WithMaxNodes(1),
            AiProfile.Normal.WithMaxNodes(1),
            AiProfile.Hard.WithMaxNodes(1),
            AiProfile.Easy.WithMaxNodes(7),
            AiProfile.Normal.WithMaxNodes(50),
            AiProfile.Hard.WithMaxNodes(300),
        };

        // ------------------------------------------------------------------
        // 常に合法手
        // ------------------------------------------------------------------

        [Test]
        public void AlwaysReturnsALegalMoveEvenWithTinyBudgets()
        {
            int checkedPositions = 0;
            for (int i = 0; i < 3000; i++)
            {
                var r = new TestRandom(0x1e6a1 + i);
                var s = AiH.RandomPosition(r, r.NextInt(9));
                if (s == null) continue;
                var profile = TinyProfiles[i % TinyProfiles.Length];
                var session = new SearchSession(s, s.Turn, profile, i, AiH.Pool);
                // 途中でも合法
                Assert.That(GameEngine.IsLegalMove(s, session.BestMove), Is.True);
                session.Step(1);
                Assert.That(GameEngine.IsLegalMove(s, session.BestMove), Is.True);
                var move = session.Run();
                Assert.That(GameEngine.IsLegalMove(s, move), Is.True, profile + " " + StateHash.Serialize(s));
                Assert.That(session.NodesSearched, Is.LessThanOrEqualTo(profile.MaxNodes));
                checkedPositions++;
            }
            Assert.That(checkedPositions, Is.GreaterThan(2500));
        }

        [Test]
        public void PlaysWholeGamesWithEveryProfileAndRuleSet()
        {
            var profiles = new[] { AiProfile.Easy, AiProfile.Normal.WithMaxNodes(5000), AiProfile.Hard.WithMaxNodes(20000) };
            for (int g = 0; g < 90; g++)
            {
                var r = new TestRandom(0x9a3e + g);
                var state = H.NewRandomGame(r);
                int ply = 0;
                while (state.Phase == Phase.Playing)
                {
                    var p = profiles[(g + (int)state.Turn) % 3];
                    var move = SearchSession.ChooseMove(state, state.Turn, p, g * 100 + ply, AiH.Pool);
                    Assert.That(GameEngine.IsLegalMove(state, move), Is.True);
                    state = GameEngine.ApplyMoveFast(state, move);
                    ply++;
                    Assert.That(ply, Is.LessThan(60));
                }
            }
        }

        [Test]
        public void RejectsEndedGamesAndTheWrongPlayer()
        {
            var r = new TestRandom(5);
            var s = AiH.RandomPosition(r, 2, open: true);
            Assert.Throws<System.ArgumentException>(() => new SearchSession(s, s.Turn.Opponent(), AiProfile.Easy, 0, AiH.Pool));
            Assert.Throws<System.ArgumentException>(() => new SearchSession(s.WithPhase(Phase.Ended), s.Turn, AiProfile.Easy, 0, AiH.Pool));
            var hidden = AiH.RandomPosition(new TestRandom(6), 2, open: false);
            Assert.Throws<System.ArgumentNullException>(() => new SearchSession(hidden, hidden.Turn, AiProfile.Hard, 0));
        }

        // ------------------------------------------------------------------
        // 決定論
        // ------------------------------------------------------------------

        static (Move Move, long Nodes, int[] Values) RunSliced(GameState s, AiProfile p, long seed, int slice, SearchWorkspace ws = null)
        {
            var session = new SearchSession(s, s.Turn, p, seed, AiH.Pool, ws);
            int calls = 0;
            while (!session.Step(slice))
            {
                calls++;
                Assert.That(calls, Is.LessThan(10_000_000));
            }
            return (session.BestMove, session.NodesSearched, session.GetEvaluations().Select(e => e.Value).ToArray());
        }

        [Test]
        public void SameInputsGiveTheSameMoveRegardlessOfHowStepIsSliced()
        {
            var profiles = new[] { AiProfile.Easy, AiProfile.Normal.WithMaxNodes(8000), AiProfile.Hard.WithMaxNodes(30000) };
            var shared = new SearchWorkspace();
            for (int i = 0; i < 60; i++)
            {
                var r = new TestRandom(0xde7 + i);
                var s = AiH.RandomPosition(r, r.NextInt(7));
                if (s == null) continue;
                var p = profiles[i % 3];
                var a = RunSliced(s, p, 42 + i, 1 << 30);
                var b = RunSliced(s, p, 42 + i, 1);
                var c = RunSliced(s, p, 42 + i, 37);
                var d = RunSliced(s, p, 42 + i, 997, shared); // 作業領域を使い回しても同じ
                var e = RunSliced(H.Clone(s), p, 42 + i, 4000, shared);
                foreach (var x in new[] { b, c, d, e })
                {
                    Assert.That(x.Move, Is.EqualTo(a.Move));
                    Assert.That(x.Nodes, Is.EqualTo(a.Nodes));
                    Assert.That(x.Values, Is.EqualTo(a.Values));
                }
            }
        }

        [Test]
        public void DifferentSeedsCanChangeEasyButNotTheLegality()
        {
            var r = new TestRandom(77);
            var s = AiH.RandomPosition(r, 0, open: true);
            var moves = new HashSet<Move>();
            for (int seed = 0; seed < 40; seed++)
            {
                var m = SearchSession.ChooseMove(s, s.Turn, AiProfile.Easy, seed, AiH.Pool);
                Assert.That(GameEngine.IsLegalMove(s, m), Is.True);
                moves.Add(m);
            }
            Assert.That(moves.Count, Is.GreaterThan(1), "Easy should vary with the seed (noise)");
        }

        // ------------------------------------------------------------------
        // ズル禁止（Open OFF）／ Open ON では相手の手札を使う
        // ------------------------------------------------------------------

        [Test]
        public void OpenOffChoosesTheSameMoveWhateverTheOpponentReallyHolds()
        {
            var profiles = new[] { AiProfile.Easy, AiProfile.Normal.WithMaxNodes(6000), AiProfile.Hard.WithMaxNodes(20000) };
            int cases = 0;
            for (int i = 0; i < 400; i++)
            {
                var r = new TestRandom(0xc4ea7 + i);
                var s = AiH.RandomPosition(r, r.NextInt(8), open: false);
                if (s == null || s.GetHand(s.Turn.Opponent()).Count == 0) continue;
                var other = AiH.WithOtherOpponentHand(s, s.Turn, r);
                Assert.That(other.GetHand(s.Turn.Opponent()), Is.Not.EqualTo(s.GetHand(s.Turn.Opponent())));
                var p = profiles[i % 3];
                var a = new SearchSession(s, s.Turn, p, i, AiH.Pool);
                var b = new SearchSession(other, s.Turn, p, i, AiH.Pool);
                Assert.That(b.Run(), Is.EqualTo(a.Run()), p.Name + " peeked at the hidden hand in " + StateHash.Serialize(s));
                Assert.That(b.NodesSearched, Is.EqualTo(a.NodesSearched));
                Assert.That(b.GetEvaluations().Select(e => e.Value), Is.EqualTo(a.GetEvaluations().Select(e => e.Value)));
                cases++;
            }
            Assert.That(cases, Is.GreaterThan(300));
        }

        [Test]
        public void HintsAlsoDoNotPeekWhenOpenIsOff()
        {
            for (int i = 0; i < 60; i++)
            {
                var r = new TestRandom(0x417 + i);
                var s = AiH.RandomPosition(r, 2 + r.NextInt(5), open: false);
                if (s == null || s.GetHand(s.Turn.Opponent()).Count == 0) continue;
                var other = AiH.WithOtherOpponentHand(s, s.Turn, r);
                var a = SearchSession.AnalyzeMoves(s, s.Turn, 20000, AiH.Pool, i);
                var b = SearchSession.AnalyzeMoves(other, s.Turn, 20000, AiH.Pool, i);
                Assert.That(b.Select(e => e.Value), Is.EqualTo(a.Select(e => e.Value)));
                Assert.That(b.Select(e => e.Solved), Is.EqualTo(a.Select(e => e.Solved)));
            }
        }

        [Test]
        public void OpenOnUsesTheOpponentsHand()
        {
            // Open ON なら相手の手札は見えている情報なので、手札が違えば選ぶ手が変わることがある
            int differs = 0, cases = 0;
            for (int i = 0; i < 200; i++)
            {
                var r = new TestRandom(0x09e + i);
                var s = AiH.RandomPosition(r, 3 + r.NextInt(4), open: true);
                if (s == null || s.GetHand(s.Turn.Opponent()).Count == 0) continue;
                var other = AiH.WithOtherOpponentHand(s, s.Turn, r);
                var p = AiProfile.Hard.WithMaxNodes(30000);
                var a = SearchSession.AnalyzeMoves(s, s.Turn, 30000, null, i);
                var b = SearchSession.AnalyzeMoves(other, s.Turn, 30000, null, i);
                cases++;
                if (!a.Select(e => e.Value).SequenceEqual(b.Select(e => e.Value))) differs++;
                Assert.That(GameEngine.IsLegalMove(s, SearchSession.ChooseMove(s, s.Turn, p, i)), Is.True);
            }
            Assert.That(cases, Is.GreaterThan(100));
            Assert.That(differs, Is.GreaterThan(cases / 2), "Open ON evaluations should depend on the visible opponent hand");
        }

        [Test]
        public void OpenOnNeedsNoCardPoolAndUsesOneWorld()
        {
            var r = new TestRandom(11);
            var s = AiH.RandomPosition(r, 1, open: true);
            var session = new SearchSession(s, s.Turn, AiProfile.Hard.WithMaxNodes(5000), 1);
            session.Run();
            Assert.That(session.WorldCount, Is.EqualTo(1));
            var hidden = AiH.RandomPosition(new TestRandom(12), 1, open: false);
            var session2 = new SearchSession(hidden, hidden.Turn, AiProfile.Hard.WithMaxNodes(5000), 1, AiH.Pool);
            session2.Run();
            Assert.That(session2.WorldCount, Is.EqualTo(AiProfile.Hard.Worlds));
        }
    }
}
