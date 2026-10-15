using System.Collections.Generic;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.Match;
using NUnit.Framework;

namespace Edgewise.Tests.EditMode
{
    /// <summary>EventPlayer の前半（イベント列 → 演出ステップ）の変換が正しい順序になること。</summary>
    public class PlaybackPlannerTests
    {
        static CardDef Def(string id, int top, int right, int bottom, int left) => new CardDef(id, id, 1, new Sides(top, right, bottom, left));

        /// <summary>
        /// 盤（index = row*3+col）:
        ///   . A C
        ///   B x D      x = P0 が置く (5,1,1,5)。A の下 5・B の右 5 と Same が成立して A・B を取る。
        ///   . . .      A の右 9 > C の左 1 で C（Combo 1 段目）、C の下 9 > D の上 1 で D（Combo 2 段目）。D は通常ルールでは取れない。
        /// </summary>
        [Test]
        public void SameThenBannerThenFlipsThenComboStepsInOrder()
        {
            var x = Def("tx", 5, 1, 1, 5);
            var a = Def("ta", 1, 9, 5, 1);
            var b = Def("tb", 1, 5, 1, 1);
            var c = Def("tc", 1, 1, 9, 1);
            var d = Def("td", 1, 1, 1, 2);
            var h0 = Def("th0", 1, 1, 1, 1);
            var h1 = Def("th1", 1, 1, 1, 1);
            var cards = new Dictionary<string, CardDef> { { x.Id, x }, { h0.Id, h0 }, { h1.Id, h1 } };
            var board = new PlacedCard?[9];
            board[1] = new PlacedCard(a, Player.P1, 0);
            board[2] = new PlacedCard(c, Player.P1, 0);
            board[3] = new PlacedCard(b, Player.P1, 0);
            board[5] = new PlacedCard(d, Player.P1, 0);
            var state = GameState.FromParts(new RuleSet(same: true, combo: true), cards, board, new Element?[9],
                new[] { "tx", "th0" }, new[] { "th1" }, Player.P0, Player.P0, 0, 0, 1u, Phase.Playing, 0);

            var result = GameEngine.ApplyMove(state, new Move(0, 4));
            // 前提: エンジンのイベント列
            Assert.That(string.Join(" ", result.Events.Select(e => e.ToJson())), Is.EqualTo(string.Join(" ",
                "{\"type\":\"place\",\"player\":0,\"cell\":4,\"cardId\":\"tx\",\"rotation\":0}",
                "{\"type\":\"rule\",\"rule\":\"same\",\"cells\":[1,3]}",
                "{\"type\":\"flip\",\"cell\":1,\"to\":0,\"cause\":\"same\",\"chainDepth\":0,\"from\":4}",
                "{\"type\":\"flip\",\"cell\":3,\"to\":0,\"cause\":\"same\",\"chainDepth\":0,\"from\":4}",
                "{\"type\":\"flip\",\"cell\":2,\"to\":0,\"cause\":\"combo\",\"chainDepth\":1,\"from\":1}",
                "{\"type\":\"flip\",\"cell\":5,\"to\":0,\"cause\":\"combo\",\"chainDepth\":2,\"from\":2}",
                "{\"type\":\"turn\",\"player\":1}")));

            var steps = PlaybackPlanner.Build(result.Events);
            Assert.That(PlaybackPlanner.Describe(steps), Is.EqualTo(
                "Place p0 cell 4 tx r0 | RuleLink same [1,3] | RuleBanner same [1,3] | FlipGroup [1:same0,3:same0] | ComboBanner x1"
                + " | FlipGroup [2:combo1] | ComboBanner x2 | FlipGroup [5:combo2] | Turn p1"));
            // 音程用の通し番号は 1 手の中で続く
            var flips = steps.Where(s => s.Kind == StepKind.FlipGroup).SelectMany(s => s.Flips).ToList();
            Assert.That(flips.Select(f => f.IndexInMove), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(flips.Select(f => f.From), Is.EqualTo(new[] { 4, 4, 1, 2 }));
            Assert.That(EventPlayer.ComboText(1), Is.EqualTo("COMBO!"));
            Assert.That(EventPlayer.ComboText(3), Is.EqualTo("COMBO ×3"));
        }

        [Test]
        public void PlusAndBasicFlipsAreSeparateGroupsAndTheLastMoveEndsWithEnd()
        {
            // x = (6,6,1,1) を 4 に置く。上 A の下 4（和 10）・右 B の左 4（和 10）で Plus。左 C の右 0 は通常ルールで取れる（1 > 0）
            var x = Def("px", 6, 6, 1, 1);
            var a = Def("pa", 1, 1, 4, 1);
            var b = Def("pb", 1, 1, 1, 4);
            var cc = Def("pc", 1, 0, 1, 1);
            var filler = Def("pf", 1, 1, 1, 1);
            var cards = new Dictionary<string, CardDef> { { x.Id, x } };
            var board = new PlacedCard?[9];
            board[1] = new PlacedCard(a, Player.P1, 0);
            board[5] = new PlacedCard(b, Player.P1, 0);
            board[3] = new PlacedCard(cc, Player.P1, 0);
            foreach (int i in new[] { 0, 2, 6, 7, 8 }) board[i] = new PlacedCard(filler, Player.P0, 0);
            var state = GameState.FromParts(new RuleSet(plus: true, combo: true), cards, board, new Element?[9],
                new[] { "px" }, new string[0], Player.P0, Player.P0, 0, 0, 1u, Phase.Playing, 0);
            var result = GameEngine.ApplyMove(state, new Move(0, 4));
            var steps = PlaybackPlanner.Build(result.Events);
            Assert.That(PlaybackPlanner.Describe(steps), Is.EqualTo(
                "Place p0 cell 4 px r0 | RuleLink plus [1,5] | RuleBanner plus [1,5] | FlipGroup [1:plus0,5:plus0] | FlipGroup [3:basic0] | End 9-0 p0"));
        }

        /// <summary>多数の対局で、ステップ列がいつも同じ形の規則を満たすこと。</summary>
        [Test]
        public void StepsFollowTheSameShapeInRandomGames()
        {
            var rules = new RuleSet(open: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true);
            var ids = CardCatalog.All.Select(c => c.Id).ToArray();
            int movesChecked = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                uint rng = Rng.SeedToState(seed);
                var deck = ids.OrderBy(_ => Rng.NextInt(rng, 1 << 20, out rng)).ToArray();
                var state = GameEngine.CreateGame(new GameConfig(rules, deck.Take(5).ToArray(), deck.Skip(5).Take(5).ToArray(), seed, CardCatalog.ById));
                int guard = 0;
                while (state.Phase == Phase.Playing && guard++ < 60)
                {
                    var legal = GameEngine.LegalMoves(state);
                    var move = legal[Rng.NextInt(rng, legal.Count, out rng)];
                    var result = GameEngine.ApplyMove(state, move);
                    var steps = PlaybackPlanner.Build(result.Events);
                    CheckShape(result.Events, steps);
                    state = result.State;
                    movesChecked++;
                }
            }
            Assert.That(movesChecked, Is.GreaterThan(300));
        }

