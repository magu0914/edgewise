using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    /// <summary>
    /// TS 版にはない追加の性質テスト。ApplyMove と ApplyMoveFast の一致を、
    /// Same / Plus / Combo / Same Wall が頻発するカードセットも含めて 10 万局面以上で確かめる。
    /// </summary>
    [TestFixture]
    public class FastPropertyTests
    {
        /// <summary>辺の値を 3〜6 と A に寄せたカード（Same / Plus / Combo / Same Wall が起きやすい）。</summary>
        static CardDef[] StressCards()
        {
            var r = new TestRandom(0x5eed);
            var values = new[] { 3, 4, 5, 6, 10 };
            return Enumerable.Range(0, 16).Select(i =>
            {
                int V() => values[r.NextInt(values.Length)];
                var sides = new Sides(V(), V(), V(), V());
                Element? element = i % 4 == 3 ? (Element?)null : (Element)r.NextInt(8);
                return new CardDef("s" + i, "Stress" + i, 1, sides, element);
            }).ToArray();
        }

        static int Run(int games, uint seedBase, IReadOnlyList<CardDef> cards, bool? suddenDeath = null)
        {
            int positions = 0;
            for (int game = 0; game < games; game++)
            {
                var r = new TestRandom(seedBase + game);
                var start = GameEngine.CreateGame(RandomConfig(r, RandomRules(r, suddenDeath: suddenDeath), cards));
                var state = start;
                while (state.Phase == Phase.Playing)
                {
                    var move = r.Pick(GameEngine.LegalMoves(state));
                    var reference = GameEngine.ApplyMove(state, move).State;
                    var fast = GameEngine.ApplyMoveFast(state, move);
                    if (!fast.Equals(reference))
                        Assert.Fail("mismatch at game " + game + "\n  ref:  " + StateHash.Serialize(reference) + "\n  fast: " + StateHash.Serialize(fast));
                    state = reference;
                    positions++;
                }
            }
            return positions;
        }

        [Test]
        public void MatchesOnMoreThan100kRandomPositionsWithRealCards()
        {
            int positions = Run(12000, 0xbeef, Edgewise.Core.Data.CardCatalog.All);
            TestContext.Out.WriteLine("positions: " + positions);
            Assert.That(positions, Is.GreaterThan(100000));
        }

        [Test]
        public void MatchesOnStressCardsWhereSamePlusAndComboAreFrequent()
        {
            int positions = Run(6000, 0x57e55, StressCards());
            TestContext.Out.WriteLine("positions: " + positions);
            Assert.That(positions, Is.GreaterThan(50000));
        }

        [Test]
        public void ApplyMoveIsDeterministicAcrossRepeatedCalls()
        {
            for (int game = 0; game < 500; game++)
            {
                var r = new TestRandom(0xd00d + game);
                var state = NewRandomGame(r);
                while (state.Phase == Phase.Playing)
                {
                    var move = r.Pick(GameEngine.LegalMoves(state));
                    var a = GameEngine.ApplyMove(state, move);
                    var b = GameEngine.ApplyMove(state, move);
                    Assert.That(a.State, Is.EqualTo(b.State));
                    Assert.That(a.Events.Select(e => e.ToJson()), Is.EqualTo(b.Events.Select(e => e.ToJson())));
                    state = a.State;
                }
            }
        }
    }
}
