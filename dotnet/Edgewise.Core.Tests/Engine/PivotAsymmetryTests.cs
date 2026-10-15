using System.Linq;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Core.Tests.AI;
using NUnit.Framework;

namespace Edgewise.Core.Tests.Engine
{
    /// <summary>
    /// Phase 3c: Pivot トークン数を先攻・後攻で別々に設定する（DESIGN §3-1「調整の方針」）。
    /// 既定値は TS 版と同じ 2/2（ゴールデン突き合わせは GoldenTests が既定値のまま検査する）。
    /// </summary>
    [TestFixture]
    public class PivotAsymmetryTests
    {
        static readonly RuleSet PivotRules = new RuleSet(open: true, pivot: true);

        static GameConfig Config(RuleSet rules, long seed, int first, int second) =>
            new GameConfig(rules, Decks.D0, Decks.D1, seed, CardCatalog.All, first, second);

        /// <summary>先攻が want になる seed を探す。</summary>
        static long SeedWithFirst(Player want)
        {
            for (long seed = 1; ; seed++)
                if (GameEngine.CreateGame(Decks.Config(RuleSet.None, seed)).FirstPlayer == want) return seed;
        }

        [Test]
        public void DefaultsAreTwoAndTwoLikeTheTypeScriptVersion()
        {
            var cfg = Decks.Config(PivotRules, 1);
            Assert.That(cfg.PivotTokensFirst, Is.EqualTo(2));
            Assert.That(cfg.PivotTokensSecond, Is.EqualTo(2));
            Assert.That(GameConstants.PivotTokens, Is.EqualTo(2));
            // 明示的に 2/2 を渡しても、何も渡さないときと同じ局面（hashState も同じ）
            for (long seed = 0; seed < 20; seed++)
            {
                var a = GameEngine.CreateGame(Decks.Config(PivotRules, seed));
                var b = GameEngine.CreateGame(Config(PivotRules, seed, 2, 2));
                Assert.That(GameEngine.HashState(b), Is.EqualTo(GameEngine.HashState(a)));
            }
        }

        [Test]
        public void TheGameUsesOneAndTwoTokensOrOneEachWithSuddenDeath()
        {
            // docs/BALANCE.md「Phase 3c: Pivot の非対称化」で決めた値
            Assert.That(GameConstants.MatchPivotTokens(new RuleSet(pivot: true)), Is.EqualTo((1, 2)));
            Assert.That(GameConstants.MatchPivotTokens(new RuleSet(pivot: true, same: true, plus: true, combo: true)), Is.EqualTo((1, 2)));
            Assert.That(GameConstants.MatchPivotTokens(new RuleSet(pivot: true, suddenDeath: true)), Is.EqualTo((1, 1)));
            // 既定値（TS 版との突き合わせ）は 2/2 のまま
            Assert.That(GameConstants.PivotTokens, Is.EqualTo(2));
        }

        [TestCase(Player.P0)]
        [TestCase(Player.P1)]
        public void TokensAreDealtByTurnOrderNotBySeat(Player first)
        {
            long seed = SeedWithFirst(first);
            var s = GameEngine.CreateGame(Config(PivotRules, seed, 1, 3));
            Assert.That(s.FirstPlayer, Is.EqualTo(first));
            Assert.That(s.GetPivotTokens(first), Is.EqualTo(1), "first player");
            Assert.That(s.GetPivotTokens(first.Opponent()), Is.EqualTo(3), "second player");
        }

        [Test]
        public void PivotOffGivesNoTokensWhateverTheConfigSays()
        {
            var s = GameEngine.CreateGame(Config(new RuleSet(open: true), 3, 1, 3));
            Assert.That(s.GetPivotTokens(Player.P0), Is.EqualTo(0));
            Assert.That(s.GetPivotTokens(Player.P1), Is.EqualTo(0));
        }

        [TestCase(-1, 2)]
        [TestCase(2, -1)]
        [TestCase(GameConstants.MaxPivotTokens + 1, 2)]
        [TestCase(2, GameConstants.MaxPivotTokens + 1)]
        public void OutOfRangeTokenCountsAreRejected(int first, int second)
        {
            Assert.That(() => Config(PivotRules, 1, first, second), Throws.TypeOf<InvalidConfigException>());
        }

        [TestCase(0, 3)]
        [TestCase(3, 0)]
        public void ExtremeButValidTokenCountsAreAccepted(int first, int second)
        {
            var s = GameEngine.CreateGame(Config(PivotRules, 5, first, second));
            Assert.That(s.GetPivotTokens(s.FirstPlayer), Is.EqualTo(first));
            Assert.That(s.GetPivotTokens(s.FirstPlayer.Opponent()), Is.EqualTo(second));
        }

