using UnityEngine;

namespace Edgewise.Game.Audio
{
    /// <summary>
    /// BGM の音量の計算だけを受け持つ（AudioSource を触らないので EditMode で単体テストできる）。
    /// - フェード: 0..1 の値を一定の速さで目標へ近づける（クロスフェードの出入り）。
    /// - ダッキング: Trigger で下げ始め、attack で duckLevel まで下げ、hold の間保ち、release で 1 へ戻す。
    /// </summary>
    public sealed class BgmFade
    {
        public float Value { get; private set; }
        public float Target { get; private set; }
        float _speed;

        public BgmFade(float value = 0f)
        {
            Value = value;
            Target = value;
        }

        /// <summary>target へ seconds 秒で動かす（0 秒なら即座）。</summary>
        public void To(float target, float seconds)
        {
            Target = Mathf.Clamp01(target);
            float d = Mathf.Abs(Target - Value);
            if (seconds <= 0f || d <= 0f)
            {
                Value = Target;
                _speed = 0f;
                return;
            }
            // 0→1 の全区間を seconds 秒で動く速さ（途中から切り替えても音量の変化の速さは同じ）
            _speed = 1f / seconds;
        }

        public bool Done => Mathf.Approximately(Value, Target);

        public float Step(float dt)
        {
            if (dt > 0f && !Done) Value = Mathf.MoveTowards(Value, Target, _speed * dt);
            return Value;
        }

        /// <summary>等パワーのクロスフェード用に、直線の値を音量の倍率へ（0→0、0.5→0.707、1→1）。</summary>
        public static float EqualPower(float linear) => Mathf.Sin(Mathf.Clamp01(linear) * Mathf.PI * 0.5f);
    }

    /// <summary>ダッキング（SAME / PLUS / COMBO の瞬間に BGM を一瞬下げる）の包絡線。値は音量の倍率（1 = 下げていない）。</summary>
    public sealed class DuckEnvelope
    {
        public float Level = 0.35f;
        public float Attack = 0.05f;
        public float Hold = 0.35f;
        public float Release = 0.6f;

        float _depth;     // 0 = 下げていない、1 = Level まで下げた
        float _holdLeft;
        bool _attacking;

        public bool Active => _depth > 0f || _attacking;

        /// <summary>今の倍率（1 → Level）。</summary>
        public float Multiplier => Mathf.Lerp(1f, Level, _depth);

        /// <summary>下げ始める。下げている途中・保っている途中に呼ぶと、保つ時間を延ばす（連鎖が続く間は下げたまま）。</summary>
        public void Trigger(float holdScale = 1f)
        {
            _attacking = true;
            _holdLeft = Mathf.Max(_holdLeft, Hold * Mathf.Max(0f, holdScale));
        }

        public float Step(float dt)
        {
            if (dt <= 0f) return Multiplier;
            if (_attacking)
            {
                _depth = Attack <= 0f ? 1f : Mathf.MoveTowards(_depth, 1f, dt / Attack);
                if (_depth >= 1f) _attacking = false;
            }
            else if (_holdLeft > 0f)
            {
                _holdLeft -= dt;
            }
            else if (_depth > 0f)
            {
                _depth = Release <= 0f ? 0f : Mathf.MoveTowards(_depth, 0f, dt / Release);
            }
            return Multiplier;
        }

        public void Reset()
        {
            _depth = 0f;
            _holdLeft = 0f;
            _attacking = false;
        }
    }
}
