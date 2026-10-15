using UnityEngine;

namespace Edgewise.Game.Juice
{
    /// <summary>
    /// プレイヤーの設定（演出速度・効果音の音量とミュート・画面の揺れ）。PlayerPrefs に保存する。
    /// </summary>
    public static class UserPrefs
    {
        const string SpeedKey = "edgewise.speed";
        const string MuteKey = "edgewise.mute";
        const string VolumeKey = "edgewise.sfxVolume";
        const string ShakeKey = "edgewise.screenShake";

        /// <summary>音量の初期値（JuiceSettings.defaultSfxVolume で上書きする）。</summary>
        public static float DefaultSfxVolume = 0.6f;

        /// <summary>設定が変わったとき（効果音の音量・揺れなど）。</summary>
        public static event System.Action Changed;

        /// <summary>効果音の音量（0..1）。</summary>
        public static float SfxVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultSfxVolume));
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

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

        public static bool Mute
        {
            get => PlayerPrefs.GetInt(MuteKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
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
