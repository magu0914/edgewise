using System.Linq;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using NUnit.Framework;

namespace Edgewise.Core.Tests.AI
{
    /// <summary>探索用の make / unmake（SearchBoard）が ApplyMoveFast と同じ結果になることの検査（DESIGN §4-4 の一致保証を AI 側にも）。</summary>
    [TestFixture]
    public class SearchBoardTests
    {
        internal static void CheckAllMoves(GameState s)
        {
            var b = AiH.LoadTrue(s);
            Assert.That(b.ComputeHashSlow(), Is.EqualTo(b.Hash));
            ulong hash0 = b.Hash;
            int occ0 = b.Occupied, own0 = b.OwnerP1, turn0 = b.Turn;
            int hm0 = b.HandMask[0], hm1 = b.HandMask[1], pv0 = b.Pivot[0], pv1 = b.Pivot[1];

            foreach (var m in GameEngine.LegalMoves(s))
            {
                var expected = GameEngine.ApplyMoveFast(s, m);
                int code = SearchBoard.Code(m.HandIndex, m.Cell, m.Rotation);
                int flipped = b.Make(code);
                Assert.That(b.ComputeHashSlow(), Is.EqualTo(b.Hash), "incremental hash");

                // Sudden Death の再戦が始まると盤面が空になるので、その場合は盤面の比較をしない
                if (expected.SuddenDeathRound == s.SuddenDeathRound)
                {
                    for (int c = 0; c < 9; c++)
                    {
                        var pc = expected.Board[c];
                        int owner = pc.HasValue ? (int)pc.Value.Owner : -1;
                        Assert.That(b.OwnerAt(c), Is.EqualTo(owner), "owner of cell " + c + " after " + m + " in " + StateHash.Serialize(s));
                    }
                    var sc = GameEngine.Score(expected);
                    Assert.That(b.ScoreDiffP0(), Is.EqualTo(sc.P0 - sc.P1));
                }
                Assert.That(b.Pivot[0], Is.EqualTo(expected.GetPivotTokens(Player.P0)));
                Assert.That(b.Pivot[1], Is.EqualTo(expected.GetPivotTokens(Player.P1)));
                Assert.That(b.Turn, Is.EqualTo(turn0 ^ 1));

                b.Unmake(code, flipped);
                Assert.That(b.Hash, Is.EqualTo(hash0));
                Assert.That(b.Occupied, Is.EqualTo(occ0));
                Assert.That(b.OwnerP1, Is.EqualTo(own0));
                Assert.That(b.Turn, Is.EqualTo(turn0));
                Assert.That(b.HandMask[0], Is.EqualTo(hm0));
                Assert.That(b.HandMask[1], Is.EqualTo(hm1));
                Assert.That(b.Pivot[0], Is.EqualTo(pv0));
                Assert.That(b.Pivot[1], Is.EqualTo(pv1));
            }
        }

        [Test]
        public void MakeUnmakeMatchesApplyMoveFastOnRandomPositions()
        {
            int positions = 0;
            for (int g = 0; g < 700; g++)
            {
                var r = new TestRandom(0xa1a1 + g);
                var state = H.NewRandomGame(r);
                while (state.Phase == Phase.Playing)
                {
                    CheckAllMoves(state);
                    positions++;
                    state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
                }
            }
            Assert.That(positions, Is.GreaterThan(5000));
        }

        [Test]
        public void MakeUnmakeMatchesApplyMoveFastWithStressCards()
        {
            // 辺の値を 3〜6 と A に寄せたカード（Same / Plus / Combo / Same Wall が頻発する）
            var r0 = new TestRandom(0x5eed);
            var values = new[] { 3, 4, 5, 6, 10 };
            var cards = Enumerable.Range(0, 16).Select(i =>
            {
                int V() => values[r0.NextInt(values.Length)];
                Element? e = i % 4 == 3 ? (Element?)null : (Element)r0.NextInt(8);
                return new CardDef("s" + i, "S" + i, 1, new Sides(V(), V(), V(), V()), e);
            }).ToArray();
            for (int g = 0; g < 700; g++)
            {
                var r = new TestRandom(0xb2b2 + g);
                var state = GameEngine.CreateGame(H.RandomConfig(r, H.RandomRules(r), cards));
                while (state.Phase == Phase.Playing)
                {
                    CheckAllMoves(state);
                    state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
                }
            }
        }

        [Test]
        public void PlayingAWholeRoundIncrementallyMatchesTheEngine()
        {
            for (int g = 0; g < 2000; g++)
            {
                var r = new TestRandom(0xc3c3 + g);
                var state = H.NewRandomGame(r, suddenDeath: false);
                var b = AiH.LoadTrue(state);
                while (state.Phase == Phase.Playing)
                {
                    var m = r.Pick(GameEngine.LegalMoves(state));
                    b.Make(SearchBoard.Code(AiH.SlotOf(b, (int)state.Turn, m.HandIndex), m.Cell, m.Rotation));
                    state = GameEngine.ApplyMoveFast(state, m);
                    for (int c = 0; c < 9; c++)
                    {
                        var pc = state.Board[c];
                        Assert.That(b.OwnerAt(c), Is.EqualTo(pc.HasValue ? (int)pc.Value.Owner : -1));
                    }
                    Assert.That(b.ComputeHashSlow(), Is.EqualTo(b.Hash));
                }
                var sc = GameEngine.Score(state);
                Assert.That(b.ScoreDiffP0(), Is.EqualTo(sc.P0 - sc.P1));
            }
        }

        [Test]
        public void TerminalValueEncodesTheScoreDifference()
        {
            for (int diff = -8; diff <= 8; diff += 2)
            {
                int v = SearchBoard.TerminalValueOfDiff(diff);
                Assert.That(SearchSession.TryDecodeTerminalValue(v, out int d), Is.True);
                Assert.That(d, Is.EqualTo(diff));
            }
            Assert.That(SearchSession.TryDecodeTerminalValue(SearchConstants.WinBonus, out _), Is.False);
            Assert.That(SearchSession.TryDecodeTerminalValue(123, out _), Is.False);
        }

        [Test]
        public void ZobristTableIsDeterministicAndHasNoDuplicates()
        {
            var keys = new System.Collections.Generic.HashSet<ulong>();
            for (int cell = 0; cell < 9; cell++)
                for (int card = 0; card < SearchBoard.MaxCards; card++)
                    for (int rot = 0; rot < 4; rot++)
                        Assert.That(keys.Add(Zobrist.Place(cell, card, rot)), Is.True);
            for (int c = 0; c < 9; c++) Assert.That(keys.Add(Zobrist.Owner(c)), Is.True);
            Assert.That(keys.Add(Zobrist.TurnKey), Is.True);
            // 固定 seed の mulberry32 から作るので、既知の値と一致する（環境によらない）
            uint s = Rng.SeedToState(0x2b0b1157u);
            ulong hi = Rng.NextUInt32(s, out s);
            ulong lo = Rng.NextUInt32(s, out s);
            Assert.That(Zobrist.Place(0, 0, 0), Is.EqualTo((hi << 32) | lo));
            Assert.That(CardCatalog.All.Count, Is.EqualTo(50));
        }
    }
}
