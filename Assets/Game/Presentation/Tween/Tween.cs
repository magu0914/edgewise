using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Edgewise.Game.Tweening
{
    /// <summary>
    /// 1 本の補間。Tweens.Play で作り、TweenRunner が毎フレーム進める。
    /// 待つときは Unity 6 の Awaitable（Tweens.Await / AwaitAll）を使う。
    /// </summary>
    public sealed class Tween
    {
        internal UnityEngine.Object Owner;
        internal string Channel;
        internal float Duration;
        internal float Delay;
        internal float Elapsed;
        internal Action<float> Apply;
        internal Ease Ease;
        internal bool OwnerWasSet;

        public bool IsDone { get; private set; }

        /// <summary>今すぐ最後の値にして終える。</summary>
        public void Complete()
        {
            if (IsDone) return;
            IsDone = true;
            if (!OwnerWasSet || Owner != null) Apply?.Invoke(Easing.Evaluate(Ease, 1f));
        }

        /// <summary>値を変えずに止める。</summary>
        public void Kill() => IsDone = true;

        /// <summary>1 フレーム分進める。終わったら true。</summary>
        internal bool Step(float dt)
        {
            if (IsDone) return true;
            if (OwnerWasSet && Owner == null)
            {
                IsDone = true;
                return true;
            }
            Elapsed += dt;
            if (Elapsed < Delay) return false;
            float t = Duration <= 0f ? 1f : (Elapsed - Delay) / Duration;
            if (t >= 1f)
            {
                Complete();
                return true;
            }
            Apply?.Invoke(Easing.Evaluate(Ease, t));
            return false;
        }
    }

    /// <summary>Tween を毎フレーム進める（シーンに 1 つ。なければ自動で作る）。</summary>
    public sealed class TweenRunner : MonoBehaviour
    {
        static TweenRunner _instance;
        readonly List<Tween> _active = new List<Tween>();
        readonly List<Tween> _added = new List<Tween>();

        /// <summary>
        /// 全 Tween の時間の倍率（3b のヒットストップ用の口。0 で一時停止）。
        /// </summary>
        public static float TimeScale = 1f;

        static float _stopRemaining;

        /// <summary>ヒットストップ: 実時間で seconds 秒、すべての Tween（と Tweens.Delay）を止める。重なったら長い方。</summary>
        public static void HitStop(float seconds)
        {
            if (seconds > _stopRemaining) _stopRemaining = seconds;
        }

        public static bool IsStopped => _stopRemaining > 0f;

        /// <summary>1 フレームで進める時間の上限（ヒッチで演出が飛ばないように）。</summary>
        const float MaxStep = 0.1f;

        public static TweenRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[TweenRunner]");
                    go.hideFlags = HideFlags.DontSave;
                    _instance = go.AddComponent<TweenRunner>();
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        internal void Add(Tween t) => _added.Add(t);

        /// <summary>同じ owner・channel の動いている Tween を止める。</summary>
        internal void KillChannel(UnityEngine.Object owner, string channel)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var t = _active[i];
                if (!t.IsDone && t.Owner == owner && t.Channel == channel) t.Kill();
            }
            for (int i = 0; i < _added.Count; i++)
            {
                var t = _added[i];
                if (!t.IsDone && t.Owner == owner && t.Channel == channel) t.Kill();
            }
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep) * TimeScale;
            if (_stopRemaining > 0f)
            {
                _stopRemaining -= Time.unscaledDeltaTime;
                dt = 0f;
            }
            if (_added.Count > 0)
            {
                _active.AddRange(_added);
                _added.Clear();
            }
            for (int i = 0; i < _active.Count; i++) _active[i].Step(dt);
            _active.RemoveAll(t => t.IsDone);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }

    /// <summary>Tween の作成と待機。duration が 0 以下なら即座に最後の値を適用する（演出速度「即時」）。</summary>
    public static class Tweens
    {
        public static Tween Play(float duration, Action<float> apply, Ease ease = Ease.OutCubic, float delay = 0f,
            UnityEngine.Object owner = null, string channel = null)
        {
            if (owner != null && channel != null && Application.isPlaying) TweenRunner.Instance.KillChannel(owner, channel);
            var t = new Tween
            {
                Owner = owner,
                OwnerWasSet = owner != null,
                Channel = channel,
                Duration = Mathf.Max(0f, duration),
                Delay = Mathf.Max(0f, delay),
                Apply = apply,
                Ease = ease,
            };
            if (t.Duration <= 0f && t.Delay <= 0f || !Application.isPlaying)
            {
                t.Complete();
                return t;
            }
            apply?.Invoke(Easing.Evaluate(ease, 0f));
            TweenRunner.Instance.Add(t);
            return t;
        }

        public static async Awaitable Await(Tween t, CancellationToken ct)
        {
            while (t != null && !t.IsDone) await Awaitable.NextFrameAsync(ct);
        }

        public static async Awaitable AwaitAll(IReadOnlyList<Tween> tweens, CancellationToken ct)
        {
            while (true)
            {
                bool done = true;
                for (int i = 0; i < tweens.Count; i++)
                {
                    if (tweens[i] != null && !tweens[i].IsDone)
                    {
                        done = false;
                        break;
                    }
                }
                if (done) return;
                await Awaitable.NextFrameAsync(ct);
            }
        }

        public static Awaitable Run(float duration, Action<float> apply, CancellationToken ct, Ease ease = Ease.OutCubic, float delay = 0f,
            UnityEngine.Object owner = null, string channel = null) =>
            Await(Play(duration, apply, ease, delay, owner, channel), ct);

        /// <summary>指定秒数待つ（TweenRunner.TimeScale に従う）。0 以下なら待たない。</summary>
        public static async Awaitable Delay(float seconds, CancellationToken ct)
        {
            if (seconds <= 0f) return;
            var t = Play(seconds, null, Ease.Linear);
            await Await(t, ct);
        }
    }
}
