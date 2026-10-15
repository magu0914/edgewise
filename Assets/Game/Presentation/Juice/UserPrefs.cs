using UnityEngine;

namespace Edgewise.Game.Juice
{
    /// <summary>
    /// プレイヤーの設定（演出速度・音量・ミュート・画面の揺れ）。PlayerPrefs に保存する。
    /// 音量（DESIGN §8-2）は 全体・BGM・効果音 の 3 本で、0〜100% を 5% 刻みの整数で持つ（浮動小数点の誤差で 5% からずれないように）。
    /// 実際の音量 = 全体 × 種類別（BGM / 効果音）。全体ミュート中は 0。
    /// Phase 3b までの保存値（効果音の音量 0..1 の float、効果音だけのミュート）は、最初に読んだときに新しいキーへ移す。
    /// </summary>
    public static class UserPrefs
    {
        const string SpeedKey = "edgewise.speed";
        const string ShakeKey = "edgewise.screenShake";

        // 音（Phase 3c）。値は 0..100 の整数（5 の倍数）
        const string AudioVersionKey = "edgewise.audio.version";
        const string MasterKey = "edgewise.audio.master";
        const string BgmKey = "edgewise.audio.bgm";
        const string SfxKey = "edgewise.audio.sfx";
        const string MuteKey = "edgewise.audio.mute";
        const string BackgroundMuteKey = "edgewise.audio.muteInBackground";
        const int AudioVersion = 2;

        // Phase 3b までのキー（移行元）
        public const string LegacyVolumeKey = "edgewise.sfxVolume";
        public const string LegacyMuteKey = "edgewise.mute";

        public const int VolumeStep = 5;

        /// <summary>効果音の音量の初期値（JuiceSettings.defaultSfxVolume で上書きする）。</summary>
        public static float DefaultSfxVolume = 0.6f;

        public const int DefaultMasterPercent = 80;
        public const int DefaultBgmPercent = 60;

        /// <summary>設定が変わったとき（音量・揺れなど）。</summary>
        public static event System.Action Changed;

        static bool _migrated;

        /// <summary>
        /// 旧設定（3b まで）からの移行。何度呼んでもよい（2 回目以降は何もしない）。
        /// - edgewise.sfxVolume（0..1 の float、10% 刻み）→ edgewise.audio.sfx（5% 刻みの整数）
        /// - edgewise.mute（効果音のミュート。3b では音は効果音だけだった）→ 全体ミュート
        /// </summary>
        public static void MigrateAudio()
        {
            if (_migrated) return;
            _migrated = true;
            if (PlayerPrefs.GetInt(AudioVersionKey, 0) >= AudioVersion) return;
            if (PlayerPrefs.HasKey(LegacyVolumeKey))
            {
                if (!PlayerPrefs.HasKey(SfxKey)) PlayerPrefs.SetInt(SfxKey, Snap(PlayerPrefs.GetFloat(LegacyVolumeKey, DefaultSfxVolume) * 100f));
                PlayerPrefs.DeleteKey(LegacyVolumeKey);
            }
            if (PlayerPrefs.HasKey(LegacyMuteKey))
            {
                if (!PlayerPrefs.HasKey(MuteKey)) PlayerPrefs.SetInt(MuteKey, PlayerPrefs.GetInt(LegacyMuteKey, 0) != 0 ? 1 : 0);
                PlayerPrefs.DeleteKey(LegacyMuteKey);
            }
            PlayerPrefs.SetInt(AudioVersionKey, AudioVersion);
            PlayerPrefs.Save();
        }

        /// <summary>テスト用: 次の読み出しで移行をやり直させる。</summary>
        public static void ResetMigrationForTests() => _migrated = false;

        /// <summary>0..100 を 5% 刻みに丸める。</summary>
        public static int Snap(float percent) => Mathf.Clamp(Mathf.RoundToInt(percent / VolumeStep) * VolumeStep, 0, 100);

        static int GetPercent(string key, int fallback)
        {
            MigrateAudio();
            return Snap(PlayerPrefs.GetInt(key, fallback));
        }

        static void SetPercent(string key, int value)
        {
            MigrateAudio();
            int v = Snap(value);
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetInt(key) == v) return;
            PlayerPrefs.SetInt(key, v);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static bool GetBool(string key, bool fallback)
        {
            MigrateAudio();
            return PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0;
        }

        static void SetBool(string key, bool value)
        {
            MigrateAudio();
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>全体の音量（0..100、5% 刻み）。</summary>
        public static int MasterPercent
        {
            get => GetPercent(MasterKey, DefaultMasterPercent);
            set => SetPercent(MasterKey, value);
        }

        /// <summary>BGM の音量（0..100、5% 刻み）。</summary>
        public static int BgmPercent
        {
            get => GetPercent(BgmKey, DefaultBgmPercent);
            set => SetPercent(BgmKey, value);
        }

        /// <summary>効果音の音量（0..100、5% 刻み）。</summary>
        public static int SfxPercent
        {
            get => GetPercent(SfxKey, Snap(DefaultSfxVolume * 100f));
            set => SetPercent(SfxKey, value);
        }

        /// <summary>効果音の音量（0..1）。5% 刻みに丸めて保存する。</summary>
        public static float SfxVolume
        {
            get => SfxPercent / 100f;
            set => SfxPercent = Snap(value * 100f);
        }

        /// <summary>全体ミュート（BGM も効果音も止める）。</summary>
        public static bool Mute
        {
            get => GetBool(MuteKey, false);
            set => SetBool(MuteKey, value);
        }

        /// <summary>タブが裏に回ったら（ウィンドウが非アクティブになったら）消音する。初期値 ON。</summary>
        public static bool MuteInBackground
        {
            get => GetBool(BackgroundMuteKey, true);
            set => SetBool(BackgroundMuteKey, value);
        }

        /// <summary>実際に掛ける効果音の音量（全体 × 効果音。ミュート中は 0）。</summary>
        public static float EffectiveSfxVolume => Mute ? 0f : MasterPercent / 100f * (SfxPercent / 100f);

        /// <summary>実際に掛ける BGM の音量（全体 × BGM。ミュート中は 0）。</summary>
        public static float EffectiveBgmVolume => Mute ? 0f : MasterPercent / 100f * (BgmPercent / 100f);

        /// <summary>画面の揺れ（動きを減らしたい人は OFF）。</summary>
        public static bool ScreenShake
        {
            get => PlayerPrefs.GetInt(ShakeKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(ShakeKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>テスト・自動操作用の上書き（null なら保存値）。</summary>
        public static PlaybackSpeed? SpeedOverride;

        public static PlaybackSpeed Speed
        {
            get => SpeedOverride ?? (PlaybackSpeed)Mathf.Clamp(PlayerPrefs.GetInt(SpeedKey, 0), 0, 2);
            set
            {
                PlayerPrefs.SetInt(SpeedKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// 演出の時間に掛ける倍率。デバッグ用のスロー再生（URL の ?slow=4 など）も掛ける。
        /// </summary>
        public static float TimeFactor => Speed.TimeFactor() * DebugSlowFactor;

        /// <summary>スクリーンショット撮影用のスロー倍率（既定 1）。</summary>
        public static float DebugSlowFactor = 1f;
    }
}
