using Edgewise.Game.Juice;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.Audio
{
    /// <summary>
    /// 効果音を起動時に AudioClip.Create で合成し、鳴らす（表示側の「音」だけを担当。DESIGN §8-1 SfxBank）。
    /// - 音量 = 設定の音量（UserPrefs.SfxVolume）× 種類ごとの音量（JuiceSettings）。ミュート中は鳴らさない。
    /// - WebGL のブラウザは最初のユーザー操作（クリック・タップ・キー）まで音を出せない。それより前の音は貯めずに捨てる
    ///   （操作した瞬間に溜まった音がまとめて鳴るのを防ぐ）。Unity は最初の操作で AudioContext を再開する。
    /// - uGUI のボタンを押したときの音もここで付ける。
    /// </summary>
    public sealed class SfxBank : MonoBehaviour
    {
        const int Voices = 10;

        [SerializeField] JuiceSettings juice;

        AudioClip[] _clips;
        AudioSource[] _sources;
        int _next;
        bool _unlocked;
        float _lastCursor = -1f;

        public static SfxBank Instance { get; private set; }

        /// <summary>鳴らした回数と最後に鳴らした音（テスト用）。</summary>
        public int PlayedCount { get; private set; }
        public SfxKind LastKind { get; private set; }
        public float LastSemitones { get; private set; }

        public bool Unlocked => _unlocked;

        public void Wire(JuiceSettings j) => juice = j;

        void Awake()
        {
            Instance = this;
            if (juice == null) juice = JuiceSettings.CreateDefault();
            UserPrefs.DefaultSfxVolume = juice.defaultSfxVolume;
            _clips = new AudioClip[SfxSynth.All.Length];
            foreach (var kind in SfxSynth.All)
            {
                var data = SfxSynth.Generate(kind);
                var clip = AudioClip.Create("sfx_" + kind, data.Length, 1, SfxSynth.SampleRate, false);
                clip.SetData(data, 0);
                _clips[(int)kind] = clip;
            }
            _sources = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.loop = false;
                _sources[i] = s;
            }
        }

        void Start()
        {
            // シーンにあるボタン（非表示のものも含む）に押した音を付ける
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                b.onClick.AddListener(() => Play(SfxKind.Button));
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (_unlocked) return;
            var p = Pointer.current;
            var kb = Keyboard.current;
            if ((p != null && p.press.wasPressedThisFrame) || (kb != null && kb.anyKey.wasPressedThisFrame)) _unlocked = true;
        }

        /// <summary>最初の操作を待たずに鳴らせるようにする（テスト・WebGL 以外）。</summary>
        public void Unlock() => _unlocked = true;

        /// <summary>種類ごとの音量（JuiceSettings）。</summary>
        public static float KindVolume(JuiceSettings juice, SfxKind k)
        {
            switch (k)
            {
                case SfxKind.Cursor: return juice.volCursor;
                case SfxKind.Pick: return juice.volPick;
                case SfxKind.Place: return juice.volPlace;
                case SfxKind.Flip: return juice.volFlip;
                case SfxKind.Same:
                case SfxKind.Plus: return juice.volRule;
                case SfxKind.Combo: return juice.volCombo;
                case SfxKind.Win:
                case SfxKind.Lose:
                case SfxKind.Draw: return juice.volResult;
                default: return juice.volButton;
            }
        }

        /// <summary>鳴らす。semitones は半音単位の音程（裏返すたびに上げる）。</summary>
        public void Play(SfxKind kind, float semitones = 0f)
        {
            if (kind == SfxKind.Cursor)
            {
                if (Time.unscaledTime - _lastCursor < juice.cursorSoundInterval) return;
                _lastCursor = Time.unscaledTime;
            }
            LastKind = kind;
            LastSemitones = semitones;
            PlayedCount++;
            if (!_unlocked || UserPrefs.Mute) return;
            float vol = UserPrefs.SfxVolume * KindVolume(juice, kind);
            if (vol <= 0.001f) return;
            var s = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            s.Stop();
            s.clip = _clips[(int)kind];
            s.pitch = Mathf.Pow(2f, semitones / 12f);
            s.volume = vol;
            s.Play();
        }
    }
}
