using System;
using System.Collections.Generic;
using System.Globalization;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// カード図鑑（DESIGN §7-1 見せ場）。タイトルから開く。50 枚の一覧 → 選ぶと大きく表示して、カーソルの方へ傾けて眺められる。
    /// カードはワールド空間の CardView（対戦と同じプレハブとシェーダー）、見出し・説明・ボタンは uGUI。
    /// - マウス: 乗せると傾いて名前が出る、クリックで拡大、拡大中はカーソルの方へ傾く、ホイールでスクロール。
    /// - タッチ: タップで拡大、上下のドラッグでスクロール、拡大中はドラッグで傾ける。
    /// - キーボード: 矢印で選ぶ → Enter で拡大、拡大中は ← → で前後のカード、Esc で一覧 → タイトル。
    /// 縦画面は 5 列でスクロール、横画面は 10 列で 50 枚が 1 画面に収まる。
    /// </summary>
    public sealed class CardDex : MonoBehaviour, IResponsiveUi
    {
        public enum Mode
        {
            Closed,
            List,
            Zoom,
        }

        const float TapPixels = 14f;

        [SerializeField] GameAssets assets;
        [SerializeField] TableLayout layout;
        [SerializeField] Transform worldRoot;
        [SerializeField] Image header;
        [SerializeField] Image footer;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI count;
        [SerializeField] TextMeshProUGUI hint;
        [SerializeField] TextMeshProUGUI focusName;
        [SerializeField] Button backButton;
        [SerializeField] Image infoPanel;
        [SerializeField] TextMeshProUGUI infoName;
        [SerializeField] TextMeshProUGUI infoMeta;

        /// <summary>固有能力（Trait）の名前と説明。Trait のないカードでは空（Phase 6）。</summary>
        [SerializeField] TextMeshProUGUI infoTrait;
        [SerializeField] TextMeshProUGUI infoCredit;
        [SerializeField] Button prevButton;
        [SerializeField] Button nextButton;
        [SerializeField] Button listButton;
        [SerializeField] Button viewButton;

        readonly List<CardView> _cards = new List<CardView>();
        CardView _zoomCard;
        DexGrid _grid;
        Mode _mode = Mode.Closed;
        bool _portrait;
        bool _keyboard;
        int _hover = -1;
        float _scroll;
        float _cardScale = 0.6f;
        float _rowH = 1f;
        float _viewTop, _viewH, _gridLeft, _cellW;
        Vector2 _pressPos, _lastPointer;
        bool _pressing, _dragging;
        float _pointerIdle = 99f;
        Vector2 _touchTilt;
        float _fpsTime;
        int _fpsFrames;
        float _fpsWorst;

        public event Action Closed;

        IReadOnlyDictionary<string, int> _collection;
        bool _collectionView;

        /// <summary>星図巡りの手持ち（Open で渡したもの。無ければ null）。</summary>
        public IReadOnlyDictionary<string, int> Collection => _collection;

        /// <summary>収集状況の表示か（false なら全カードを見られる）。</summary>
        public bool CollectionView => _collectionView;

        public Button ViewButton => viewButton;
        public string CountText => count.text;
        public string InfoName => infoName.text;
        public string InfoTrait => infoTrait != null ? infoTrait.text : "";

        /// <summary>手持ちの枚数（収集状況の表示でないときは -1）。</summary>
        public int OwnedCount(string id) => !_collectionView ? -1 : _collection != null && _collection.TryGetValue(id, out int n) ? n : 0;

        bool IsUnknown(string id) => _collectionView && OwnedCount(id) <= 0;

        public Mode CurrentMode => _mode;
        public bool IsOpen => _mode != Mode.Closed;
        public DexGrid Grid => _grid;
        public CardView ZoomCard => _zoomCard;
        public IReadOnlyList<CardView> Cards => _cards;
        public Button BackButton => backButton;
        public string ZoomedCardId => _mode == Mode.Zoom && _zoomCard != null ? _zoomCard.CardId : null;
        public string InfoCredit => infoCredit.text;

        // ------------------------------------------------------------------
        // 生成（Editor の SceneBuilder）
        // ------------------------------------------------------------------
        public void Build(GameAssets a, TableLayout tableLayout, Transform root)
        {
            assets = a;
            layout = tableLayout;
            worldRoot = root;
            var r = (RectTransform)transform;
            UiKit.Stretch(r);
            for (int i = r.childCount - 1; i >= 0; i--) DestroyImmediate(r.GetChild(i).gameObject);
            header = UiKit.Panel(r, "Header", a, new Color(0.03f, 0.04f, 0.09f, 0.88f), false);
            footer = UiKit.Panel(r, "Footer", a, new Color(0.03f, 0.04f, 0.09f, 0.82f), false);
            title = UiKit.Label(r, "Title", a, UiText.DexTitle, 34, Theme.Gold, TextAlignmentOptions.Left);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            count = UiKit.Label(r, "Count", a, UiText.DexCountPrefix + CardCatalog.All.Count + UiText.DexCountSuffix, 22, Theme.InkDim,
                TextAlignmentOptions.Left);
            count.textWrappingMode = TextWrappingModes.NoWrap;
            hint = UiKit.Label(r, "Hint", a, UiText.DexHintList, 17, Theme.InkDim);
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            hint.enableAutoSizing = true;
            hint.fontSizeMin = 12;
            focusName = UiKit.Label(r, "FocusName", a, "", 24, Theme.Ink);
            focusName.textWrappingMode = TextWrappingModes.NoWrap;
            backButton = UiKit.Button(r, "DexBackButton", a, UiText.Back, 24, out _);

            infoPanel = UiKit.Panel(r, "InfoPanel", a, Theme.Panel, false);
            var p = infoPanel.transform;
            infoName = UiKit.Label(p, "InfoName", a, "", 34, Theme.Gold, TextAlignmentOptions.Left, true);
            infoName.textWrappingMode = TextWrappingModes.NoWrap;
            infoName.enableAutoSizing = true;
            infoName.fontSizeMin = 20;
            infoName.fontSizeMax = 34;
            infoMeta = UiKit.Label(p, "InfoMeta", a, "", 24, Theme.Ink, TextAlignmentOptions.Left);
            infoTrait = UiKit.Label(p, "InfoTrait", a, "", 22, Theme.Ink, TextAlignmentOptions.TopLeft);
            infoTrait.enableAutoSizing = true;
            infoTrait.fontSizeMin = 14;
            infoTrait.fontSizeMax = 22;
            infoCredit = UiKit.Label(p, "InfoCredit", a, "", 17, Theme.InkDim, TextAlignmentOptions.Left);
            prevButton = UiKit.Button(r, "DexPrevButton", a, UiText.DexPrev, 24, out _);
            nextButton = UiKit.Button(r, "DexNextButton", a, UiText.DexNext, 24, out _);
            listButton = UiKit.Button(r, "DexListButton", a, UiText.DexBackToList, 24, out _, new Color(0.25f, 0.42f, 0.40f));
            // 収集状況 ⇔ すべて（星図巡りの手持ちがあるときだけ出す。5b-2）
            viewButton = UiKit.Button(r, "DexViewButton", a, UiText.DexShowAll, 22, out _);
            ApplyLayout(false);
            gameObject.SetActive(false);
            if (worldRoot != null) worldRoot.gameObject.SetActive(false);
        }

        void Awake()
        {
            backButton.onClick.AddListener(Close);
            prevButton.onClick.AddListener(() => StepZoom(-1));
            nextButton.onClick.AddListener(() => StepZoom(+1));
            listButton.onClick.AddListener(BackToList);
            viewButton.onClick.AddListener(() => SetCollectionView(!_collectionView));
            _grid = new DexGrid(CardCatalog.All.Count, 10);
            if (layout != null) layout.Changed += OnTableLayoutChanged;
        }

        void OnDestroy()
        {
            if (layout != null) layout.Changed -= OnTableLayoutChanged;
        }

        /// <summary>画面の大きさが変わったら（カメラの距離が変わるので）並べ直す。</summary>
        void OnTableLayoutChanged()
        {
            if (_mode == Mode.Closed) return;
            LayoutWorld(false);
            if (_mode == Mode.List) LogLayout();
            if (_mode == Mode.Zoom && _zoomCard != null)
            {
                foreach (var c in _cards) c.gameObject.SetActive(false);
                var (pos, scale) = ZoomPlacement();
                _zoomCard.transform.position = pos;
                _zoomCard.transform.localScale = Vector3.one * scale;
            }
        }

        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            float w = portrait ? 720f : 1280f;
            var top = new Vector2(0.5f, 1f);
            var bottom = new Vector2(0.5f, 0f);
            float headerH = portrait ? 132f : 88f;
            float footerH = portrait ? 96f : 64f;
            UiKit.Place((RectTransform)header.transform, top, new Vector2(0f, 0f), new Vector2(w * 2f, headerH));
            UiKit.Place((RectTransform)footer.transform, bottom, new Vector2(0f, 0f), new Vector2(w * 2f, footerH));
            if (portrait)
            {
                UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(420f, 52f));
                UiKit.Place(count.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -74f), new Vector2(300f, 36f));
                UiKit.Place((RectTransform)backButton.transform, new Vector2(1f, 1f), new Vector2(-20f, -24f), new Vector2(170f, 84f));
                UiKit.Place((RectTransform)viewButton.transform, new Vector2(1f, 1f), new Vector2(-204f, -24f), new Vector2(200f, 84f));
                viewButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 26;
                UiKit.Place(hint.rectTransform, bottom, new Vector2(0f, 10f), new Vector2(680f, 34f));
                UiKit.Place(focusName.rectTransform, bottom, new Vector2(0f, 48f), new Vector2(680f, 40f));
                // 拡大表示: カードは上、説明は下
                UiKit.Place((RectTransform)infoPanel.transform, bottom, new Vector2(0f, 200f), new Vector2(680f, 296f));
                UiKit.Place(infoName.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -14f), new Vector2(630f, 52f));
                UiKit.Place(infoMeta.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -68f), new Vector2(630f, 40f));
                UiKit.Place(infoTrait.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -112f), new Vector2(630f, 84f));
                UiKit.Place(infoCredit.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -202f), new Vector2(630f, 84f));
                UiKit.Place((RectTransform)prevButton.transform, bottom, new Vector2(-230f, 104f), new Vector2(200f, 80f));
                UiKit.Place((RectTransform)listButton.transform, bottom, new Vector2(0f, 104f), new Vector2(200f, 80f));
                UiKit.Place((RectTransform)nextButton.transform, bottom, new Vector2(230f, 104f), new Vector2(200f, 80f));
                hint.fontSizeMax = 22;
                focusName.fontSize = 28;
                count.fontSize = 24;
                infoMeta.fontSize = 28;
                infoCredit.fontSize = 22;
            }
            else
            {
                UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -16f), new Vector2(360f, 50f));
                UiKit.Place(count.rectTransform, new Vector2(0f, 1f), new Vector2(250f, -28f), new Vector2(200f, 32f));
                UiKit.Place((RectTransform)backButton.transform, new Vector2(1f, 1f), new Vector2(-24f, -16f), new Vector2(150f, 56f));
                UiKit.Place((RectTransform)viewButton.transform, new Vector2(1f, 1f), new Vector2(-188f, -16f), new Vector2(170f, 56f));
                viewButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 22;
                UiKit.Place(hint.rectTransform, bottom, new Vector2(0f, 8f), new Vector2(1100f, 26f));
                UiKit.Place(focusName.rectTransform, bottom, new Vector2(0f, 30f), new Vector2(900f, 34f));
                // 拡大表示: カードは左、説明は右
                UiKit.Place((RectTransform)infoPanel.transform, new Vector2(1f, 0.5f), new Vector2(-40f, 40f), new Vector2(520f, 300f));
                UiKit.Place(infoName.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -22f), new Vector2(470f, 52f));
                UiKit.Place(infoMeta.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -78f), new Vector2(470f, 40f));
                UiKit.Place(infoTrait.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -122f), new Vector2(470f, 84f));
                UiKit.Place(infoCredit.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -212f), new Vector2(470f, 80f));
                UiKit.Place((RectTransform)prevButton.transform, new Vector2(1f, 0.5f), new Vector2(-405f, -170f), new Vector2(150f, 60f));
                UiKit.Place((RectTransform)listButton.transform, new Vector2(1f, 0.5f), new Vector2(-235f, -170f), new Vector2(150f, 60f));
                UiKit.Place((RectTransform)nextButton.transform, new Vector2(1f, 0.5f), new Vector2(-65f, -170f), new Vector2(150f, 60f));
                hint.fontSizeMax = 17;
                focusName.fontSize = 24;
                count.fontSize = 22;
                infoMeta.fontSize = 24;
                infoCredit.fontSize = 17;
            }
            if (Application.isPlaying && IsOpen)
            {
                LayoutWorld(false);
                LogLayout();
            }
        }

        // ------------------------------------------------------------------
        // 開く・閉じる
        // ------------------------------------------------------------------
        /// <summary>
        /// 開く。collection（星図巡りの手持ち）を渡すと収集状況の表示（持っている枚数・収集率・未入手は暗いシルエット）で開き、
        /// 「すべて見る」で全カードの表示に切り替えられる。null なら今までどおり全カード。
        /// </summary>
        public void Open(IReadOnlyDictionary<string, int> collection)
        {
            _collection = collection;
            _collectionView = collection != null;
            Open();
        }

        public void Open()
        {
            gameObject.SetActive(true);
            worldRoot.gameObject.SetActive(true);
            EnsureCards();
            _mode = Mode.List;
            _hover = -1;
            _keyboard = false;
            _scroll = 0f;
            _grid.Select(0);
            ApplyView();
            ShowListUi();
            LayoutWorld(false);
            ResetFps();
            EventSystem.current?.SetSelectedGameObject(null);
            LogLayout();
        }

        /// <summary>閉じてタイトルへ戻る（戻るボタン・Esc）。</summary>
        public void Close()
        {
            if (_mode == Mode.Closed) return;
            Hide();
            Closed?.Invoke();
        }

        /// <summary>画面の切り替えで隠す（Closed は呼ばない）。</summary>
        public void Hide()
        {
            if (_mode != Mode.Closed) LogFps();
            _mode = Mode.Closed;
            gameObject.SetActive(false);
            if (worldRoot != null) worldRoot.gameObject.SetActive(false);
        }

        /// <summary>収集状況 ⇔ すべて を切り替える（手持ちが無ければ常にすべて）。</summary>
        public void SetCollectionView(bool on)
        {
            _collectionView = on && _collection != null;
            ApplyView();
            if (_mode == Mode.Zoom) OpenZoom(_grid.Selected);
            else UpdateFocusName();
            if (AutomationLog.Enabled) AutomationLog.Write("dex view " + (_collectionView ? "collection" : "all") + " " + count.text);
        }

        /// <summary>カードの見た目（未入手は暗いシルエット・枚数のバッジ）と、見出しの数を今の表示に合わせる。</summary>
        void ApplyView()
        {
            int owned = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                string id = CardCatalog.All[i].Id;
                int n = OwnedCount(id);
                if (n > 0) owned++;
                _cards[i].SetUnknown(IsUnknown(id));
                _cards[i].SetCount(_collectionView ? n : 0);
            }
            count.text = _collectionView
                ? UiText.ProgressCollect + owned + "/" + CardCatalog.All.Count
                : UiText.DexCountPrefix + CardCatalog.All.Count + UiText.DexCountSuffix;
            count.color = _collectionView ? Theme.Gold : Theme.InkDim;
            viewButton.gameObject.SetActive(_collection != null);
            viewButton.GetComponentInChildren<TextMeshProUGUI>().text = _collectionView ? UiText.DexShowAll : UiText.DexShowCollection;
        }

        void EnsureCards()
        {
            if (_cards.Count > 0) return;
            for (int i = 0; i < CardCatalog.All.Count; i++)
            {
                var c = Instantiate(assets.cardPrefab, worldRoot);
                c.name = "Dex " + CardCatalog.All[i].Id;
                c.SetTraitsEnabled(true); // 図鑑では Trait のバッジを常に出す（対戦では Traits ルールが ON のときだけ）
                c.Bind(CardCatalog.All[i], Player.P0, 0, false);
                c.SetOrder(20);
                _cards.Add(c);
            }
            _zoomCard = Instantiate(assets.cardPrefab, worldRoot);
            _zoomCard.name = "Dex Zoom";
            _zoomCard.SetTraitsEnabled(true);
            _zoomCard.gameObject.SetActive(false);
        }

        void ShowListUi()
        {
            infoPanel.gameObject.SetActive(false);
            prevButton.gameObject.SetActive(false);
            nextButton.gameObject.SetActive(false);
            listButton.gameObject.SetActive(false);
            focusName.gameObject.SetActive(true);
            count.gameObject.SetActive(true);
            hint.text = Touchscreen.current != null && _portrait ? UiText.DexHintListTouch : UiText.DexHintList;
            UpdateFocusName();
        }

        /// <summary>index のカードを拡大表示する（クリック・Enter・テストから呼ぶ）。</summary>
        public void OpenZoom(int index)
        {
            if (_mode == Mode.Closed) return;
            if (_mode == Mode.List) LogFps();
            _grid.Select(index);
            _mode = Mode.Zoom;
            var def = CardCatalog.All[_grid.Selected];
            _zoomCard.gameObject.SetActive(true);
            _zoomCard.Bind(def, Player.P0, 0, false);
            bool unknown = IsUnknown(def.Id);
            _zoomCard.SetUnknown(unknown);
            _zoomCard.SetCount(_collectionView ? OwnedCount(def.Id) : 0);
            _zoomCard.SetOrder(80);
            _zoomCard.ResetTilt();
            var from = _cards[_grid.Selected].transform;
            _zoomCard.transform.SetPositionAndRotation(from.position, Quaternion.identity);
            _zoomCard.transform.localScale = from.localScale;
            foreach (var c in _cards) c.gameObject.SetActive(false);
            var (pos, scale) = ZoomPlacement();
            HandView.MoveCard(_zoomCard, pos, Quaternion.identity, scale, 0.22f);

            infoPanel.gameObject.SetActive(true);
            prevButton.gameObject.SetActive(true);
            nextButton.gameObject.SetActive(true);
            listButton.gameObject.SetActive(true);
            focusName.gameObject.SetActive(false);
            count.gameObject.SetActive(false);
            hint.text = UiText.DexHintZoom;
            var table = assets.cardArtTable;
            if (unknown)
            {
                // 未入手: 名前・数字・属性は伏せる（シルエットだけ）。アイコンの名前からカード名が推測できるので、クレジットも伏せる（5c）
                infoName.text = UiText.DexUnknownName;
                infoMeta.text = UiText.DexNotOwned;
            }
            else
            {
                infoName.text = UiText.DexLevel + def.Level + "  " + def.Name;
                infoMeta.text = UiText.DexElement + "  " + ElementName(def.Element) + "　　" + UiText.DexTribe + "  " + TribeName(def.Tribe)
                    + (_collectionView ? "　　" + UiText.DexOwned + OwnedCount(def.Id) : "");
            }
            infoTrait.text = !unknown && def.Trait.HasValue ? TraitInfoText(def.Trait.Value) : "";
            infoCredit.text = UiText.DexIcon + ": " + (unknown ? UiText.DexCreditHidden : table != null ? table.CreditLine(def.Id) : "");
            _pointerIdle = 99f;
            if (AutomationLog.Enabled)
            {
                var s = layout.TableToScreen(pos);
                float halfW = CardView.Size.x * scale * 0.5f, halfH = CardView.Size.y * scale * 0.5f;
                var corner = layout.TableToScreen(pos + new Vector3(halfW, halfH, 0f));
                AutomationLog.Write("dex zoom " + def.Id + (unknown ? " unknown" : "") + " lv" + def.Level + " " + F(s.x) + " " + F(Screen.height - s.y) + " "
                    + F(corner.x - s.x) + " " + F(corner.y - s.y));
                AutomationLog.Buttons("DexZoom", transform);
            }
        }

        /// <summary>図鑑の拡大表示の固有能力の説明（名前＝漢字 1 字 + 名前、1 行の説明、使えるルール）。</summary>
        public static string TraitInfoText(TraitId t)
        {
            string color = ColorUtility.ToHtmlStringRGB(Theme.TraitColor(t));
            return "<color=#" + color + "><b>" + UiText.DexTrait + "  " + Theme.TraitGlyph(t) + " " + UiText.TraitName(t) + "</b></color>\n"
                + UiText.TraitDesc(t) + "\n<size=75%><color=#A8B3D1>" + UiText.DexTraitNote + "</color></size>";
        }

        public void StepZoom(int dir)
        {
            if (_mode != Mode.Zoom) return;
            OpenZoom(_grid.Step(dir));
        }

        public void BackToList()
        {
            if (_mode != Mode.Zoom) return;
            _mode = Mode.List;
            _zoomCard.gameObject.SetActive(false);
            foreach (var c in _cards) c.gameObject.SetActive(true);
            _scroll = _grid.ScrollToShow(_scroll, _rowH, _viewH);
            ShowListUi();
            LayoutWorld(false);
            ResetFps();
            LogLayout();
        }

        (Vector3 pos, float scale) ZoomPlacement()
        {
            float h = layout.VisibleHeight;
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float w = h * aspect;
            if (_portrait)
            {
                // 上 132px の見出しの下から、下の説明パネル（下から 450px）の上まで
                float top = h * 0.5f - h * (132f / 1280f) - 0.12f;
                float bottomY = -h * 0.5f + h * (516f / 1280f);
                float scale = Mathf.Min((top - bottomY) / CardView.Size.y, w * 0.8f / CardView.Size.x);
                return (new Vector3(0f, (top + bottomY) * 0.5f, -0.2f), scale);
            }
            else
            {
                float top = h * 0.5f - h * (88f / 720f) - 0.1f;
                float bottomY = -h * 0.5f + h * (64f / 720f) + 0.1f;
                float scale = Mathf.Min((top - bottomY) * 0.94f / CardView.Size.y, w * 0.4f / CardView.Size.x);
                float x = -w * 0.5f + w * 0.27f;
                return (new Vector3(x, (top + bottomY) * 0.5f, -0.2f), scale);
            }
        }

        // ------------------------------------------------------------------
        // 一覧の配置
        // ------------------------------------------------------------------
        void LayoutWorld(bool animate)
        {
            if (_cards.Count == 0 || layout == null) return;
            float h = layout.VisibleHeight;
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float w = h * aspect;
            int cols = _portrait ? 5 : 10;
            _grid.SetColumns(cols);
            float headerH = h * (_portrait ? 132f / 1280f : 88f / 720f);
            float footerH = h * (_portrait ? 96f / 1280f : 64f / 720f);
            _viewTop = h * 0.5f - headerH - 0.06f;
            _viewH = h - headerH - footerH - 0.12f;
            _cellW = w * 0.95f / cols;
            _cardScale = _cellW / (CardView.Size.x * 1.12f);
            _rowH = CardView.Size.y * _cardScale * 1.09f;
            if (!_portrait && _grid.Rows * _rowH > _viewH)
            {
                // 横画面は 50 枚を 1 画面に収める
                _rowH = _viewH / _grid.Rows;
                _cardScale = _rowH / (CardView.Size.y * 1.09f);
                _cellW = CardView.Size.x * _cardScale * 1.12f;
            }
            _gridLeft = -_cellW * cols * 0.5f;
            _scroll = DexGrid.ClampScroll(_scroll, _grid.Rows * _rowH, _viewH);
            for (int i = 0; i < _cards.Count; i++) PlaceCard(i, animate);
        }

        Vector3 SlotPosition(int i)
        {
            float x = _gridLeft + (_grid.ColumnOf(i) + 0.5f) * _cellW;
            float y = _viewTop - (_grid.RowOf(i) + 0.5f) * _rowH + _scroll;
            return new Vector3(x, y, 0f);
        }

        void PlaceCard(int i, bool animate)
        {
            var c = _cards[i];
            bool focus = i == _hover || (_keyboard && i == _grid.Selected);
            float scale = _cardScale * (focus ? 1.12f : 1f);
            var pos = SlotPosition(i) + (focus ? new Vector3(0f, 0f, -0.1f) : Vector3.zero);
            c.SetOrder(focus ? 40 : 20);
            c.SetSelect(_keyboard && i == _grid.Selected ? 1f : 0f);
            c.SetLift(focus ? 0.3f : 0f);
            if (!focus) c.SetTiltTarget(Vector2.zero);
            // 見える範囲の外（見出し・足元の下）は描かない
            float half = CardView.Size.y * scale * 0.5f;
            bool visible = _mode == Mode.List && pos.y - half < _viewTop + 0.2f && pos.y + half > _viewTop - _viewH - 0.2f;
            c.gameObject.SetActive(visible);
            if (animate && visible) HandView.MoveCard(c, pos, Quaternion.identity, scale, 0.1f);
            else
            {
                c.transform.SetPositionAndRotation(pos, Quaternion.identity);
                c.transform.localScale = Vector3.one * scale;
            }
        }

        int HitTest(Vector3 world)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (!_cards[i].gameObject.activeSelf) continue;
                var c = SlotPosition(i);
                if (Mathf.Abs(world.x - c.x) <= _cellW * 0.5f && Mathf.Abs(world.y - c.y) <= _rowH * 0.5f) return i;
            }
            return -1;
        }

        void SetHover(int index)
        {
            if (index == _hover) return;
            int old = _hover;
            _hover = index;
            if (old >= 0) PlaceCard(old, true);
            if (index >= 0) PlaceCard(index, true);
            UpdateFocusName();
        }

        void UpdateFocusName()
        {
            int i = _hover >= 0 ? _hover : _keyboard ? _grid.Selected : -1;
            if (i < 0)
            {
                focusName.text = "";
                return;
            }
            var d = CardCatalog.All[i];
            focusName.text = IsUnknown(d.Id) ? UiText.DexUnknownName : UiText.DexLevel + d.Level + "  " + d.Name;
        }

        // ------------------------------------------------------------------
        // 入力
        // ------------------------------------------------------------------
        void Update()
        {
            if (_mode == Mode.Closed) return;
            float dt = Time.unscaledDeltaTime;
            if (_mode == Mode.List) CountFps(dt);
            HandleKeyboard();
            if (_mode == Mode.Closed) return;
            if (_mode == Mode.Zoom)
            {
                // 既定はゆっくり揺れる（ポインタが無い・動かしていないとき）。ポインタの処理が上書きする
                float t = Time.unscaledTime;
                _zoomCard.SetTiltTarget(new Vector2(Mathf.Sin(t * 0.7f) * 0.6f, Mathf.Sin(t * 0.5f + 1f) * 0.4f));
            }
            HandlePointer(dt);
            if (_mode == Mode.List && _keyboard && _hover < 0)
            {
                float t = Time.unscaledTime;
                _cards[_grid.Selected].SetTiltTarget(new Vector2(Mathf.Sin(t * 0.9f) * 0.5f, Mathf.Sin(t * 0.6f + 1f) * 0.35f));
            }
        }

        void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame;
            bool right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
            bool down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            bool esc = kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
            if (_mode == Mode.Zoom)
            {
                if (esc) BackToList();
                else if (left || up) StepZoom(-1);
                else if (right || down) StepZoom(+1);
                return;
            }
            if (esc)
            {
                Close();
                return;
            }
            if (left || right || up || down)
            {
                int old = _grid.Selected;
                if (!_keyboard)
                {
                    _keyboard = true;
                    if (_hover >= 0) _grid.Select(_hover);
                    SetHover(-1);
                }
                else _grid.Move(left ? -1 : right ? 1 : 0, up ? -1 : down ? 1 : 0);
                float before = _scroll;
                _scroll = _grid.ScrollToShow(_scroll, _rowH, _viewH);
                if (!Mathf.Approximately(before, _scroll)) LayoutWorld(false);
                else
                {
                    PlaceCard(old, true);
                    PlaceCard(_grid.Selected, true);
                }
                UpdateFocusName();
                if (AutomationLog.Enabled) AutomationLog.Write("dex select " + _grid.Selected);
            }
            else if (enter && _keyboard)
            {
                OpenZoom(_grid.Selected);
            }
        }

        bool PointerOverUi(Vector2 pos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = pos };
            var hits = new List<RaycastResult>();
            es.RaycastAll(data, hits);
            return hits.Count > 0;
        }

        void HandlePointer(float dt)
        {
            var pointer = Pointer.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();
            bool pressed = pointer.press.wasPressedThisFrame;
            bool released = pointer.press.wasReleasedThisFrame;
            bool moved = (pos - _lastPointer).sqrMagnitude > 1f;
            var delta = pos - _lastPointer;
            _lastPointer = pos;
            bool touch = pointer is Touchscreen;
            var world = layout.ScreenToTable(pos);
            _pointerIdle = moved ? 0f : _pointerIdle + dt;

            if (pressed)
            {
                _pressing = !PointerOverUi(pos);
                _dragging = false;
                _pressPos = pos;
            }
            if (_pressing && pointer.press.isPressed && (pos - _pressPos).magnitude > TapPixels) _dragging = true;

            if (_mode == Mode.List)
            {
                if (moved && !touch && _keyboard)
                {
                    _keyboard = false;
                    PlaceCard(_grid.Selected, true);
                }
                if (!touch && !_keyboard) SetHover(HitTest(world));
                if (_hover >= 0 && !touch) _cards[_hover].TiltToward(world);

                // スクロール: ホイール・ドラッグ（中身が見える高さより大きいときだけ動く）
                float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
                float pxToWorld = layout.VisibleHeight / Mathf.Max(1f, Screen.height);
                float newScroll = _scroll;
                if (Mathf.Abs(wheel) > 0.01f) newScroll -= Mathf.Sign(wheel) * _rowH * 0.5f;
                if (_dragging && _pressing) newScroll += delta.y * pxToWorld;
                newScroll = DexGrid.ClampScroll(newScroll, _grid.Rows * _rowH, _viewH);
                if (!Mathf.Approximately(newScroll, _scroll))
                {
                    _scroll = newScroll;
                    LayoutWorld(false);
                }

                if (released && _pressing)
                {
                    _pressing = false;
                    if (!_dragging)
                    {
                        int hit = HitTest(world);
                        if (hit >= 0) OpenZoom(hit);
                    }
                    else if (AutomationLog.Enabled) LogLayout();
                }
                return;
            }

            // 拡大表示: カーソル（指）の方へ傾ける。しばらく動かさなければゆっくり揺れる
            if (touch)
            {
                if (_pressing && pointer.press.isPressed) _touchTilt = Vector2.ClampMagnitude(_touchTilt + delta * 0.006f, 1f);
                else _touchTilt = Vector2.Lerp(_touchTilt, Vector2.zero, 1f - Mathf.Exp(-dt / 0.6f));
                _zoomCard.SetTiltTarget(_touchTilt);
            }
            else if (_pointerIdle < 2.5f) _zoomCard.TiltToward(world);
            else
            {
                float t = Time.unscaledTime;
                _zoomCard.SetTiltTarget(new Vector2(Mathf.Sin(t * 0.7f) * 0.6f, Mathf.Sin(t * 0.5f + 1f) * 0.4f));
            }
            if (released && _pressing)
            {
                _pressing = false;
                // カードの外を（ドラッグせずに）クリックしたら一覧へ
                if (!_dragging && !touch && !OverZoomCard(world)) BackToList();
            }
        }

        bool OverZoomCard(Vector3 world)
        {
            var c = _zoomCard.transform.position;
            float s = _zoomCard.transform.localScale.x;
            return Mathf.Abs(world.x - c.x) <= CardView.Size.x * s * 0.5f && Mathf.Abs(world.y - c.y) <= CardView.Size.y * s * 0.5f;
        }

        // ------------------------------------------------------------------
        // 表示用の文字・計測・ログ
        // ------------------------------------------------------------------
        public static string ElementName(Element? e)
        {
            if (!e.HasValue) return UiText.DexNone;
            switch (e.Value)
            {
                case Element.Fire: return UiText.ElementFireName;
                case Element.Ice: return UiText.ElementIceName;
                case Element.Thunder: return UiText.ElementThunderName;
                case Element.Earth: return UiText.ElementEarthName;
                case Element.Water: return UiText.ElementWaterName;
                case Element.Wind: return UiText.ElementWindName;
                case Element.Holy: return UiText.ElementHolyName;
                case Element.Dark: return UiText.ElementDarkName;
                default: return UiText.DexNone;
            }
        }

        public static string TribeName(Tribe? t)
        {
            if (!t.HasValue) return UiText.DexNone;
            switch (t.Value)
            {
                case Tribe.Beast: return UiText.TribeBeastName;
                case Tribe.Machine: return UiText.TribeMachineName;
                case Tribe.Spirit: return UiText.TribeSpiritName;
                case Tribe.Dragon: return UiText.TribeDragonName;
                default: return UiText.DexNone;
            }
        }

        void ResetFps()
        {
            _fpsTime = 0f;
            _fpsFrames = 0;
            _fpsWorst = 0f;
        }

        void CountFps(float dt)
        {
            _fpsTime += dt;
            _fpsFrames++;
            if (_fpsFrames > 10 && dt > _fpsWorst) _fpsWorst = dt;
        }

        void LogFps()
        {
            if (!AutomationLog.Enabled || _fpsTime < 0.5f) return;
            AutomationLog.Write("fps-dex avg " + (_fpsFrames / _fpsTime).ToString("0.0", CultureInfo.InvariantCulture) + " worst "
                + (_fpsWorst * 1000f).ToString("0", CultureInfo.InvariantCulture) + "ms over " + _fpsTime.ToString("0.0", CultureInfo.InvariantCulture) + "s");
            ResetFps();
        }

        static string F(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);

        void LogLayout()
        {
            if (!AutomationLog.Enabled) return;
            AutomationLog.Buttons("Dex", transform);
            for (int i = 0; i < _cards.Count; i++)
            {
                if (!_cards[i].gameObject.activeSelf) continue;
                var s = layout.TableToScreen(SlotPosition(i));
                AutomationLog.Write("dex card " + i + " " + F(s.x) + " " + F(Screen.height - s.y) + " " + _cards[i].CardId);
            }
            AutomationLog.Write("dex list " + (_portrait ? "portrait" : "landscape") + " scale " + _cardScale.ToString("0.00", CultureInfo.InvariantCulture));
        }
    }
}
