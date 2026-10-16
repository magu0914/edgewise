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
    /// 3a（時間）と 3b（ヒットストップ・揺れ・光・音・CPU の考える演出・決着・ヒント）の調整値をすべてここに持つ。
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
        [Tooltip("SAME! / PLUS! / COMBO の文字の大きさ（画面中央の文字に対する倍率）。置いたカードの少し上に出す")] public float ruleTextScale = 0.6f;

        [Header("Combo")]
        [Tooltip("連鎖の段ごとの待ち")] public float comboStepWait = 0.2f;
        [Tooltip("COMBO の文字の時間")] public float comboBannerTime = 0.45f;
        [Tooltip("連鎖の段が 1 つ深くなるごとの文字の拡大")] public float comboTextGrowth = 0.15f;
        [Tooltip("連鎖の段が 1 つ深くなるごとの光（白い光・光の輪）の強さの増分")] public float comboLightPerDepth = 0.6f;
        [Tooltip("裏返った瞬間の白い光の時間")] public float flipFlashTime = 0.2f;

        [Header("ヒットストップ（演出の時間を一瞬止める）")]
        [Tooltip("連鎖の段ごとのヒットストップ（秒）")] public float comboHitStop = 0.05f;
        [Tooltip("SAME! / PLUS! のヒットストップ（秒）")] public float ruleHitStop = 0.04f;

        [Header("画面の揺れ（設定で OFF にできる）")]
        [Tooltip("揺れの最大のずれ（ワールド単位）")] public float shakeMaxOffset = 0.16f;
        [Tooltip("揺れの細かさ（Hz）")] public float shakeFrequency = 24f;
        [Tooltip("揺れが収まる速さ（1 秒あたりに減る強さ）")] public float shakeDecay = 2.4f;
        [Tooltip("置いたときの揺れの強さ（0..1）")] public float placeShake = 0.12f;
        [Tooltip("SAME! / PLUS! の揺れの強さ（0..1）")] public float ruleShake = 0.3f;
        [Tooltip("連鎖の段 1 つあたりの揺れの強さ（0..1）")] public float comboShakePerDepth = 0.32f;

        [Header("Pivot")]
        [Tooltip("90° 回すアニメーションの時間")] public float rotateTime = 0.12f;

        [Header("手番・CPU")]
        [Tooltip("CPU が考える最低時間")] public float cpuMinThink = 0.4f;
        [Tooltip("CPU が考えている間、手札の 1 枚が浮く高さ")] public float cpuBobHeight = 0.2f;
        [Tooltip("浮いたり戻ったりする 1 回の時間（秒）。1 回ごとに別のカードに移る")] public float cpuBobPeriod = 0.8f;
        [Tooltip("先攻表示・SUDDEN DEATH などの文字の時間")] public float infoBannerTime = 0.8f;
        [Tooltip("配る・手札の並べ直しの時間")] public float dealTime = 0.3f;
        [Tooltip("配るときの 1 枚ごとのずらし")] public float dealStagger = 0.04f;

        [Header("スコア・決着")]
        [Tooltip("スコアが弾む時間")] public float scorePopTime = 0.2f;
        [Tooltip("スコアが弾む大きさ")] public float scorePopScale = 1.4f;
        [Tooltip("結果画面のカウントアップ 1 点あたりの時間")] public float resultCountStep = 0.09f;
        [Tooltip("WIN / LOSE / DRAW の文字が出る時間")] public float resultTextTime = 0.45f;
        [Tooltip("決着: 盤のカードが勝者の色に光る 1 枚ごとの間隔")] public float finaleStep = 0.09f;
        [Tooltip("決着: 全部光ってから結果を出すまでの間")] public float finaleHold = 0.45f;

        [Header("予告")]
        [Tooltip("裏返る予定のカードの光の強さ")] public float previewGlow = 1f;
        [Tooltip("置く位置に出す半透明カードの不透明度")] public float ghostAlpha = 0.45f;

        [Header("ヒント（AnalyzeMoves。相手の手札は盗み見ない）")]
        [Tooltip("ヒントの探索ノード数")] public int hintNodeBudget = 250000;
        [Tooltip("ヒントの探索に 1 フレームで使う時間（ミリ秒）")] public float hintFrameBudgetMs = 6f;

        [Header("効果音（AudioClip.Create の合成音）")]
        [Tooltip("効果音の音量の初期値（設定で変えられる）")] [Range(0f, 1f)] public float defaultSfxVolume = 0.6f;
        [Tooltip("裏返すたびに上げる音程（半音）")] public float flipSemitoneStep = 1f;
        [Tooltip("音程を上げる上限（半音）")] public float flipSemitoneMax = 12f;
        [Range(0f, 1f)] public float volCursor = 0.35f;
        [Range(0f, 1f)] public float volPick = 0.6f;
        [Range(0f, 1f)] public float volPlace = 0.8f;
        [Range(0f, 1f)] public float volFlip = 0.7f;
        [Range(0f, 1f)] public float volRule = 0.75f;
        [Range(0f, 1f)] public float volCombo = 0.75f;
        [Range(0f, 1f)] public float volResult = 0.8f;
        [Range(0f, 1f)] public float volButton = 0.45f;
        [Tooltip("星図巡り: 報酬（NEW! / ×2）・鍵が外れる音")] [Range(0f, 1f)] public float volReward = 0.8f;
        [Tooltip("星図巡り: 会話の文字送りの音")] [Range(0f, 1f)] public float volTalk = 0.25f;
        [Tooltip("カーソル移動の音を続けて鳴らす最短の間隔（秒）")] public float cursorSoundInterval = 0.05f;

        /// <summary>ScriptableObject が見つからないとき（テストなど）の既定値。</summary>
        public static JuiceSettings CreateDefault()
        {
            var j = CreateInstance<JuiceSettings>();
            j.name = "JuiceSettings (default)";
            return j;
        }
    }
}
