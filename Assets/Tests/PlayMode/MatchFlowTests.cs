using System.Collections;
using System.Linq;
using Edgewise.Core;
using Edgewise.Game.App;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Edgewise.Tests.PlayMode
{
    /// <summary>Main シーンを読み込み、対戦を最後まで流して結果画面に着くことを確かめる。</summary>
    public class MatchFlowTests
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
            Assert.That(_app, Is.Not.Null, "AppFlow must be in Main.unity");
            Assert.That(_app.Title.gameObject.activeSelf, Is.True, "the title screen comes first");
        }

        [TearDown]
        public void Reset()
        {
            UserPrefs.SpeedOverride = null;
        }

        [UnityTest]
        public IEnumerator CpuVersusCpuAtInstantSpeedReachesTheResultScreen()
        {
            var settings = new MatchSettings
            {
                seat0 = SeatKind.Cpu,
                seat1 = SeatKind.Cpu,
                seat0Difficulty = Difficulty.Normal,
                difficulty = Difficulty.Easy,
                band = CardBand.Intermediate,
                open = false,
                random = true,
                same = true,
                sameWall = true,
                plus = true,
                combo = true,
                elemental = true,
                suddenDeath = true,
                pivot = true,
                seed = 12345,
            };
            _app.StartMatch(settings);
            float t = 0f;
            while (!_app.ResultReady && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(_app.ResultReady, Is.True, "result screen within " + TimeoutSeconds + " s");
            Assert.That(_app.Result.IsShown, Is.True);
            var outcome = _app.LastOutcome;
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome.Moves, Is.GreaterThanOrEqualTo(9));
            Assert.That(outcome.Score.P0 + outcome.Score.P1, Is.EqualTo(10));
            Assert.That(_app.Result.ResultLabel, Is.EqualTo(outcome.Result == GameResult.P0 ? "WIN" : outcome.Result == GameResult.P1 ? "LOSE" : "DRAW"));
            // 表示とエンジンの一致: 盤上のカードの所有者
            var state = _app.Match.State;
            for (int c = 0; c < 9; c++)
            {
                var view = _app.Match.Board.Placed[c];
                Assert.That(view, Is.Not.Null, "cell " + c);
                Assert.That(view.Owner, Is.EqualTo(state.Board[c].Value.Owner), "owner of cell " + c);
                Assert.That(view.CardId, Is.EqualTo(state.Board[c].Value.CardId));
            }
            Assert.That(_app.Hud.Score(Player.P0), Is.EqualTo(outcome.Score.P0));
            Assert.That(_app.Hud.TurnText, Is.EqualTo("対戦終了"), "the turn display changes after the end");
        }

        /// <summary>ヒント: プレイヤーの手番にボタン 1 つで最善手を出し、そのカードとマスを光らせて「予想 a-b」を出す。</summary>
        [UnityTest]
        public IEnumerator HintShowsALegalMoveAndCanBePlayed()
        {
            _app.StartMatch(new MatchSettings { seat0 = SeatKind.Human, seat1 = SeatKind.Cpu, difficulty = Difficulty.Easy, open = false, pivot = true, seed = 21 });
            var pc = _app.Controls;
            int hinted = 0;
            float t = 0f;
            while (!_app.ResultReady && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                if (pc.IsAwaitingMove && hinted < 2)
                {
                    Assert.That(_app.Match.RequestHint(), Is.True);
                    float w = 0f;
                    while (_app.Match.Hint.IsRunning && w < 30f)
                    {
                        w += Time.unscaledDeltaTime;
                        yield return null;
                    }
                    var hint = _app.Match.ShownHint;
                    Assert.That(hint.HasValue, Is.True);
                    var state = pc.State;
                    Assert.That(GameEngine.IsLegalMove(state, hint.Value.Move), Is.True);
                    Assert.That(_app.Match.Board.HintVisible, Is.True);
                    Assert.That(_app.Hud.HintText, Does.StartWith("予想 " + hint.Value.Mine + "-" + hint.Value.Theirs));
                    Assert.That(pc.Pick(hint.Value.Move.HandIndex), Is.True);
                    for (int i = 0; i < hint.Value.Move.Rotation; i++) pc.Rotate(+1);
                    pc.Hover(hint.Value.Move.Cell);
                    Assert.That(pc.Place(hint.Value.Move.Cell), Is.True);
                    hinted++;
                    for (int f = 0; f < 3 && _app.Match.Board.HintVisible; f++) yield return null;
                    Assert.That(_app.Match.Board.HintVisible, Is.False, "the hint is cleared after the move");
                }
                else if (pc.IsAwaitingMove)
                {
                    pc.Pick(0);
                    int cell = Enumerable.Range(0, 9).First(c => !pc.State.Board[c].HasValue);
                    pc.Place(cell);
                }
                yield return null;
            }
            Assert.That(_app.ResultReady, Is.True);
            Assert.That(hinted, Is.EqualTo(2));
        }

        /// <summary>設定の「画面の揺れ OFF」: 演出つき（×2）の対局でカメラが一度も揺れない。ON なら揺れる。効果音も鳴らしている。</summary>
        [UnityTest]
        public IEnumerator ScreenShakeSettingIsHonored()
        {
            bool savedShake = UserPrefs.ScreenShake;
            var layout = Object.FindAnyObjectByType<Edgewise.Game.View.TableLayout>();
            try
            {
                foreach (bool shakeOn in new[] { false, true })
                {
                    UserPrefs.ScreenShake = shakeOn;
                    UserPrefs.SpeedOverride = PlaybackSpeed.Double;
                    var sfx = Edgewise.Game.Audio.SfxBank.Instance;
                    int soundsBefore = sfx.PlayedCount;
                    _app.StartMatch(new MatchSettings
                    {
                        seat0 = SeatKind.Cpu, seat1 = SeatKind.Cpu, seat0Difficulty = Difficulty.Easy, difficulty = Difficulty.Easy,
                        same = true, plus = true, combo = true, seed = 4,
                    });
                    yield return null;
                    float maxOffset = 0f, maxTrauma = 0f, t = 0f;
                    while (!_app.ResultReady && t < TimeoutSeconds)
                    {
                        t += Time.unscaledDeltaTime;
                        maxOffset = Mathf.Max(maxOffset, layout.ShakeOffset.magnitude);
                        maxTrauma = Mathf.Max(maxTrauma, _app.Match.JuiceFx.Shake.Trauma);
                        yield return null;
                    }
                    Assert.That(_app.ResultReady, Is.True);
                    if (shakeOn) Assert.That(maxTrauma, Is.GreaterThan(0f), "shake ON must shake");
                    else
                    {
                        Assert.That(maxOffset, Is.EqualTo(0f), "shake OFF must not move the camera");
                        Assert.That(maxTrauma, Is.EqualTo(0f));
                    }
                    Assert.That(sfx.PlayedCount, Is.GreaterThan(soundsBefore + 9), "place / flip / result sounds");
                }
            }
            finally
            {
                UserPrefs.ScreenShake = savedShake;
                UserPrefs.SpeedOverride = PlaybackSpeed.Instant;
            }
        }

        /// <summary>Sudden Death の再戦（盤のカードを手札に戻して並べ直す）を含む対局でも、最後に表示とエンジンが一致する。</summary>
        [UnityTest]
        public IEnumerator SuddenDeathRematchKeepsViewsInSync()
        {
            bool sawSuddenDeath = false;
            for (int seed = 1; seed <= 60 && !sawSuddenDeath; seed++)
            {
                var settings = new MatchSettings
                {
                    seat0 = SeatKind.Cpu,
                    seat1 = SeatKind.Cpu,
                    seat0Difficulty = Difficulty.Easy,
                    difficulty = Difficulty.Easy,
                    band = CardBand.Beginner,
                    open = true,
                    same = true,
                    plus = true,
                    suddenDeath = true,
                    seed = seed,
                };
                _app.StartMatch(settings);
                float t = 0f;
                // 前の結果画面が消えるまで待つ
                yield return null;
                while (!_app.ResultReady && t < TimeoutSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.That(_app.ResultReady, Is.True);
                var state = _app.Match.State;
                if (state.SuddenDeathRound > 0) sawSuddenDeath = true;
                for (int c = 0; c < 9; c++)
                    Assert.That(_app.Match.Board.Placed[c].Owner, Is.EqualTo(state.Board[c].Value.Owner), "seed " + seed + " cell " + c);
                Assert.That(_app.Match.Hand(Player.P0).Cards.Count + _app.Match.Hand(Player.P1).Cards.Count, Is.EqualTo(1));
            }
            Assert.That(sawSuddenDeath, Is.True, "no draw within 60 seeds");
        }

        [UnityTest]
        public IEnumerator ResigningEndsTheMatchAsALoss()
        {
            _app.StartMatch(new MatchSettings { seat0 = SeatKind.Human, seat1 = SeatKind.Cpu, difficulty = Difficulty.Easy, seed = 5 });
            float t = 0f;
            while (!_app.Controls.IsAwaitingMove && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _app.Match.Resign();
            t = 0f;
            while (!_app.ResultReady && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(_app.ResultReady, Is.True);
            Assert.That(_app.LastOutcome.Resigned, Is.True);
            Assert.That(_app.LastOutcome.Result, Is.EqualTo(GameResult.P1));
            Assert.That(_app.Result.ResultLabel, Is.EqualTo("LOSE"));
        }

        /// <summary>プレイヤー役の入力をコードから注入する: 持つ → （回す）→ マスに重ねる（予告）→ 置く を結果まで繰り返す。</summary>
        [UnityTest]
        public IEnumerator InjectedPlayerInputPlaysAWholeGame()
        {
            var settings = new MatchSettings
            {
                seat0 = SeatKind.Human,
                seat1 = SeatKind.Cpu,
                difficulty = Difficulty.Easy,
                band = CardBand.Beginner,
                open = true,
                same = true,
                plus = true,
                combo = true,
                pivot = true,
                seed = 777,
            };
            _app.StartMatch(settings);
            var pc = _app.Controls;
            int placed = 0;
            bool rotated = false;
            float t = 0f;
            while (!_app.ResultReady && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                if (pc.IsAwaitingMove)
                {
                    var state = pc.State;
                    Assert.That(state.Turn, Is.EqualTo(Player.P0));
                    Assert.That(pc.Pick(0), Is.True, "pick");
                    Assert.That(pc.IsHolding, Is.True);
                    yield return null;
                    if (!rotated && pc.CanRotate)
                    {
                        Assert.That(pc.Rotate(+1), Is.True);
                        Assert.That(pc.Rotation, Is.EqualTo(1));
                        rotated = true;
                    }
                    int cell = Enumerable.Range(0, 9).First(c => !state.Board[c].HasValue);
                    pc.Hover(cell);
                    Assert.That(pc.Preview, Is.Not.Null, "preview on an empty cell");
                    var expected = GameEngine.ApplyMove(state, new Move(0, cell, pc.Rotation)).Events.OfType<FlipEvent>().Select(f => f.Cell);
                    Assert.That(pc.Preview.FlipCells, Is.EqualTo(expected));
                    yield return null;
                    Assert.That(pc.Place(cell), Is.True, "place");
                    placed++;
                }
                yield return null;
            }
            Assert.That(_app.ResultReady, Is.True, "result screen within " + TimeoutSeconds + " s");
            Assert.That(placed, Is.GreaterThanOrEqualTo(4));
            Assert.That(rotated, Is.True, "the Pivot rotation was used");
            Assert.That(_app.LastOutcome.Moves, Is.EqualTo(9));
            Assert.That(_app.Match.State.GetPivotTokens(Player.P0), Is.EqualTo(1), "one token was spent");
        }
    }
}
