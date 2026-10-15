using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using NUnit.Framework;
using UnityEngine;

namespace Edgewise.Tests.EditMode
{
    /// <summary>3b: ヒント・画面の揺れ・音程・合成音・設定の保存。</summary>
    public class JuiceTests
    {
        static GameState NewGame(RuleSet rules, string[] deck0, string[] deck1, long seed) =>
            GameEngine.CreateGame(new GameConfig(rules, deck0, deck1, seed, CardCatalog.ById));

        [Test]
        public void HintReturnsALegalMoveWithAForecastThatAddsUpToTen()
        {
            var ids = CardCatalog.All.Select(c => c.Id).ToArray();
            var state = NewGame(new RuleSet(open: true, same: true, plus: true, combo: true, pivot: true),
                ids.Skip(10).Take(5).ToArray(), ids.Skip(20).Take(5).ToArray(), 3);
            int checkedStates = 0;
            while (state.Phase == Phase.Playing)
            {
                var hint = new HintEngine();
                hint.Begin(state, state.Turn, 60000, 1);
                var r = hint.RunToEnd();
                Assert.That(GameEngine.IsLegalMove(state, r.Move), Is.True, "hint must be legal: " + r);
                Assert.That(r.Mine + r.Theirs, Is.EqualTo(10));
                // 同じ局面・同じ seed なら同じヒント（決定論）
                var again = new HintEngine();
                again.Begin(state, state.Turn, 60000, 1);
                Assert.That(again.RunToEnd().Move, Is.EqualTo(r.Move));
                state = GameEngine.ApplyMove(state, r.Move).State;
                checkedStates++;
            }
            Assert.That(checkedStates, Is.GreaterThanOrEqualTo(9));
        }

        [Test]
        public void HintSolvesTheEndgameExactly()
        {
            var ids = CardCatalog.All.Select(c => c.Id).ToArray();
            var state = NewGame(new RuleSet(open: true, same: true, plus: true), ids.Take(5).ToArray(), ids.Skip(5).Take(5).ToArray(), 9);
            var legal = GameEngine.LegalMoves(state);
            for (int i = 0; i < 6; i++) state = GameEngine.ApplyMove(state, GameEngine.LegalMoves(state)[0]).State;
            var hint = new HintEngine();
            hint.Begin(state, state.Turn, 400000, 1);
            var r = hint.RunToEnd();
            Assert.That(r.Exact, Is.True, "3 plies left must be read to the end");
            Assert.That(legal.Count, Is.GreaterThan(0));
        }

        /// <summary>Open OFF: 相手の本当の手札だけが違う 2 つの局面で、ヒントが同じになる（盗み見ない）。</summary>
        [Test]
        public void HintDoesNotPeekAtTheHiddenHand()
        {
            var ids = CardCatalog.All.Select(c => c.Id).ToArray();
            var mine = ids.Skip(15).Take(5).ToArray();
            var theirsA = ids.Skip(25).Take(5).ToArray();
            var theirsB = ids.Skip(35).Take(5).ToArray();
            var rules = new RuleSet(same: true, plus: true, combo: true);
            long seed = 1;
            while (NewGame(rules, mine, theirsA, seed).FirstPlayer != Player.P0) seed++;
            var a = NewGame(rules, mine, theirsA, seed);
            var b = NewGame(rules, mine, theirsB, seed);
            Assert.That(b.FirstPlayer, Is.EqualTo(Player.P0));
            var ha = new HintEngine();
            ha.Begin(a, Player.P0, 40000, 5);
            var hb = new HintEngine();
            hb.Begin(b, Player.P0, 40000, 5);
            Assert.That(hb.RunToEnd().Move, Is.EqualTo(ha.RunToEnd().Move));
            Assert.That(hb.Result.Mine, Is.EqualTo(ha.Result.Mine));
        }

        [Test]
        public void ScreenShakeOffProducesNoOffset()
        {
            var off = new ShakeModel { Enabled = false };
            off.Add(1f);
            for (int i = 0; i < 30; i++) Assert.That(off.Step(1f / 60f), Is.EqualTo(Vector2.zero));
            Assert.That(off.Trauma, Is.EqualTo(0f));

            var on = new ShakeModel { Enabled = true };
            on.Add(1f);
            float max = 0f;
            for (int i = 0; i < 30; i++) max = Mathf.Max(max, on.Step(1f / 60f).magnitude);
            Assert.That(max, Is.GreaterThan(0.01f));
            // 時間がたてば収まる
            for (int i = 0; i < 120; i++) on.Step(1f / 60f);
            Assert.That(on.Trauma, Is.EqualTo(0f));
            Assert.That(on.Step(1f / 60f), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void FlipPitchRisesBySemitonesWithinAMove()
        {
            var j = JuiceSettings.CreateDefault();
            Assert.That(JuiceFx.FlipSemitones(0, j), Is.EqualTo(0f));
            Assert.That(JuiceFx.FlipSemitones(1, j), Is.EqualTo(1f));
            Assert.That(JuiceFx.FlipSemitones(5, j), Is.EqualTo(5f));
            Assert.That(JuiceFx.FlipSemitones(40, j), Is.EqualTo(j.flipSemitoneMax));
            Object.DestroyImmediate(j);
        }

        [Test]
        public void SynthesizedSoundsAreGentle()
        {
            foreach (var kind in SfxSynth.All)
            {
                var s = SfxSynth.Generate(kind);
                Assert.That(s.Length, Is.GreaterThan(SfxSynth.SampleRate / 50), kind.ToString());
                float peak = s.Max(x => Mathf.Abs(x));
                Assert.That(float.IsNaN(peak), Is.False, kind.ToString());
                Assert.That(peak, Is.InRange(0.1f, 0.51f), kind + " peak");
                // 頭と尻はなめらか（クリックしない）
                Assert.That(Mathf.Abs(s[0]), Is.LessThan(0.01f), kind + " start");
                Assert.That(Mathf.Abs(s[s.Length - 1]), Is.LessThan(0.01f), kind + " end");
            }
            var wav = SfxSynth.ToWav(SfxSynth.Generate(SfxKind.Place));
            Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
        }

        [Test]
        public void SettingsAreSavedInPlayerPrefs()
        {
            float vol = UserPrefs.SfxVolume;
            bool shake = UserPrefs.ScreenShake;
            bool mute = UserPrefs.Mute;
            try
            {
                UserPrefs.SfxVolume = 0.3f;
                UserPrefs.ScreenShake = false;
                UserPrefs.Mute = true;
                Assert.That(UserPrefs.SfxVolume, Is.EqualTo(0.3f).Within(1e-4));
                Assert.That(UserPrefs.ScreenShake, Is.False);
                Assert.That(UserPrefs.Mute, Is.True);
            }
            finally
            {
                UserPrefs.SfxVolume = vol;
                UserPrefs.ScreenShake = shake;
                UserPrefs.Mute = mute;
            }
        }
    }
}
