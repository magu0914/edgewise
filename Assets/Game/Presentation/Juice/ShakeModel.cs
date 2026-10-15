using UnityEngine;

namespace Edgewise.Game.Juice
{
    /// <summary>
    /// 画面の揺れの計算（「トラウマ」方式: 強さを足し、時間で減らし、強さの 2 乗でずらす）。MonoBehaviour ではないのでテストできる。
    /// Enabled = false（設定で揺れ OFF）のときは何を足しても常にずれ 0。
    /// </summary>
    public sealed class ShakeModel
    {
        float _trauma;
        float _time;

        public bool Enabled = true;
        public float MaxOffset = 0.16f;
        public float Frequency = 24f;
        public float Decay = 2.4f;

        /// <summary>今の強さ（0..1）。</summary>
        public float Trauma => _trauma;

        public void Add(float amount)
        {
            if (!Enabled) return;
            _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
        }

        public void Clear() => _trauma = 0f;

        /// <summary>dt 秒進めて、今のずれ（ワールド単位、xy）を返す。</summary>
        public Vector2 Step(float dt)
        {
            if (!Enabled)
            {
                _trauma = 0f;
                return Vector2.zero;
            }
            _time += dt;
            _trauma = Mathf.Max(0f, _trauma - Decay * dt);
            if (_trauma <= 0f) return Vector2.zero;
            float s = _trauma * _trauma * MaxOffset;
            float t = _time * Frequency;
            float x = Mathf.PerlinNoise(t, 0.37f) * 2f - 1f;
            float y = Mathf.PerlinNoise(0.71f, t) * 2f - 1f;
            return new Vector2(x, y) * s;
        }
    }
}
