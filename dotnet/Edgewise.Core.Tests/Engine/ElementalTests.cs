using System;
using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.Data;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // 移植元: tripletriad-ts-legacy/tests/engine/elemental.test.ts

    [TestFixture]
    public class ElementalBasicComparisonTests
    {
        static readonly CardDef Opp = MkCard("A", 1, 1, 5, 1); // bottom 5（属性なしのマス）

        [Test]
        public void MatchingElementGivesPlusOne()
        {
            // 5 vs 5 becomes 6 vs 5
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 5, 1, 1, 1, Element.Fire), Opp },
                Rules = new RuleSet(elemental: true),
                Board = { { 1, "A", 1 } },
                CellElements = { [4] = Element.Fire },
            });
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(0));
            // ルール OFF なら補正なし
            Assert.That(Owners(Both(s.WithRules(RuleSet.None), PlayCenter).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void MismatchingElementGivesMinusOne()
        {
            // 6 vs 5 becomes 5 vs 5
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 6, 1, 1, 1, Element.Ice), Opp },
                Rules = new RuleSet(elemental: true),
                Board = { { 1, "A", 1 } },
                CellElements = { [4] = Element.Fire },
            });
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void ACardWithoutElementGetsMinusOneOnAnElementalCell()
        {
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 6, 1, 1, 1), Opp },
                Rules = new RuleSet(elemental: true),
                Board = { { 1, "A", 1 } },
                CellElements = { [4] = Element.Holy },
            });
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void TheNeighborsOwnCellElementAppliesToTheNeighborsSides()
        {
            // 相手カードが不一致のマスにいる → bottom 5 が 4 に下がり、P の 5 が勝つ
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 5, 1, 1, 1), Opp },
                Rules = new RuleSet(elemental: true),
                Board = { { 1, "A", 1 } },
                CellElements = { [1] = Element.Dark },
            });
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(0));
        }

        [Test]
        public void ValuesAreNotClampedAPlusOneIs11AndBeatsAnA()
        {
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 10, 1, 1, 1, Element.Wind), MkCard("A", 1, 1, 10, 1, Element.Wind) },
                Rules = new RuleSet(elemental: true),
                Board = { { 1, "A", 1 } },
                CellElements = { [4] = Element.Wind },
            });
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class ElementalSamePlusTests
    {
        [Test]
        public void SameUsesCorrectedValues()
        {
            // P (fire, fire マス) [4,4,1,1] → [5,5,2,2]。上の bottom 5、右の left 5 と Same 成立
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 4, 4, 1, 1, Element.Fire), MkCard("A", 1, 1, 5, 1), MkCard("B", 1, 1, 1, 5) },
                Rules = new RuleSet(elemental: true, same: true),
                Board = { { 1, "A", 1 }, { 5, "B", 1 } },
                CellElements = { [4] = Element.Fire },
            });
            var events = Both(s, PlayCenter).Events;
            Assert.That(Flips(events).Select(f => (f.Cell, f.Cause)), Is.EqualTo(new[] { (1, FlipCause.Same), (5, FlipCause.Same) }));
            // 補正なしなら 4≠5 で不成立、5 > 4 なので通常ルールでも取れない
            Assert.That(Flips(Both(s.WithRules(new RuleSet(same: true)), PlayCenter).Events), Is.Empty);
        }

        [Test]
        public void PlusUsesCorrectedValuesIncludingTheNeighborCorrection()
        {
            // P [3,3,1,1]（属性なしマス）。上: 3+6=9。右: 自分の B (holy, holy マス) left 5→6、3+6=9
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 3, 3, 1, 1), MkCard("A", 1, 1, 6, 1), MkCard("B", 1, 1, 1, 5, Element.Holy) },
                Rules = new RuleSet(elemental: true, plus: true),
                Board = { { 1, "A", 1 }, { 5, "B", 0 } },
                CellElements = { [5] = Element.Holy },
            });
            var events = Both(s, PlayCenter).Events;
            Assert.That(Flips(events), Is.EqualTo(new[] { F(1, FlipCause.Plus, 0, 4) }));
            // 補正なしなら 9 と 8 で不成立
            Assert.That(Flips(Both(s.WithRules(new RuleSet(plus: true)), PlayCenter).Events), Is.Empty);
        }

        [Test]
        public void SameWallComparesTheCorrectedValueWithTheWall()
        {
            // 9+1 = A
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 9, 4, 1, 1, Element.Earth), MkCard("A", 1, 1, 1, 5) },
                Rules = new RuleSet(elemental: true, same: true, sameWall: true),
                Board = { { 1, "A", 1 } },
                CellElements = { [0] = Element.Earth },
            });
            var events = Both(s, new Move(0, 0, 0)).Events;
            Assert.That(events.First(e => e is RuleEvent), Is.EqualTo(new RuleEvent(RuleHeading.SameWall, new[] { 1 })));
        }
    }

    [TestFixture]
    public class ElementalLayoutTests
    {
        static readonly string[] Deck0 = CardCatalog.All.Take(5).Select(c => c.Id).ToArray();
        static readonly string[] Deck1 = CardCatalog.All.Skip(5).Take(5).Select(c => c.Id).ToArray();

        [Test]
        public void IsAllNullWhenElementalIsOff()
        {
            var s = GameEngine.CreateGame(new GameConfig(RuleSet.None, Deck0, Deck1, 1, CardCatalog.All));
            Assert.That(s.CellElements.ToArray(), Is.EqualTo(new Element?[9]));
        }

        [Test]
        public void IsDeterministicPerSeedAndRoughlyAQuarterOfCellsGetAnElement()
        {
            var rules = new RuleSet(elemental: true);
            int withElement = 0;
            var seen = new HashSet<Element>();
            const int N = 4000;
            for (int seed = 0; seed < N; seed++)
            {
                var a = GameEngine.CreateGame(new GameConfig(rules, Deck0, Deck1, seed, CardCatalog.All));
                var b = GameEngine.CreateGame(new GameConfig(rules, Deck0, Deck1, seed, CardCatalog.All));
                Assert.That(a.CellElements.ToArray(), Is.EqualTo(b.CellElements.ToArray()));
                foreach (var e in a.CellElements)
                {
                    if (e.HasValue)
                    {
                        withElement++;
                        seen.Add(e.Value);
                    }
                }
            }
            // |ratio - 1/4| < 0.02 を整数で: |4 * withElement - cells| < 0.08 * cells
            int cells = N * 9;
            Assert.That(Math.Abs(4 * withElement - cells) * 100, Is.LessThan(8 * cells));
            Assert.That(seen.OrderBy(e => e), Is.EqualTo(Enum.GetValues(typeof(Element)).Cast<Element>().OrderBy(e => e)));
        }
    }
}
