using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // 盤面のマス番号
    // 0 1 2
    // 3 4 5
    // 6 7 8
    // 移植元: tripletriad-ts-legacy/tests/engine/basic.test.ts

    [TestFixture]
    public class BoardGeometryTests
    {
        const int W = GameConstants.Wall;

        [Test]
        public void NeighborsAreListedTopRightBottomLeftAndNeverWrapAround()
        {
            Assert.That(Board.NeighborsOf(0), Is.EqualTo(new[] { W, 1, 3, W }));
            Assert.That(Board.NeighborsOf(2), Is.EqualTo(new[] { W, W, 5, 1 }));
            Assert.That(Board.NeighborsOf(3), Is.EqualTo(new[] { 0, 4, 6, W }));
            Assert.That(Board.NeighborsOf(4), Is.EqualTo(new[] { 1, 5, 7, 3 }));
            Assert.That(Board.NeighborsOf(5), Is.EqualTo(new[] { 2, W, 8, 4 }));
            Assert.That(Board.NeighborsOf(8), Is.EqualTo(new[] { 5, W, W, 7 }));
        }
    }

    [TestFixture]
    public class BasicComparisonTests
    {
        static readonly CardDef P = MkCard("P", 5, 5, 5, 5);
        static readonly CardDef Above = MkCard("A", 1, 1, 4, 1); // bottom 4 < 5
        static readonly CardDef RightCard = MkCard("B", 1, 1, 1, 5); // left 5 == 5
        static readonly CardDef Below = MkCard("C", 6, 1, 1, 1); // top 6 > 5
        static readonly CardDef OwnLeft = MkCard("D", 1, 1, 1, 1); // 自分のカード（right 1 < 5）

        static GameState State() => MakeState(new Setup
        {
            Cards = new[] { P, Above, RightCard, Below, OwnLeft },
            Board = { { 1, "A", 1 }, { 5, "B", 1 }, { 7, "C", 1 }, { 3, "D", 0 } },
            Hands = new[] { new[] { "P" }, new[] { "Q" } },
        });

        [Test]
        public void FlipsOnlyStrictlySmallerOpponentCardsNeverFlipsOwnCards()
        {
            var r = GameEngine.ApplyMove(State(), new Move(0, 4, 0));
            Assert.That(Owners(r.State), Is.EqualTo(Owners(null, 0, null, 0, 0, 1, null, 1, null)));
            Assert.That(r.Events, Is.EqualTo(new GameEvent[]
            {
                new PlaceEvent(Player.P0, 4, "P", 0),
                new FlipEvent(1, Player.P0, FlipCause.Basic, 0, 4),
                new TurnEvent(Player.P1),
            }));
        }

        [Test]
        public void RemovesTheCardFromTheHandAndPassesTheTurn()
        {
            var next = GameEngine.ApplyMove(State(), new Move(0, 4, 0)).State;
            Assert.That(next.GetHand(Player.P0).ToArray(), Is.Empty);
            Assert.That(next.GetHand(Player.P1).ToArray(), Is.EqualTo(new[] { "Q" }));
            Assert.That(next.Turn, Is.EqualTo(Player.P1));
            Assert.That(next.Board[4], Is.EqualTo(new PlacedCard(P, Player.P0, 0)));
        }

        [Test]
        public void DoesNotMutateTheInputState()
        {
            // TS 版は deepFreeze した入力で検査していた。C# の GameState は外から変更できないので、
            // 適用前後で正規化文字列と構造が変わらないことを確かめる。
            var state = State();
            var snapshot = Clone(state);
            var serialized = StateHash.Serialize(state);
            GameEngine.ApplyMove(state, new Move(0, 4, 0));
            GameEngine.ApplyMoveFast(state, new Move(0, 4, 0));
            Assert.That(state, Is.EqualTo(snapshot));
            Assert.That(StateHash.Serialize(state), Is.EqualTo(serialized));
        }
    }

    [TestFixture]
    public class BoardEdgeTests
    {
        static readonly CardDef Strong = MkCard("S", 10, 10, 10, 10);
        static readonly CardDef Weak = MkCard("W", 1, 1, 1, 1);

        [Test]
        public void Cell2AndCell3AreNotNeighbors()
        {
            var s1 = MakeState(new Setup { Cards = new[] { Strong, Weak }, Board = { { 2, "W", 1 } }, Hands = new[] { new[] { "S" }, new[] { "W" } } });
            var r1 = GameEngine.ApplyMove(s1, new Move(0, 3, 0));
            Assert.That(Owners(r1.State)[2], Is.EqualTo(1));
            Assert.That(Flips(r1.Events), Is.Empty);

            var s2 = MakeState(new Setup { Cards = new[] { Strong, Weak }, Board = { { 3, "W", 1 } }, Hands = new[] { new[] { "S" }, new[] { "W" } } });
            var r2 = GameEngine.ApplyMove(s2, new Move(0, 2, 0));
            Assert.That(Owners(r2.State)[3], Is.EqualTo(1));
        }

        [Test]
        public void CornerPlacementOnlyChecksTheTwoOnBoardNeighbors()
        {
            var s = MakeState(new Setup
            {
                Cards = new[] { Strong, Weak },
                Board = { { 1, "W", 1 }, { 3, "W", 1 }, { 4, "W", 1 }, { 8, "W", 1 } },
                Hands = new[] { new[] { "S" }, new[] { "W" } },
            });
            var events = GameEngine.ApplyMove(s, new Move(0, 0, 0)).Events;
            Assert.That(Flips(events).Select(f => f.Cell), Is.EqualTo(new[] { 1, 3 }));
        }
    }

    [TestFixture]
    public class RotationTests
    {
        [Test]
        public void Rotation1Is90DegreesClockwiseTheOriginalLeftSideComesToTheTop()
        {
            var sides = new Sides(1, 2, 3, 4);
            Assert.That(Board.RotateSides(sides, 0), Is.EqualTo(new Sides(1, 2, 3, 4)));
            Assert.That(Board.RotateSides(sides, 1), Is.EqualTo(new Sides(4, 1, 2, 3)));
            Assert.That(Board.RotateSides(sides, 2), Is.EqualTo(new Sides(3, 4, 1, 2)));
            Assert.That(Board.RotateSides(sides, 3), Is.EqualTo(new Sides(2, 3, 4, 1)));
        }

        static readonly CardDef P = MkCard("P", 1, 1, 1, 9); // left 9
        static readonly CardDef A = MkCard("A", 1, 1, 5, 1); // bottom 5

        [Test]
        public void ARotatedCardComparesWithItsRotatedSidesAndConsumesAToken()
        {
            var s = MakeState(new Setup
            {
                Cards = new[] { P, A },
                Rules = new RuleSet(pivot: true),
                Board = { { 1, "A", 1 } },
                Hands = new[] { new[] { "P" }, new[] { "Q" } },
            });
            var plain = GameEngine.ApplyMove(s, new Move(0, 4, 0));
            Assert.That(Owners(plain.State)[1], Is.EqualTo(1));
            Assert.That(new[] { plain.State.GetPivotTokens(Player.P0), plain.State.GetPivotTokens(Player.P1) }, Is.EqualTo(new[] { 2, 2 }));

            var rotated = GameEngine.ApplyMove(s, new Move(0, 4, 1));
            Assert.That(Owners(rotated.State)[1], Is.EqualTo(0));
            Assert.That(rotated.State.Board[4], Is.EqualTo(new PlacedCard(P, Player.P0, 1)));
            Assert.That(new[] { rotated.State.GetPivotTokens(Player.P0), rotated.State.GetPivotTokens(Player.P1) }, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(rotated.Events[0], Is.EqualTo(new PlaceEvent(Player.P0, 4, "P", 1)));
        }

        [Test]
        public void APlacedRotatedCardKeepsItsRotationWhenComparedAsANeighbor()
        {
            // 盤上の A は rotation=2 なので bottom = 元の top = 1
            var s = MakeState(new Setup
            {
                Cards = new[] { MkCard("P", 2, 1, 1, 1), MkCard("A", 1, 1, 5, 1) },
                Rules = new RuleSet(pivot: true),
                Board = { { 1, "A", 1, 2 } },
                Hands = new[] { new[] { "P" }, new[] { "Q" } },
            });
            Assert.That(Owners(GameEngine.ApplyMove(s, new Move(0, 4, 0)).State)[1], Is.EqualTo(0));
        }

        [Test]
        public void LegalMovesIncludesRotationsOnlyWhilePivotIsOnAndTokensRemain()
        {
            var cards = new[] { MkCard("a", 1, 1, 1, 1) };
            var hands = new[] { new[] { "a", "a", "a", "a", "a" }, new[] { "a", "a", "a", "a", "a" } };
            Assert.That(GameEngine.LegalMoves(MakeState(new Setup { Cards = cards, Hands = hands })).Count, Is.EqualTo(5 * 9));
            Assert.That(GameEngine.LegalMoves(MakeState(new Setup { Cards = cards, Hands = hands, Rules = new RuleSet(pivot: true) })).Count,
                Is.EqualTo(5 * 9 * 4));
            var noTokens = GameEngine.LegalMoves(MakeState(new Setup
            {
                Cards = cards, Hands = hands, Rules = new RuleSet(pivot: true), PivotTokens = new[] { 0, 2 },
            }));
            Assert.That(noTokens.Count, Is.EqualTo(5 * 9));
            Assert.That(noTokens.All(m => m.Rotation == 0), Is.True);
        }
    }

    [TestFixture]
    public class IllegalMoveTests
    {
        static readonly CardDef[] Cards = { MkCard("P", 1, 1, 1, 1), MkCard("A", 1, 1, 1, 1) };

        static GameState Base() => MakeState(new Setup { Cards = Cards, Board = { { 4, "A", 1 } }, Hands = new[] { new[] { "P", "P" }, new[] { "A" } } });

        static GameState PivotNoTokens() => MakeState(new Setup
        {
            Cards = Cards, Board = { { 4, "A", 1 } }, Hands = new[] { new[] { "P" }, new[] { "A" } }, Rules = new RuleSet(pivot: true),
            PivotTokens = new[] { 0, 2 },
        });

        static GameState Ended() => Base().WithPhase(Phase.Ended);

        static IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData("base", new Move(0, 4, 0)).SetName("occupied cell");
            yield return new TestCaseData("base", new Move(2, 0, 0)).SetName("hand index too large");
            yield return new TestCaseData("base", new Move(-1, 0, 0)).SetName("negative hand index");
            // TS 版の「fractional hand index（0.5）」は C# の int では表現できないので、int の最大値で代替する
            yield return new TestCaseData("base", new Move(int.MaxValue, 0, 0)).SetName("hand index int.MaxValue (replaces fractional hand index)");
            yield return new TestCaseData("base", new Move(0, 9, 0)).SetName("cell out of board");
            yield return new TestCaseData("base", new Move(0, -1, 0)).SetName("negative cell");
            yield return new TestCaseData("base", new Move(0, 0, 1)).SetName("rotation while Pivot is off");
            yield return new TestCaseData("pivot", new Move(0, 0, 3)).SetName("rotation without tokens");
            yield return new TestCaseData("pivot", new Move(0, 0, 4)).SetName("invalid rotation value");
            yield return new TestCaseData("ended", new Move(0, 0, 0)).SetName("move after the game ended");
        }

        static GameState StateFor(string name) => name == "base" ? Base() : name == "pivot" ? PivotNoTokens() : Ended();

        [TestCaseSource(nameof(Cases))]
        public void Throws(string stateName, Move move)
        {
            var state = StateFor(stateName);
            Assert.Throws<IllegalMoveException>(() => GameEngine.ApplyMove(state, move));
            Assert.Throws<IllegalMoveException>(() => GameEngine.ApplyMoveFast(state, move));
            Assert.That(GameEngine.IsLegalMove(state, move), Is.False);
        }

        [Test]
        public void LegalMovesIsEmptyAfterTheGameEnded()
        {
            Assert.That(GameEngine.LegalMoves(Ended()), Is.Empty);
        }
    }
}
