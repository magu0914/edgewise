using Edgewise.Game.Juice;
using UnityEngine;

namespace Edgewise.Game.Audio
{
    /// <summary>
    /// BGM の再生（DESIGN §8-2）。場面（BgmCue）を指定すると、BgmLibrary の曲へクロスフェードで切り替える。
    /// - 2 つのデッキ（AudioSource）を交互に使う。出ていく曲は crossfadeTime で下げて止め、入ってくる曲は crossfadeTime で上げる
    ///   （ジングルなど intro から始まる曲は頭を立たせるため 0.05 秒で上げる）。
    /// - intro（1 回）→ loop（ループ）。intro が終わったら同じデッキで loop に切り替える。
    /// - Duck(): SAME / PLUS / COMBO の瞬間に一瞬下げる（DuckEnvelope）。
    /// - 音量 = 全体 × BGM（UserPrefs、全体ミュートで 0）× 曲ごとの音量 × フェード × ダッキング。
    /// - 曲が未設定の場面は無音（前の曲をフェードアウトするだけ）で、エラーにしない。BgmLibrary 自体が無くても動く。
    /// 時間は Time.unscaledDeltaTime で進める（ヒットストップや timeScale の影響を受けない）。
    /// </summary>
    public sealed class BgmPlayer : MonoBehaviour
    {
        const float IntroFadeIn = 0.05f;

        [SerializeField] BgmLibrary library;

        sealed class Deck
        {
            public AudioSource Source;
            public BgmTrack Track;
            public BgmCue Cue;
            public readonly BgmFade Fade = new BgmFade();
            public bool InIntro;
            public float Elapsed;

            public bool Active => Track != null;
        }

        readonly Deck[] _decks = new Deck[2];
        int _current = -1; // 今の場面の曲を鳴らしているデッキ（無音なら -1）
        readonly DuckEnvelope _duck = new DuckEnvelope();
        float _prefVolume = 1f;

        public static BgmPlayer Instance { get; private set; }

        public BgmLibrary Library => library;

        /// <summary>今の場面（無音の場面も含む）。</summary>
        public BgmCue CurrentCue { get; private set; } = BgmCue.None;

        /// <summary>今の場面で鳴らしている曲（intro 中は intro、その後は loop。無音なら null）。</summary>
        public AudioClip CurrentClip => _current >= 0 ? _decks[_current].Source.clip : null;

        /// <summary>今の場面の曲が intro を鳴らしている途中か。</summary>
        public bool InIntro => _current >= 0 && _decks[_current].InIntro;

        /// <summary>今の場面の曲の実際の音量（AudioSource.volume）。無音なら 0。</summary>
        public float CurrentVolume => _current >= 0 ? _decks[_current].Source.volume : 0f;

        /// <summary>フェードアウト中のデッキも含めて、鳴っている（音量 0 より大きい）デッキの数。</summary>
        public int AudibleDecks
        {
            get
            {
                int n = 0;
                foreach (var d in _decks)
                    if (d != null && d.Active && d.Source.volume > 0f) n++;
                return n;
            }
        }

        /// <summary>ダッキングの倍率（1 = 下げていない）。</summary>
        public float DuckMultiplier => _duck.Multiplier;

        /// <summary>Duck() を呼んだ回数（テスト用）。</summary>
        public int DuckCount { get; private set; }

        public void Wire(BgmLibrary lib) => library = lib;