        [Test]
        public void ThePlayerWithOneTokenCanRotateOnceAndThenNoMore()
        {
            long seed = SeedWithFirst(Player.P0);
            var s = GameEngine.CreateGame(Config(PivotRules, seed, 1, 2));
            Assert.That(s.Turn, Is.EqualTo(Player.P0));
            // 先攻（トークン 1）が回して置く
            s = GameEngine.ApplyMove(s, new Move(0, 0, 1)).State;
            Assert.That(s.GetPivotTokens(Player.P0), Is.EqualTo(0));
            Assert.That(s.GetPivotTokens(Player.P1), Is.EqualTo(2));
            // 後攻は 2 回まで回せる
            s = GameEngine.ApplyMove(s, new Move(0, 1, 2)).State;
            Assert.That(s.GetPivotTokens(Player.P1), Is.EqualTo(1));
            // 先攻はもう回せない（例外のメッセージは TS 版と同じ）
            Assert.That(GameEngine.LegalMoves(s).All(m => m.Rotation == 0), Is.True);
            var ex = Assert.Throws<IllegalMoveException>(() => GameEngine.ApplyMove(s, new Move(0, 2, 3)));
            Assert.That(ex.Message, Is.EqualTo("no Pivot tokens left"));
            Assert.Throws<IllegalMoveException>(() => GameEngine.ApplyMoveFast(s, new Move(0, 2, 1)));
            s = GameEngine.ApplyMove(s, new Move(0, 2, 0)).State;
            // 後攻の 2 回目は合法、3 回目は不足で例外
            Assert.That(GameEngine.LegalMoves(s).Any(m => m.Rotation != 0), Is.True);
            s = GameEngine.ApplyMove(s, new Move(0, 3, 1)).State;
            Assert.That(s.GetPivotTokens(Player.P1), Is.EqualTo(0));
            s = GameEngine.ApplyMove(s, new Move(0, 4, 0)).State;
            Assert.Throws<IllegalMoveException>(() => GameEngine.ApplyMove(s, new Move(0, 5, 2)));
        }

        [Test]
        public void ZeroTokensFromTheStartMeansNoRotationAtAll()
        {
            long seed = SeedWithFirst(Player.P1);
            var s = GameEngine.CreateGame(Config(PivotRules, seed, 0, 1));
            Assert.That(s.Turn, Is.EqualTo(Player.P1));
            Assert.That(GameEngine.LegalMoves(s).All(m => m.Rotation == 0), Is.True);
            Assert.That(GameEngine.IsLegalMove(s, new Move(0, 4, 1)), Is.False);
            s = GameEngine.ApplyMove(s, new Move(0, 4, 0)).State;
            // 後攻（P0）は 1 個持っている
            Assert.That(GameEngine.LegalMoves(s).Count(m => m.Rotation != 0), Is.EqualTo(5 * 8 * 3));
        }

        [Test]
        public void SuddenDeathCarriesAsymmetricTokensOverEvenThoughTheFirstPlayerSwaps()
        {
            // 先攻 1・後攻 3 で、Sudden Death の再戦まで回転しないで打ち、トークンがそのまま残ることを見る
            var rules = new RuleSet(open: true, suddenDeath: true, pivot: true);
            for (long seed = 1; seed < 400; seed++)
            {
                var s = GameEngine.CreateGame(Config(rules, seed, 1, 3));
                var first = s.FirstPlayer;
                var r = new TestRandom((int)seed);
                while (s.Phase == Phase.Playing && s.SuddenDeathRound == 0)
                {
                    var plain = GameEngine.LegalMoves(s).Where(m => m.Rotation == 0).ToList();
                    s = GameEngine.ApplyMove(s, r.Pick(plain)).State;
                }
                if (s.SuddenDeathRound == 0) continue;
                Assert.That(s.FirstPlayer, Is.EqualTo(first.Opponent()), "the rematch starts with the previous second player");
                Assert.That(s.GetPivotTokens(first), Is.EqualTo(1));
                Assert.That(s.GetPivotTokens(first.Opponent()), Is.EqualTo(3));
                return;
            }
            Assert.Fail("no Sudden Death within 400 seeds");
        }

        // ------------------------------------------------------------------
        // AI（SearchBoard / Zobrist / SearchSession）が残数を正しく扱う
        // ------------------------------------------------------------------

