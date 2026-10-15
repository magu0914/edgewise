using System;
using UnityEngine;

namespace Edgewise.Game.Audio
{
    /// <summary>BGM を鳴らす場面（DESIGN §8-2）。</summary>
    public enum BgmCue
    {
        /// <summary>無音（フェードアウトして止める）。</summary>
        None = 0,

        /// <summary>タイトル・対戦設定。</summary>
        Title = 1,

        /// <summary>対戦中。</summary>
        Match = 2,

        /// <summary>勝利: ジングル（intro）→ 結果画面のループ（loop）。</summary>
        Victory = 3,

        Defeat = 4,
        Draw = 5,
    }

    /// <summary>
    /// 1 つの場面の曲。intro（1 回だけ）→ loop（ループ）の順に鳴らす。どちらか片方だけでもよい。両方空ならその場面は無音。
    /// 例: タイトル = loop だけ、勝利 = intro にジングル・loop に結果画面の曲。
    /// </summary>
    [Serializable]
    public sealed class BgmTrack
    {
        [Tooltip("最初に 1 回だけ鳴らす曲（ジングルなど）。空なら loop から始める")] public AudioClip intro;
        [Tooltip("ループ再生する曲。空なら intro が終わったら無音")] public AudioClip loop;
        [Tooltip("この場面の音量（曲ごとの音量差をそろえる）")] [Range(0f, 1f)] public float volume = 1f;

        public bool IsEmpty => intro == null && loop == null;
    }

    /// <summary>
    /// 場面ごとの曲の割り当て（ScriptableObject。Assets/Game/Settings/BgmLibrary.asset）と、切り替え・ダッキングの数値。
    /// 曲ファイルは Assets/Audio/Bgm/ に置き、ここに割り当てるだけで鳴る（docs/AUDIO.md）。曲が未設定の場面は無音で、エラーにしない。
    /// </summary>
    [CreateAssetMenu(menuName = "Edgewise/BGM Library", fileName = "BgmLibrary")]
    public sealed class BgmLibrary : ScriptableObject
    {
        [Header("場面ごとの曲")]
        public BgmTrack title = new BgmTrack();
        public BgmTrack match = new BgmTrack();
        [Tooltip("intro = 勝利のジングル、loop = 結果画面のループ")] public BgmTrack victory = new BgmTrack();
        public BgmTrack defeat = new BgmTrack();
        public BgmTrack draw = new BgmTrack();

        [Header("切り替え")]
        [Tooltip("曲を切り替えるときのクロスフェードの時間（秒）")] [Min(0f)] public float crossfadeTime = 0.8f;

        [Header("ダッキング（SAME / PLUS / COMBO の瞬間に BGM を一瞬下げる）")]
        [Tooltip("下げたときの音量の倍率")] [Range(0f, 1f)] public float duckLevel = 0.35f;
        [Tooltip("下げきるまでの時間（秒）")] [Min(0f)] public float duckAttack = 0.05f;
        [Tooltip("下げたままにする時間（秒）。連鎖が続くと延びる")] [Min(0f)] public float duckHold = 0.35f;
        [Tooltip("元に戻るまでの時間（秒）")] [Min(0f)] public float duckRelease = 0.6f;

        public BgmTrack Track(BgmCue cue)
        {
            switch (cue)
            {
                case BgmCue.Title: return title;
                case BgmCue.Match: return match;
                case BgmCue.Victory: return victory;
                case BgmCue.Defeat: return defeat;
                case BgmCue.Draw: return draw;
                default: return null;
            }
        }

        /// <summary>この場面に曲が割り当てられているか。</summary>
        public bool Has(BgmCue cue)
        {
            var t = Track(cue);
            return t != null && !t.IsEmpty;
        }

        public static BgmLibrary CreateEmpty()
        {
            var lib = CreateInstance<BgmLibrary>();
            lib.name = "BgmLibrary (empty)";
            return lib;
        }
    }
}
