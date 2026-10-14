using System.Linq;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // 0 1 2
    // 3 4 5
    // 6 7 8
    // 移植元: tripletriad-ts-legacy/tests/engine/events.test.ts
    // event order follows §2: scan top → right → bottom → left, chain queue FIFO

    [TestFixture]
    public class EventOrderTests
    {
        [Test]
        public void BasicFlipsAreEmittedInTopRightBottomLeftOrder()
        {
            var weak = MkCard("w", 1, 1, 1, 1);
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 9, 9, 9, 9), weak },
                // わざと左→下→右→上の順で並べても、イベントは走査順になる
                Board = { { 3, "w", 1 }, { 7, "w", 1 }, { 5, "w", 1 }, { 1, "w", 1 } },
            });
            var events = GameEngine.ApplyMove(s, new Move(0, 4, 0)).Events;
            Assert.That(events.Select(e => e.Type), Is.EqualTo(new[] { "place", "flip", "flip", "flip", "flip", "turn" }));
            Assert.That(Flips(events).Select(f => f.Cell), Is.EqualTo(new[] { 1, 5, 7, 3 }));
        }

        [Test]
        public void ComboChainIsBreadthFirstWithAFifoQueue()
        {
            // 中央に置いて Same で 1（上）と 7（下）を取る。
            // 1 → 2, 0 / 7 → 8, 6 が 1 段目、2 → 5 / 0 → 3 が 2 段目。
            var cards = new[]
            {
                MkCard("P", 5, 1, 6, 1),
                MkCard("t", 1, 9, 5, 9), // 1: bottom 5 = P.top
                MkCard("b", 6, 9, 1, 9), // 7: top 6 = P.bottom
                MkCard("r0", 1, 1, 9, 1), // 0 / 2: 横は 1、bottom 9
                MkCard("s", 1, 1, 1, 1), // 6 / 8
                MkCard("m5", 1, 1, 1, 2), // 5: left 2 > P.right 1（通常ルールで取られない）
                MkCard("m3", 1, 2, 1, 1), // 3: right 2 > P.left 1
            };
            var s = MakeState(new Setup
            {
                Cards = cards,
                Rules = new RuleSet(same: true, combo: true),
                Board =
                {
                    { 0, "r0", 1 }, { 1, "t", 1 }, { 2, "r0", 1 }, { 3, "m3", 1 },
                    { 5, "m5", 1 }, { 6, "s", 1 }, { 7, "b", 1 }, { 8, "s", 1 },
                },
                Hands = new[] { new[] { "P" }, new string[0] },
            });
            var r = GameEngine.ApplyMove(s, new Move(0, 4, 0));
            Assert.That(Flips(r.Events), Is.EqualTo(new[]
            {
                F(1, FlipCause.Same, 0, 4),
                F(7, FlipCause.Same, 0, 4),
                F(2, FlipCause.Combo, 1, 1),
                F(0, FlipCause.Combo, 1, 1),
                F(8, FlipCause.Combo, 1, 7),
                F(6, FlipCause.Combo, 1, 7),
                F(5, FlipCause.Combo, 2, 2),
                F(3, FlipCause.Combo, 2, 0),
            }));
            // 盤が埋まったので最後は end
            Assert.That(r.Events.Select(e => e.Type), Is.EqualTo(new[]
            {
                "place", "rule", "flip", "flip", "flip", "flip", "flip", "flip", "flip", "flip", "end",
            }));
            Assert.That(r.Events[r.Events.Count - 1], Is.EqualTo(new EndEvent((9, 0), GameResult.P0)));
            Assert.That(GameEngine.ApplyMoveFast(s, new Move(0, 4, 0)), Is.EqualTo(r.State));
        }

        [Test]
        public void EveryFlipEventRefersToACellAdjacentToItsSource()
        {
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 9, 9, 9, 9), MkCard("w", 1, 1, 1, 1) },
                Board = { { 1, "w", 1 }, { 3, "w", 1 } },
            });
            foreach (var f in Flips(GameEngine.ApplyMove(s, new Move(0, 4, 0)).Events))
            {
                Assert.That(new[] { 1, 3, 5, 7 }, Does.Contain(f.Cell));
                Assert.That(f.From, Is.EqualTo(4));
            }
        }
    }
}
