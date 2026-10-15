using Edgewise.Core;
using Edgewise.Game.UI;
using NUnit.Framework;

namespace Edgewise.Tests.EditMode
{
    /// <summary>3c: ゲーム本体の Pivot トークン数（先攻・後攻で非対称）と、対戦設定画面の説明文が食い違わないこと。</summary>
    public class PivotDefaultsTests
    {
        [Test]
        public void SetupDescriptionsMatchTheTokenCountsUsedByTheGame()
        {
            var normal = GameConstants.MatchPivotTokens(new RuleSet(pivot: true));
            Assert.That(UiText.RulePivotDesc, Does.Contain("先攻 " + normal.First + " 回"));
            Assert.That(UiText.RulePivotDesc, Does.Contain("後攻 " + normal.Second + " 回"));
            Assert.That(UiText.PivotDesc(false), Is.EqualTo(UiText.RulePivotDesc));

            var sd = GameConstants.MatchPivotTokens(new RuleSet(pivot: true, suddenDeath: true));
            Assert.That(sd.First, Is.EqualTo(sd.Second), "the Sudden Death text says 'the same for both'");
            Assert.That(UiText.RulePivotDescSuddenDeath, Does.Contain(sd.First + " 回ずつ"));
            Assert.That(UiText.PivotDesc(true), Is.EqualTo(UiText.RulePivotDescSuddenDeath));
            Assert.That(MenuPanel.DescribeRules(new RuleSet(pivot: true, suddenDeath: true)), Does.Contain(UiText.RulePivotDescSuddenDeath));
        }
    }
}
