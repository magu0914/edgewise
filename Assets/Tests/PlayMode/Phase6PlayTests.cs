using System.Collections;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Edgewise.Tests.PlayMode
{
    /// <summary>
    /// Phase 6（Resonance と Trait。DESIGN §3-4）:
    /// 対戦設定に 2 項目が加わる（初期 OFF）／ON にした対戦を即時速度で最後まで通せる（例外なし）／盤上のバッジと共鳴の縁が状態と一致する／図鑑に Trait の説明が出る。
    /// </summary>
    public class Phase6PlayTests
    {
        const float TimeoutSeconds = 180f;
        AppFlow _app;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            UserPrefs.SpeedOverride = PlaybackSpeed.Instant;
            UserPrefs.DebugSlowFactor = 1f;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = Object.FindAnyObjectByType<AppFlow>();
            Assert.That(_app, Is.Not.Null);
        }

        [TearDown]
        public void Reset() => UserPrefs.SpeedOverride = null;

        [UnityTest]
        public IEnumerator TheSetupScreenListsResonanceAndTraitsAndTheyStartOff()
        {
            // 前回の設定が残っていても、この 2 項目は保存された値に従う。まず既定値に戻して確かめる
            var fresh = new MatchSettings();
            _app.Setup.Show(fresh);
            yield return null;
            var setup = _app.Setup;
            Assert.That(SetupScreen_RuleCount(), Is.EqualTo(11));
            Assert.That(setup.Current.resonance, Is.False);
            Assert.That(setup.Current.traits, Is.False);
            setup.RuleButton(9).onClick.Invoke();
            Assert.That(setup.Current.resonance, Is.True);
            Assert.That(setup.Current.traits, Is.False);
            setup.RuleButton(10).onClick.Invoke();
            Assert.That(setup.Current.traits, Is.True);
            Assert.That(setup.Current.ToRuleSet().ToBitString().Substring(9), Is.EqualTo("11"));
            setup.RuleButton(9).onClick.Invoke();
            Assert.That(setup.Current.resonance, Is.False);
        }

        static int SetupScreen_RuleCount() => Edgewise.Game.UI.SetupScreen.RuleCount;

        /// <summary>CPU 同士の 1 局を即時速度で最後まで流す。Resonance・Traits を ON にしたまま、盤の表示がエンジンの状態と一致する。</summary>
        [UnityTest]
        public IEnumerator AFullMatchWithResonanceAndTraitsRunsToTheEndAndTheBoardMatchesTheEngine()
        {
            int guards = 0, resonanceSeen = 0, badgesSeen = 0;
            foreach (long seed in new long[] { 6001, 6002, 6003 })
            {
                _app.StartMatch(new MatchSettings
                {
                    seat0 = SeatKind.Cpu,
                    seat1 = SeatKind.Cpu,
                    seat0Difficulty = Difficulty.Normal,
                    difficulty = Difficulty.Easy,
                    band = CardBand.Advanced,
                    open = true,
                    same = true,
                    plus = true,
                    combo = true,
                    pivot = true,
                    resonance = true,
                    traits = true,
                    seed = seed,
                });
                float t = 0f;
                while (!_app.ResultReady && t < TimeoutSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.That(_app.ResultReady, Is.True, "seed " + seed + ": result screen within " + TimeoutSeconds + " s");
                var state = _app.Match.State;
                Assert.That(state.Rules.Resonance && state.Rules.Traits, Is.True);
                var board = state.Board.ToArray();
                for (int c = 0; c < 9; c++)
                {
                    var view = _app.Match.Board.Placed[c];
                    Assert.That(view, Is.Not.Null);
                    Assert.That(view.Owner, Is.EqualTo(board[c].Value.Owner), "owner of cell " + c);
                    Assert.That(view.Resonating, Is.EqualTo(Edgewise.Core.Board.IsResonant(board, c)), "resonance rim of cell " + c);
                    if (view.Resonating) resonanceSeen++;
                    var trait = board[c].Value.Card.Trait;
                    Assert.That(view.TraitBadgeVisible, Is.EqualTo(trait.HasValue), "trait badge of cell " + c);
                    if (trait.HasValue)
                    {
                        badgesSeen++;
                        Assert.That(view.TraitBadgeText, Is.EqualTo(Edgewise.Game.View.Theme.TraitGlyph(trait.Value)));
                        if (trait == TraitId.Guard) Assert.That(view.TraitUsed, Is.EqualTo(board[c].Value.GuardUsed), "spent guard of cell " + c);
                    }
                    if (board[c].Value.GuardUsed) guards++;
                }
                yield return null;
            }
            Assert.That(resonanceSeen + badgesSeen, Is.GreaterThan(0), "the three games showed at least some resonance or badges");
            TestContext.Out.WriteLine("resonance " + resonanceSeen + ", badges " + badgesSeen + ", spent guards " + guards);
        }

        [UnityTest]
        public IEnumerator WithTheRulesOffNoBadgeAndNoRimAppear()
        {
            _app.StartMatch(new MatchSettings
            {
                seat0 = SeatKind.Cpu,
                seat1 = SeatKind.Cpu,
                seat0Difficulty = Difficulty.Easy,
                difficulty = Difficulty.Easy,
                band = CardBand.Advanced,
                seed = 6004,
            });
            float t = 0f;
            while (!_app.ResultReady && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(_app.ResultReady, Is.True);
            for (int c = 0; c < 9; c++)
            {
                var view = _app.Match.Board.Placed[c];
                Assert.That(view.TraitBadgeVisible, Is.False, "cell " + c);
                Assert.That(view.Resonating, Is.False, "cell " + c);
            }
        }

        [UnityTest]
        public IEnumerator TheDexShowsTheTraitOfATraitCardAndNothingForOthers()
        {
            _app.Title.DexButton.onClick.Invoke();
            yield return null;
            var dex = _app.Dex;
            int pierce = CardCatalog.All.ToList().FindIndex(c => c.Trait == TraitId.Pierce);
            dex.OpenZoom(pierce);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(dex.ZoomCard.TraitBadgeVisible, Is.True, "the dex always shows the badge");
            Assert.That(dex.InfoTrait, Does.Contain("貫").And.Contain("貫通"));
            int plain = CardCatalog.All.ToList().FindIndex(c => !c.Trait.HasValue);
            dex.OpenZoom(plain);
            yield return null;
            Assert.That(dex.ZoomCard.TraitBadgeVisible, Is.False);
            Assert.That(dex.InfoTrait, Is.Empty);
            dex.BackButton.onClick.Invoke();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheMenuRulesTextListsTheNewRulesWhenOn()
        {
            _app.StartMatch(new MatchSettings
            {
                seat0 = SeatKind.Human,
                seat1 = SeatKind.Cpu,
                difficulty = Difficulty.Easy,
                resonance = true,
                traits = true,
                seed = 6005,
            });
            yield return null;
            yield return null;
            Assert.That(_app.Match.State.Rules.Resonance, Is.True);
            string text = Edgewise.Game.UI.MenuPanel.DescribeRules(_app.Match.State.Rules);
            Assert.That(text, Does.Contain("Resonance").And.Contain("Traits").And.Contain("守"));
            _app.Match.Resign();
            float t = 0f;
            while (!_app.ResultReady && t < 30f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(_app.ResultReady, Is.True);
        }
    }
}
