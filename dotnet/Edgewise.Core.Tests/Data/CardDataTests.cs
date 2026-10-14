using System;
using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.Data;
using NUnit.Framework;

namespace Edgewise.Core.Tests.Data
{
    // 移植元: tripletriad-ts-legacy/tests/data/cards.test.ts

    [TestFixture]
    public class CardDataTests
    {
        static readonly IReadOnlyList<CardDef> Cards = CardCatalog.All;

        [Test]
        public void Has50CardsFivePerLevelFromLv1ToLv10()
        {
            Assert.That(Cards.Count, Is.EqualTo(50));
            for (int level = Budget.MinLevel; level <= Budget.MaxLevel; level++)
                Assert.That(Cards.Count(c => c.Level == level), Is.EqualTo(Budget.CardsPerLevel), "Lv" + level);
        }

        [Test]
        public void PassesValidation()
        {
            // ids, names, sides, budget, A restriction, element, tribe
            Assert.That(CardValidator.CardListProblems(Cards), Is.Empty);
        }

        [Test]
        public void EveryCardIsWithinItsLevelBudget()
        {
            foreach (var c in Cards)
            {
                var budget = Budget.LevelBudgets[c.Level];
                Assert.That(c.Sides.Sum, Is.GreaterThanOrEqualTo(budget.Min), c.Id);
                Assert.That(c.Sides.Sum, Is.LessThanOrEqualTo(budget.Max), c.Id);
            }
        }

        [Test]
        public void AceAppearsOnlyOnLv8PlusCards()
        {
            foreach (var c in Cards)
            {
                if (c.Level < Budget.MinLevelForAce) Assert.That(c.Sides.Contains(10), Is.False, c.Id);
            }
            Assert.That(Cards.Any(c => c.Sides.Contains(10)), Is.True);
        }

        [Test]
        public void EveryCardHasATribeAndAllTribesAndElementsAreUsed()
        {
            foreach (var c in Cards) Assert.That(c.Tribe.HasValue, Is.True, c.Id);
            Assert.That(Cards.Select(c => c.Tribe).Distinct().Count(), Is.EqualTo(GameConstants.TribeCount));
            Assert.That(Cards.Where(c => c.Element.HasValue).Select(c => c.Element).Distinct().Count(), Is.EqualTo(GameConstants.ElementCount));
        }

        [Test]
        public void ByIdIndexesEveryCardById()
        {
            Assert.That(CardCatalog.ById.Count, Is.EqualTo(Cards.Count));
            foreach (var c in Cards) Assert.That(CardCatalog.ById[c.Id], Is.SameAs(c));
        }

        [Test]
        public void DoesNotUseWellKnownNamesFromTheOriginalSeries()
        {
            // sanity check
            var banned = new[]
            {
                "triple triad", "bahamut", "shiva", "ifrit", "quezacotl", "siren", "leviathan", "odin", "gilgamesh",
                "diablos", "carbuncle", "pandemona", "cerberus", "alexander", "doomtrain", "eden", "tonberry",
                "cactuar", "chocobo", "moogle", "malboro", "squall", "rinoa", "seifer", "zell", "quistis", "selphie",
                "irvine", "laguna", "edea", "ultimecia", "griever", "kiros", "minimog", "sacred", "minotaur",
            };
            foreach (var c in Cards)
            {
                var name = c.Name.ToLowerInvariant();
                foreach (var b in banned) Assert.That(name.Contains(b), Is.False, c.Name + " contains \"" + b + "\"");
            }
        }
    }

    [TestFixture]
    public class BudgetTableTests
    {
        [Test]
        public void IsLinearFrom10To13AtLv1To30To33AtLv10WithAWidthOf3()
        {
            Assert.That(Budget.LevelBudgets[1], Is.EqualTo(new LevelBudget(10, 13)));
            Assert.That(Budget.LevelBudgets[10], Is.EqualTo(new LevelBudget(30, 33)));
            for (int level = Budget.MinLevel; level <= Budget.MaxLevel; level++)
            {
                var b = Budget.LevelBudgets[level];
                Assert.That(b.Max - b.Min, Is.EqualTo(3));
                // round(10 + (level-1) * 20/9) を整数演算で（0.5 は切り上げ = JS の Math.round と同じ）
                int numerator = 90 + (level - 1) * 20; // = 9 * (10 + (level-1)*20/9)
                Assert.That(b.Min, Is.EqualTo((2 * numerator + 9) / 18));
                if (level > Budget.MinLevel) Assert.That(b.Min, Is.GreaterThan(Budget.LevelBudgets[level - 1].Min));
            }
        }
    }

    [TestFixture]
    public class CardValidatorTests
    {
        static readonly CardDef Ok = new CardDef("ok", "Ok", 1, new Sides(3, 3, 3, 3), null, Tribe.Beast);

        [Test]
        public void AcceptsAValidCard()
        {
            Assert.That(CardValidator.CardProblems(Ok), Is.Empty);
        }

        static IEnumerable<TestCaseData> BadCards()
        {
            yield return new TestCaseData(Ok.With(sides: new Sides(4, 4, 4, 4))).SetName("over budget");
            yield return new TestCaseData(Ok.With(sides: new Sides(1, 1, 1, 1))).SetName("under budget");
            yield return new TestCaseData(Ok.With(level: 7, sides: new Sides(10, 5, 5, 4))).SetName("A below Lv8");
            yield return new TestCaseData(Ok.With(level: 11)).SetName("level out of range");
            yield return new TestCaseData(Ok.With(id: "Bad Id")).SetName("bad id");
            yield return new TestCaseData(Ok.With(name: " ")).SetName("empty name");
        }

        [TestCaseSource(nameof(BadCards))]
        public void RejectsBadCard(CardDef card)
        {
            Assert.That(CardValidator.CardProblems(card), Is.Not.Empty);
        }

        [Test]
        public void DetectsDuplicateIdsAndNames()
        {
            Assert.That(CardValidator.CardListProblems(new[] { Ok, Ok }), Is.EqualTo(new[] { "duplicate id: ok", "duplicate name: Ok" }));
        }
    }
}
