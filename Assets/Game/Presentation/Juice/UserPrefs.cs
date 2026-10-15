using UnityEngine;

namespace Edgewise.Game.Juice
{
    /// <summary>
    /// プレイヤーの表示設定（演出速度・ミュート）。PlayerPrefs に保存する。
    /// 3b の設定画面（音量・画面の揺れ OFF）もここに足す。
    /// </summary>
    public static class UserPrefs
    {
        const string SpeedKey = "edgewise.speed";
        const string MuteKey = "edgewise.mute";

        /// <summary>テスト・自動操作用の上書き（null なら保存値）。</summary>
        public static PlaybackSpeed? SpeedOverride;

        public static PlaybackSpeed Speed
        {
            get => SpeedOverride ?? (PlaybackSpeed)Mathf.Clamp(PlayerPrefs.GetInt(SpeedKey, 0), 0, 2);
            set
            {
                PlayerPrefs.SetInt(SpeedKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static bool Mute
        {
            get => PlayerPrefs.GetInt(MuteKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
                PlayerPrefs.Save();
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
