using System.Collections.Generic;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.Debugging;
using NUnit.Framework;

namespace Edgewise.Tests.EditMode
{
    /// <summary>
    /// Unity（Mono / Unity の C# コンパイラ）上でも Core が同じように動くことを確かめるスモークテスト。
    /// ルールの網羅的な検査は dotnet 側（dotnet/Edgewise.Core.Tests）で行う。
    /// </summary>
    public class CoreSmokeTests
    {
        static readonly string[] Deck0 = { "glimmouse", "dustwisp", "coglet", "emberkit", "scalefry" };
        static readonly string[] Deck1 = { "pyxmoth", "bolthare", "rivetcrab", "frostnewt", "mirelamp" };

        [Test]
        public void CardCatalogHas50ValidCards()
        {
            Assert.That(CardCatalog.All.Count, Is.EqualTo(50));
            Assert.That(CardValidator.CardListProblems(CardCatalog.All), Is.Empty);
        }

        [Test]
        public void RngMatchesTheTypeScriptReferenceValues()
        {
            // TS 版 rng.ts で計算した既知の値
            Assert.That(Rng.SeedToState(42), Is.EqualTo(142593372u));
            Assert.That(Rng.SeedToState(-123456789), Is.EqualTo(2059234239u));
            Assert.That(Rng.NextUInt32(Rng.SeedToState(2026), out var next), Is.EqualTo(2499603679u));
            Assert.That(next, Is.EqualTo(1188261623u));
        }

        [Test]
        public void InitialHashMatchesTheTypeScriptEngine()
        {
            // golden.json の basic#0 と同じ設定（TS 版の初期局面 hash = c8554ac16fd1139f）
            var state = GameEngine.CreateGame(new GameConfig(RuleSet.None,
                new[] { "bolthare", "forgecolossus", "scorpidrake", "taurocrag", "aquilaburst" },
                new[] { "corvusarc", "serpenscoil", "stormgear", "velahound", "polarion" },
                0, CardCatalog.ById));
            Assert.That(GameEngine.HashState(state), Is.EqualTo("c8554ac16fd1139f"));
        }

        [Test]
        public void RandomGamesEndInNineMovesAndFastMatchesReference()
        {
            var rules = new RuleSet(same: true, sameWall: true, plus: true, combo: true, elemental: true, pivot: true);
            uint rng = Rng.SeedToState(2026);
            for (int game = 0; game < 200; game++)
            {
                var state = GameEngine.CreateGame(new GameConfig(rules, Deck0, Deck1, game, CardCatalog.ById));
                int moves = 0;
                while (state.Phase == Phase.Playing)
                {
                    var legal = GameEngine.LegalMoves(state);
                    var move = legal[Rng.NextInt(rng, legal.Count, out rng)];
                    var result = GameEngine.ApplyMove(state, move);
                    Assert.That(GameEngine.ApplyMoveFast(state, move), Is.EqualTo(result.State));
                    state = result.State;
                    moves++;
                }
                Assert.That(moves, Is.EqualTo(9));
                var score = GameEngine.Score(state);
                Assert.That(score.P0 + score.P1, Is.EqualTo(10));
            }
        }

        [Test]
        public void SuddenDeathReplaysAreDeterministic()
        {
            var rules = new RuleSet(suddenDeath: true, same: true, plus: true, combo: true);
            var a = new DebugMatch(rules, 99);
            var b = new DebugMatch(rules, 99);
            Assert.That(a.Final, Is.EqualTo(b.Final));
            Assert.That(a.Log, Is.EqualTo(b.Log));
            Assert.That(a.MoveCount, Is.EqualTo(9 * (a.Final.SuddenDeathRound + 1)));
        }

        [Test]
        public void DebugMatchRecordsEveryEvent()
        {
            var match = new DebugMatch(new RuleSet(), 1);
            Assert.That(match.Final.Phase, Is.EqualTo(Phase.Ended));
            Assert.That(match.MoveCount, Is.EqualTo(9));
            Assert.That(match.EventCount, Is.GreaterThanOrEqualTo(18)); // place + turn/end が最低 9 手分
            Assert.That(match.Log.Count(l => l.Contains("\"type\":\"end\"")), Is.EqualTo(1));
        }

        [Test]
        public void IllegalMovesThrow()
        {
            var state = GameEngine.CreateGame(new GameConfig(RuleSet.None, Deck0, Deck1, 1, CardCatalog.ById));
            Assert.Throws<IllegalMoveException>(() => GameEngine.ApplyMove(state, new Move(0, 9, 0)));
            Assert.Throws<IllegalMoveException>(() => GameEngine.ApplyMove(state, new Move(0, 0, 1)));
            var events = new List<GameEvent>(GameEngine.ApplyMove(state, new Move(0, 4, 0)).Events);
            Assert.That(events[0], Is.InstanceOf<PlaceEvent>());
        }
    }
}
