using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // 0 1 2
    // 3 4 5
    // 6 7 8
    // 移植元: tripletriad-ts-legacy/tests/engine/same-plus-combo.test.ts

    [TestFixture]
    public class SameTests
    {
        // P の top 3 = 上の相手カードの bottom 3、P の right 7 = 右の「自分の」カードの left 7
        static readonly CardDef[] Cards =
        {
            MkCard("P", 3, 7, 2, 5),
            MkCard("A", 1, 1, 3, 1),
            MkCard("B", 1, 1, 1, 7),
            MkCard("C", 9, 1, 1, 1),
        };

        static Setup Std(RuleSet rules) => new Setup { Cards = Cards, Rules = rules, Board = { { 1, "A", 1 }, { 5, "B", 0 }, { 7, "C", 1 } } };

        [Test]
        public void CountsOwnCardsTowardTheTwoSideConditionButFlipsOnlyOpponentCards()
        {
            var r = Both(MakeState(Std(new RuleSet(same: true))), PlayCenter);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(null, 0, null, null, 0, 0, null, 1, null)));
            Assert.That(RuleEvents(r.Events), Is.EqualTo(new[] { new RuleEvent(RuleHeading.Same, new[] { 1, 5 }) }));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(1, FlipCause.Same, 0, 4) }));
        }

        [Test]
        public void DoesNothingWithOnlyOneMatchingSide()
        {
            var s = MakeState(new Setup { Cards = Cards, Rules = new RuleSet(same: true), Board = { { 1, "A", 1 }, { 7, "C", 1 } } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State)[1], Is.EqualTo(1));
            Assert.That(RuleEvents(r.Events), Is.Empty);
        }

        [Test]
        public void DoesNothingWhenTheRuleIsOff()
        {
            var s = MakeState(Std(RuleSet.None));
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void EmitsNoRuleEventWhenEveryMatchedCardIsAlreadyOurs()
        {
            var s = MakeState(new Setup { Cards = Cards, Rules = new RuleSet(same: true), Board = { { 1, "A", 0 }, { 5, "B", 0 } } });
            Assert.That(RuleEvents(Both(s, PlayCenter).Events), Is.Empty);
        }
    }

    [TestFixture]
    public class SameWallTests
    {
        // 角（マス 0）に top = A のカードを置く。上は壁 = A として Same が 1 辺成立
        static readonly CardDef[] Cards = { MkCard("P", 10, 4, 6, 1), MkCard("A", 1, 1, 1, 4) };
        static readonly Move M = new Move(0, 0, 0);

        static GameState S(RuleSet rules, CardDef[] cards = null) =>
            MakeState(new Setup { Cards = cards ?? Cards, Rules = rules, Board = { { 1, "A", 1 } } });

        [Test]
        public void TreatsTheWallAsAForSame()
        {
            var r = Both(S(new RuleSet(same: true, sameWall: true)), M);
            Assert.That(Owners(r.State)[1], Is.EqualTo(0));
            Assert.That(RuleEvents(r.Events), Is.EqualTo(new[] { new RuleEvent(RuleHeading.SameWall, new[] { 1 }) }));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(1, FlipCause.Same, 0, 0) }));
        }

        [Test]
        public void WithoutSameWallASingleMatchIsNotEnough()
        {
            Assert.That(Owners(Both(S(new RuleSet(same: true)), M).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void HasNoEffectWhenSameItselfIsOff()
        {
            Assert.That(Owners(Both(S(new RuleSet(sameWall: true)), M).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void WallsAreNotUsedByPlus()
        {
            // top 10 + 壁(10) = 20、right 10 + A の left 10 = 20 だが壁は Plus に数えない
            var plusCards = new[] { MkCard("P", 10, 10, 1, 1), MkCard("A", 1, 1, 1, 10) };
            Assert.That(Owners(Both(S(new RuleSet(plus: true, sameWall: true), plusCards), M).State)[1], Is.EqualTo(1));
        }
    }

    [TestFixture]
    public class PlusTests
    {
        // top: 2 + 6 = 8（相手）、right: 5 + 3 = 8（自分）、bottom: 1 + 4 = 5（相手）
        static readonly CardDef[] Cards =
        {
            MkCard("P", 2, 5, 1, 1),
            MkCard("A", 1, 1, 6, 1),
            MkCard("B", 1, 1, 1, 3),
            MkCard("C", 4, 1, 1, 1),
        };

        [Test]
        public void FlipsOpponentCardsWhoseSumsMatchOwnCardsCountTowardTheCondition()
        {
            var s = MakeState(new Setup { Cards = Cards, Rules = new RuleSet(plus: true), Board = { { 1, "A", 1 }, { 5, "B", 0 }, { 7, "C", 1 } } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(null, 0, null, null, 0, 0, null, 1, null)));
            Assert.That(RuleEvents(r.Events), Is.EqualTo(new[] { new RuleEvent(RuleHeading.Plus, new[] { 1, 5 }) }));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(1, FlipCause.Plus, 0, 4) }));
        }

        [Test]
        public void DoesNothingWhenNoTwoSumsAreEqual()
        {
            var s = MakeState(new Setup { Cards = Cards, Rules = new RuleSet(plus: true), Board = { { 1, "A", 1 }, { 7, "C", 1 } } });
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(1));
        }
    }

    [TestFixture]
    public class SameAndPlusTogetherTests
    {
        [Test]
        public void EvaluatesBothOnTheSameBoardAndAppliesThemTogether()
        {
            // P [4,4,2,1]: top 4=4 (same, 和 8)、right 4=4 (same, 和 8)、bottom 2+7=9、left 1+8=9
            var cards = new[]
            {
                MkCard("P", 4, 4, 2, 1),
                MkCard("T", 1, 1, 4, 1),
                MkCard("R", 1, 1, 1, 4),
                MkCard("B", 7, 1, 1, 1),
                MkCard("L", 1, 8, 1, 1),
            };
            var s = MakeState(new Setup
            {
                Cards = cards,
                Rules = new RuleSet(same: true, plus: true),
                Board = { { 1, "T", 1 }, { 5, "R", 1 }, { 7, "B", 1 }, { 3, "L", 1 } },
            });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(null, 0, null, 0, 0, 0, null, 0, null)));
            Assert.That(RuleEvents(r.Events), Is.EqualTo(new[]
            {
                new RuleEvent(RuleHeading.Same, new[] { 1, 5 }),
                new RuleEvent(RuleHeading.Plus, new[] { 1, 5, 7, 3 }),
            }));
            // 両方で成立したマスは Same として 1 回だけ裏返す。走査順は上→右→下→左
            Assert.That(Flips(r.Events), Is.EqualTo(new[]
            {
                F(1, FlipCause.Same, 0, 4),
                F(5, FlipCause.Same, 0, 4),
                F(7, FlipCause.Plus, 0, 4),
                F(3, FlipCause.Plus, 0, 4),
            }));
        }
    }

    [TestFixture]
    public class ComboTests
    {
        // マス 3 に置き、Same（top=5 vs 0 の bottom 5、right=5 vs 自分の 4 の left 5）でマス 0 を取る。
        // 0 → 1 → 2 → 5 と通常ルールで 3 段連鎖する。
        static readonly CardDef[] Cards =
        {
            MkCard("P", 5, 5, 9, 1),
            MkCard("own", 1, 1, 1, 5),
            MkCard("c0", 1, 8, 5, 1),
            MkCard("c1", 1, 7, 1, 2),
            MkCard("c2", 1, 1, 6, 3),
            MkCard("c5", 1, 1, 1, 1),
        };

        static readonly Move M = new Move(0, 3, 0);

        static GameState Std(RuleSet rules) => MakeState(new Setup
        {
            Cards = Cards,
            Rules = rules,
            Board = { { 4, "own", 0 }, { 0, "c0", 1 }, { 1, "c1", 1 }, { 2, "c2", 1 }, { 5, "c5", 1 } },
        });

        [Test]
        public void ChainsFromSameFlipsWithIncreasingChainDepth()
        {
            var r = Both(Std(new RuleSet(same: true, combo: true)), M);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(0, 0, 0, 0, 0, 0, null, null, null)));
            Assert.That(Flips(r.Events), Is.EqualTo(new[]
            {
                F(0, FlipCause.Same, 0, 3),
                F(1, FlipCause.Combo, 1, 0),
                F(2, FlipCause.Combo, 2, 1),
                F(5, FlipCause.Combo, 3, 2),
            }));
        }

        [Test]
        public void DoesNotChainWhenComboIsOff()
        {
            var r = Both(Std(new RuleSet(same: true)), M);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(0, 1, 1, 0, 0, 1, null, null, null)));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(0, FlipCause.Same, 0, 3) }));
        }

        [Test]
        public void ChainsFromPlusFlipsToo()
        {
            // P [2,6,1,1]: top 2 + 0 の bottom 7 = 9、right 6 + 4 の left 3 = 9
            var plusCards = new[]
            {
                MkCard("P", 2, 6, 1, 1),
                MkCard("own", 1, 1, 1, 3),
                MkCard("c0", 1, 8, 7, 1),
                MkCard("c1", 1, 1, 1, 2),
            };
            var s = MakeState(new Setup
            {
                Cards = plusCards,
                Rules = new RuleSet(plus: true, combo: true),
                Board = { { 4, "own", 0 }, { 0, "c0", 1 }, { 1, "c1", 1 } },
            });
            var r = Both(s, M);
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(0, FlipCause.Plus, 0, 3), F(1, FlipCause.Combo, 1, 0) }));
        }

        [Test]
        public void DoesNotReEvaluateSamePlusDuringTheChain()
        {
            // 0 のカードは right 3 = 1 の left 3、bottom 5 = 置いたカードの top 5 で Same の形になるが、
            // 連鎖では通常ルールのみなので 1 は裏返らない（3 > 3 ではない）
            var chainCards = new[]
            {
                MkCard("P", 5, 5, 1, 1),
                MkCard("own", 1, 1, 1, 5),
                MkCard("c0", 1, 3, 5, 1),
                MkCard("c1", 1, 1, 1, 3),
            };
            var s = MakeState(new Setup
            {
                Cards = chainCards,
                Rules = new RuleSet(same: true, plus: true, combo: true),
                Board = { { 4, "own", 0 }, { 0, "c0", 1 }, { 1, "c1", 1 } },
            });
            var r = Both(s, M);
            Assert.That(Owners(r.State)[1], Is.EqualTo(1));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(0, FlipCause.Same, 0, 3) }));
        }

        [Test]
        public void BasicFlipsAreNotChainSources()
        {
            // 置いたカードが通常ルールで 1 を取る。1 の right 9 > 2 の left 1 だが連鎖しない
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 5, 1, 1, 1), MkCard("c1", 1, 9, 1, 1), MkCard("c2", 1, 1, 1, 1) },
                Rules = new RuleSet(same: true, plus: true, combo: true),
                Board = { { 1, "c1", 1 }, { 2, "c2", 1 } },
            });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(null, 0, 1, null, 0, null, null, null, null)));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(1, FlipCause.Basic, 0, 4) }));
        }
    }
}