        /// <summary>曲の割り当てを差し替える（テストで合成した仮の曲を使うため）。鳴っている曲は止める。</summary>
        public void SetLibrary(BgmLibrary lib)
        {
            library = lib;
            StopImmediately();
        }

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < 2; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.volume = 0f;
                s.priority = 0; // BGM は効果音に押し出されないように
                _decks[i] = new Deck { Source = s };
            }
            UserPrefs.Changed += OnPrefsChanged;
            OnPrefsChanged();
        }

        void OnDestroy()
        {
            UserPrefs.Changed -= OnPrefsChanged;
            if (Instance == this) Instance = null;
        }

        void OnPrefsChanged() => _prefVolume = UserPrefs.EffectiveBgmVolume;

        /// <summary>
        /// 場面を切り替える。同じ場面なら何もしない（曲を頭から鳴らし直さない）。
        /// 戻り値: その場面に曲が割り当てられているか（false なら無音）。
        /// </summary>
        public bool Play(BgmCue cue)
        {
            bool has = library != null && library.Has(cue);
            if (cue == CurrentCue) return has;
            CurrentCue = cue;
            float xfade = library != null ? library.crossfadeTime : 0.8f;

            // 今の曲はフェードアウト（止めるのは Update で音量が 0 になってから）
            if (_current >= 0) _decks[_current].Fade.To(0f, xfade);
            _current = -1;
            if (!has) return false;

            // 空いている方（もう一方）のデッキで次の曲を始める。両方鳴っていたら、小さい方を止めて使う
            int next = PickDeck();
            var d = _decks[next];
            var track = library.Track(cue);
            d.Track = track;
            d.Cue = cue;
            d.Elapsed = 0f;
            d.InIntro = track.intro != null;
            d.Source.Stop();
            d.Source.clip = d.InIntro ? track.intro : track.loop;
            d.Source.loop = !d.InIntro;
            d.Fade.To(0f, 0f);
            d.Fade.To(1f, d.InIntro ? IntroFadeIn : xfade);
            Apply(d);
            d.Source.Play();
            _current = next;
            return true;
        }

        int PickDeck()
        {
            for (int i = 0; i < 2; i++)
                if (!_decks[i].Active) return i;
            int quieter = _decks[0].Fade.Value <= _decks[1].Fade.Value ? 0 : 1;
            Release(_decks[quieter]);
            return quieter;
        }

        /// <summary>BGM を一瞬下げる（SAME / PLUS / COMBO）。holdScale で下げている時間を伸ばす（連鎖が深いほど長く）。</summary>
        public void Duck(float holdScale = 1f)
        {
            DuckCount++;
            if (library != null)
            {
                _duck.Level = library.duckLevel;
                _duck.Attack = library.duckAttack;
                _duck.Hold = library.duckHold;
                _duck.Release = library.duckRelease;
            }
            _duck.Trigger(holdScale);
        }

        /// <summary>すべて即座に止める（フェードなし）。</summary>
        public void StopImmediately()
        {
            foreach (var d in _decks)
                if (d != null) Release(d);
            _current = -1;
            CurrentCue = BgmCue.None;
            _duck.Reset();
        }

        static void Release(Deck d)
        {
            d.Source.Stop();
            d.Source.clip = null;
            d.Source.volume = 0f;
            d.Track = null;
            d.Cue = BgmCue.None;
            d.InIntro = false;
            d.Fade.To(0f, 0f);
        }

        void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>フェード・ダッキング・intro → loop を dt 秒進める（Update から。テストでも直接呼べる）。</summary>
        public void Tick(float dt)
        {
            _duck.Step(dt);
            for (int i = 0; i < 2; i++)
            {
                var d = _decks[i];
                if (d == null || !d.Active) continue;
                d.Fade.Step(dt);
                d.Elapsed += dt;
                if (i != _current && d.Fade.Value <= 0f)
                {
                    Release(d);
                    continue;
                }
                if (d.InIntro) AdvanceIntro(d, i);
                Apply(d);
            }
        }

        void AdvanceIntro(Deck d, int index)
        {
            var intro = d.Track.intro;
            // 終わったら（再生できない環境でも intro の長さ + 0.5 秒で）loop へ
            bool ended = d.Elapsed >= intro.length && !d.Source.isPlaying;
            bool failsafe = d.Elapsed >= intro.length + 0.5f;
            if (!ended && !failsafe) return;
            d.InIntro = false;
            if (d.Track.loop == null)
            {
                d.Source.Stop();
                if (index == _current) _current = -1;
                Release(d);
                return;
            }
            d.Source.clip = d.Track.loop;
            d.Source.loop = true;
            d.Source.Play();
        }

        void Apply(Deck d)
        {
            if (!d.Active) return;
            float gain = BgmFade.EqualPower(d.Fade.Value);
            d.Source.volume = _prefVolume * d.Track.volume * gain * _duck.Multiplier;
        }
    }
}