        [Test]
        public void SearchBoardMakeUnmakeTracksAsymmetricTokensIncludingTheMaximum()
        {
            int positions = 0;
            int[][] candidates = { new[] { 1, 2 }, new[] { 0, 3 }, new[] { 3, 1 }, new[] { 3, 3 }, new[] { 2, 0 } };
            for (int g = 0; g < 250; g++)
            {
                var r = new TestRandom(0x3c3c + g);
                var rules = H.RandomRules(r, pivot: true);
                var baseCfg = H.RandomConfig(r, rules);
                var t = candidates[g % candidates.Length];
                var cfg = new GameConfig(rules, baseCfg.Deck0.ToArray(), baseCfg.Deck1.ToArray(), baseCfg.Seed, CardCatalog.All, t[0], t[1]);
                var state = GameEngine.CreateGame(cfg);
                var b = AiH.LoadTrue(state);
                Assert.That(b.Pivot[(int)state.FirstPlayer], Is.EqualTo(t[0]));
                Assert.That(b.Pivot[1 - (int)state.FirstPlayer], Is.EqualTo(t[1]));
                while (state.Phase == Phase.Playing)
                {
                    SearchBoardTests.CheckAllMoves(state);
                    positions++;
                    state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
                    Assert.That(state.GetPivotTokens(Player.P0), Is.GreaterThanOrEqualTo(0));
                    Assert.That(state.GetPivotTokens(Player.P1), Is.GreaterThanOrEqualTo(0));
                }
            }
            Assert.That(positions, Is.GreaterThan(2000));
        }

        [Test]
        public void ZobristPivotKeysCoverZeroToMaxTokensWithoutCollisions()
        {
            var keys = new System.Collections.Generic.HashSet<ulong>();
            for (int p = 0; p < 2; p++)
                for (int t = 0; t <= GameConstants.MaxPivotTokens; t++)
                    Assert.That(keys.Add(Zobrist.PivotKey(p, t)), Is.True, "player " + p + " tokens " + t);
            Assert.That(keys.Contains(Zobrist.TurnKey), Is.False);
        }

        [Test]
        public void SearchGeneratesRotationsOnlyForTheSideThatStillHasTokens()
        {
            long seed = SeedWithFirst(Player.P0);
            var s = GameEngine.CreateGame(Config(PivotRules, seed, 0, 3));
            var b = AiH.LoadTrue(s);
            var buf = new int[2048];
            int n = b.GenerateMoves(buf, 0);
            Assert.That(Enumerable.Range(0, n).All(i => SearchBoard.RotOf(buf[i]) == 0), Is.True, "P0 has no tokens");
            // 相手（トークン 3）の手番では回転つきの手も読む
            s = GameEngine.ApplyMove(s, new Move(0, 4, 0)).State;
            b = AiH.LoadTrue(s);
            n = b.GenerateMoves(buf, 0);
            Assert.That(Enumerable.Range(0, n).Any(i => SearchBoard.RotOf(buf[i]) != 0), Is.True, "P1 has 3 tokens");
        }

        [Test]
        public void EveryProfilePlaysLegalWholeGamesWithAsymmetricTokens()
        {
            var profiles = new[] { AiProfile.Easy, AiProfile.Normal.WithMaxNodes(4000), AiProfile.Hard.WithMaxNodes(15000) };
            int[][] candidates = { new[] { 1, 2 }, new[] { 0, 3 }, new[] { 3, 0 }, new[] { 1, 1 } };
            int rotations = 0;
            for (int g = 0; g < 36; g++)
            {
                var r = new TestRandom(0x7a7a + g);
                var rules = H.RandomRules(r, pivot: true);
                var baseCfg = H.RandomConfig(r, rules);
                var t = candidates[g % candidates.Length];
                var state = GameEngine.CreateGame(new GameConfig(rules, baseCfg.Deck0.ToArray(), baseCfg.Deck1.ToArray(), baseCfg.Seed,
                    CardCatalog.All, t[0], t[1]));
                int ply = 0;
                while (state.Phase == Phase.Playing)
                {
                    var p = profiles[(g + (int)state.Turn) % 3];
                    int before = state.GetPivotTokens(state.Turn);
                    var move = SearchSession.ChooseMove(state, state.Turn, p, g * 100 + ply, AiH.Pool);
                    Assert.That(GameEngine.IsLegalMove(state, move), Is.True, p + " " + StateHash.Serialize(state));
                    if (before == 0) Assert.That(move.Rotation, Is.EqualTo(0), "no tokens, no rotation");
                    if (move.Rotation != 0) rotations++;
                    state = GameEngine.ApplyMove(state, move).State;
                    ply++;
                }
            }
            Assert.That(rotations, Is.GreaterThan(0), "the AI still uses Pivot");
        }

        [Test]
        public void SearchIsDeterministicWithAsymmetricTokens()
        {
            long seed = SeedWithFirst(Player.P1);
            var s = GameEngine.CreateGame(Config(new RuleSet(open: true, same: true, plus: true, pivot: true), seed, 1, 2));
            for (int i = 0; i < 3; i++) s = GameEngine.ApplyMove(s, GameEngine.LegalMoves(s)[i * 7 % GameEngine.LegalMoves(s).Count]).State;
            var a = SearchSession.ChooseMove(s, s.Turn, AiProfile.Hard.WithMaxNodes(50000), 9, AiH.Pool);
            var b = SearchSession.ChooseMove(s, s.Turn, AiProfile.Hard.WithMaxNodes(50000), 9, AiH.Pool);
            Assert.That(b, Is.EqualTo(a));
        }
    }
}
