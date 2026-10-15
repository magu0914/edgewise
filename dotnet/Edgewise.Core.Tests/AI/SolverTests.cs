using System.Linq;
using Edgewise.Core.AI;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.AI
{
    /// <summary>読み切りの検証（総当たりとの一致）・詰め問題・Hard と Easy の強さの差。</summary>
    [TestFixture]
    public class SolverTests
    {
        // ------------------------------------------------------------------
        // 読み切り: 残り手数が少ない局面で、Hard の評価値が総当たりのミニマックスと一致する
        // ------------------------------------------------------------------

        [Test]
        public void HardEvaluationsMatchBruteForceMinimaxNearTheEnd()
        {
            int positions = 0, moves = 0;
            for (int i = 0; i < 700; i++)
            {
                var r = new TestRandom(0x50e7 + i);
                // 残り 2〜6 手（Pivot ON は分岐が多いので最大 4 手）
                int empties = 2 + r.NextInt(5);
                var s = AiH.RandomPosition(r, 9 - empties, open: true);
                if (s == null) continue;
                if (s.Rules.Pivot && empties > 4) continue;
                var evals = SearchSession.AnalyzeMoves(s, s.Turn, 2_000_000);
                var legal = GameEngine.LegalMoves(s);
                Assert.That(evals.Select(e => e.Move), Is.EqualTo(legal));
                foreach (var e in evals)
                {
                    int expected = AiH.BruteForceChild(s, e.Move);
                    Assert.That(e.Solved, Is.True, e.ToString());
                    Assert.That(e.Value, Is.EqualTo(expected), e.Move + " in " + StateHash.Serialize(s));
                    moves++;
                }
                // 対局モード（ルートの窓あり）でも、選ぶ手は最善と同じ結果（勝ち / 引き分け / 負け）になる手。
                // 勝ちなら最大の差で勝つ手。引き分け・負けのときは同じ結果の手の中から浅い読みで選ぶ（TieBreakDepth）
                int bestValue = AiH.BruteForce(s);
                var best = SearchSession.ChooseMove(s, s.Turn, AiProfile.Hard, i);
                int chosen = AiH.BruteForceChild(s, best);
                if (bestValue > 0) Assert.That(chosen, Is.EqualTo(bestValue), StateHash.Serialize(s));
                else Assert.That(System.Math.Sign(chosen), Is.EqualTo(System.Math.Sign(bestValue)), StateHash.Serialize(s));
                // 同点の選び方をしない設定なら、値まで最善と一致する
                var plain = SearchSession.ChooseMove(s, s.Turn, AiProfile.Hard.WithTieBreakDepth(0), i);
                Assert.That(AiH.BruteForceChild(s, plain), Is.EqualTo(bestValue), StateHash.Serialize(s));
                positions++;
            }
            TestContext.Out.WriteLine("positions " + positions + ", moves " + moves);
            Assert.That(positions, Is.GreaterThan(500));
        }

        [Test]
        public void SolvedValueGivesThePredictedFinalScore()
        {
            var r = new TestRandom(0xf1a1);
            var s = AiH.RandomPosition(r, 5, open: true, suddenDeath: false);
            var evals = SearchSession.AnalyzeMoves(s, s.Turn, 2_000_000);
            foreach (var e in evals)
            {
                Assert.That(e.TryGetFinalScore(out int mine, out int theirs), Is.True);
                Assert.That(mine + theirs, Is.EqualTo(10));
            }
        }

        // ------------------------------------------------------------------
        // 詰め問題（手作り）。総当たりで「唯一の勝ち手」であることを確かめてから、Hard が正解するか見る
        // ------------------------------------------------------------------

        static readonly CardDef Weak1 = MkCard("u", 1, 1, 1, 1);
        static readonly CardDef Weak2 = MkCard("v", 1, 1, 1, 1);
        static readonly CardDef WeakT = MkCard("t", 1, 1, 1, 1);

        /// <summary>
        /// Same → Combo。中央に S を置くと上（b）と左（d）で Same が成立し、b から Combo で a・c も取れる。
        /// それ以外の手では相手が中央をふさいで負ける。
        /// </summary>
        static GameState SameComboPuzzle() => MakeState(new Setup
        {
            Cards = new[]
            {
                MkCard("a", 1, 2, 1, 1), MkCard("b", 1, 8, 5, 8), MkCard("c", 1, 1, 1, 2), MkCard("d", 1, 4, 7, 1),
                MkCard("e", 1, 1, 7, 9), MkCard("f", 9, 7, 1, 7), MkCard("S", 5, 1, 1, 4), WeakT, Weak1, Weak2,
            },
            Rules = new RuleSet(open: true, same: true, combo: true),
            Board = { { 0, "a", P1 }, { 1, "b", P1 }, { 2, "c", P1 }, { 3, "d", P1 }, { 5, "e", P0 }, { 7, "f", P0 } },
            Hands = new[] { new[] { "t", "S" }, new[] { "u", "v" } },
            Turn = P0,
            FirstPlayer = P0,
        });

        /// <summary>Plus → Combo。中央に S を置くと上（3+5）と左（6+2）の和が 8 で揃い、b から Combo で a・c も取れる。</summary>
        static GameState PlusComboPuzzle() => MakeState(new Setup
        {
            Cards = new[]
            {
                MkCard("a", 1, 2, 1, 1), MkCard("b", 1, 8, 5, 8), MkCard("c", 1, 1, 1, 2), MkCard("d", 1, 2, 7, 1),
                MkCard("e", 1, 1, 7, 9), MkCard("f", 9, 6, 1, 6), MkCard("S", 3, 1, 1, 6), WeakT, Weak1, Weak2,
            },
            Rules = new RuleSet(open: true, plus: true, combo: true),
            Board = { { 0, "a", P1 }, { 1, "b", P1 }, { 2, "c", P1 }, { 3, "d", P1 }, { 5, "e", P0 }, { 7, "f", P0 } },
            Hands = new[] { new[] { "t", "S" }, new[] { "u", "v" } },
            Turn = P0,
            FirstPlayer = P0,
        });

        /// <summary>
        /// Same Wall → Combo 2 段。左上の角に A を上に向けて置くと、上の壁と右（b）で Same Wall が成立し、
        /// b → c・g → e と連鎖する。
        /// </summary>
        static GameState SameWallComboPuzzle() => MakeState(new Setup
        {
            Cards = new[]
            {
                MkCard("b", 1, 9, 9, 6), MkCard("c", 1, 1, 9, 2), MkCard("d", 9, 1, 7, 1), MkCard("g", 2, 1, 1, 1),
                MkCard("e", 2, 1, 10, 1), MkCard("f", 1, 7, 1, 7), MkCard("S", 10, 6, 1, 3), WeakT, Weak1, Weak2,
            },
            Rules = new RuleSet(open: true, same: true, sameWall: true, combo: true),
            Board = { { 1, "b", P1 }, { 2, "c", P1 }, { 3, "d", P0 }, { 4, "g", P1 }, { 5, "e", P1 }, { 7, "f", P0 } },
            Hands = new[] { new[] { "t", "S" }, new[] { "u", "v" } },
            Turn = P0,
            FirstPlayer = P0,
        });

        static void AssertUniqueWinAndHardFindsIt(GameState s, Move answer, RuleHeading heading)
        {
            // 1) 総当たりで、answer だけが勝ち（値 > 0）
            foreach (var m in GameEngine.LegalMoves(s))
            {
                int v = AiH.BruteForceChild(s, m);
                if (m == answer) Assert.That(v, Is.GreaterThan(0), "answer must win");
                else Assert.That(v, Is.LessThanOrEqualTo(0), m + " should not win");
            }
            // 2) answer は本当に Same / Plus → Combo を起こす
            var events = GameEngine.ApplyMove(s, answer).Events;
            Assert.That(events.OfType<RuleEvent>().Select(e => e.Rule), Does.Contain(heading));
            Assert.That(events.OfType<FlipEvent>().Any(f => f.Cause == FlipCause.Combo), Is.True);
            // 3) Hard（と解析 API）が正解する。Normal（3 手読み）でも届く深さ
            Assert.That(SearchSession.ChooseMove(s, s.Turn, AiProfile.Hard, 1), Is.EqualTo(answer));
            var best = SearchSession.AnalyzeMoves(s, s.Turn, 100000).OrderByDescending(e => e.Value).First();
            Assert.That(best.Move, Is.EqualTo(answer));
            Assert.That(best.Solved, Is.True);
            Assert.That(best.TryGetFinalScore(out int mine, out int theirs), Is.True);
            Assert.That(mine, Is.GreaterThan(theirs));
        }

        [Test]
        public void PuzzleSameThenCombo() => AssertUniqueWinAndHardFindsIt(SameComboPuzzle(), new Move(1, 4, 0), RuleHeading.Same);

        [Test]
        public void PuzzlePlusThenCombo() => AssertUniqueWinAndHardFindsIt(PlusComboPuzzle(), new Move(1, 4, 0), RuleHeading.Plus);

        [Test]
        public void PuzzleSameWallThenTwoStepCombo() =>
            AssertUniqueWinAndHardFindsIt(SameWallComboPuzzle(), new Move(1, 0, 0), RuleHeading.SameWall);

        [Test]
        public void PuzzleWithPivotNeedsTheRightRotation()
        {
            // Same → Combo の局面で S を 180° 回した向きでしか Same が成立しないようにする（Pivot トークン 1 個）
            var s = MakeState(new Setup
            {
                Cards = new[]
                {
                    MkCard("a", 1, 2, 1, 1), MkCard("b", 1, 8, 5, 8), MkCard("c", 1, 1, 1, 2), MkCard("d", 1, 4, 7, 1),
                    MkCard("e", 1, 1, 7, 9), MkCard("f", 9, 7, 1, 7), MkCard("S", 1, 4, 5, 1), WeakT, Weak1, Weak2,
                },
                Rules = new RuleSet(open: true, same: true, combo: true, pivot: true),
                Board = { { 0, "a", P1 }, { 1, "b", P1 }, { 2, "c", P1 }, { 3, "d", P1 }, { 5, "e", P0 }, { 7, "f", P0 } },
                Hands = new[] { new[] { "t", "S" }, new[] { "u", "v" } },
                PivotTokens = new[] { 1, 0 },
                Turn = P0,
                FirstPlayer = P0,
            });
            // 180° 回転: (top,right,bottom,left) = (1,4,5,1) → (5,1,1,4)
            AssertUniqueWinAndHardFindsIt(s, new Move(1, 4, 2), RuleHeading.Same);
        }

        // ------------------------------------------------------------------
        // 強さ: Hard が Easy に勝ち越す（軽量版。本格的な計測は Edgewise.Sim）
        // ------------------------------------------------------------------

        [Test]
        public void HardBeatsEasyMostOfTheTime()
        {
            var hard = AiProfile.Hard.WithMaxNodes(40000);
            var easy = AiProfile.Easy;
            int hardWins = 0, easyWins = 0, draws = 0;
            var ws = new SearchWorkspace();
            for (int deal = 0; deal < 20; deal++)
            {
                for (int seat = 0; seat < 2; seat++)
                {
                    var r = new TestRandom(0x4a2d + deal);
                    var rules = new RuleSet(open: deal % 2 == 0, same: deal % 4 < 2, plus: deal % 4 < 2, combo: deal % 4 < 2);
                    var state = GameEngine.CreateGame(H.RandomConfig(r, rules));
                    var hardPlayer = (Player)seat;
                    int ply = 0;
                    while (state.Phase == Phase.Playing)
                    {
                        var p = state.Turn == hardPlayer ? hard : easy;
                        var move = SearchSession.ChooseMove(state, state.Turn, p, deal * 1000 + ply++, AiH.Pool, ws);
                        state = GameEngine.ApplyMoveFast(state, move);
                    }
                    var res = GameEngine.Result(state).Value;
                    if (res == GameResult.Draw) draws++;
                    else if ((int)res == seat) hardWins++;
                    else easyWins++;
                }
            }
            TestContext.Out.WriteLine("hard " + hardWins + " / easy " + easyWins + " / draw " + draws);
            Assert.That(hardWins, Is.GreaterThanOrEqualTo(4 * easyWins), "hard " + hardWins + " easy " + easyWins);
            Assert.That(hardWins, Is.GreaterThan(20));
        }
    }
}
