using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Game.AI;
using Edgewise.Game.Debugging;
using NUnit.Framework;

namespace Edgewise.Tests.EditMode
{
    /// <summary>
    /// Unity（Mono）上でも CPU が同じ手を選ぶこと・AiDriver のフレーム分割が結果を変えないことのスモークテスト。
    /// 網羅的な検査は dotnet 側（dotnet/Edgewise.Core.Tests/AI）で行う。
    /// </summary>
    public class AiSmokeTests
    {
        [Test]
        public void AiDriverWithAFrameBudgetChoosesTheSameMoveAsAnUnslicedSearch()
        {
            var rules = new RuleSet(same: true, plus: true, combo: true, pivot: true);
            var profile = AiProfile.Hard.WithMaxNodes(30000);
            var state = GameEngine.CreateGame(new GameConfig(rules,
                new[] { "bolthare", "forgecolossus", "scorpidrake", "taurocrag", "aquilaburst" },
                new[] { "corvusarc", "serpenscoil", "stormgear", "velahound", "polarion" },
                5, CardCatalog.ById));
            var driver = new AiDriver { FrameBudgetMs = 1 };
            int ply = 0, frames = 0;
            while (state.Phase == Phase.Playing)
            {
                driver.Begin(state, state.Turn, profile, ply, CardCatalog.All);
                while (!driver.Tick()) frames++;
                var expected = SearchSession.ChooseMove(state, state.Turn, profile, ply, CardCatalog.All);
                Assert.That(driver.BestMove, Is.EqualTo(expected));
                Assert.That(driver.Nodes, Is.LessThanOrEqualTo(profile.MaxNodes));
                state = GameEngine.ApplyMove(state, driver.BestMove).State;
                ply++;
            }
            Assert.That(ply, Is.GreaterThanOrEqualTo(9));
            Assert.That(frames, Is.GreaterThan(0));
        }

        [Test]
        public void CpuMatchPlaysAWholeGame()
        {
            var match = new CpuMatch(new RuleSet(), 3, AiProfile.Normal, AiProfile.Easy);
            var driver = new AiDriver();
            while (!match.IsOver)
            {
                match.BeginThinking(driver);
                driver.RunToEnd();
                var rec = match.ApplyThought(driver, 60);
                Assert.That(rec.Nodes, Is.LessThanOrEqualTo(AiProfile.Normal.MaxNodes));
            }
            Assert.That(match.Records.Count, Is.EqualTo(9));
        }

        [Test]
        public void SearchIsDeterministicOnUnityToo()
        {
            var state = GameEngine.CreateGame(new GameConfig(new RuleSet(same: true, plus: true, combo: true),
                new[] { "bolthare", "forgecolossus", "scorpidrake", "taurocrag", "aquilaburst" },
                new[] { "corvusarc", "serpenscoil", "stormgear", "velahound", "polarion" },
                0, CardCatalog.ById));
            var a = new SearchSession(state, state.Turn, AiProfile.Hard.WithMaxNodes(50000), 3, CardCatalog.All);
            var b = new SearchSession(state, state.Turn, AiProfile.Hard.WithMaxNodes(50000), 3, CardCatalog.All);
            var ma = a.Run();
            while (!b.Step(333))
            {
            }
            Assert.That(b.BestMove, Is.EqualTo(ma));
            Assert.That(b.NodesSearched, Is.EqualTo(a.NodesSearched));
            Assert.That(GameEngine.IsLegalMove(state, ma), Is.True);
        }
    }
}
