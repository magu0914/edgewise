using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Edgewise.Core.Data;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // 移植元: tripletriad-ts-legacy/tests/engine/determinism.test.ts

    [TestFixture]
    public class DeterminismAndHashTests
    {
        static List<string> Replay(int seed)
        {
            var r = new TestRandom(1000 + seed);
            var config = RandomConfig(r, RandomRules(r));
            var state = GameEngine.CreateGame(new GameConfig(config.Rules, config.Deck0, config.Deck1, seed, config.Cards));
            var hashes = new List<string> { GameEngine.HashState(state) };
            while (state.Phase == Phase.Playing)
            {
                state = GameEngine.ApplyMove(state, r.Pick(GameEngine.LegalMoves(state))).State;
                hashes.Add(GameEngine.HashState(state));
            }
            return hashes;
        }

        [Test]
        public void SameSeedAndSameMovesGiveTheSameHashSequence()
        {
            for (int seed = 0; seed < 200; seed++) Assert.That(Replay(seed), Is.EqualTo(Replay(seed)));
        }

        [Test]
        public void HashIsA16DigitHexStringAndDiffersBetweenDifferentPositions()
        {
            var hex = new Regex("^[0-9a-f]{16}$");
            var seen = new HashSet<string>();
            var serialized = new HashSet<string>();
            for (int seed = 0; seed < 300; seed++)
            {
                var r = new TestRandom(seed);
                var state = NewRandomGame(r);
                while (state.Phase == Phase.Playing)
                {
                    var hash = GameEngine.HashState(state);
                    Assert.That(hex.IsMatch(hash), Is.True, hash);
                    var key = StateHash.Serialize(state);
                    if (serialized.Add(key)) seen.Add(hash);
                    state = GameEngine.ApplyMove(state, r.Pick(GameEngine.LegalMoves(state))).State;
                }
            }
            // 異なる局面は（実用上）異なるハッシュ
            Assert.That(seen.Count, Is.EqualTo(serialized.Count));
        }

        [Test]
        public void HashDependsOnTheSeedEvenForTheSameDecks()
        {
            var d0 = CardCatalog.All.Take(5).Select(c => c.Id).ToArray();
            var d1 = CardCatalog.All.Skip(5).Take(5).Select(c => c.Id).ToArray();
            var rules = new RuleSet(elemental: true);
            var hashes = new HashSet<string>();
            for (int seed = 0; seed < 100; seed++)
                hashes.Add(GameEngine.HashState(GameEngine.CreateGame(new GameConfig(rules, d0, d1, seed, CardCatalog.All))));
            Assert.That(hashes.Count, Is.EqualTo(100));
        }

        [Test]
        public void HashStateIsAStructuralFunction()
        {
            // clones hash the same
            var s = NewRandomGame(new TestRandom(5));
            Assert.That(GameEngine.HashState(Clone(s)), Is.EqualTo(GameEngine.HashState(s)));
        }
    }

    [TestFixture]
    public class FastMatchesReferenceTests
    {
        [Test]
        public void ProducesIdenticalStatesOnThousandsOfRandomPositions()
        {
            int positions = 0;
            for (int game = 0; game < 3000; game++)
            {
                var r = new TestRandom(0xfa57 + game);
                var start = NewRandomGame(r);
                PlayRandom(start, r, (s, m) =>
                {
                    var reference = GameEngine.ApplyMove(s, m).State;
                    var fast = GameEngine.ApplyMoveFast(s, m);
                    Assert.That(fast, Is.EqualTo(reference));
                    Assert.That(GameEngine.HashState(fast), Is.EqualTo(GameEngine.HashState(reference)));
                    positions++;
                    return reference;
                });
            }
            Assert.That(positions, Is.GreaterThan(27000));
        }

        [Test]
        public void AlsoMatchesWhenSamePlusComboElementalAreAllOn()
        {
            for (int game = 0; game < 1000; game++)
            {
                var r = new TestRandom(0xc0b0 + game);
                var start = NewRandomGame(r, same: true, sameWall: true, plus: true, combo: true, elemental: true, pivot: true);
                PlayRandom(start, r, (s, m) =>
                {
                    var reference = GameEngine.ApplyMove(s, m).State;
                    Assert.That(GameEngine.ApplyMoveFast(s, m), Is.EqualTo(reference));
                    return reference;
                });
            }
        }
    }

    [TestFixture]
    public class SmokeSelfPlayTests
    {
        /// <summary>イベント列を再生して盤面の所有者を復元できること（UI がイベントだけで演出できることの確認）。</summary>
        static int?[] ReplayOwners(GameState before, IReadOnlyList<GameEvent> events)
        {
            var owners = Owners(before);
            foreach (var e in events)
            {
                switch (e)
                {
                    case PlaceEvent p:
                        owners[p.Cell] = (int)p.Player;
                        break;
                    case FlipEvent f:
                        Assert.That(owners[f.Cell], Is.Not.EqualTo((int)f.To));
                        owners[f.Cell] = (int)f.To;
                        break;
                    case SuddenDeathEvent _:
                        for (int i = 0; i < owners.Length; i++) owners[i] = null;
                        break;
                }
            }
            return owners;
        }

        [Test]
        public void TenThousandRandomGamesWithoutSuddenDeathAlwaysEndInExactlyNineMoves()
        {
            var results = new Dictionary<GameResult, int> { [GameResult.P0] = 0, [GameResult.P1] = 0, [GameResult.Draw] = 0 };
            for (int game = 0; game < 10000; game++)
            {
                var r = new TestRandom(game);
                var state = NewRandomGame(r, suddenDeath: false);
                int moves = 0;
                IReadOnlyList<GameEvent> lastEvents = new List<GameEvent>();
                while (state.Phase == Phase.Playing)
                {
                    var before = state;
                    var res = GameEngine.ApplyMove(state, r.Pick(GameEngine.LegalMoves(state)));
                    Assert.That(ReplayOwners(before, res.Events), Is.EqualTo(Owners(res.State)));
                    state = res.State;
                    lastEvents = res.Events;
                    moves++;
                }
                Assert.That(moves, Is.EqualTo(9));
                var s = GameEngine.Score(state);
                Assert.That(s.P0 + s.P1, Is.EqualTo(10));
                var result = GameEngine.Result(state).Value;
                Assert.That(lastEvents[lastEvents.Count - 1], Is.EqualTo(new EndEvent(s, result)));
                results[result]++;
            }
            Assert.That(results.Values.Sum(), Is.EqualTo(10000));
            Assert.That(results[GameResult.Draw], Is.GreaterThan(0));
        }

        [Test]
        public void WithSuddenDeathGamesEndAfterNineTimesRoundsPlusOneMovesAndAtMostFiveReplays()
        {
            int replays = 0;
            for (int game = 0; game < 2000; game++)
            {
                var r = new TestRandom(0x5d + game);
                var (state, moves) = PlayRandom(NewRandomGame(r, suddenDeath: true), r, (s, m) => GameEngine.ApplyMove(s, m).State);
                Assert.That(state.SuddenDeathRound, Is.LessThanOrEqualTo(5));
                Assert.That(moves, Is.EqualTo(9 * (state.SuddenDeathRound + 1)));
                if (state.SuddenDeathRound < 5) Assert.That(GameEngine.Result(state), Is.Not.EqualTo(GameResult.Draw));
                replays += state.SuddenDeathRound;
            }
            Assert.That(replays, Is.GreaterThan(0));
        }
    }
}
