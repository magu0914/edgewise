using System;
using System.Collections.Generic;
using System.Threading;
using Edgewise.Core;
using Edgewise.Game.Audio;
using Edgewise.Game.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Edgewise.Game.View
{
    /// <summary>
    /// 星図巡りの「カードを受け取る」演出の舞台（ワールド空間）。導入（はじめの 7 枚）と報酬（裏向きの 5 枚）で使う。
    /// 対戦と同じ CardView（シェーダー・傾き・レベル加工）を並べ、裏向きのカードはゆっくり浮き沈みし、
    /// カーソルを乗せると傾いて浮く（DESIGN §8-1 の手触り）。選ぶと手前に出てめくれ（Y 軸の 3D 回転、90° で表に）、
    /// 光（Shape シェーダーの柔らかい光と光の輪。Bloom で光る）を出して、手持ち（画面の点）へ弧を描いて飛んでいく。
    /// 入力: マウスは乗せる → クリック、タッチはタップ、キーボードは ←→ で選んで Enter。
    /// </summary>
    public sealed class WorldCardStage : MonoBehaviour
    {
        const float TapPixels = 18f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ModeId = Shader.PropertyToID("_Mode");
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");

        [SerializeField] GameAssets assets;
        [SerializeField] TableLayout layout;

        sealed class Slot
        {
            public CardView Card;
            public MeshRenderer Glow;
            public MeshRenderer Ring;
            public Vector3 Home;
            public float Scale;
            public bool Busy;
            public bool Done;
            public float GlowAmount;
            public Color GlowColor = Color.white;
            public float Hover;
        }

        readonly List<Slot> _slots = new List<Slot>();
        readonly List<CardView> _pool = new List<CardView>();
        readonly List<MeshRenderer> _shapePool = new List<MeshRenderer>();
        MaterialPropertyBlock _mpb;
        int[] _rows = { 5 };
        int _count;
        int _hover = -1;
        int _keySel = -1;
        bool _keyboard;
        Vector2 _pressPos;
        bool _pressing;
        Vector2 _lastPointer;

        /// <summary>カードを選べる状態か（false の間は浮き沈みだけ）。</summary>
        public bool Interactive { get; set; }

        /// <summary>選ばれた（クリック・タップ・Enter）。</summary>
        public event Action<int> Clicked;

        public int Count => _count;
        public int HoverIndex => _hover;
        public CardView Card(int i) => _slots[i].Card;
        public bool IsDone(int i) => _slots[i].Done;
        public TableLayout Layout => layout;

        public void Wire(GameAssets a, TableLayout tableLayout)
        {
            assets = a;
            layout = tableLayout;
        }

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (layout != null) layout.Changed += OnLayoutChanged;
        }

        void OnDestroy()
        {
            if (layout != null) layout.Changed -= OnLayoutChanged;
        }

        void OnLayoutChanged()
        {
            if (!gameObject.activeInHierarchy || _count == 0) return;
            ComputeHomes();
            for (int i = 0; i < _count; i++)
            {
                var s = _slots[i];
                if (s.Busy || s.Done) continue;
                s.Card.transform.position = s.Home;
                s.Card.transform.localScale = Vector3.one * s.Scale;
            }
        }

        // ------------------------------------------------------------------
        // 並べる
        // ------------------------------------------------------------------

        /// <summary>
        /// カードを並べる。rowsLandscape / rowsPortrait は各行の枚数（例: 横 {5}・縦 {3, 2}）。faceDown なら裏向き。
        /// dealFrom があれば、そこ（ワールド座標）から配る動きをつける（timeFactor 0 なら即座に置く）。
        /// </summary>
        public async Awaitable ShowAsync(IReadOnlyList<CardDef> defs, bool faceDown, Player owner, int[] rowsLandscape, int[] rowsPortrait,
            Vector3? dealFrom, float timeFactor, CancellationToken ct)
        {
            gameObject.SetActive(true);
            _count = defs.Count;
            _rowsLandscape = rowsLandscape;
            _rowsPortrait = rowsPortrait;
            while (_slots.Count < _count) _slots.Add(new Slot());
            while (_pool.Count < _count)
            {
                var c = Instantiate(assets.cardPrefab, transform);
                c.name = "StageCard" + _pool.Count;
                _pool.Add(c);
            }
            for (int i = 0; i < _pool.Count; i++) _pool[i].gameObject.SetActive(i < _count);
            for (int i = 0; i < _count; i++)
            {
                var s = _slots[i];
                s.Card = _pool[i];
                s.Busy = false;
                s.Done = false;
                s.Hover = 0f;
                s.GlowAmount = 0f;
                s.Glow = s.Glow != null ? s.Glow : Shape("Glow" + i);
                s.Ring = s.Ring != null ? s.Ring : Shape("Ring" + i);
                s.Glow.gameObject.SetActive(false);
                s.Ring.gameObject.SetActive(false);
                var card = s.Card;
                card.Bind(defs[i], owner, 0, faceDown);
                card.SetGlow(0f, Color.white);
                card.SetSelect(0f);
                card.SetFlash(0f);
                card.SetLift(0f);
                card.SetAlpha(1f);
                card.SetModifier(0);
                card.SetCaptureMark(Vector2.zero);
                card.ResetTilt();
                card.SetOrder(30 + i);
                card.transform.rotation = Quaternion.identity;
            }
            _hover = -1;
            _keySel = -1;
            _keyboard = false;
            ComputeHomes();
            var tweens = new List<Tween>();
            for (int i = 0; i < _count; i++)
            {
                var s = _slots[i];
                var card = s.Card;
                card.transform.localScale = Vector3.one * s.Scale;
                if (dealFrom.HasValue && timeFactor > 0f)
                {
                    var from = dealFrom.Value;
                    var to = s.Home;
                    s.Busy = true;
                    card.transform.position = from;
                    var slot = s;
                    int idx = i;
                    float spin = (idx - (_count - 1) * 0.5f) * 8f;
                    tweens.Add(Tweens.Play(0.42f * timeFactor, k =>
                    {
                        card.transform.position = Vector3.LerpUnclamped(from, to, k) + new Vector3(0f, 0f, -0.6f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k)));
                        card.transform.rotation = Quaternion.Euler(0f, 0f, (1f - Mathf.Clamp01(k)) * spin);
                        if (k >= 1f) slot.Busy = false;
                    }, Ease.OutCubic, idx * 0.07f * timeFactor, card, "move"));
                    tweens.Add(Tweens.Play(0.01f, k => { if (k >= 1f) SfxBank.Instance?.Play(SfxKind.Cursor, idx); }, Ease.Linear, idx * 0.07f * timeFactor));
                }
                else card.transform.position = s.Home;
            }
            await Tweens.AwaitAll(tweens, ct);
            for (int i = 0; i < _count; i++) _slots[i].Busy = false;
        }

        int[] _rowsLandscape = { 5 };
        int[] _rowsPortrait = { 3, 2 };

        /// <summary>横・縦に合わせた各カードの位置と大きさ。</summary>
        void ComputeHomes()
        {
            if (layout == null) return;
            bool portrait = layout.Portrait;
            _rows = portrait ? _rowsPortrait : _rowsLandscape;
            float h = layout.VisibleHeight;
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float w = h * aspect;
            int maxPerRow = 0;
            foreach (var n in _rows) maxPerRow = Mathf.Max(maxPerRow, n);
            float gap = portrait ? 0.16f : 0.3f;
            float scaleW = (w * (portrait ? 0.92f : 0.84f) - gap * (maxPerRow - 1)) / (CardView.Size.x * maxPerRow);
            float scaleH = h * (portrait ? 0.21f : 0.36f) / CardView.Size.y;
            float scale = Mathf.Min(scaleW, scaleH, portrait ? 1.15f : 1.3f);
            float rowH = CardView.Size.y * scale + (portrait ? 0.25f : 0.3f);
            float centerY = portrait ? h * 0.02f : h * 0.02f;
            float top = centerY + (rowH * (_rows.Length - 1)) * 0.5f;
            int k = 0;
            for (int r = 0; r < _rows.Length; r++)
            {
                int n = _rows[r];
                float step = CardView.Size.x * scale + gap;
                for (int j = 0; j < n && k < _count; j++, k++)
                {
                    float x = (j - (n - 1) * 0.5f) * step;
                    _slots[k].Home = new Vector3(x, top - r * rowH, 0f);
                    _slots[k].Scale = scale;
                }
            }
        }

        public void Hide()
        {
            Interactive = false;
            foreach (var s in _slots)
            {
                if (s.Glow != null) s.Glow.gameObject.SetActive(false);
                if (s.Ring != null) s.Ring.gameObject.SetActive(false);
            }
            gameObject.SetActive(false);
        }

        MeshRenderer Shape(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = assets.quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = assets.shapeMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return r;
        }

        void SetShape(MeshRenderer r, Color color, float mode, Vector2 size, float thickness = 0.05f, float radius = 0.4f)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, color);
            _mpb.SetFloat(ModeId, mode);
            _mpb.SetVector(SizeId, size);
            _mpb.SetFloat(SoftnessId, 0.02f);
            _mpb.SetFloat(ThicknessId, thickness);
            _mpb.SetFloat(RadiusId, radius);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>カードの画面上の位置（ピクセル、左下原点）。</summary>
        public Vector2 ScreenPos(int i) => layout.TableToScreen(_slots[i].Card.transform.position);

        public Vector2 HomeScreenPos(int i) => layout.TableToScreen(_slots[i].Home);

        // ------------------------------------------------------------------
        // 毎フレーム: 浮き沈み・カーソル・光
        // ------------------------------------------------------------------
        float _statTime, _statWorst;
        int _statFrames;

        /// <summary>FPS の計測をやり直す（自動操作のログ用）。</summary>
        public void ResetStats()
        {
            _statTime = 0f;
            _statFrames = 0;
            _statWorst = 0f;
        }

        public string Stats() =>
            "avg " + (_statTime > 0f ? _statFrames / _statTime : 0f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            + " worst " + (_statWorst * 1000f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "ms over "
            + _statTime.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";

        void Update()
        {
            if (_count == 0 || layout == null) return;
            float t = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            _statTime += dt;
            _statFrames++;
            if (_statFrames > 5 && dt > _statWorst) _statWorst = dt;
            if (Interactive) HandleInput();
            for (int i = 0; i < _count; i++)
            {
                var s = _slots[i];
                var card = s.Card;
                // 光（背後の柔らかい光）
                if (s.Glow.gameObject.activeSelf || s.GlowAmount > 0.001f)
                {
                    bool on = s.GlowAmount > 0.001f;
                    s.Glow.gameObject.SetActive(on);
                    if (on)
                    {
                        float pulse = 1f + 0.08f * Mathf.Sin(t * 4f + i);
                        var c = s.GlowColor * 1.6f;
                        c.a = Mathf.Clamp01(s.GlowAmount) * 0.85f;
                        var p = card.transform.position;
                        s.Glow.transform.position = new Vector3(p.x, p.y, p.z + 0.02f);
                        float size = card.transform.localScale.x * 2.6f * pulse;
                        s.Glow.transform.localScale = new Vector3(size, size * 1.1f, 1f);
                        s.Glow.sortingOrder = card.Order * 10 - 5;
                        SetShape(s.Glow, c, 3f, new Vector2(size, size * 1.1f));
                    }
                }
                if (s.Busy || s.Done) continue;
                bool hovered = Interactive && (i == _hover || (_keyboard && i == _keySel));
                s.Hover = Mathf.MoveTowards(s.Hover, hovered ? 1f : 0f, dt / 0.12f);
                float e = Easing.Evaluate(Ease.OutCubic, s.Hover);
                // 裏向きのカードはゆっくり浮き沈みする（1 枚ずつずらす）
                float bob = Mathf.Sin(t * 1.7f + i * 0.9f) * 0.05f * (1f - e);
                card.transform.position = s.Home + new Vector3(0f, bob + 0.14f * e, -0.45f * e);
                card.transform.localScale = Vector3.one * s.Scale * (1f + 0.1f * e);
                card.SetLift(0.45f * e);
                card.SetOrder(hovered ? 60 : 30 + i);
                card.SetSelect(_keyboard && i == _keySel ? 1f : 0f);
                if (!hovered)
                {
                    // 乗せていないカードもわずかに揺れる（ホロ・光沢が流れるように）
                    card.SetTiltTarget(new Vector2(Mathf.Sin(t * 0.8f + i * 1.7f) * 0.25f, Mathf.Sin(t * 0.6f + i) * 0.18f));
                }
                if (Interactive)
                {
                    // 乗せたカードの背後に淡い金の光
                    s.GlowColor = Theme.Gold;
                    s.GlowAmount = 0.35f * e;
                }
            }
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
                bool right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
                bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                if (left || right)
                {
                    if (!_keyboard)
                    {
                        _keyboard = true;
                        _keySel = NextOpen(_hover >= 0 ? _hover - 1 : -1, +1);
                        _hover = -1;
                    }
                    else _keySel = NextOpen(_keySel, left ? -1 : +1);
                    SfxBank.Instance?.Play(SfxKind.Cursor);
                }
                else if (enter && _keyboard && _keySel >= 0 && !_slots[_keySel].Done)
                {
                    Clicked?.Invoke(_keySel);
                    return;
                }
            }
            var pointer = Pointer.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();
            bool moved = (pos - _lastPointer).sqrMagnitude > 1f;
            _lastPointer = pos;
            bool touch = pointer is Touchscreen;
            if (moved && !touch && _keyboard)
            {
                _keyboard = false;
                _keySel = -1;
            }
            var world = layout.ScreenToTable(pos);
            int hit = HitTest(world);
            if (!touch && !_keyboard)
            {
                if (hit != _hover && hit >= 0) SfxBank.Instance?.Play(SfxKind.Cursor, 2f);
                _hover = hit;
                if (_hover >= 0) _slots[_hover].Card.TiltToward(world);
            }
            if (pointer.press.wasPressedThisFrame)
            {
                _pressing = !OverUi(pos);
                _pressPos = pos;
                if (touch && _pressing) _hover = hit;
            }
            if (touch && _pressing && pointer.press.isPressed && _hover >= 0) _slots[_hover].Card.TiltToward(world);
            if (pointer.press.wasReleasedThisFrame && _pressing)
            {
                _pressing = false;
                if ((pos - _pressPos).magnitude <= TapPixels && hit >= 0 && !_slots[hit].Done) Clicked?.Invoke(hit);
                if (touch) _hover = -1;
            }
        }

        static bool OverUi(Vector2 pos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = pos };
            var hits = new List<RaycastResult>();
            es.RaycastAll(data, hits);
            foreach (var h in hits)
            {
                // 全面の透明な受け皿は数えない（ボタンなど操作できるものの上だけ UI とみなす）
                if (h.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
            }
            return false;
        }

        int NextOpen(int from, int dir)
        {
            for (int k = 1; k <= _count; k++)
            {
                int i = ((from + dir * k) % _count + _count) % _count;
                if (!_slots[i].Done && !_slots[i].Busy) return i;
            }
            return -1;
        }

        int HitTest(Vector3 world)
        {
            int best = -1;
            int bestOrder = int.MinValue;
            for (int i = 0; i < _count; i++)
            {
                var s = _slots[i];
                if (s.Done || s.Busy) continue;
                var c = s.Home;
                float half = s.Scale * 0.5f;
                if (Mathf.Abs(world.x - c.x) <= CardView.Size.x * half * 1.05f && Mathf.Abs(world.y - c.y) <= CardView.Size.y * half * 1.05f)
                {
                    if (s.Card.Order > bestOrder)
                    {
                        bestOrder = s.Card.Order;
                        best = i;
                    }
                }
            }
            return best;
        }

        /// <summary>キーボードで i を選んだ状態にする（テスト用）。</summary>
        public void SelectWithKeyboard(int i)
        {
            _keyboard = true;
            _keySel = i;
        }

        // ------------------------------------------------------------------
        // 演出
        // ------------------------------------------------------------------

        /// <summary>選んだカードを手前の中央へ出す（他のカードは少し下がって暗くなる）。</summary>
        public async Awaitable FocusAsync(int i, float scaleMul, float timeFactor, CancellationToken ct)
        {
            var s = _slots[i];
            s.Busy = true;
            var card = s.Card;
            card.SetOrder(80);
            card.SetSelect(0f);
            var from = card.transform.position;
            var fromScale = card.transform.localScale.x;
            bool portrait = layout.Portrait;
            // 少し下げる（上に「NEW!」と見出しを出すので）
            var to = new Vector3(0f, portrait ? -layout.VisibleHeight * 0.02f : -layout.VisibleHeight * 0.045f, -0.9f);
            float toScale = s.Scale * scaleMul;
            for (int k = 0; k < _count; k++)
            {
                if (k == i || _slots[k].Done) continue;
                var other = _slots[k].Card;
                var o = other;
                float a0 = other.Alpha;
                Tweens.Play(0.25f * timeFactor, x => o.SetAlpha(Mathf.Lerp(a0, 0.45f, x)), Ease.OutCubic, 0f, o, "alpha");
            }
            await Tweens.Run(0.3f * timeFactor, k =>
            {
                card.transform.position = Vector3.LerpUnclamped(from, to, k);
                card.transform.localScale = Vector3.one * Mathf.LerpUnclamped(fromScale, toScale, k);
                card.SetLift(Mathf.Lerp(0.45f, 0.8f, k));
            }, ct, Ease.OutCubic, 0f, card, "move");
            // ため: ふるえる
            await Tweens.Run(0.16f * timeFactor, k =>
                card.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 4f) * 3.5f * (1f - k)), ct, Ease.Linear);
            card.transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// めくる: Y 軸の 3D 回転（0 → 90° で表に替えて → 0°、着地で少し行き過ぎて戻る）。表にした瞬間に白く光る。
        /// </summary>
        public async Awaitable FlipAsync(int i, CardDef def, Player owner, float timeFactor, CancellationToken ct, Action onFace = null)
        {
            var s = _slots[i];
            var card = s.Card;
            s.Busy = true;
            bool faced = false;
            var basePos = card.transform.position;
            SfxBank.Instance?.Play(SfxKind.Flip, 3f);
            await Tweens.Run(0.42f * timeFactor, k =>
            {
                float angle = 180f * k;
                if (!faced && angle >= 90f)
                {
                    faced = true;
                    card.Bind(def, owner, 0, false);
                    card.SetFlash(1f);
                    onFace?.Invoke();
                }
                float a = faced ? angle - 180f : angle;
                card.transform.rotation = Quaternion.Euler(0f, a, 0f);
                card.transform.position = basePos + new Vector3(0f, 0f, -0.35f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k)));
            }, ct, Ease.InOutQuad, 0f, card, "flip");
            if (!faced)
            {
                card.Bind(def, owner, 0, false);
                onFace?.Invoke();
            }
            card.transform.rotation = Quaternion.identity;
            card.transform.position = basePos;
            Tweens.Play(0.45f * timeFactor, k => card.SetFlash((1f - k) * 0.9f), Ease.OutQuad, 0f, card, "flash");
        }

        /// <summary>裏向きのまま並んでいるカードを、その場でめくる（導入・選ばなかったカードを見せる）。</summary>
        public async Awaitable FlipInPlaceAsync(int i, CardDef def, Player owner, float timeFactor, CancellationToken ct)
        {
            var s = _slots[i];
            s.Busy = true;
            await FlipAsync(i, def, owner, timeFactor, ct);
            s.Busy = false;
        }

        /// <summary>カードの背後の光（color、強さ 0..1）。</summary>
        public void SetGlow(int i, Color color, float amount)
        {
            var s = _slots[i];
            s.GlowColor = color;
            s.GlowAmount = amount;
        }

        /// <summary>光の輪が広がって消える（NEW! の瞬間）。</summary>
        public void Burst(int i, Color color, float timeFactor, float size = 1f)
        {
            var s = _slots[i];
            var r = s.Ring;
            var card = s.Card;
            r.gameObject.SetActive(true);
            float baseSize = card.transform.localScale.x * 3.2f * size;
            r.sortingOrder = card.Order * 10 + 5;
            Tweens.Play(Mathf.Max(0.0001f, 0.6f * timeFactor), k =>
            {
                var p = card.transform.position;
                r.transform.position = new Vector3(p.x, p.y, p.z - 0.01f);
                r.transform.localScale = new Vector3(baseSize, baseSize, 1f);
                var c = color * 2.4f;
                c.a = (1f - k) * 0.9f;
                SetShape(r, c, 2f, new Vector2(baseSize, baseSize), Mathf.Lerp(0.02f, 0.06f, k), Mathf.Lerp(0.12f, 0.48f, k));
                if (k >= 1f) r.gameObject.SetActive(false);
            }, Ease.OutQuad, 0f, r, "burst");
        }

        /// <summary>手持ち（target、ワールド座標）へ弧を描いて飛んでいき、小さくなって消える。</summary>
        public async Awaitable FlyAsync(int i, Vector3 target, float timeFactor, CancellationToken ct)
        {
            var s = _slots[i];
            var card = s.Card;
            s.Busy = true;
            var from = card.transform.position;
            float fromScale = card.transform.localScale.x;
            var ctrl = (from + target) * 0.5f + new Vector3(0f, layout.VisibleHeight * 0.18f, -0.6f);
            float glow0 = s.GlowAmount;
            SfxBank.Instance?.Play(SfxKind.Whoosh);
            await Tweens.Run(0.5f * timeFactor, k =>
            {
                float u = Mathf.Clamp01(k);
                var a = Vector3.LerpUnclamped(from, ctrl, u);
                var b = Vector3.LerpUnclamped(ctrl, target, u);
                card.transform.position = Vector3.LerpUnclamped(a, b, u);
                card.transform.localScale = Vector3.one * Mathf.Lerp(fromScale, fromScale * 0.18f, u);
                card.transform.rotation = Quaternion.Euler(0f, 0f, -25f * u);
                card.SetAlpha(1f - Mathf.Clamp01((u - 0.75f) / 0.25f));
                s.GlowAmount = glow0 * (1f - u);
            }, ct, Ease.InCubic, 0f, card, "move");
            s.Done = true;
            s.Busy = false;
            s.GlowAmount = 0f;
            card.gameObject.SetActive(false);
        }

        /// <summary>カードを元の位置に戻す（他のカードの暗さも戻す）。</summary>
        public void Restore(int except = -1)
        {
            for (int k = 0; k < _count; k++)
            {
                if (k == except || _slots[k].Done) continue;
                var o = _slots[k].Card;
                Tweens.Play(0.2f, x => o.SetAlpha(Mathf.Lerp(0.45f, 1f, x)), Ease.OutCubic, 0f, o, "alpha");
            }
        }

        /// <summary>取られる演出: i のカードが赤く光り、持ち上がって上（相手の方）へ去る。</summary>
        public async Awaitable TakeAwayAsync(int i, float timeFactor, CancellationToken ct)
        {
            var s = _slots[i];
            var card = s.Card;
            s.Busy = true;
            SetGlow(i, Theme.P1, 0.9f);
            card.SetOrder(80);
            var from = card.transform.position;
            await Tweens.Run(0.45f * timeFactor, k =>
            {
                card.transform.position = from + new Vector3(0f, 0.2f * k, -0.4f * k);
                card.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 3f) * 4f);
            }, ct, Ease.OutCubic, 0f, card, "move");
            var mid = card.transform.position;
            var to = new Vector3(from.x * 0.3f, layout.VisibleHeight * 0.62f, -0.4f);
            await Tweens.Run(0.6f * timeFactor, k =>
            {
                card.transform.position = Vector3.LerpUnclamped(mid, to, k);
                card.transform.rotation = Quaternion.Euler(0f, 0f, 12f * k);
                card.SetAlpha(1f - k);
                s.GlowAmount = 0.9f * (1f - k);
            }, ct, Ease.InCubic, 0f, card, "move");
            s.Done = true;
            s.Busy = false;
            s.GlowAmount = 0f;
            card.gameObject.SetActive(false);
        }

        /// <summary>守られた: 赤く光って揺れるが、元の位置に戻る。</summary>
        public async Awaitable ShakeKeepAsync(int i, float timeFactor, CancellationToken ct)
        {
            var s = _slots[i];
            var card = s.Card;
            s.Busy = true;
            SetGlow(i, Theme.P1, 0.7f);
            var home = s.Home;
            await Tweens.Run(0.5f * timeFactor, k =>
            {
                card.transform.position = home + new Vector3(Mathf.Sin(k * Mathf.PI * 6f) * 0.08f * (1f - k), 0.15f * Mathf.Sin(Mathf.PI * k), -0.2f * Mathf.Sin(Mathf.PI * k));
            }, ct, Ease.Linear, 0f, card, "move");
            SetGlow(i, Theme.P0, 0.5f);
            card.transform.position = home;
            s.Busy = false;
            s.Done = true;
        }
    }
}
