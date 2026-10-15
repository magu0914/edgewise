using UnityEngine;

namespace Edgewise.Game.Juice
{
    /// <summary>演出の再生速度（DESIGN §8: 演出スキップ）。</summary>
    public enum PlaybackSpeed
    {
        Normal = 0,
        Double = 1,
        Instant = 2,
    }

    public static class PlaybackSpeedExtensions
    {
        /// <summary>演出の時間に掛ける倍率（×1 = 1、×2 = 0.5、即時 = 0）。</summary>
        public static float TimeFactor(this PlaybackSpeed s) => s == PlaybackSpeed.Normal ? 1f : s == PlaybackSpeed.Double ? 0.5f : 0f;
    }

    /// <summary>
    /// 演出の数値（DESIGN §8-1「演出のタイミング」。×1 速度の初期値）。エディタで調整できる。
    /// Phase 3a で使う時間の値だけを持つ。3b（ヒットストップ・揺れ・音程・光）の値はここに足していく。
    /// </summary>
    [CreateAssetMenu(menuName = "Edgewise/Juice Settings", fileName = "JuiceSettings")]
    public sealed class JuiceSettings : ScriptableObject
    {
        [Header("持つ")]
        [Tooltip("持ち上げにかかる時間（秒）")] public float pickTime = 0.08f;
        [Tooltip("持っている間の拡大率")] public float pickScale = 1.12f;
        [Tooltip("持っている間の浮き上がり（カメラ方向、ワールド単位）")] public float pickLift = 0.7f;
        [Tooltip("持っている間の傾き（度）")] public float holdTiltDegrees = 5f;
        [Tooltip("持っている間の揺れの速さ（Hz）")] public float holdWobbleHz = 1.1f;
        [Tooltip("手札にカーソルを乗せたときの浮き上がり")] public float hoverRaise = 0.16f;

        [Header("置く")]
        [Tooltip("手札→マスの移動時間（減速して到着）")] public float placeMoveTime = 0.18f;
        [Tooltip("着地でつぶれて戻る時間")] public float landSquashTime = 0.12f;
        [Tooltip("着地の瞬間の縦横の倍率")] public Vector2 landSquash = new Vector2(1.12f, 0.88f);
        [Tooltip("着地の光の輪の時間")] public float landBurstTime = 0.35f;

        [Header("裏返す")]
        [Tooltip("1 枚の 3D 回転の時間")] public float flipTime = 0.25f;
        [Tooltip("複数枚を裏返すときのずらし")] public float flipStagger = 0.05f;
        [Tooltip("回転中の浮き上がり")] public float flipLift = 0.35f;

        [Header("Same / Plus")]
        [Tooltip("成立した辺を光の線で結ぶ時間")] public float ruleLinkTime = 0.3f;
        [Tooltip("SAME! / PLUS! の文字の時間")] public float ruleBannerTime = 0.5f;

        [Header("Combo")]
        [Tooltip("連鎖の段ごとの待ち")] public float comboStepWait = 0.2f;
        [Tooltip("COMBO の文字の時間")] public float comboBannerTime = 0.45f;

        [Header("Pivot")]
        [Tooltip("90° 回すアニメーションの時間")] public float rotateTime = 0.12f;

        [Header("手番・CPU")]
        [Tooltip("CPU が考える最低時間")] public float cpuMinThink = 0.4f;
        [Tooltip("先攻表示・SUDDEN DEATH などの文字の時間")] public float infoBannerTime = 0.8f;
        [Tooltip("配る・手札の並べ直しの時間")] public float dealTime = 0.3f;
        [Tooltip("配るときの 1 枚ごとのずらし")] public float dealStagger = 0.04f;

        [Header("スコア・決着")]
        [Tooltip("スコアが弾む時間")] public float scorePopTime = 0.2f;
        [Tooltip("スコアが弾む大きさ")] public float scorePopScale = 1.4f;
        [Tooltip("結果画面のカウントアップ 1 点あたりの時間")] public float resultCountStep = 0.09f;
        [Tooltip("WIN / LOSE / DRAW の文字が出る時間")] public float resultTextTime = 0.45f;

        [Header("予告")]
        [Tooltip("裏返る予定のカードの光の強さ")] public float previewGlow = 1f;
        [Tooltip("置く位置に出す半透明カードの不透明度")] public float ghostAlpha = 0.45f;

        /// <summary>ScriptableObject が見つからないとき（テストなど）の既定値。</summary>
        public static JuiceSettings CreateDefault()
        {
            var j = CreateInstance<JuiceSettings>();
            j.name = "JuiceSettings (default)";
            return j;
        }
    }
}
