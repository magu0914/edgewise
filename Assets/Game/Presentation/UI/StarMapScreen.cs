using System;
using System.Collections.Generic;
using System.Threading;
using Edgewise.Core.Campaign;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Campaign;
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
    /// 星図（DESIGN §12-11）: 濃紺の星空（背景の Starfield）の上に 4 つの星域を星座のように置き、線でつなぐ（横は左 → 右、縦は上 → 下）。
    /// 各星域の印: 名前・ルールの小さな札・進み具合（勝った相手 ●、未勝利 ○）。未解放は暗く、錠前の印。
    /// 主に初めて勝って戻ったときは、次の星域への線が光って伸び、錠前が外れる（PlayUnlockAsync）。
    /// </summary>
    public sealed class StarMapScreen : MonoBehaviour, IResponsiveUi
    {
        public const int MaxChips = 8;

        [SerializeField] Image header;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI collect;
        [SerializeField] TextMeshProUGUI hint;
        [SerializeField] TextMeshProUGUI notice;
        [SerializeField] Button backButton;
        [SerializeField] RawImage[] lineBase = new RawImage[3];
        [SerializeField] RawImage[] lineFill = new RawImage[3];
        [SerializeField] Button[] nodes = new Button[4];
        [SerializeField] RawImage[] nodeGlow = new RawImage[4];
        [SerializeField] RawImage[] nodeDisc = new RawImage[4];
        [SerializeField] RawImage[] nodeRing = new RawImage[4];
        [SerializeField] RawImage[] nodeLock = new RawImage[4];
        [SerializeField] TextMeshProUGUI[] nodeNumber = new TextMeshProUGUI[4];
        [SerializeField] TextMeshProUGUI[] nodeName = new TextMeshProUGUI[4];
        [SerializeField] TextMeshProUGUI[] nodeProgress = new TextMeshProUGUI[4];
        [SerializeField] RectTransform[] nodeChips = new RectTransform[4];
        [SerializeField] TextMeshProUGUI toast;

        CampaignState _state;
        bool _portrait;
        int _animating = -1;

        public event Action<Region> RegionSelected;
        public event Action BackPressed;

        public bool IsOpen => gameObject.activeSelf;
        public Button BackButton => backButton;
        public Button Node(int i) => nodes[i];
        public bool IsAnimating => _animating >= 0;

        /// <summary>星域 i の印が解放済みの見た目か（テスト用）。</summary>
        public bool NodeLooksUnlocked(int i) => nodeLock[i] != null && !nodeLock[i].gameObject.activeSelf;

        public string NodeProgress(int i) => nodeProgress[i].text;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            header = UiKit.Panel(root, "Header", a, new Color(0.03f, 0.04f, 0.09f, 0.85f), false);
            title = UiKit.Label(root, "Title", a, UiText.MapTitle, 36, Theme.Gold, TextAlignmentOptions.Left, true);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            collect = UiKit.Label(root, "Collect", a, "", 22, Theme.InkDim, TextAlignmentOptions.Left);
            collect.textWrappingMode = TextWrappingModes.NoWrap;
            hint = UiKit.Label(root, "Hint", a, UiText.MapHint, 20, Theme.InkDim);
            notice = UiKit.Label(root, "Notice", a, "", 18, new Color(1f, 0.75f, 0.6f));
            backButton = UiKit.Button(root, "MapBackButton", a, UiText.MapToTitle, 22, out _);

            lineBase = new RawImage[3];
            lineFill = new RawImage[3];
            for (int i = 0; i < 3; i++)
            {
                lineBase[i] = Raw(root, "LineBase" + i);
                lineFill[i] = Raw(root, "LineFill" + i);
            }
            nodes = new Button[4];
            nodeGlow = new RawImage[4];
            nodeDisc = new RawImage[4];
            nodeRing = new RawImage[4];
            nodeLock = new RawImage[4];
            nodeNumber = new TextMeshProUGUI[4];
            nodeName = new TextMeshProUGUI[4];
            nodeProgress = new TextMeshProUGUI[4];
            nodeChips = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var nodeRt = UiKit.Rect(root, "Region" + (i + 1));
                var hit = nodeRt.gameObject.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
                var b = nodeRt.gameObject.AddComponent<Button>();
                b.targetGraphic = hit;
                var colors = b.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0f);
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.06f);
                colors.selectedColor = new Color(1f, 0.95f, 0.7f, 0.10f);
                colors.pressedColor = new Color(1f, 1f, 1f, 0.12f);
                colors.colorMultiplier = 1f;
                b.colors = colors;
                nodes[i] = b;
                var star = UiKit.Rect(nodeRt, "Star");
                nodeGlow[i] = Raw(star, "Glow");
                nodeDisc[i] = Raw(star, "Disc");
                nodeRing[i] = Raw(star, "Ring");
                nodeNumber[i] = UiKit.Label(star, "Number", a, (i + 1).ToString(), 44, Theme.Ink, TextAlignmentOptions.Center, true);
                nodeNumber[i].fontStyle = FontStyles.Bold;
                nodeLock[i] = Raw(star, "Lock");
                nodeName[i] = UiKit.Label(nodeRt, "Name", a, "", 28, Theme.Ink, TextAlignmentOptions.Center, true);
                nodeName[i].textWrappingMode = TextWrappingModes.NoWrap;
                nodeProgress[i] = UiKit.Label(nodeRt, "Progress", a, "", 24, Theme.Gold);
                nodeProgress[i].textWrappingMode = TextWrappingModes.NoWrap;
                nodeChips[i] = UiKit.Rect(nodeRt, "Chips");
                for (int k = 0; k < MaxChips; k++)
                {
                    var chip = UiKit.Panel(nodeChips[i], "Chip" + k, a, new Color(0.10f, 0.13f, 0.26f, 0.92f), false);
                    var t = UiKit.Label(chip.transform, "Text", a, "", 15, Theme.Ink);
                    UiKit.Stretch(t.rectTransform, 2, 0, 2, 0);
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    chip.gameObject.SetActive(false);
                }
            }
            toast = UiKit.Label(root, "Toast", a, UiText.MapUnlocked, 30, Theme.Gold, TextAlignmentOptions.Center, true);
            toast.textWrappingMode = TextWrappingModes.NoWrap;
            toast.gameObject.SetActive(false);
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        static RawImage Raw(Transform parent, string name)
        {
            var img = UiKit.Rect(parent, name).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        void Awake()
        {
            for (int i = 0; i < 3; i++)
            {
                lineBase[i].texture = UiGlyphs.Line;
                lineFill[i].texture = UiGlyphs.Line;
            }
            for (int i = 0; i < 4; i++)
            {
                nodeGlow[i].texture = UiGlyphs.Glow;
                nodeDisc[i].texture = UiGlyphs.Circle;
                nodeRing[i].texture = UiGlyphs.Ring;
                nodeLock[i].texture = UiGlyphs.Lock;
                int k = i;
                nodes[i].onClick.AddListener(() =>
                {
                    if (_animating >= 0 || _state == null) return;
                    var r = CampaignData.Regions[k];
                    if (!_state.IsUnlocked(r.Id))
                    {
                        Shake(k);
                        return;
                    }
                    RegionSelected?.Invoke(r);
                });
            }
            backButton.onClick.AddListener(() =>
            {
                if (_animating < 0) BackPressed?.Invoke();
            });
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) && _animating < 0 && !SettingsPanel.AnyOpen)
                BackPressed?.Invoke();
            // 星の印はゆっくり瞬く
            float t = Time.unscaledTime;
            for (int i = 0; i < 4; i++)
            {
                if (i == _animating) continue;
                bool open = _state != null && _state.IsUnlocked(CampaignData.Regions[i].Id);
                float pulse = open ? 1f + 0.06f * Mathf.Sin(t * 1.7f + i * 1.3f) : 1f;
                nodeGlow[i].rectTransform.localScale = Vector3.one * pulse;
            }
        }

        // ------------------------------------------------------------------
        // 配置
        // ------------------------------------------------------------------

        /// <summary>星域 i の星の中心（画面中央からの位置。横 1280×720 / 縦 720×1280 の基準）。</summary>
        public static Vector2 NodePosition(int i, bool portrait)
        {
            if (portrait)
            {
                float[] xs = { -200f, -100f, -200f, -100f };
                float[] ys = { 380f, 130f, -120f, -370f };
                return new Vector2(xs[i], ys[i]);
            }
            float[] lx = { -450f, -150f, 150f, 450f };
            float[] ly = { -10f, 80f, -40f, 60f };
            return new Vector2(lx[i], ly[i] - 10f);
        }

        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            var c = new Vector2(0.5f, 0.5f);
            float w = portrait ? 720f : 1280f;
            float headerH = portrait ? 120f : 84f;
            UiKit.Place((RectTransform)header.transform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(w * 2f, headerH));
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(28f, portrait ? -14f : -16f), new Vector2(200f, 52f));
            UiKit.Place(collect.rectTransform, new Vector2(0f, 1f), new Vector2(portrait ? 30f : 150f, portrait ? -70f : -28f),
                new Vector2(320f, 34f));
            collect.fontSize = portrait ? 26 : 22;
            UiKit.Place((RectTransform)backButton.transform, new Vector2(1f, 1f), new Vector2(-20f, portrait ? -22f : -14f),
                portrait ? new Vector2(220f, 76f) : new Vector2(190f, 56f));
            backButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 28 : 22;
            UiKit.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, portrait ? 26f : 20f), new Vector2(680f, 34f));
            hint.fontSize = portrait ? 26 : 20;
            UiKit.Place(notice.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, portrait ? 66f : 52f), new Vector2(portrait ? 680f : 1100f, 30f));

            float disc = portrait ? 130f : 112f;
            for (int i = 0; i < 4; i++)
            {
                var p = NodePosition(i, portrait);
                var nodeRt = (RectTransform)nodes[i].transform;
                var star = (RectTransform)nodeDisc[i].transform.parent;
                if (portrait)
                {
                    // 星を左、文字を右に（縦の道筋）。1 行目に名前と進み具合、その下にルールの札
                    UiKit.Place(nodeRt, c, new Vector2(p.x + 170f, p.y), new Vector2(500f, 210f));
                    UiKit.Place(star, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(disc * 0.5f + 20f, 0f), new Vector2(disc, disc));
                    UiKit.Place(nodeName[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(disc + 40f, 46f), new Vector2(200f, 46f));
                    nodeName[i].alignment = TextAlignmentOptions.Left;
                    nodeName[i].fontSize = 34;
                    UiKit.Place(nodeProgress[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(disc + 240f, 46f), new Vector2(100f, 46f));
                    nodeProgress[i].alignment = TextAlignmentOptions.Right;
                    nodeProgress[i].fontSize = 28;
                    UiKit.Place(nodeChips[i], new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(disc + 40f, 14f), new Vector2(300f, 36f));
                }
                else
                {
                    UiKit.Place(nodeRt, c, new Vector2(p.x, p.y - 50f), new Vector2(280f, 250f));
                    UiKit.Place(star, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -disc * 0.5f - 6f), new Vector2(disc, disc));
                    UiKit.Place(nodeName[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -disc - 12f), new Vector2(280f, 38f));
                    nodeName[i].alignment = TextAlignmentOptions.Center;
                    nodeName[i].fontSize = 28;
                    UiKit.Place(nodeProgress[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -disc - 50f), new Vector2(280f, 28f));
                    nodeProgress[i].alignment = TextAlignmentOptions.Center;
                    nodeProgress[i].fontSize = 22;
                    UiKit.Place(nodeChips[i], new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -disc - 82f), new Vector2(280f, 54f));
                }
                Stretch(nodeGlow[i].rectTransform, -0.55f);
                Stretch(nodeDisc[i].rectTransform, 0f);
                Stretch(nodeRing[i].rectTransform, 0.04f);
                // 錠前は星の上に小さく（星の右下に重ねる）
                var lockRt = nodeLock[i].rectTransform;
                lockRt.anchorMin = new Vector2(0.52f, -0.08f);
                lockRt.anchorMax = new Vector2(1.08f, 0.48f);
                lockRt.offsetMin = Vector2.zero;
                lockRt.offsetMax = Vector2.zero;
                UiKit.Stretch(nodeNumber[i].rectTransform);
                nodeNumber[i].fontSize = portrait ? 52 : 46;
                LayoutChips(i);
            }
            LayoutLines();
            UiKit.Place(toast.rectTransform, c, new Vector2(0f, portrait ? -520f : 250f), new Vector2(700f, 50f));
            if (_state != null) Refresh(false);
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = new Vector2(inset, inset);
            rt.anchorMax = new Vector2(1f - inset, 1f - inset);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>星の中心（星図の画面の座標）。</summary>
        Vector2 StarCenter(int i)
        {
            var p = NodePosition(i, _portrait);
            float disc = _portrait ? 130f : 112f;
            return _portrait ? new Vector2(p.x + 170f - 250f + disc * 0.5f + 20f, p.y) : new Vector2(p.x, p.y - 50f + 125f - disc * 0.5f - 6f);
        }

        void LayoutLines()
        {
            for (int i = 0; i < 3; i++)
            {
                var a = StarCenter(i);
                var b = StarCenter(i + 1);
                foreach (var img in new[] { lineBase[i], lineFill[i] })
                {
                    var rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.anchoredPosition = a;
                    var d = b - a;
                    rt.sizeDelta = new Vector2(d.magnitude, img == lineFill[i] ? 12f : 8f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                }
            }
        }

        void LayoutChips(int i)
        {
            var row = nodeChips[i];
            float chipH = _portrait ? 34f : 24f;
            float gap = 4f;
            float x = 0f;
            float maxW = _portrait ? 300f : 280f;
            var active = new List<RectTransform>();
            for (int k = 0; k < row.childCount; k++)
            {
                var chip = (RectTransform)row.GetChild(k);
                if (!chip.gameObject.activeSelf) continue;
                active.Add(chip);
            }
            // 2 行まで（横は中央揃え、縦は左揃え）
            var rows = new List<List<(RectTransform rt, float w)>> { new List<(RectTransform, float)>() };
            foreach (var chip in active)
            {
                var t = chip.GetComponentInChildren<TextMeshProUGUI>();
                t.fontSize = _portrait ? 21 : 15;
                float w = t.GetPreferredValues(t.text).x + (_portrait ? 18f : 12f);
                if (x + w > maxW && rows[rows.Count - 1].Count > 0)
                {
                    rows.Add(new List<(RectTransform, float)>());
                    x = 0f;
                }
                rows[rows.Count - 1].Add((chip, w));
                x += w + gap;
            }
            for (int r = 0; r < rows.Count; r++)
            {
                float total = -gap;
                foreach (var e in rows[r]) total += e.w + gap;
                float cx = _portrait ? 0f : -total * 0.5f;
                foreach (var e in rows[r])
                {
                    var anchor = _portrait ? new Vector2(0f, 1f) : new Vector2(0.5f, 1f);
                    UiKit.Place(e.rt, anchor, new Vector2(0f, 1f), new Vector2(cx, -r * (chipH + 3f)), new Vector2(e.w, chipH));
                    cx += e.w + gap;
                }
            }
        }

        // ------------------------------------------------------------------
        // 表示
        // ------------------------------------------------------------------

        /// <summary>星図を出す。focus の星域にキーボードの選択を合わせる。hideRegion の星域は（鍵を外す演出の前なので）未解放の見た目にする。</summary>
        public void Show(CampaignState state, Region focus, Region hideRegion = null, string noticeText = null)
        {
            _state = state;
            gameObject.SetActive(true);
            toast.gameObject.SetActive(false);
            notice.text = noticeText ?? "";
            collect.text = CampaignTexts.Collection(state);
            // 鍵を外す演出の前の星域は、まだ未解放の見た目にしておく（_animating の間は Refresh がそう描く）
            _animating = hideRegion != null ? hideRegion.Number - 1 : -1;
            Refresh(false);
            int f = focus != null ? focus.Number - 1 : 0;
            if (hideRegion != null) f = Mathf.Max(0, hideRegion.Number - 2);
            EventSystem.current?.SetSelectedGameObject(nodes[Mathf.Clamp(f, 0, 3)].gameObject);
            AutomationLog.Buttons("Map", transform);
        }

        public void Hide()
        {
            _animating = -1;
            gameObject.SetActive(false);
        }

        void Refresh(bool _)
        {
            for (int i = 0; i < 4; i++)
            {
                var r = CampaignData.Regions[i];
                bool hidden = i == _animating;
                bool open = _state.IsUnlocked(r.Id) && !hidden;
                SetNode(i, r, open, open ? 1f : 0f);
                for (int k = 0; k < MaxChips; k++) nodeChips[i].GetChild(k).gameObject.SetActive(false);
                var chips = CampaignTexts.RuleChips(r.Rules);
                for (int k = 0; k < chips.Count && k < MaxChips; k++)
                {
                    var chip = nodeChips[i].GetChild(k);
                    chip.gameObject.SetActive(true);
                    chip.GetComponentInChildren<TextMeshProUGUI>().text = chips[k];
                }
                LayoutChips(i);
            }
            for (int i = 0; i < 3; i++)
            {
                bool lit = _state.IsUnlocked(CampaignData.Regions[i + 1].Id) && i + 1 != _animating;
                SetLine(i, lit ? 1f : 0f);
            }
        }

        /// <summary>星域の印の見た目。k = 0（未解放）〜 1（解放済み）。</summary>
        void SetNode(int i, Region r, bool open, float k)
        {
            var color = CampaignTheme.RegionColor(r);
            var locked = new Color(0.22f, 0.24f, 0.34f);
            var c = Color.Lerp(locked, color, k);
            nodeDisc[i].color = Color.Lerp(new Color(0.05f, 0.06f, 0.12f), c, 0.35f + 0.1f * k);
            nodeRing[i].color = new Color(c.r, c.g, c.b, 0.55f + 0.45f * k);
            nodeGlow[i].color = new Color(c.r, c.g, c.b, 0.15f + 0.35f * k);
            nodeNumber[i].color = Color.Lerp(new Color(0.45f, 0.48f, 0.6f), Color.Lerp(c, Color.white, 0.6f), k);
            nodeNumber[i].alpha = k < 0.5f ? 0.35f : 1f;
            nodeLock[i].gameObject.SetActive(!open);
            nodeLock[i].color = new Color(0.75f, 0.78f, 0.9f, 0.95f);
            nodeLock[i].rectTransform.localScale = Vector3.one;
            nodeLock[i].rectTransform.localRotation = Quaternion.identity;
            nodeName[i].text = r.Name;
            nodeName[i].color = Color.Lerp(new Color(0.5f, 0.53f, 0.66f), Theme.Ink, k);
            nodeProgress[i].text = open ? CampaignTexts.RegionProgress(_state, r) : UiText.MapLocked;
            nodeProgress[i].color = open ? Theme.Gold : new Color(0.5f, 0.53f, 0.66f);
            foreach (var t in nodeChips[i].GetComponentsInChildren<TextMeshProUGUI>(true)) t.alpha = 0.4f + 0.6f * k;
            foreach (var img in nodeChips[i].GetComponentsInChildren<Image>(true)) img.color = new Color(0.10f, 0.13f, 0.26f, 0.5f + 0.42f * k);
        }

        void SetLine(int i, float k)
        {
            var baseRt = lineBase[i].rectTransform;
            lineBase[i].color = new Color(0.45f, 0.50f, 0.72f, 0.35f);
            var fill = lineFill[i].rectTransform;
            fill.sizeDelta = new Vector2(baseRt.sizeDelta.x * Mathf.Clamp01(k), fill.sizeDelta.y);
            lineFill[i].color = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.9f);
            lineFill[i].gameObject.SetActive(k > 0.001f);
        }

        void Shake(int i)
        {
            var rt = nodeLock[i].rectTransform;
            SfxBank.Instance?.Play(SfxKind.Cursor);
            Tweens.Play(0.3f, k => rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 6f) * 14f * (1f - k)), Ease.Linear, 0f, rt, "shake");
        }

        // ------------------------------------------------------------------
        // 鍵が外れる演出
        // ------------------------------------------------------------------

        /// <summary>
        /// 主に初めて勝って星図に戻ったとき: 前の星域から次の星域へ金の線が伸び、錠前が揺れて外れ、星が明るく灯る。timeFactor 0 なら即座に終える。
        /// </summary>
        public async Awaitable PlayUnlockAsync(Region region, float timeFactor, CancellationToken ct)
        {
            int i = region.Number - 1;
            if (i <= 0 || i > 3)
            {
                _animating = -1;
                Refresh(false);
                return;
            }
            _animating = i;
            if (AutomationLog.Enabled) AutomationLog.Write("map unlock " + region.Id + " begin");
            float tf = timeFactor;
            try
            {
                await Tweens.Delay(0.35f * tf, ct);
                // 1. 線が伸びる（先に小さな光）
                SfxBank.Instance?.Play(SfxKind.Unlock, -5f);
                await Tweens.Run(0.75f * tf, k => SetLine(i - 1, k), ct, Ease.InOutCubic);
                // 2. 錠前が揺れる
                var lockRt = nodeLock[i].rectTransform;
                await Tweens.Run(0.32f * tf, k => lockRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 5f) * 16f), ct, Ease.Linear);
                // 3. 錠前が弾けて消え、星が灯る
                SfxBank.Instance?.Play(SfxKind.Unlock);
                var r = CampaignData.Regions[i];
                var glowRt = nodeGlow[i].rectTransform;
                await Tweens.Run(0.55f * tf, k =>
                {
                    lockRt.localScale = Vector3.one * Mathf.Lerp(1f, 1.9f, k);
                    lockRt.localRotation = Quaternion.Euler(0f, 0f, -30f * k);
                    nodeLock[i].color = new Color(1f, 0.95f, 0.75f, 1f - k);
                    SetNode(i, r, true, k);
                    nodeLock[i].gameObject.SetActive(k < 1f);
                    glowRt.localScale = Vector3.one * (1f + 0.9f * Mathf.Sin(Mathf.PI * k));
                }, ct, Ease.OutCubic);
                nodeLock[i].gameObject.SetActive(false);
                // 4. 「新しい星域が開いた！」
                toast.gameObject.SetActive(true);
                var trt = toast.rectTransform;
                await Tweens.Run(0.35f * tf, k => trt.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, k), ct, Ease.OutBack);
                await Tweens.Delay(0.9f * tf, ct);
            }
            finally
            {
                _animating = -1;
                if (_state != null && gameObject.activeSelf) Refresh(false);
                if (this != null && gameObject.activeSelf)
                {
                    EventSystem.current?.SetSelectedGameObject(nodes[i].gameObject);
                    toast.gameObject.SetActive(tf > 0f && toast.gameObject.activeSelf);
                }
            }
            if (AutomationLog.Enabled)
            {
                AutomationLog.Write("map unlock " + region.Id + " done");
                AutomationLog.Buttons("Map", transform);
            }
        }
    }
}
