using System;
using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Campaign;
using Edgewise.Game.Juice;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// デッキ編集（DESIGN §12-4 / §12-11）。対戦前に必ず通る。
    /// 上段: 5 つの枠と「合計 Lv 13 / 15」の目盛り（上限を超えると赤）。下段: 手持ちの一覧（レベル順・属性と系統で絞り込み・枚数バッジ・
    /// 残りの予算で入らないカードは薄く）。タップで入れる・外す、「おまかせ」で自動編成、「この 5 枚で挑む」で開始（条件を満たすまで押せない）。
    /// 横（縦画面では上）に相手の情報（立ち絵・ルール・見えている持ちカード）。操作の中身は DeckEditModel（EditMode でテスト）。
    /// 手触り（5c）: 小さなカードは 1 枚ごとに 15 個ほどの uGUI の部品と TextMeshPro を持つので、50 種類を一度に作る・表示すると
    /// 開いた直後のフレームが落ちる。そこで (1) 星図・星域の画面にいる間に少しずつ作っておき（PrewarmStep。画面は非表示のまま作るので描画の負荷はない）、
    /// (2) 一覧は 1 フレームに BindPerFrame 枚ずつ表示する（上の段から順に、配られるように現れる）。
    /// </summary>
    public sealed class DeckEditScreen : MonoBehaviour, IResponsiveUi
    {
        const int ElementChips = 9; // すべて + 8 属性
        const int TribeChips = 5; // すべて + 4 系統

        [SerializeField] GameAssets assets;
        [SerializeField] Image header;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] Button backButton;
        [SerializeField] RectTransform slotRoot;
        [SerializeField] MiniCardView[] slots = new MiniCardView[5];
        [SerializeField] Button[] slotButtons = new Button[5];
        [SerializeField] TextMeshProUGUI totalText;
        [SerializeField] Image gaugeBack;
        [SerializeField] Image gaugeFill;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] TextMeshProUGUI hint;
        [SerializeField] Button[] elementChips = new Button[ElementChips];
        [SerializeField] Button[] tribeChips = new Button[TribeChips];
        [SerializeField] ScrollRect scroll;
        [SerializeField] RectTransform content;
        [SerializeField] GridLayoutGroup grid;
        [SerializeField] Button autoButton;
        [SerializeField] Button beginButton;
        [SerializeField] Image sidePanel;
        [SerializeField] PortraitView portrait;
        [SerializeField] TextMeshProUGUI oppTitle;
        [SerializeField] TextMeshProUGUI oppName;
        [SerializeField] TextMeshProUGUI oppRules;
        [SerializeField] TextMeshProUGUI oppSeenLabel;
        [SerializeField] MiniCardView[] oppCards = new MiniCardView[RegionScreen.MaxCards];

        /// <summary>一覧の小さなカードを 1 フレームに何枚ずつ表示するか（開いた直後も 60FPS を保つため）。</summary>
        public const int BindPerFrame = 4;

        readonly List<MiniCardView> _items = new List<MiniCardView>();
        IReadOnlyList<CardDef> _listing;
        int _bound;
        Coroutine _binding;
        MiniCardView[] _newSlots;
        MiniCardView[] _newOpp;
        DeckEditModel _model;
        CampaignState _state;
        Opponent _opponent;
        bool _portrait;
        GameObject _lastSelected;

        public event Action BeginPressed;
        public event Action BackPressed;

        public bool IsOpen => gameObject.activeSelf;
        public DeckEditModel Model => _model;
        public Button AutoButton => autoButton;
        public Button BeginButton => beginButton;
        public Button BackButton => backButton;
        public Button ElementChip(int i) => elementChips[i];
        public Button TribeChip(int i) => tribeChips[i];
        public string TotalText => totalText.text;
        public string StatusText => statusText.text;
        public Color TotalColor => totalText.color;
        public IReadOnlyList<MiniCardView> Items => _items;

        /// <summary>一覧を少しずつ表示している途中か（終わると false）。</summary>
        public bool Populating => _binding != null;

        /// <summary>作ってある一覧の小さなカードの数（PrewarmStep の進み具合）。</summary>
        public int PooledItems => _items.Count;
        public MiniCardView Slot(int i) => slots[i];

        static readonly Element?[] ElementOrder =
        {
            null, Element.Fire, Element.Ice, Element.Thunder, Element.Earth, Element.Water, Element.Wind, Element.Holy, Element.Dark,
        };

        static readonly Tribe?[] TribeOrder = { null, Tribe.Beast, Tribe.Machine, Tribe.Spirit, Tribe.Dragon };

        public static readonly Color OverColor = new Color(1f, 0.38f, 0.36f);

        public void Build(GameAssets a)
        {
            assets = a;
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", new Color(0f, 0f, 0.03f, 0.45f), false);
            header = UiKit.Panel(root, "Header", a, new Color(0.03f, 0.04f, 0.09f, 0.88f), false);
            title = UiKit.Label(root, "Title", a, UiText.DeckTitle, 34, Theme.Gold, TextAlignmentOptions.Left, true);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            backButton = UiKit.Button(root, "DeckBackButton", a, UiText.Back, 22, out _);

            // 5 つの枠と相手の持ちカード（小さなカード）は、シーンを軽くするため実行時に作る（EnsureCards）
            slots = new MiniCardView[0];
            slotButtons = new Button[0];
            slotRoot = UiKit.Rect(root, "Slots");
            UiKit.Stretch(slotRoot);
            totalText = UiKit.Label(root, "Total", a, "", 28, Theme.Ink, TextAlignmentOptions.Left, true);
            totalText.textWrappingMode = TextWrappingModes.NoWrap;
            gaugeBack = UiKit.Panel(root, "GaugeBack", a, new Color(0.06f, 0.07f, 0.14f, 1f), false);
            gaugeFill = UiKit.Panel(gaugeBack.transform, "GaugeFill", a, Theme.P0, false);
            statusText = UiKit.Label(root, "Status", a, "", 20, Theme.InkDim, TextAlignmentOptions.Left);
            statusText.textWrappingMode = TextWrappingModes.NoWrap;
            hint = UiKit.Label(root, "Hint", a, UiText.DeckHint, 17, Theme.InkDim, TextAlignmentOptions.Right);
            hint.textWrappingMode = TextWrappingModes.NoWrap;

            elementChips = new Button[ElementChips];
            for (int i = 0; i < ElementChips; i++)
            {
                string label = ElementOrder[i].HasValue ? Theme.ElementGlyph(ElementOrder[i].Value) : UiText.DeckAll;
                elementChips[i] = UiKit.Button(root, "Elem" + i, a, label, 20, out _);
            }
            tribeChips = new Button[TribeChips];
            for (int i = 0; i < TribeChips; i++)
            {
                string label = TribeOrder[i].HasValue ? Theme.TribeGlyph(TribeOrder[i].Value) : UiText.DeckAll;
                tribeChips[i] = UiKit.Button(root, "Tribe" + i, a, label, 20, out _);
            }

            // 手持ちの一覧（縦にスクロール）
            var scrollRt = UiKit.Rect(root, "Collection");
            var bg = scrollRt.gameObject.AddComponent<Image>();
            bg.sprite = a.roundRect;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.04f, 0.05f, 0.11f, 0.75f);
            scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            var viewport = UiKit.Rect(scrollRt, "Viewport");
            UiKit.Stretch(viewport, 8, 8, 8, 8);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            content = UiKit.Rect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment = TextAnchor.UpperCenter;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            autoButton = UiKit.Button(root, "AutoButton", a, UiText.DeckAuto, 26, out _);
            beginButton = UiKit.Button(root, "BeginButton", a, UiText.DeckBegin, 28, out _, new Color(0.25f, 0.42f, 0.40f));

            sidePanel = UiKit.Panel(root, "Opponent", a, Theme.Panel, false);
            var sp = sidePanel.transform;
            portrait = PortraitView.Create(sp, "Portrait", a);
            oppTitle = UiKit.Label(sp, "OpponentTitle", a, "", 18, Theme.InkDim, TextAlignmentOptions.Left);
            oppName = UiKit.Label(sp, "OpponentName", a, "", 28, Theme.Ink, TextAlignmentOptions.Left, true);
            oppRules = UiKit.Label(sp, "OpponentRules", a, "", 17, Theme.Ink, TextAlignmentOptions.TopLeft);
            oppSeenLabel = UiKit.Label(sp, "SeenLabel", a, UiText.DeckSeen, 16, Theme.InkDim, TextAlignmentOptions.Left);
            foreach (var t in new[] { oppTitle, oppName, oppSeenLabel })
            {
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.enableAutoSizing = true;
                t.fontSizeMin = 11;
            }
            oppCards = new MiniCardView[0];
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        bool HasCards => slots != null && slots.Length == 5 && slots[0] != null;

        void EnsureCards()
        {
            int made = 0;
            MakeFixedCards(ref made, int.MaxValue);
        }

        /// <summary>5 つの枠と相手の持ちカードの小さなカードを、合わせて budget 枚まで作る。すべてそろったら true。</summary>
        bool MakeFixedCards(ref int made, int budget)
        {
            if (HasCards) return true;
            if (_newSlots == null)
            {
                _newSlots = new MiniCardView[5];
                _newOpp = new MiniCardView[RegionScreen.MaxCards];
            }
            for (int i = 0; i < _newSlots.Length && made < budget; i++)
            {
                if (_newSlots[i] != null) continue;
                _newSlots[i] = MiniCardView.Create(slotRoot, "Slot" + i, assets, true);
                made++;
            }
            for (int i = 0; i < _newOpp.Length && made < budget; i++)
            {
                if (_newOpp[i] != null) continue;
                _newOpp[i] = MiniCardView.Create(sidePanel.transform, "Seen" + i, assets, false);
                made++;
            }
            if (Array.IndexOf(_newSlots, null) >= 0 || Array.IndexOf(_newOpp, null) >= 0) return false;
            slots = _newSlots;
            slotButtons = new Button[5];
            for (int i = 0; i < 5; i++) slotButtons[i] = slots[i].Button;
            oppCards = _newOpp;
            _newSlots = null;
            _newOpp = null;
            ApplyLayout(_portrait);
            return true;
        }

        MiniCardView CreateItem()
        {
            var v = MiniCardView.Create(content, "Own", assets, true);
            v.gameObject.SetActive(false);
            var item = v;
            v.Button.onClick.AddListener(() => OnItem(item));
            _items.Add(v);
            return v;
        }

        /// <summary>
        /// 画面を開く前に小さなカードを少しずつ作っておく（星図・星域の画面から毎フレーム呼ぶ。1 回に budget 枚まで）。
        /// 一覧は poolSize 枚（手持ちの種類数）まで。すべて作り終えたら true。画面は非表示のままなので、作るだけで描画の負荷は出ない。
        /// </summary>
        public bool PrewarmStep(int poolSize, int budget)
        {
            int made = 0;
            if (!MakeFixedCards(ref made, budget)) return false;
            while (_items.Count < poolSize && made < budget)
            {
                CreateItem();
                made++;
            }
            return _items.Count >= poolSize;
        }

        void Awake()
        {
            EnsureCards();
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                slotButtons[i].onClick.AddListener(() =>
                {
                    if (_model == null || !_model.RemoveAt(k)) return;
                    SfxBank.Instance?.Play(SfxKind.Cursor);
                    Refresh();
                });
            }
            for (int i = 0; i < ElementChips; i++)
            {
                int k = i;
                elementChips[i].onClick.AddListener(() =>
                {
                    if (_model == null) return;
                    _model.ElementFilter = ElementOrder[k];
                    Rebuild();
                });
            }
            for (int i = 0; i < TribeChips; i++)
            {
                int k = i;
                tribeChips[i].onClick.AddListener(() =>
                {
                    if (_model == null) return;
                    _model.TribeFilter = TribeOrder[k];
                    Rebuild();
                });
            }
            autoButton.onClick.AddListener(Auto);
            beginButton.onClick.AddListener(() =>
            {
                if (_model != null && _model.CanBegin) BeginPressed?.Invoke();
            });
            backButton.onClick.AddListener(() => BackPressed?.Invoke());
        }

        float _fpsTime, _fpsWorst, _fpsFirst, _fps1sTime;
        int _fpsFrames, _fps1sFrames;

        /// <summary>
        /// この画面を開いていた間の FPS（自動操作のログ用。絵つきの小さなカードが多い画面で 60FPS を保てているか）。
        /// avg は開いていた間の平均、worst は 6 フレーム目以降の最悪値、first は開いた直後の 1 フレームの時間、1s は開いてから 1 秒間の平均。
        /// </summary>
        void LogFps()
        {
            if (!AutomationLog.Enabled || _fpsTime < 0.3f) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            AutomationLog.Write("fps-deck avg " + (_fpsFrames / _fpsTime).ToString("0.0", inv) + " worst "
                + (_fpsWorst * 1000f).ToString("0", inv) + "ms over "
                + _fpsTime.ToString("0.0", inv) + "s items " + ActiveItems() + " first " + (_fpsFirst * 1000f).ToString("0", inv) + "ms 1s "
                + (_fps1sTime > 0f ? _fps1sFrames / _fps1sTime : 0f).ToString("0.0", inv));
            _fpsTime = 0f;
            _fpsFrames = 0;
            _fpsWorst = 0f;
            _fpsFirst = 0f;
            _fps1sTime = 0f;
            _fps1sFrames = 0;
        }

        int ActiveItems()
        {
            int n = 0;
            foreach (var it in _items)
            {
                if (it.gameObject.activeSelf) n++;
            }
            return n;
        }

        void OnDisable()
        {
            LogFps();
            // 表示の途中で閉じた（コルーチンは止まる）: 次に開いたときに最初から表示し直す
            _binding = null;
        }

        void Update()
        {
            float fdt = Time.unscaledDeltaTime;
            if (_fpsFrames == 0) _fpsFirst = fdt;
            _fpsTime += fdt;
            _fpsFrames++;
            if (_fps1sTime < 1f)
            {
                _fps1sTime += fdt;
                _fps1sFrames++;
            }
            if (_fpsFrames > 5 && fdt > _fpsWorst) _fpsWorst = fdt;
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) && !SettingsPanel.AnyOpen) BackPressed?.Invoke();
            // キーボードで一覧のカードを選んだら、見える位置までスクロールする
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (sel != _lastSelected)
            {
                _lastSelected = sel;
                if (sel != null && sel.transform.parent == content) ScrollTo((RectTransform)sel.transform);
            }
            // 上限超えの目盛りは脈打つ
            if (_model != null && _model.OverCap)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
                gaugeFill.color = Color.Lerp(OverColor, new Color(1f, 0.7f, 0.6f), k * 0.6f);
            }
        }

        void ScrollTo(RectTransform item)
        {
            var vp = scroll.viewport;
            float contentH = content.rect.height, viewH = vp.rect.height;
            if (contentH <= viewH) return;
            float y = -item.anchoredPosition.y; // 上からの距離（中心）
            float half = item.rect.height * 0.5f;
            float top = (1f - scroll.verticalNormalizedPosition) * (contentH - viewH);
            if (y - half < top) top = y - half - 8f;
            else if (y + half > top + viewH) top = y + half - viewH + 8f;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - top / (contentH - viewH));
        }

        // ------------------------------------------------------------------
        // 配置
        // ------------------------------------------------------------------
        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            float w = portrait ? 720f : 1280f;
            var top = new Vector2(0.5f, 1f);
            var tl = new Vector2(0f, 1f);
            float headerH = portrait ? 104f : 76f;
            UiKit.Place((RectTransform)header.transform, top, Vector2.zero, new Vector2(w * 2f, headerH));
            UiKit.Place(title.rectTransform, tl, new Vector2(26f, portrait ? -22f : -12f), new Vector2(360f, 52f));
            title.fontSize = portrait ? 40 : 34;
            UiKit.Place((RectTransform)backButton.transform, new Vector2(1f, 1f), new Vector2(-20f, portrait ? -14f : -10f),
                portrait ? new Vector2(180f, 76f) : new Vector2(150f, 56f));
            backButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 28 : 22;

            if (!HasCards)
            {
                // まだ小さなカードを作っていない（エディタでシーンを作るとき・最初に表示する前）
            }
            else if (!portrait)
            {
                const float sw = 108f, sh = 135f, sg = 12f;
                for (int i = 0; i < 5; i++)
                    UiKit.Place((RectTransform)slots[i].transform, tl, tl, new Vector2(24f + i * (sw + sg), -90f), new Vector2(sw, sh));
                float gx = 24f + 5 * (sw + sg) + 8f;
                UiKit.Place(totalText.rectTransform, tl, tl, new Vector2(gx, -96f), new Vector2(260f, 40f));
                totalText.fontSize = 30;
                UiKit.Place((RectTransform)gaugeBack.transform, tl, tl, new Vector2(gx, -142f), new Vector2(250f, 22f));
                UiKit.Place(statusText.rectTransform, tl, tl, new Vector2(gx, -172f), new Vector2(270f, 30f));
                statusText.fontSize = 20;
                UiKit.Place(hint.rectTransform, tl, tl, new Vector2(gx, -200f), new Vector2(250f, 26f));
                hint.alignment = TextAlignmentOptions.Left;
                hint.fontSize = 16;
                const float cw = 48f, cg = 5f;
                for (int i = 0; i < ElementChips; i++)
                    UiKit.Place((RectTransform)elementChips[i].transform, tl, tl, new Vector2(24f + i * (cw + cg) + (i > 0 ? 18f : 0f), -238f),
                        new Vector2(i == 0 ? 66f : cw, 40f));
                float tx = 24f + ElementChips * (cw + cg) + 18f + 40f;
                for (int i = 0; i < TribeChips; i++)
                    UiKit.Place((RectTransform)tribeChips[i].transform, tl, tl, new Vector2(tx + i * (cw + cg) + (i > 0 ? 18f : 0f), -238f),
                        new Vector2(i == 0 ? 66f : cw, 40f));
                UiKit.Place((RectTransform)scroll.transform, tl, tl, new Vector2(20f, -286f), new Vector2(880f, 352f));
                grid.cellSize = new Vector2(96f, 120f);
                grid.spacing = new Vector2(10f, 10f);
                grid.constraintCount = 8;
                grid.padding = new RectOffset(6, 6, 6, 6);
                UiKit.Place((RectTransform)autoButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(330f, 12f), new Vector2(200f, 60f));
                UiKit.Place((RectTransform)beginButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(550f, 12f), new Vector2(330f, 60f));
                autoButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 26;
                beginButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28;

                // 右: 相手の情報
                UiKit.Place((RectTransform)sidePanel.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -88f), new Vector2(350f, 616f));
                UiKit.Place((RectTransform)this.portrait.transform, tl, tl, new Vector2(16f, -16f), new Vector2(96f, 96f));
                UiKit.Place(oppTitle.rectTransform, tl, tl, new Vector2(124f, -20f), new Vector2(214f, 26f));
                oppTitle.fontSizeMax = 18;
                UiKit.Place(oppName.rectTransform, tl, tl, new Vector2(124f, -48f), new Vector2(214f, 40f));
                oppName.fontSizeMax = 28;
                UiKit.Place(oppRules.rectTransform, tl, tl, new Vector2(16f, -124f), new Vector2(318f, 240f));
                oppRules.fontSize = 17;
                UiKit.Place(oppSeenLabel.rectTransform, tl, tl, new Vector2(16f, -380f), new Vector2(318f, 24f));
                oppSeenLabel.fontSizeMax = 16;
                const float ow = 72f, oh = 90f, og = 6f;
                for (int i = 0; i < oppCards.Length; i++)
                    UiKit.Place((RectTransform)oppCards[i].transform, tl, tl, new Vector2(16f + (i % 4) * (ow + og), -410f - (i / 4) * (oh + og)), new Vector2(ow, oh));
            }
            else
            {
                // 上: 相手の帯
                UiKit.Place((RectTransform)sidePanel.transform, top, new Vector2(0f, -110f), new Vector2(694f, 196f));
                UiKit.Place((RectTransform)this.portrait.transform, tl, tl, new Vector2(14f, -12f), new Vector2(92f, 92f));
                UiKit.Place(oppTitle.rectTransform, tl, tl, new Vector2(118f, -10f), new Vector2(230f, 28f));
                oppTitle.fontSizeMax = 22;
                UiKit.Place(oppName.rectTransform, tl, tl, new Vector2(118f, -38f), new Vector2(230f, 42f));
                oppName.fontSizeMax = 32;
                UiKit.Place(oppRules.rectTransform, tl, tl, new Vector2(360f, -12f), new Vector2(320f, 96f));
                oppRules.fontSize = 18;
                oppSeenLabel.gameObject.SetActive(false);
                const float ow = 76f, oh = 82f, og = 8f;
                for (int i = 0; i < oppCards.Length; i++)
                    UiKit.Place((RectTransform)oppCards[i].transform, tl, tl, new Vector2(14f + i * (ow + og), -108f), new Vector2(ow, oh));
                const float sw = 124f, sh = 155f, sg = 12f;
                for (int i = 0; i < 5; i++)
                    UiKit.Place((RectTransform)slots[i].transform, top, new Vector2(0.5f, 1f), new Vector2((i - 2) * (sw + sg), -318f), new Vector2(sw, sh));
                UiKit.Place(totalText.rectTransform, tl, tl, new Vector2(24f, -484f), new Vector2(300f, 46f));
                totalText.fontSize = 34;
                UiKit.Place((RectTransform)gaugeBack.transform, tl, tl, new Vector2(300f, -496f), new Vector2(396f, 24f));
                UiKit.Place(statusText.rectTransform, tl, tl, new Vector2(24f, -530f), new Vector2(420f, 34f));
                statusText.fontSize = 24;
                UiKit.Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -530f), new Vector2(260f, 34f));
                hint.alignment = TextAlignmentOptions.Right;
                hint.fontSize = 22;
                const float cw = 66f, cg = 8f;
                for (int i = 0; i < ElementChips; i++)
                    UiKit.Place((RectTransform)elementChips[i].transform, tl, tl, new Vector2(20f + i * (cw + cg) + (i > 0 ? 8f : 0f), -572f),
                        new Vector2(i == 0 ? 88f : cw, 54f));
                for (int i = 0; i < TribeChips; i++)
                    UiKit.Place((RectTransform)tribeChips[i].transform, tl, tl, new Vector2(20f + i * (cw + 16f + cg) + (i > 0 ? 8f : 0f), -632f),
                        new Vector2(i == 0 ? 88f : cw + 16f, 54f));
                UiKit.Place((RectTransform)scroll.transform, tl, tl, new Vector2(14f, -696f), new Vector2(692f, 462f));
                grid.cellSize = new Vector2(122f, 150f);
                grid.spacing = new Vector2(10f, 10f);
                grid.constraintCount = 5;
                grid.padding = new RectOffset(6, 6, 6, 6);
                UiKit.Place((RectTransform)autoButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 16f), new Vector2(240f, 92f));
                UiKit.Place((RectTransform)beginButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 16f), new Vector2(420f, 92f));
                autoButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 32;
                beginButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 34;
            }
            if (!portrait) oppSeenLabel.gameObject.SetActive(true);
            foreach (var b in elementChips) b.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 26 : 19;
            foreach (var b in tribeChips) b.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 26 : 19;
        }

        // ------------------------------------------------------------------
        // 表示
        // ------------------------------------------------------------------
        public void Show(CampaignState state, Region region, Opponent opponent, DeckEditModel model)
        {
            _state = state;
            _opponent = opponent;
            _model = model;
            gameObject.SetActive(true);
            portrait.Show(opponent);
            oppTitle.text = opponent.Title;
            oppName.text = opponent.Name;
            var rules = CampaignTexts.RuleLines(CampaignRules.MatchRules(opponent));
            var sb = new System.Text.StringBuilder();
            foreach (var r in rules)
            {
                if (_portrait) sb.Append("<color=#F5D17A>").Append(r.Key).Append("</color>  ");
                else sb.Append("<b><color=#F5D17A>").Append(r.Key).Append("</color></b>  <size=14>").Append(r.Value).Append("</size>\n");
            }
            oppRules.text = sb.ToString();
            var seen = CampaignTexts.OpponentCards(state, opponent);
            for (int i = 0; i < oppCards.Length; i++)
            {
                if (i >= seen.Count)
                {
                    oppCards[i].gameObject.SetActive(false);
                    continue;
                }
                if (seen[i].Seen)
                {
                    oppCards[i].Show(CardCatalog.ById[seen[i].CardId]);
                    oppCards[i].SetBadge(0);
                    oppCards[i].SetMark(seen[i].Captured ? UiText.MarkCaptured : null, new Color(1f, 0.55f, 0.45f));
                }
                else oppCards[i].ShowUnknown();
                oppCards[i].SetDim(false);
            }
            Rebuild();
            scroll.verticalNormalizedPosition = 1f;
            EventSystem.current?.SetSelectedGameObject(_model.CanBegin ? beginButton.gameObject : autoButton.gameObject);
            AutomationLog.Buttons("Deck", transform);
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// 一覧を作り直す（開いたとき・絞り込みを変えたとき）。いったん隠し、BindPerFrame 枚ずつ上の段から表示する（BindStep）。
        /// 押せるのは表示し終えたカードだけ。
        /// </summary>
        void Rebuild()
        {
            _listing = _model.Listing();
            _bound = 0;
            foreach (var it in _items)
            {
                if (it.gameObject.activeSelf) it.gameObject.SetActive(false);
            }
            Refresh();
            if (_binding != null) StopCoroutine(_binding);
            _binding = null;
            if (!isActiveAndEnabled)
            {
                while (BindStep(int.MaxValue)) { }
                return;
            }
            BindStep(BindPerFrame);
            _binding = StartCoroutine(Populate());
        }

        /// <summary>一覧のカードを max 枚まで表示する。まだ残っていれば true。</summary>
        bool BindStep(int max)
        {
            if (_listing == null) return false;
            int n = 0;
            while (_bound < _listing.Count && n < max)
            {
                var def = _listing[_bound];
                var v = _bound < _items.Count ? _items[_bound] : CreateItem();
                v.name = "Own_" + def.Id;
                v.gameObject.SetActive(true);
                v.Show(def, true);
                ApplyItemState(v);
                var rt = (RectTransform)v.transform;
                if (max != int.MaxValue && UserPrefs.TimeFactor > 0f)
                    Tweens.Play(0.16f * UserPrefs.TimeFactor, k => rt.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, k), Ease.OutBack, 0f, rt, "deal");
                else rt.localScale = Vector3.one;
                _bound++;
                n++;
            }
            return _bound < _listing.Count;
        }

        System.Collections.IEnumerator Populate()
        {
            while (true)
            {
                yield return null;
                if (!BindStep(BindPerFrame)) break;
            }
            _binding = null;
            if (AutomationLog.Enabled)
            {
                // 一覧の位置はレイアウトが済んだ次のフレームに出す
                yield return null;
                AutomationLog.Write("deck listed " + _bound);
                AutomationLog.Buttons("Deck", transform);
            }
        }

        /// <summary>表示し終えるのを待たずに、残りを全部表示する（テスト用）。</summary>
        public void FinishPopulating()
        {
            if (_binding != null) StopCoroutine(_binding);
            _binding = null;
            while (BindStep(int.MaxValue)) { }
        }

        void OnItem(MiniCardView item)
        {
            if (_model == null || item.CardId == null) return;
            var r = _model.Toggle(item.CardId);
            switch (r)
            {
                case DeckToggle.Added:
                    SfxBank.Instance?.Play(SfxKind.Pick);
                    int at = _model.Selected.Count - 1;
                    Refresh();
                    Pop((RectTransform)slots[at].transform);
                    break;
                case DeckToggle.Removed:
                    SfxBank.Instance?.Play(SfxKind.Cursor);
                    Refresh();
                    break;
                case DeckToggle.Full:
                    // 5 枚そろっている: 枠を揺らして知らせる
                    SfxBank.Instance?.Play(SfxKind.Cursor, -7f);
                    foreach (var s in slots) Shake((RectTransform)s.transform);
                    break;
            }
        }

        /// <summary>おまかせ（テストからも呼ぶ）。</summary>
        public void Auto()
        {
            if (_model == null || !_model.Auto()) return;
            SfxBank.Instance?.Play(SfxKind.Same);
            Refresh();
            for (int i = 0; i < 5; i++) Pop((RectTransform)slots[i].transform, i * 0.04f);
            EventSystem.current?.SetSelectedGameObject(beginButton.gameObject);
        }

        /// <summary>手持ちの一覧から id のカードをタップしたのと同じ操作（テスト用）。</summary>
        public DeckToggle Tap(string id)
        {
            var r = _model.Toggle(id);
            Refresh();
            return r;
        }

        static void Pop(RectTransform rt, float delay = 0f)
        {
            Tweens.Play(0.22f * UserPrefs.TimeFactor, k => rt.localScale = Vector3.one * Mathf.Lerp(1.18f, 1f, k), Ease.OutBack, delay, rt, "pop");
        }

        static void Shake(RectTransform rt)
        {
            var basePos = rt.anchoredPosition;
            Tweens.Play(0.25f, k => rt.anchoredPosition = basePos + new Vector2(Mathf.Sin(k * Mathf.PI * 6f) * 6f * (1f - k), 0f), Ease.Linear, 0f, rt, "shake");
        }

        void Refresh()
        {
            if (_model == null) return;
            var sel = _model.Selected;
            for (int i = 0; i < 5; i++)
            {
                if (i < sel.Count)
                {
                    slots[i].Show(CardCatalog.ById[sel[i]], true);
                    slots[i].SetBadge(0);
                    slots[i].SetMark(null, Color.white);
                    slots[i].SetDim(false);
                }
                else slots[i].ShowEmpty();
            }
            int total = _model.TotalLevel;
            bool over = _model.OverCap;
            totalText.text = CampaignTexts.DeckTotal(total, _model.Cap);
            totalText.color = over ? OverColor : total == _model.Cap ? Theme.Gold : Theme.Ink;
            var fillRt = (RectTransform)gaugeFill.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(Mathf.Clamp01(_model.Cap > 0 ? total / (float)_model.Cap : 1f), 1f);
            fillRt.offsetMin = new Vector2(2f, 2f);
            fillRt.offsetMax = new Vector2(-2f, -2f);
            gaugeFill.color = over ? OverColor : total == _model.Cap ? Theme.Gold : Theme.P0;
            gaugeBack.color = over ? new Color(0.35f, 0.08f, 0.08f, 1f) : new Color(0.06f, 0.07f, 0.14f, 1f);
            statusText.text = CampaignTexts.DeckStatus(_model);
            statusText.color = over ? OverColor : _model.CanBegin ? Theme.P0 : Theme.InkDim;
            beginButton.interactable = _model.CanBegin;
            foreach (var item in _items)
            {
                if (!item.gameObject.activeSelf || item.CardId == null) continue;
                ApplyItemState(item);
            }
            for (int i = 0; i < ElementChips; i++) Mark(elementChips[i], _model.ElementFilter == ElementOrder[i]);
            for (int i = 0; i < TribeChips; i++) Mark(tribeChips[i], _model.TribeFilter == TribeOrder[i]);
            if (AutomationLog.Enabled)
                AutomationLog.Write("deck " + string.Join(",", sel) + " total " + total + "/" + _model.Cap + (over ? " over" : "") + (_model.CanBegin ? " ready" : ""));
        }

        /// <summary>一覧の 1 枚に、枚数のバッジ・デッキに入っている印・入らないときの薄さを付ける。</summary>
        void ApplyItemState(MiniCardView item)
        {
            if (_model == null || item.CardId == null) return;
            bool inDeck = _model.Contains(item.CardId);
            item.SetBadge(_model.Count(item.CardId));
            item.SetMark(inDeck ? UiText.MarkOwned : null, Theme.P0);
            item.SetDim(!_model.Fits(item.CardId));
            if (inDeck) item.SetSelected(true);
        }

        static void Mark(Button b, bool on)
        {
            ((Image)b.targetGraphic).color = on ? Theme.ButtonOn : Theme.Button;
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = on ? new Color(0.08f, 0.06f, 0.02f) : Theme.ButtonText;
        }
    }
}
