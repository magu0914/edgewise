using System.IO;
using Edgewise.EditorTools;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Edgewise.Tests.EditMode
{
    /// <summary>
    /// 3c: 音のオプション（全体・BGM・効果音の 5% 刻み、全体ミュート、裏では消音、旧設定からの移行）と
    /// BGM の音量計算（クロスフェード・ダッキング）、曲のインポート設定。
    /// </summary>
    public class AudioOptionsTests
    {
        static readonly string[] Keys =
        {
            "edgewise.audio.version", "edgewise.audio.master", "edgewise.audio.bgm", "edgewise.audio.sfx", "edgewise.audio.mute",
            "edgewise.audio.muteInBackground", UserPrefs.LegacyVolumeKey, UserPrefs.LegacyMuteKey,
        };

        // テストの前後でプレイヤーの保存値を壊さない
        readonly System.Collections.Generic.Dictionary<string, object> _saved = new System.Collections.Generic.Dictionary<string, object>();

        [SetUp]
        public void Save()
        {
            _saved.Clear();
            foreach (var k in Keys)
            {
                if (!PlayerPrefs.HasKey(k)) continue;
                // 型がわからないので int → float → string の順に読む（PlayerPrefs は既定値で型違いを判別する）
                int i = PlayerPrefs.GetInt(k, int.MinValue);
                if (i != int.MinValue) _saved[k] = i;
                else
                {
                    float f = PlayerPrefs.GetFloat(k, float.NaN);
                    _saved[k] = float.IsNaN(f) ? (object)PlayerPrefs.GetString(k) : f;
                }
            }
            foreach (var k in Keys) PlayerPrefs.DeleteKey(k);
            UserPrefs.ResetMigrationForTests();
        }

        [TearDown]
        public void Restore()
        {
            foreach (var k in Keys) PlayerPrefs.DeleteKey(k);
            foreach (var kv in _saved)
            {
                if (kv.Value is int i) PlayerPrefs.SetInt(kv.Key, i);
                else if (kv.Value is float f) PlayerPrefs.SetFloat(kv.Key, f);
                else PlayerPrefs.SetString(kv.Key, (string)kv.Value);
            }
            PlayerPrefs.Save();
            UserPrefs.ResetMigrationForTests();
        }

        [Test]
        public void DefaultsAreSensibleAndMuteInBackgroundIsOn()
        {
            Assert.That(UserPrefs.MasterPercent, Is.EqualTo(UserPrefs.DefaultMasterPercent));
            Assert.That(UserPrefs.BgmPercent, Is.EqualTo(UserPrefs.DefaultBgmPercent));
            Assert.That(UserPrefs.SfxPercent, Is.EqualTo(UserPrefs.Snap(UserPrefs.DefaultSfxVolume * 100f)));
            Assert.That(UserPrefs.Mute, Is.False);
            Assert.That(UserPrefs.MuteInBackground, Is.True, "DESIGN §8-2: initial value ON");
        }

        [Test]
        public void VolumesAreStoredInFivePercentSteps()
        {
            UserPrefs.MasterPercent = 73;
            Assert.That(UserPrefs.MasterPercent, Is.EqualTo(75));
            UserPrefs.BgmPercent = 2;
            Assert.That(UserPrefs.BgmPercent, Is.EqualTo(0));
            UserPrefs.SfxPercent = 140;
            Assert.That(UserPrefs.SfxPercent, Is.EqualTo(100));
            UserPrefs.SfxVolume = 0.33f;
            Assert.That(UserPrefs.SfxPercent, Is.EqualTo(35));
            Assert.That(UserPrefs.SfxVolume, Is.EqualTo(0.35f).Within(1e-5));
            // スライダーは 0..20 段
            Assert.That(SettingsPanel.VolumeSteps, Is.EqualTo(20));
            for (int s = 0; s <= 20; s++) Assert.That(SettingsPanel.StepsToPercent(s), Is.EqualTo(s * 5));
            Assert.That(SettingsPanel.StepsToPercent(7.4f), Is.EqualTo(35));
        }

        [Test]
        public void EffectiveVolumeIsMasterTimesKindAndZeroWhenMuted()
        {
            UserPrefs.MasterPercent = 50;
            UserPrefs.BgmPercent = 80;
            UserPrefs.SfxPercent = 40;
            Assert.That(UserPrefs.EffectiveBgmVolume, Is.EqualTo(0.4f).Within(1e-5));
            Assert.That(UserPrefs.EffectiveSfxVolume, Is.EqualTo(0.2f).Within(1e-5));
            UserPrefs.Mute = true;
            Assert.That(UserPrefs.EffectiveBgmVolume, Is.EqualTo(0f));
            Assert.That(UserPrefs.EffectiveSfxVolume, Is.EqualTo(0f));
            UserPrefs.Mute = false;
            UserPrefs.MasterPercent = 0;
            Assert.That(UserPrefs.EffectiveBgmVolume, Is.EqualTo(0f));
        }

        [Test]
        public void ChangingAVolumeRaisesChangedOnlyWhenTheValueChanges()
        {
            int n = 0;
            void H() => n++;
            UserPrefs.Changed += H;
            try
            {
                UserPrefs.BgmPercent = 40;
                UserPrefs.BgmPercent = 41; // 丸めると 40 で同じ
                UserPrefs.BgmPercent = 45;
                Assert.That(n, Is.EqualTo(2));
            }
            finally
            {
                UserPrefs.Changed -= H;
            }
        }

        [Test]
        public void LegacyPhase3bSettingsAreMigrated()
        {
            // 3b: 効果音の音量（float、10% 刻み）と効果音のミュート
            PlayerPrefs.SetFloat(UserPrefs.LegacyVolumeKey, 0.3f);
            PlayerPrefs.SetInt(UserPrefs.LegacyMuteKey, 1);
            UserPrefs.ResetMigrationForTests();
            Assert.That(UserPrefs.SfxPercent, Is.EqualTo(30));
            Assert.That(UserPrefs.Mute, Is.True, "the old mute (only SFX existed) becomes the master mute");
            Assert.That(UserPrefs.MasterPercent, Is.EqualTo(UserPrefs.DefaultMasterPercent));
            Assert.That(UserPrefs.MuteInBackground, Is.True);
            Assert.That(PlayerPrefs.HasKey(UserPrefs.LegacyVolumeKey), Is.False, "old keys are removed");
            Assert.That(PlayerPrefs.HasKey(UserPrefs.LegacyMuteKey), Is.False);
            // 2 回目以降は移行しない（新しい値を古いキーで上書きしない）
            UserPrefs.SfxPercent = 65;
            PlayerPrefs.SetFloat(UserPrefs.LegacyVolumeKey, 0.1f);
            UserPrefs.ResetMigrationForTests();
            Assert.That(UserPrefs.SfxPercent, Is.EqualTo(65));
        }

        [Test]
        public void LegacyValuesOffTheGridAreSnapped()
        {
            PlayerPrefs.SetFloat(UserPrefs.LegacyVolumeKey, 0.62f);
            UserPrefs.ResetMigrationForTests();
            Assert.That(UserPrefs.SfxPercent, Is.EqualTo(60));
            Assert.That(UserPrefs.Mute, Is.False);
        }

        // ------------------------------------------------------------------
        // BGM の音量計算
        // ------------------------------------------------------------------

        [Test]
        public void FadeMovesLinearlyAndEqualPowerKeepsLoudnessInTheMiddle()
        {
            var f = new BgmFade(0f);
            f.To(1f, 0.8f);
            f.Step(0.4f);
            Assert.That(f.Value, Is.EqualTo(0.5f).Within(1e-4));
            f.Step(1f);
            Assert.That(f.Value, Is.EqualTo(1f));
            Assert.That(f.Done, Is.True);
            f.To(0f, 0f);
            Assert.That(f.Value, Is.EqualTo(0f), "0 seconds = immediate");
            Assert.That(BgmFade.EqualPower(0f), Is.EqualTo(0f));
            Assert.That(BgmFade.EqualPower(1f), Is.EqualTo(1f).Within(1e-5));
            // 等パワー: 出入りの 2 曲の音量の 2 乗和は常に 1（途中で音が痩せない）
            for (float x = 0f; x <= 1f; x += 0.1f)
            {
                float a = BgmFade.EqualPower(x), b = BgmFade.EqualPower(1f - x);
                Assert.That(a * a + b * b, Is.EqualTo(1f).Within(1e-4));
            }
        }

        [Test]
        public void DuckDipsHoldsAndRecovers()
        {
            var d = new DuckEnvelope { Level = 0.3f, Attack = 0.05f, Hold = 0.3f, Release = 0.5f };
            Assert.That(d.Multiplier, Is.EqualTo(1f));
            d.Trigger();
            d.Step(0.05f);
            Assert.That(d.Multiplier, Is.EqualTo(0.3f).Within(1e-4), "attack reaches the duck level");
            d.Step(0.2f);
            Assert.That(d.Multiplier, Is.EqualTo(0.3f).Within(1e-4), "held");
            // 保っている途中にもう一度（連鎖）→ 保つ時間が延びる
            d.Trigger(2f);
            d.Step(0.4f);
            Assert.That(d.Multiplier, Is.EqualTo(0.3f).Within(1e-4), "hold extended by the second trigger");
            float t = 0f;
            while (d.Active && t < 5f)
            {
                d.Step(0.02f);
                t += 0.02f;
            }
            Assert.That(d.Multiplier, Is.EqualTo(1f).Within(1e-4));
            Assert.That(t, Is.LessThan(1.5f));
        }

        [Test]
        public void EmptyLibraryHasNoTracks()
        {
            var lib = BgmLibrary.CreateEmpty();
            try
            {
                foreach (BgmCue c in System.Enum.GetValues(typeof(BgmCue))) Assert.That(lib.Has(c), Is.False, c.ToString());
                Assert.That(lib.crossfadeTime, Is.EqualTo(0.8f), "DESIGN §8-2: crossfade 0.8 s");
                lib.victory.intro = AudioClip.Create("jingle", 4410, 1, 44100, false);
                Assert.That(lib.Has(BgmCue.Victory), Is.True);
                Assert.That(lib.Track(BgmCue.Victory), Is.SameAs(lib.victory));
                Object.DestroyImmediate(lib.victory.intro);
            }
            finally
            {
                Object.DestroyImmediate(lib);
            }
        }

        [Test]
        public void ShippedLibraryExistsAndIsReferencedByGameAssets()
        {
            var lib = AssetDatabase.LoadAssetAtPath<BgmLibrary>(ArtBuilder.BgmLibraryPath);
            Assert.That(lib, Is.Not.Null, "ProjectSetup creates " + ArtBuilder.BgmLibraryPath);
            var assets = AssetDatabase.LoadAssetAtPath<Edgewise.Game.View.GameAssets>(ArtBuilder.GameAssetsPath);
            Assert.That(assets.bgm, Is.SameAs(lib));
        }

        // ------------------------------------------------------------------
        // インポート設定（Assets/Audio/Bgm/ に置いた曲）
        // ------------------------------------------------------------------

        [Test]
        public void BgmFilesGetWebGLFriendlyImportSettings()
        {
            Assert.That(BgmImportSettings.IsBgmPath("Assets/Audio/Bgm/title.ogg"), Is.True);
            Assert.That(BgmImportSettings.IsBgmPath("Assets/Audio/Sfx/x.wav"), Is.False);

            // テスト専用の仮の曲（合成音を WAV にしたもの）を置いて取り込み、すぐ消す
            string dir = "Assets/Audio/Bgm";
            if (!AssetDatabase.IsValidFolder("Assets/Audio")) AssetDatabase.CreateFolder("Assets", "Audio");
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Audio", "Bgm");
            string path = dir + "/__test_tmp_bgm.wav";
            try
            {
                File.WriteAllBytes(path, SfxSynth.ToWav(SfxSynth.Generate(SfxKind.Win)));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer, Is.Not.Null);
                Assert.That(importer.userData, Does.Contain(BgmImportSettings.Marker));
                Assert.That(importer.loadInBackground, Is.True);
                Assert.That(importer.forceToMono, Is.False);
                Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.CompressedInMemory));
                Assert.That(importer.defaultSampleSettings.quality, Is.EqualTo(BgmImportSettings.Quality).Within(1e-3));
                Assert.That(importer.defaultSampleSettings.sampleRateSetting, Is.EqualTo(AudioSampleRateSetting.PreserveSampleRate));
                // 印が付いた後は手で変えた設定を上書きしない
                var s = importer.defaultSampleSettings;
                s.quality = 0.9f;
                importer.defaultSampleSettings = s;
                importer.SaveAndReimport();
                importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.defaultSampleSettings.quality, Is.EqualTo(0.9f).Within(1e-3));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
            Assert.That(File.Exists(path), Is.False);
        }
    }
}
