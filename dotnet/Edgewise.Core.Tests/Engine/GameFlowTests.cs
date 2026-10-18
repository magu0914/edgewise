using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.Data;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // 移植元: tripletriad-ts-legacy/tests/engine/game-flow.test.ts

    static class Decks
    {
        public static readonly string[] AllIds = CardCatalog.All.Select(c => c.Id).ToArray();
        public static readonly string[] D0 = AllIds.Take(5).ToArray();
        public static readonly string[] D1 = AllIds.Skip(5).Take(5).ToArray();

        public static string[] Slice(int start, int end) => AllIds.Skip(start).Take(end - start).ToArray();

        public static GameConfig Config(RuleSet rules, long seed) => new GameConfig(rules, D0, D1, seed, CardCatalog.All);
    }

    [TestFixture]
    public class CreateGameTests
    {
        [Test]
        public void StartsWithAnEmptyBoardFiveCardHandsAndTheSeedChosenFirstPlayer()
        {
            var s = GameEngine.CreateGame(Decks.Config(RuleSet.None, 42));
            Assert.That(s.Board.ToArray(), Is.EqualTo(new PlacedCard?[9]));
            Assert.That(s.GetHand(Player.P0).ToArray(), Is.EqualTo(Decks.D0));
            Assert.That(s.GetHand(Player.P1).ToArray(), Is.EqualTo(Decks.D1));
            Assert.That(s.Turn, Is.EqualTo(s.FirstPlayer));
            Assert.That(s.Phase, Is.EqualTo(Phase.Playing));
            Assert.That(s.SuddenDeathRound, Is.EqualTo(0));
            Assert.That(new[] { s.GetPivotTokens(Player.P0), s.GetPivotTokens(Player.P1) }, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(s.Cards.Keys.OrderBy(k => k, System.StringComparer.Ordinal),
                Is.EqualTo(Decks.D0.Concat(Decks.D1).OrderBy(k => k, System.StringComparer.Ordinal)));
            Assert.That(GameEngine.Score(s), Is.EqualTo((5, 5)));
            Assert.That(GameEngine.Result(s), Is.Null);
        }

        [Test]
        public void GivesTwoPivotTokensEachWhenPivotIsOn()
        {
            var s = GameEngine.CreateGame(Decks.Config(new RuleSet(pivot: true), 1));
            Assert.That(new[] { s.GetPivotTokens(Player.P0), s.GetPivotTokens(Player.P1) }, Is.EqualTo(new[] { 2, 2 }));
        }

        [Test]
        public void PicksTheFirstPlayerAtRandomFromTheSeed()
        {
            // both happen, roughly evenly
            int p0 = 0;
            for (int seed = 0; seed < 2000; seed++)
            {
                if (GameEngine.CreateGame(Decks.Config(RuleSet.None, seed)).FirstPlayer == Player.P0) p0++;
            }
            Assert.That(p0, Is.GreaterThan(900));
            Assert.That(p0, Is.LessThan(1100));
        }

        [Test]
        public void RandomDrawsFiveDistinctDeckPositionsDeterministicallyFromTheSeed()
        {
            var big0 = Decks.Slice(0, 12);
            var big1 = Decks.Slice(20, 32);
            var rules = new RuleSet(random: true);
            var hands = new HashSet<string>();
            for (int seed = 0; seed < 50; seed++)
            {
                var a = GameEngine.CreateGame(new GameConfig(rules, big0, big1, seed, CardCatalog.All));
                var b = GameEngine.CreateGame(new GameConfig(rules, big0, big1, seed, CardCatalog.All));
                Assert.That(a.GetHand(Player.P0).ToArray(), Is.EqualTo(b.GetHand(Player.P0).ToArray()));
                Assert.That(a.GetHand(Player.P1).ToArray(), Is.EqualTo(b.GetHand(Player.P1).ToArray()));
                foreach (var (p, big) in new[] { (Player.P0, big0), (Player.P1, big1) })
                {
                    var hand = a.GetHand(p).ToArray();
                    Assert.That(hand, Has.Length.EqualTo(GameConstants.HandSize));
                    Assert.That(new HashSet<string>(hand).Count, Is.EqualTo(GameConstants.HandSize));
                    foreach (var id in hand) Assert.That(big, Does.Contain(id));
                }
                hands.Add(string.Join(",", a.GetHand(Player.P0)));
            }
            Assert.That(hands.Count, Is.GreaterThan(40));
        }

        [Test]
        public void RejectsBadConfigs()
        {
            Assert.Throws<InvalidConfigException>(() =>
                GameEngine.CreateGame(new GameConfig(RuleSet.None, Decks.Slice(0, 4), Decks.Slice(5, 10), 1, CardCatalog.All)));
            Assert.Throws<InvalidConfigException>(() =>
                GameEngine.CreateGame(new GameConfig(RuleSet.None, Decks.Slice(0, 6), Decks.Slice(5, 10), 1, CardCatalog.All)));
            Assert.Throws<InvalidConfigException>(() =>
                GameEngine.CreateGame(new GameConfig(new RuleSet(random: true), Decks.Slice(0, 4), Decks.Slice(5, 10), 1, CardCatalog.All)));
            Assert.Throws<InvalidConfigException>(() =>
                GameEngine.CreateGame(new GameConfig(RuleSet.None, new[] { "nope" }.Concat(Decks.Slice(0, 4)).ToArray(), Decks.Slice(5, 10), 1,
                    CardCatalog.All)));
        }

        [Test]
        public void ResonanceAndTraitsAreAcceptedSinceTheyAreImplemented()
        {
            Assert.DoesNotThrow(() => GameEngine.CreateGame(Decks.Config(new RuleSet(resonance: true), 1)));
            Assert.DoesNotThrow(() => GameEngine.CreateGame(Decks.Config(new RuleSet(traits: true), 1)));
            Assert.DoesNotThrow(() => GameEngine.CreateGame(Decks.Config(new RuleSet(resonance: true, traits: true), 1)));
        }

        [Test]
        public void AcceptsTheCardDatabaseAsADictionaryToo()
        {
            var db = CardCatalog.All.ToDictionary(c => c.Id);
            var a = GameEngine.CreateGame(new GameConfig(RuleSet.None, Decks.D0, Decks.D1, 7, db));
            var b = GameEngine.CreateGame(new GameConfig(RuleSet.None, Decks.D0, Decks.D1, 7, CardCatalog.All));
            Assert.That(a, Is.EqualTo(b));
        }
    }

    [TestFixture]
    public class OpenRuleTests
    {
        static GameState S() => GameEngine.CreateGame(Decks.Config(RuleSet.None, 3));

        [Test]
        public void HidesTheOpponentHandContentsWhenOff()
        {
            // count only
            Assert.That(GameEngine.VisibleHand(S(), Player.P0, Player.P0), Is.EqualTo(Decks.D0));
            Assert.That(GameEngine.VisibleHand(S(), Player.P0, Player.P1), Is.EqualTo(new string[] { null, null, null, null, null }));
        }

        [Test]
        public void ShowsBothHandsWhenOn()
        {
            var open = S().WithRules(new RuleSet(open: true));
            Assert.That(GameEngine.VisibleHand(open, Player.P0, Player.P1), Is.EqualTo(Decks.D1));
        }
    }

    [TestFixture]
    public class ScoreAndEndTests
    {
        /// <summary>すべて 1 のカードで、裏返りが起きない 1 局を流す。</summary>
        static (GameState State, List<GameEvent> Events) PlayNoFlipGame(int firstPlayer)
        {
            var cards = Enumerable.Range(0, 10).Select(i => MkCard("k" + i, 1, 1, 1, 1)).ToArray();
            var state = MakeState(new Setup
            {
                Cards = cards,
                Hands = new[] { cards.Take(5).Select(c => c.Id).ToArray(), cards.Skip(5).Select(c => c.Id).ToArray() },
                Turn = firstPlayer,
                FirstPlayer = firstPlayer,
            });
            var events = new List<GameEvent>();
            for (int i = 0; i < 9; i++)
            {
                var r = GameEngine.ApplyMove(state, GameEngine.LegalMoves(state)[0]);
                state = r.State;
                events.AddRange(r.Events);
            }
            return (state, events);
        }

        [Test]
        public void CountsBoardCardsPlusTheSecondPlayersRemainingHandCard()
        {
            // total 10
            foreach (var first in new[] { 0, 1 })
            {
                var (state, events) = PlayNoFlipGame(first);
                var second = 1 - first;
                Assert.That(state.Phase, Is.EqualTo(Phase.Ended));
                Assert.That(state.GetHand((Player)first).Count, Is.EqualTo(0));
                Assert.That(state.GetHand((Player)second).Count, Is.EqualTo(1));
                Assert.That(state.Board.Count(pc => pc.HasValue && (int)pc.Value.Owner == first), Is.EqualTo(5));
                Assert.That(state.Board.Count(pc => pc.HasValue && (int)pc.Value.Owner == second), Is.EqualTo(4));
                Assert.That(GameEngine.Score(state), Is.EqualTo((5, 5)));
                Assert.That(GameEngine.Result(state), Is.EqualTo(GameResult.Draw));
                Assert.That(events[events.Count - 1], Is.EqualTo(new EndEvent((5, 5), GameResult.Draw)));
                Assert.That(events.Count(e => e is TurnEvent), Is.EqualTo(8));
            }
        }

        [Test]
        public void DeclaresTheWinnerByScore()
        {
            var cards = new[] { MkCard("W", 9, 9, 9, 9), MkCard("L", 1, 1, 1, 1) };
            // 8 マス埋まった局面で p0 が最後の 1 枚を置き、上（5）と左（7）の相手カードを取る
            var s = MakeState(new Setup
            {
                Cards = cards,
                Board = { { 0, "L", 0 }, { 1, "L", 1 }, { 2, "L", 0 }, { 3, "L", 1 }, { 4, "L", 0 }, { 5, "L", 1 }, { 6, "L", 0 }, { 7, "L", 1 } },
                Hands = new[] { new[] { "W" }, new[] { "L" } },
            });
            var r = GameEngine.ApplyMove(s, new Move(0, 8, 0));
            // p0: 盤上 4 + 置いた 1 + 取った 2 = 7、p1: 盤上 2 + 手札 1 = 3
            Assert.That(GameEngine.Score(r.State), Is.EqualTo((7, 3)));
            Assert.That(GameEngine.Result(r.State), Is.EqualTo(GameResult.P0));
            Assert.That(r.Events[r.Events.Count - 1], Is.EqualTo(new EndEvent((7, 3), GameResult.P0)));
        }
    }

    [TestFixture]
    public class SuddenDeathTests
    {
        // p0 先攻。8 マス埋まって p0 / p1 が 4 枚ずつ所有、p0 が最後の 1 枚を置くと 5-5 の引き分け。
        // 所有権の移動を確認するため、マス 3 の b0 は p0 所有、マス 4 の a3 は p1 所有にしておく。
        static readonly CardDef[] Cards =
            new[] { "a0", "a1", "a2", "a3", "a4", "b0", "b1", "b2", "b3", "b4" }.Select(id => MkCard(id, 1, 1, 1, 1)).ToArray();

        static GameState LastMoveDraw(bool suddenDeath, int round = 0) => MakeState(new Setup
        {
            Cards = Cards,
            Rules = new RuleSet(suddenDeath: suddenDeath, pivot: true),
            Board =
            {
                { 0, "a0", 0 }, { 1, "a1", 0 }, { 2, "a2", 0 }, { 3, "b0", 0 },
                { 4, "a3", 1 }, { 5, "b1", 1 }, { 6, "b2", 1 }, { 7, "b3", 1 },
            },
            Hands = new[] { new[] { "a4" }, new[] { "b4" } },
            Turn = 0,
            FirstPlayer = 0,
            PivotTokens = new[] { 1, 0 },
            SuddenDeathRound = round,
        });

        [Test]
        public void RestartsWithTheCardsEachPlayerOwnsWhenTheGameIsADraw()
        {
            var r = GameEngine.ApplyMove(LastMoveDraw(true), new Move(0, 8, 0));
            var state = r.State;
            Assert.That(state.Phase, Is.EqualTo(Phase.Playing));
            Assert.That(state.SuddenDeathRound, Is.EqualTo(1));
            Assert.That(state.Board.ToArray(), Is.EqualTo(new PlacedCard?[9]));
            Assert.That(state.GetHand(Player.P0).ToArray(), Is.EqualTo(new[] { "a0", "a1", "a2", "b0", "a4" }));
            Assert.That(state.GetHand(Player.P1).ToArray(), Is.EqualTo(new[] { "a3", "b1", "b2", "b3", "b4" }));
            // 先攻は交代、Pivot トークンは持ち越し
            Assert.That(state.FirstPlayer, Is.EqualTo(Player.P1));
            Assert.That(state.Turn, Is.EqualTo(Player.P1));
            Assert.That(new[] { state.GetPivotTokens(Player.P0), state.GetPivotTokens(Player.P1) }, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(r.Events, Is.EqualTo(new GameEvent[]
            {
                new PlaceEvent(Player.P0, 8, "a4", 0),
                new SuddenDeathEvent(1),
                new TurnEvent(Player.P1),
            }));
            Assert.That(GameEngine.ApplyMoveFast(LastMoveDraw(true), new Move(0, 8, 0)), Is.EqualTo(state));
        }

        [Test]
        public void StopsAfterTheFifthReplayAndEndsAsADraw()
        {
            var r = GameEngine.ApplyMove(LastMoveDraw(true, GameConstants.MaxSuddenDeathRounds), new Move(0, 8, 0));
            Assert.That(r.State.Phase, Is.EqualTo(Phase.Ended));
            Assert.That(GameEngine.Result(r.State), Is.EqualTo(GameResult.Draw));
            Assert.That(r.Events[r.Events.Count - 1], Is.EqualTo(new EndEvent((5, 5), GameResult.Draw)));
        }

        [Test]
        public void EndsAsADrawWhenTheRuleIsOff()
        {
            var state = GameEngine.ApplyMove(LastMoveDraw(false), new Move(0, 8, 0)).State;
            Assert.That(state.Phase, Is.EqualTo(Phase.Ended));
            Assert.That(GameEngine.Result(state), Is.EqualTo(GameResult.Draw));
        }

        [Test]
        public void ReplaysUpToFiveTimesInAnAllDrawGameAndThenEnds()
        {
            // 全カード 1 なので毎ラウンド 5-5 の引き分けになる
            var all = Enumerable.Range(0, 10).Select(i => MkCard("k" + i, 1, 1, 1, 1)).ToArray();
            var state = GameEngine.CreateGame(new GameConfig(new RuleSet(suddenDeath: true),
                all.Take(5).Select(c => c.Id).ToArray(), all.Skip(5).Select(c => c.Id).ToArray(), 9, all));
            var rounds = new List<int>();
            int moves = 0;
            while (state.Phase == Phase.Playing)
            {
                var r = GameEngine.ApplyMove(state, GameEngine.LegalMoves(state)[0]);
                rounds.AddRange(r.Events.OfType<SuddenDeathEvent>().Select(e => e.Round));
                state = r.State;
                moves++;
            }
            Assert.That(rounds, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(moves, Is.EqualTo(9 * 6));
            Assert.That(GameEngine.Result(state), Is.EqualTo(GameResult.Draw));
        }
    }
}
