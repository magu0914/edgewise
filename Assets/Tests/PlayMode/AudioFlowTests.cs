using System.Collections;
using System.Linq;
using Edgewise.Core;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using Edgewise.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Edgewise.Tests.PlayMode
{
    /// <summary>
    /// 3c: BGM の再生基盤と音量設定。曲ファイルはまだ無いので、テストの中でだけ合成した仮の曲（AudioClip.Create）を
    /// BgmLibrary に割り当てて、場面の切り替え・クロスフェード・ジングル → ループ・ダッキング・音量が効くことを確かめる。
    /// 仮の曲はアセットにしない（ビルドに入らない）。
    /// </summary>
    public class AudioFlowTests
    {
        const float TimeoutSeconds = 180f;

        AppFlow _app;
        BgmLibrary _lib;
        int _master, _bgm, _sfx;
        bool _mute, _bgMute;

        static AudioClip Tone(string name, float seconds, float hz)
        {
            int n = Mathf.RoundToInt(44100 * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / 44100f;
                float env = Mathf.Min(1f, Mathf.Min(t, seconds - t) * 50f);
                data[i] = 0.15f * env * Mathf.Sin(2f * Mathf.PI * hz * t);
            }
            var clip = AudioClip.Create(name, n, 1, 44100, false);
            clip.SetData(data, 0);
            return clip;
        }

        static BgmLibrary TestLibrary(bool full)
        {
            var lib = BgmLibrary.CreateEmpty();
            lib.name = "BgmLibrary (test, synthesized)";
            lib.crossfadeTime = 0.3f;
            lib.duckRelease = 0.3f;
            lib.match.loop = Tone("test_match", 1.5f, 330f);
            if (!full) return lib;
            lib.title.loop = Tone("test_title", 1.5f, 220f);
            lib.victory.intro = Tone("test_jingle", 0.35f, 523f);
            lib.victory.loop = Tone("test_result", 1.5f, 392f);
            lib.defeat.loop = Tone("test_defeat", 1.5f, 196f);
            lib.draw.loop = Tone("test_draw", 1.5f, 294f);
            return lib;
        }

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            _master = UserPrefs.MasterPercent;
            _bgm = UserPrefs.BgmPercent;
            _sfx = UserPrefs.SfxPercent;
            _mute = UserPrefs.Mute;
            _bgMute = UserPrefs.MuteInBackground;
            UserPrefs.Mute = false;
            UserPrefs.MasterPercent = 100;
            UserPrefs.BgmPercent = 60;
            UserPrefs.SfxPercent = 60;
            UserPrefs.SpeedOverride = PlaybackSpeed.Instant;
            UserPrefs.DebugSlowFactor = 1f;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = Object.FindAnyObjectByType<AppFlow>();
            Assert.That(_app, Is.Not.Null);
            Assert.That(_app.Bgm, Is.Not.Null, "BgmPlayer must be in Main.unity");
        }

        [TearDown]
        public void Reset()
        {
            UserPrefs.SpeedOverride = null;
            UserPrefs.MasterPercent = _master;
            UserPrefs.BgmPercent = _bgm;
            UserPrefs.SfxPercent = _sfx;
            UserPrefs.Mute = _mute;
            UserPrefs.MuteInBackground = _bgMute;
            if (_lib != null)
            {
                foreach (var t in new[] { _lib.title, _lib.match, _lib.victory, _lib.defeat, _lib.draw })
                {
                    if (t.intro != null) Object.Destroy(t.intro);
                    if (t.loop != null) Object.Destroy(t.loop);
                }
                Object.Destroy(_lib);
                _lib = null;
            }
        }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        IEnumerator WaitForResult()
        {
            float t = 0f;
            while (!_app.ResultReady && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(_app.ResultReady, Is.True, "result screen within " + TimeoutSeconds + " s");
        }

        static MatchSettings CpuMatch(long seed, bool rules = false) => new MatchSettings
        {
            seat0 = SeatKind.Cpu, seat1 = SeatKind.Cpu, seat0Difficulty = Difficulty.Easy, difficulty = Difficulty.Easy,
            band = CardBand.Beginner, open = true, same = rules, plus = rules, combo = rules, pivot = true, seed = seed,
        };

        /// <summary>出荷時の BgmLibrary（曲が未設定）のままでも、タイトル → 対戦 → 結果がエラーなく流れ、結果の合成音のジングルが鳴る。</summary>
        [UnityTest]
        public IEnumerator WithoutAnyMusicEverythingIsSilentAndErrorFree()
        {
            Assert.That(_app.Bgm.Library, Is.Not.Null, "the shipped library is wired");
            Assert.That(_app.Bgm.CurrentCue, Is.EqualTo(BgmCue.Title), "the title asks for its music at start-up");
            // 曲が 1 曲も無い状態（出荷時と同じ）を明示的に作る（あとで曲を割り当ててもこのテストが意味を保つように）
            _lib = BgmLibrary.CreateEmpty();
            _app.Bgm.SetLibrary(_lib);
            _app.ShowTitle();
            yield return null;
            Assert.That(_app.Bgm.CurrentCue, Is.EqualTo(BgmCue.Title));
            Assert.That(_app.Bgm.CurrentClip, Is.Null);
            var sfx = SfxBank.Instance;
            _app.StartMatch(CpuMatch(3));
            yield return WaitForResult();
            Assert.That(_app.Bgm.CurrentClip, Is.Null);
            Assert.That(_app.Bgm.AudibleDecks, Is.EqualTo(0));
            Assert.That(new[] { SfxKind.Win, SfxKind.Lose, SfxKind.Draw }, Does.Contain(sfx.LastKind), "no result music → the synthesized jingle");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>一部の場面だけ曲がある: 無い場面（タイトル）は無音、ある場面（対戦）は鳴る。</summary>
        [UnityTest]
        public IEnumerator UnassignedScenesAreSilentAndAssignedOnesPlay()
        {
            _lib = TestLibrary(full: false);
            _app.Bgm.SetLibrary(_lib);
            _app.ShowTitle();
            yield return Wait(0.2f);
            Assert.That(_app.Bgm.CurrentCue, Is.EqualTo(BgmCue.Title));
            Assert.That(_app.Bgm.CurrentClip, Is.Null);
            Assert.That(_app.Bgm.AudibleDecks, Is.EqualTo(0));
            // 即時の CPU 同士は 1 フレームで終わることがあるので、始めた直後に確かめる
            _app.StartMatch(CpuMatch(5));
            Assert.That(_app.Bgm.CurrentCue, Is.EqualTo(BgmCue.Match));
            Assert.That(_app.Bgm.CurrentClip, Is.SameAs(_lib.match.loop));
            yield return WaitForResult();
            // 勝敗の曲は未設定 → 対戦の曲はフェードアウトして無音
            yield return Wait(0.5f);
            Assert.That(_app.Bgm.CurrentClip, Is.Null);
            Assert.That(_app.Bgm.AudibleDecks, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>タイトル → 対戦 → 結果で曲が切り替わり、切り替えの途中は 2 曲が重なる（クロスフェード）。勝利はジングル → ループ。</summary>
        [UnityTest]
        public IEnumerator ScenesSwitchWithACrossfadeAndVictoryGoesFromJingleToLoop()
        {
            _lib = TestLibrary(full: true);
            _app.Bgm.SetLibrary(_lib);
            UserPrefs.SpeedOverride = PlaybackSpeed.Double; // 対戦がクロスフェードより先に終わらないように
            _app.ShowTitle();
            Assert.That(_app.Bgm.CurrentClip, Is.SameAs(_lib.title.loop));
            yield return Wait(0.5f);
            float full = UserPrefs.EffectiveBgmVolume;
            Assert.That(_app.Bgm.CurrentVolume, Is.EqualTo(full).Within(0.01f), "after the fade-in, the volume is master × BGM");

            _app.StartMatch(CpuMatch(7));
            bool overlapped = false;
            float t = 0f;
            while (t < 0.25f)
            {
                if (_app.Bgm.AudibleDecks == 2) overlapped = true;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(overlapped, Is.True, "the title and match music overlap during the crossfade");
            yield return Wait(0.3f);
            Assert.That(_app.Bgm.AudibleDecks, Is.EqualTo(1), "the old track is stopped after the crossfade");
            Assert.That(_app.Bgm.CurrentClip, Is.SameAs(_lib.match.loop));

            yield return WaitForResult();
            var r = _app.LastOutcome.Result;
            var expected = r == GameResult.P0 ? BgmCue.Victory : r == GameResult.P1 ? BgmCue.Defeat : BgmCue.Draw;
            Assert.That(_app.Bgm.CurrentCue, Is.EqualTo(expected));

            // 勝利: ジングル（1 回）→ 結果のループ
            _app.Bgm.Play(BgmCue.Title);
            _app.Bgm.Play(BgmCue.Victory);
            Assert.That(_app.Bgm.CurrentClip, Is.SameAs(_lib.victory.intro));
            Assert.That(_app.Bgm.InIntro, Is.True);
            yield return Wait(1.1f);
            Assert.That(_app.Bgm.InIntro, Is.False);
            Assert.That(_app.Bgm.CurrentClip, Is.SameAs(_lib.victory.loop));

            // 「もう一度」→ 対戦の曲に戻る、同じ場面の再要求では頭から鳴らし直さない
            _app.StartMatch(CpuMatch(8));
            var clip = _app.Bgm.CurrentClip;
            Assert.That(clip, Is.SameAs(_lib.match.loop));
            Assert.That(_app.Bgm.Play(BgmCue.Match), Is.True);
            Assert.That(_app.Bgm.CurrentClip, Is.SameAs(clip));
            yield return WaitForResult();
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>SAME / PLUS / COMBO の瞬間に BGM が一瞬下がり、その後もとに戻る。</summary>
        [UnityTest]
        public IEnumerator BgmDucksOnSamePlusAndCombo()
        {
            _lib = TestLibrary(full: true);
            _app.Bgm.SetLibrary(_lib);
            UserPrefs.SpeedOverride = PlaybackSpeed.Double;
            float minDuck = 1f, duckedVolume = -1f, duckedBound = 0f;
            int ducks = 0;
            for (long seed = 1; seed <= 12 && ducks == 0; seed++)
            {
                _app.StartMatch(CpuMatch(seed, rules: true));
                yield return null;
                float t = 0f;
                while (!_app.ResultReady && t < TimeoutSeconds)
                {
                    float m = _app.Bgm.DuckMultiplier;
                    if (m < minDuck && _app.Bgm.CurrentCue == BgmCue.Match)
                    {
                        // 下がっている瞬間の AudioSource の音量（Update で計算済み）が、ダッキングの倍率どおりに下がっている
                        minDuck = m;
                        duckedVolume = _app.Bgm.CurrentVolume;
                        duckedBound = UserPrefs.EffectiveBgmVolume * m;
                    }
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.That(_app.ResultReady, Is.True);
                ducks = _app.Bgm.DuckCount;
            }
            Assert.That(ducks, Is.GreaterThan(0), "a SAME / PLUS / COMBO happened within 12 games");
            Assert.That(minDuck, Is.LessThan(0.5f), "the music is ducked to the duck level");
            Assert.That(duckedVolume, Is.GreaterThanOrEqualTo(0f));
            Assert.That(duckedVolume, Is.LessThanOrEqualTo(duckedBound + 1e-3f), "and the AudioSource volume really goes down");
            Assert.That(duckedVolume, Is.LessThan(UserPrefs.EffectiveBgmVolume * 0.5f));
            yield return Wait(1.2f);
            Assert.That(_app.Bgm.DuckMultiplier, Is.EqualTo(1f).Within(1e-3), "and recovers");
        }

        /// <summary>全体・BGM・効果音の音量と全体ミュートが、BGM の AudioSource と効果音の音量に効く。</summary>
        [UnityTest]
        public IEnumerator VolumeSettingsApplyToMusicAndEffects()
        {
            _lib = TestLibrary(full: true);
            _app.Bgm.SetLibrary(_lib);
            _app.ShowTitle();
            yield return Wait(0.5f);
            UserPrefs.MasterPercent = 100;
            UserPrefs.BgmPercent = 50;
            yield return null;
            Assert.That(_app.Bgm.CurrentVolume, Is.EqualTo(0.5f).Within(0.01f));
            UserPrefs.MasterPercent = 50;
            UserPrefs.BgmPercent = 40;
            yield return null;
            Assert.That(_app.Bgm.CurrentVolume, Is.EqualTo(0.2f).Within(0.01f));
            UserPrefs.Mute = true;
            yield return null;
            Assert.That(_app.Bgm.CurrentVolume, Is.EqualTo(0f));
            var sfx = SfxBank.Instance;
            sfx.Play(SfxKind.Place);
            Assert.That(sfx.LastVolume, Is.EqualTo(0f), "muted effects");
            UserPrefs.Mute = false;
            UserPrefs.SfxPercent = 80;
            yield return null;
            Assert.That(_app.Bgm.CurrentVolume, Is.EqualTo(0.2f).Within(0.01f));
            sfx.Play(SfxKind.Place);
            float kind = SfxBank.KindVolume(sfx.Juice, SfxKind.Place);
            Assert.That(sfx.LastVolume, Is.EqualTo(0.5f * 0.8f * kind).Within(0.01f), "master × SFX × kind");
        }

        /// <summary>設定画面（タイトルから開く）: スライダーは 5% 刻みで保存、ミュートと「裏では消音」の切り替え、縦画面でもはみ出さない。</summary>
        [UnityTest]
        public IEnumerator SettingsPanelControlsTheAudioOptions()
        {
            _app.ShowTitle();
            _app.Title.SettingsButton.onClick.Invoke();
            yield return null;
            var panel = _app.Settings;
            Assert.That(panel.IsOpen, Is.True);
            var bgmSlider = panel.VolumeSlider(SettingsPanel.Volume.Bgm);
            Assert.That(bgmSlider.wholeNumbers, Is.True);
            Assert.That(bgmSlider.maxValue, Is.EqualTo(20f));
            bgmSlider.value = 7;
            Assert.That(UserPrefs.BgmPercent, Is.EqualTo(35));
            Assert.That(panel.VolumeText(SettingsPanel.Volume.Bgm), Is.EqualTo("35%"));
            panel.VolumeSlider(SettingsPanel.Volume.Master).value = 18;
            Assert.That(UserPrefs.MasterPercent, Is.EqualTo(90));
            panel.VolumeSlider(SettingsPanel.Volume.Sfx).value = 0;
            Assert.That(UserPrefs.SfxPercent, Is.EqualTo(0));
            bool mute = UserPrefs.Mute;
            panel.MuteButton.onClick.Invoke();
            Assert.That(UserPrefs.Mute, Is.EqualTo(!mute));
            panel.MuteButton.onClick.Invoke();
            bool bg = UserPrefs.MuteInBackground;
            panel.BackgroundMuteButton.onClick.Invoke();
            Assert.That(UserPrefs.MuteInBackground, Is.EqualTo(!bg));
            panel.BackgroundMuteButton.onClick.Invoke();

            // 開き直すと保存値が反映されている
            panel.CloseButton.onClick.Invoke();
            Assert.That(panel.IsOpen, Is.False);
            _app.Title.SettingsButton.onClick.Invoke();
            Assert.That(panel.VolumeSlider(SettingsPanel.Volume.Bgm).value, Is.EqualTo(7f));

            // 横・縦どちらの配置でも、操作部品がパネルの内側に収まり、重ならない
            foreach (bool portrait in new[] { false, true })
            {
                panel.ApplyLayout(portrait);
                var panelRt = (RectTransform)panel.transform.Find("Panel");
                var outer = panelRt.rect;
                Vector2 refSize = portrait ? new Vector2(720, 1280) : new Vector2(1280, 720);
                Assert.That(outer.width, Is.LessThanOrEqualTo(refSize.x));
                Assert.That(outer.height, Is.LessThanOrEqualTo(refSize.y));
                var rects = panelRt.GetComponentsInChildren<Selectable>().Select(s => LocalRect(panelRt, (RectTransform)s.transform)).ToList();
                Assert.That(rects.Count, Is.EqualTo(3 + 3 + 3 + 1), "speed ×3, sliders ×3, toggles ×3, close");
                foreach (var r in rects)
                {
                    bool inside = r.xMin >= outer.xMin - 0.5f && r.xMax <= outer.xMax + 0.5f && r.yMin >= outer.yMin - 0.5f && r.yMax <= outer.yMax + 0.5f;
                    Assert.That(inside, Is.True, (portrait ? "portrait " : "landscape ") + r + " in " + outer);
                }
                for (int i = 0; i < rects.Count; i++)
                    for (int j = i + 1; j < rects.Count; j++)
                        Assert.That(rects[i].Overlaps(rects[j]), Is.False, "overlap " + rects[i] + " " + rects[j]);
                // 縦画面では 360×640 で 0.5 倍になるので、つまみは 22px（基準 44）以上の高さにする
                if (portrait) Assert.That(((RectTransform)bgmSlider.transform).rect.height, Is.GreaterThanOrEqualTo(44f));
            }
            panel.ApplyLayout(false);
            panel.Close();
        }

        static Rect LocalRect(RectTransform space, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 a = space.InverseTransformPoint(c[0]);
            Vector2 b = space.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
    }
}
