using Edgewise.Core;
using Edgewise.Game.Audio;
using Edgewise.Game.Match;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using UnityEngine;

namespace Edgewise.Game.Juice
{
    /// <summary>
    /// 演出の味付け（Phase 3b）: 効果音・ヒットストップ・画面の揺れ。EventPlayer と PlayerController が節目ごとに呼ぶ（IMatchFx）。
    /// 光（白い光・光の輪）は EventPlayer が直接出す。値はすべて JuiceSettings。
    /// 演出速度「即時」のときは、ヒットストップ・揺れ・裏返しの音を出さない（一度に大量に鳴るのを避ける）。
    /// </summary>
    public sealed class JuiceFx : MonoBehaviour, IMatchFx
    {
        [SerializeField] JuiceSettings juice;
        [SerializeField] SfxBank sfx;
        [SerializeField] TableLayout layout;

        readonly ShakeModel _shake = new ShakeModel();

        public ShakeModel Shake => _shake;

        public void Wire(JuiceSettings j, SfxBank s, TableLayout l)
        {
            juice = j;
            sfx = s;
            layout = l;
        }

        static bool Animated => UserPrefs.TimeFactor > 0f;

        void Awake()
        {
            if (juice == null) juice = JuiceSettings.CreateDefault();
        }

        void Update()
        {
            _shake.Enabled = UserPrefs.ScreenShake;
            _shake.MaxOffset = juice.shakeMaxOffset;
            _shake.Frequency = juice.shakeFrequency;
            _shake.Decay = juice.shakeDecay;
            var offset = _shake.Step(Time.unscaledDeltaTime);
            if (layout != null) layout.ShakeOffset = offset;
        }

        /// <summary>1 手の中で indexInMove 枚目の裏返しの音程（半音）。同じ手の中ではリセットしない。</summary>
        public static float FlipSemitones(int indexInMove, JuiceSettings j) => Mathf.Min(indexInMove * j.flipSemitoneStep, j.flipSemitoneMax);

        void Play(SfxKind k, float semi = 0f)
        {
            if (sfx != null) sfx.Play(k, semi);
        }

        public void OnPick() => Play(SfxKind.Pick);

        public void OnCursorMove() => Play(SfxKind.Cursor);

        public void OnPlace(int cell)
        {
            Play(SfxKind.Place);
            if (Animated) _shake.Add(juice.placeShake);
        }

        public void OnFlip(int cell, Player to, int indexInMove, int chainDepth)
        {
            if (!Animated) return;
            Play(SfxKind.Flip, FlipSemitones(indexInMove, juice));
        }

        public void OnRule(RuleHeading rule)
        {
            if (!Animated) return;
            Play(rule == RuleHeading.Plus ? SfxKind.Plus : SfxKind.Same);
            TweenRunner.HitStop(juice.ruleHitStop);
            _shake.Add(juice.ruleShake);
        }

        public void OnCombo(int depth)
        {
            if (!Animated) return;
            Play(SfxKind.Combo, Mathf.Min(2f * (depth - 1), 7f));
            // 段ごとに 50ms のヒットストップと、段が深いほど強い揺れ
            TweenRunner.HitStop(juice.comboHitStop);
            _shake.Add(juice.comboShakePerDepth * depth);
        }

        public void OnEnd(GameResult resultForSeat0) =>
            Play(resultForSeat0 == GameResult.P0 ? SfxKind.Win : resultForSeat0 == GameResult.P1 ? SfxKind.Lose : SfxKind.Draw);
    }
}