        static void CheckShape(IReadOnlyList<GameEvent> events, List<PlaybackStep> steps)
        {
            Assert.That(steps[0].Kind, Is.EqualTo(StepKind.Place));
            var flipEvents = events.OfType<FlipEvent>().ToList();
            var flipSteps = steps.Where(s => s.Kind == StepKind.FlipGroup).SelectMany(s => s.Flips).ToList();
            Assert.That(flipSteps.Select(f => f.Cell), Is.EqualTo(flipEvents.Select(f => f.Cell)), "flip order must follow the events");
            Assert.That(flipSteps.Select(f => f.IndexInMove), Is.EqualTo(Enumerable.Range(0, flipSteps.Count)));

            int firstFlip = steps.FindIndex(s => s.Kind == StepKind.FlipGroup);
            int lastRule = steps.FindLastIndex(s => s.Kind == StepKind.RuleBanner || s.Kind == StepKind.RuleLink);
            if (firstFlip >= 0 && lastRule >= 0) Assert.That(lastRule, Is.LessThan(firstFlip), "rule headings come before flips");
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Kind == StepKind.RuleLink) Assert.That(steps[i + 1].Kind, Is.EqualTo(StepKind.RuleBanner));
                if (steps[i].Kind == StepKind.ComboBanner)
                {
                    Assert.That(steps[i + 1].Kind, Is.EqualTo(StepKind.FlipGroup));
                    Assert.That(steps[i + 1].Flips.All(f => f.ChainDepth == steps[i].ComboDepth));
                }
                if (steps[i].Kind == StepKind.FlipGroup && steps[i].Flips[0].ChainDepth > 0)
                    Assert.That(steps[i - 1].Kind, Is.EqualTo(StepKind.ComboBanner));
            }
            int depth = 0;
            foreach (var f in flipSteps)
            {
                Assert.That(f.ChainDepth, Is.GreaterThanOrEqualTo(depth));
                depth = f.ChainDepth;
            }
            var last = steps[steps.Count - 1].Kind;
            Assert.That(last == StepKind.Turn || last == StepKind.End, "last step: " + last);
        }
    }
}
