using System;
using System.Collections.Generic;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.Campaign;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 星域の画面（相手選択。DESIGN §12-11）: 上部に星域のルール（1 行ずつ）とデッキの上限。3 人の相手を並べ、主には冠。
    /// 各相手: 立ち絵・肩書きと名前・強さ（★1〜5）・勝敗数・持っているカード（見たことのあるカードは小さく表、未見は「？」、
    /// 手に入れたカードには ◆、原作モードで取られたカードには「取」）。主の欄には「主との対戦は決着がつくまで（Sudden Death）」。
    /// </summary>
    public sealed class RegionScreen : MonoBehaviour, IResponsiveUi
    {
        public const int MaxCards = 8;
        public const int MaxRuleLines = 8;

        [Serializable]
        public sealed class OpponentPanel
        {
            public Image panel;
            public PortraitView portrait;
            public TextMeshProUGUI title;
            public TextMeshProUGUI name;
            public TextMeshProUGUI stars;
            public TextMeshProUGUI record;
            public TextMeshProUGUI cardsLabel;
            public TextMeshProUGUI note;
            public MiniCardView[] cards = new MiniCardView[MaxCards];
            public Button challenge;
        }

        [SerializeField] Image header;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] Button backButton;
        [SerializeField] Image rulesPanel;
        [SerializeField] TextMeshProUGUI rulesLabel;
        [SerializeField] TextMeshProUGUI[] ruleLines = new TextMeshProUGUI[MaxRuleLines];
        [SerializeField] TextMeshProUGUI capText;
        [SerializeField] OpponentPanel[] panels = new OpponentPanel[3];
        [SerializeField] GameAssets assets;

        Region _region;
        CampaignState _state;
        bool _portrait;
        int _ruleCount;

        public event Action<Opponent> ChallengePressed;
        public event Action BackPressed;

        public bool IsOpen => gameObject.activeSelf;
        public Region Region => _region;
        public Button BackButton => backButton;
        public Button ChallengeButton(int i) => panels[i].challenge;
        public PortraitView Portrait(int i) => panels[i].portrait;
        public MiniCardView Card(int panel, int i) => panels[panel].cards[i];
        public string RuleText(int i) => ruleLines[i].text;
        public int RuleCount => _ruleCount;
        public string NoteText(int i) => panels[i].note.gameObject.activeSelf ? panels[i].note.text : "";
        public string CapText => capText.text;

        public void Build(GameAssets a)
        {
            assets = a;
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", new Color(0f, 0f, 0.03f, 0.35f), false);
            header = UiKit.Panel(root, "Header", a, new Color(0.03f, 0.04f, 0.09f, 0.85f), false);
            title = UiKit.Label(root, "Title", a, "", 36, Theme.Gold, TextAlignmentOptions.Left, true);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            backButton = UiKit.Button(root, "RegionBackButton", a, UiText.RegionBack, 22, out _);
            rulesPanel = UiKit.Panel(root, "RulesPanel", a, new Color(0.05f, 0.07f, 0.15f, 0.9f), false);
            rulesLabel = UiKit.Label(rulesPanel.transform, "RulesLabel", a, UiText.RegionRules, 18, Theme.InkDim, TextAlignmentOptions.Left);
            rulesLabel.textWrappingMode = TextWrappingModes.NoWrap;
            ruleLines = new TextMeshProUGUI[MaxRuleLines];
            for (int i = 0; i < MaxRuleLines; i++)
            {
                ruleLines[i] = UiKit.Label(rulesPanel.transform, "Rule" + i, a, "", 19, Theme.Ink, TextAlignmentOptions.Left);
                ruleLines[i].textWrappingMode = TextWrappingModes.NoWrap;
                ruleLines[i].enableAutoSizing = true;
                ruleLines[i].fontSizeMin = 12;
                ruleLines[i].fontSizeMax = 19;
            }
            capText = UiKit.Label(rulesPanel.transform, "Cap", a, "", 20, Theme.Gold, TextAlignmentOptions.Right);
            capText.textWrappingMode = TextWrappingModes.NoWrap;

            panels = new OpponentPanel[3];
            for (int i = 0; i < 3; i++)
            {
                var p = new OpponentPanel();
                p.panel = UiKit.Panel(root, "Opponent" + i, a, Theme.Panel, false);
                var t = p.panel.transform;
                p.portrait = PortraitView.Create(t, "Portrait", a);
                p.title = UiKit.Label(t, "OpponentTitle", a, "", 18, Theme.InkDim, TextAlignmentOptions.Left);
                p.name = UiKit.Label(t, "OpponentName", a, "", 30, Theme.Ink, TextAlignmentOptions.Left, true);
                p.name.fontStyle = FontStyles.Bold;
                p.stars = UiKit.Label(t, "Stars", a, "", 22, Theme.Gold, TextAlignmentOptions.Left);
                p.record = UiKit.Label(t, "Record", a, "", 18, Theme.InkDim, TextAlignmentOptions.Left);
                p.cardsLabel = UiKit.Label(t, "CardsLabel", a, UiText.RegionCards, 17, Theme.InkDim, TextAlignmentOptions.Left);
                p.note = UiKit.Label(t, "BossNote", a, UiText.RegionBossNote, 16, Theme.Gold, TextAlignmentOptions.Left);
                foreach (var l in new[] { p.title, p.name, p.stars, p.record, p.cardsLabel, p.note })
                {
                    l.textWrappingMode = TextWrappingModes.NoWrap;
                    l.enableAutoSizing = true;
                    l.fontSizeMin = 11;
                }
                // 小さなカード（TextMeshPro が多い）はシーンを軽くするため実行時に作る（EnsureCards）
                p.cards = new MiniCardView[0];
                p.challenge = UiKit.Button(t, "Challenge" + i, a, UiText.RegionChallenge, 26, out _, new Color(0.25f, 0.42f, 0.40f));
                panels[i] = p;
            }
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        /// <summary>小さなカードを作る（最初に表示するとき）。</summary>
        void EnsureCards()
        {
            bool made = false;
            foreach (var p in panels)
            {
                if (p.cards != null && p.cards.Length == MaxCards && p.cards[0] != null) continue;
                p.cards = new MiniCardView[MaxCards];
                for (int k = 0; k < MaxCards; k++) p.cards[k] = MiniCardView.Create(p.panel.transform, "Card" + k, assets, false);
                made = true;
            }
            if (made) ApplyLayout(_portrait);
        }

        void Awake()
        {
            EnsureCards();
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                panels[i].challenge.onClick.AddListener(() =>
                {
                    if (_region != null) ChallengePressed?.Invoke(_region.Opponents[k]);
                });
            }
            backButton.onClick.AddListener(() => BackPressed?.Invoke());
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) && !SettingsPanel.AnyOpen) BackPressed?.Invoke();
        }

        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            float w = portrait ? 720f : 1280f;
            var top = new Vector2(0.5f, 1f);
            var tl = new Vector2(0f, 1f);
            float headerH = portrait ? 110f : 80f;
            UiKit.Place((RectTransform)header.transform, top, Vector2.zero, new Vector2(w * 2f, headerH));
            UiKit.Place(title.rectTransform, tl, new Vector2(28f, portrait ? -24f : -14f), new Vector2(480f, 52f));
            title.fontSize = portrait ? 40 : 36;
            UiKit.Place((RectTransform)backButton.transform, new Vector2(1f, 1f), new Vector2(-20f, portrait ? -18f : -12f),
                portrait ? new Vector2(200f, 76f) : new Vector2(170f, 56f));
            backButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 28 : 22;

            if (!portrait)
            {
                // ルール: 2 列 × 4 行、右に上限
                UiKit.Place((RectTransform)rulesPanel.transform, top, new Vector2(0f, -88f), new Vector2(1236f, 132f));
                UiKit.Place(rulesLabel.rectTransform, tl, new Vector2(20f, -8f), new Vector2(400f, 26f));
                rulesLabel.fontSize = 17;
                for (int i = 0; i < MaxRuleLines; i++)
                {
                    int col = i / 4, row = i % 4;
                    UiKit.Place(ruleLines[i].rectTransform, tl, new Vector2(20f + col * 450f, -34f - row * 24f), new Vector2(440f, 24f));
                    ruleLines[i].fontSizeMax = 18;
                }
                UiKit.Place(capText.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -36f), new Vector2(300f, 60f));
                capText.fontSize = 20;
                capText.alignment = TextAlignmentOptions.TopRight;
                capText.textWrappingMode = TextWrappingModes.Normal;
                for (int i = 0; i < 3; i++) LayoutPanelLandscape(panels[i], i);
            }
            else
            {
                UiKit.Place((RectTransform)rulesPanel.transform, top, new Vector2(0f, -118f), new Vector2(690f, 300f));
                UiKit.Place(rulesLabel.rectTransform, tl, new Vector2(20f, -10f), new Vector2(400f, 32f));
                rulesLabel.fontSize = 24;
                for (int i = 0; i < MaxRuleLines; i++)
                {
                    UiKit.Place(ruleLines[i].rectTransform, tl, new Vector2(20f, -80f - i * 30f), new Vector2(650f, 30f));
                    ruleLines[i].fontSizeMax = 24;
                }
                // 縦: 上限は見出しの下の行に
                UiKit.Place(capText.rectTransform, tl, new Vector2(20f, -44f), new Vector2(650f, 32f));
                capText.fontSize = 24;
                capText.alignment = TextAlignmentOptions.TopLeft;
                capText.textWrappingMode = TextWrappingModes.NoWrap;
                for (int i = 0; i < 3; i++) LayoutPanelPortrait(panels[i], i);
            }
            if (_region != null) Refresh();
        }

        static bool HasCards(OpponentPanel p) => p.cards != null && p.cards.Length == MaxCards && p.cards[0] != null;

        void LayoutPanelLandscape(OpponentPanel p, int i)
        {
            var tl = new Vector2(0f, 1f);
            const float pw = 404f, ph = 470f;
            UiKit.Place((RectTransform)p.panel.transform, new Vector2(0.5f, 1f), new Vector2((i - 1) * (pw + 12f), -232f), new Vector2(pw, ph));
            UiKit.Place((RectTransform)p.portrait.transform, tl, tl, new Vector2(18f, -16f), new Vector2(104f, 104f));
            float tx = 138f, tw = pw - tx - 14f;
            Text(p.title, tl, tx, -14f, tw, 26f, 18f);
            Text(p.name, tl, tx, -40f, tw, 40f, 30f);
            Text(p.stars, tl, tx, -82f, tw, 28f, 22f);
            Text(p.record, tl, tx, -110f, tw, 24f, 18f);
            Text(p.cardsLabel, tl, 18f, -138f, pw - 36f, 24f, 17f);
            const float cw = 76f, ch = 95f, gap = 9f;
            float left = (pw - (4 * cw + 3 * gap)) * 0.5f;
            for (int k = 0; k < MaxCards && HasCards(p); k++)
            {
                int col = k % 4, row = k / 4;
                UiKit.Place((RectTransform)p.cards[k].transform, tl, tl, new Vector2(left + col * (cw + gap), -166f - row * (ch + gap)), new Vector2(cw, ch));
            }
            Text(p.note, tl, 18f, -370f, pw - 36f, 24f, 16f);
            UiKit.Place((RectTransform)p.challenge.transform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(220f, 52f));
            p.challenge.GetComponentInChildren<TextMeshProUGUI>().fontSize = 26;
        }

        void LayoutPanelPortrait(OpponentPanel p, int i)
        {
            var tl = new Vector2(0f, 1f);
            const float pw = 690f, ph = 268f;
            UiKit.Place((RectTransform)p.panel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -428f - i * (ph + 10f)), new Vector2(pw, ph));
            UiKit.Place((RectTransform)p.portrait.transform, tl, tl, new Vector2(16f, -14f), new Vector2(112f, 112f));
            float tx = 144f, tw = 330f;
            Text(p.title, tl, tx, -10f, tw, 30f, 22f);
            Text(p.name, tl, tx, -40f, tw, 44f, 34f);
            Text(p.stars, tl, tx, -86f, 170f, 32f, 26f);
            Text(p.record, tl, tx + 172f, -88f, 170f, 30f, 22f);
            p.cardsLabel.gameObject.SetActive(false);
            const float cw = 76f, ch = 95f, gap = 7f;
            float left = (pw - (8 * cw + 7 * gap)) * 0.5f;
            for (int k = 0; k < MaxCards && HasCards(p); k++)
                UiKit.Place((RectTransform)p.cards[k].transform, tl, tl, new Vector2(left + k * (cw + gap), -134f), new Vector2(cw, ch));
            Text(p.note, tl, 16f, -234f, pw - 32f, 30f, 21f);
            UiKit.Place((RectTransform)p.challenge.transform, new Vector2(1f, 1f), new Vector2(-16f, -24f), new Vector2(180f, 76f));
            p.challenge.GetComponentInChildren<TextMeshProUGUI>().fontSize = 30;
        }

        static void Text(TextMeshProUGUI t, Vector2 anchor, float x, float y, float w, float h, float size)
        {
            UiKit.Place(t.rectTransform, anchor, anchor, new Vector2(x, y), new Vector2(w, h));
            t.fontSizeMax = size;
            t.fontSize = size;
        }

        public void Show(CampaignState state, Region region, Opponent focus = null)
        {
            _state = state;
            _region = region;
            gameObject.SetActive(true);
            EnsureCards();
            Refresh();
            int f = focus != null ? Mathf.Max(0, IndexOf(region, focus)) : 0;
            EventSystem.current?.SetSelectedGameObject(panels[f].challenge.gameObject);
            AutomationLog.Buttons("Region", transform);
        }

        static int IndexOf(Region r, Opponent o)
        {
            for (int i = 0; i < r.Opponents.Count; i++)
            {
                if (r.Opponents[i] == o) return i;
            }
            return -1;
        }

        public void Hide() => gameObject.SetActive(false);

        void Refresh()
        {
            title.text = UiText.ProgressRegion + _region.Number + "  " + _region.Name;
            var rules = CampaignTexts.RuleLines(_region.Rules);
            _ruleCount = Mathf.Min(rules.Count, MaxRuleLines);
            for (int i = 0; i < MaxRuleLines; i++)
            {
                bool on = i < rules.Count;
                ruleLines[i].gameObject.SetActive(on);
                if (on) ruleLines[i].text = "<b><color=#F5D17A>" + rules[i].Key + "</color></b>  " + rules[i].Value;
            }
            capText.text = UiText.RegionCap + _region.DeckLevelCap + (_portrait ? "（" + UiText.RegionBossCap + "）" : "\n<size=16>" + UiText.RegionBossCap + "</size>");
            for (int i = 0; i < 3; i++)
            {
                var p = panels[i];
                bool has = i < _region.Opponents.Count;
                p.panel.gameObject.SetActive(has);
                if (!has) continue;
                var o = _region.Opponents[i];
                p.portrait.Show(o);
                p.title.text = o.Title + (o.IsBoss ? "  <color=#F5D17A>" + UiText.RegionBoss + "</color>" : "");
                p.name.text = o.Name;
                p.stars.text = CampaignTexts.StarText(CampaignTexts.Stars(o));
                p.record.text = CampaignTexts.Record(_state.Record(o.Id));
                p.note.gameObject.SetActive(o.IsBoss);
                p.panel.color = o.IsBoss ? Color.Lerp(Theme.Panel, new Color(0.25f, 0.20f, 0.08f, 0.95f), 0.5f) : Theme.Panel;
                var slots = CampaignTexts.OpponentCards(_state, o);
                for (int k = 0; k < MaxCards; k++)
                {
                    var mc = p.cards[k];
                    if (k >= slots.Count)
                    {
                        mc.gameObject.SetActive(false);
                        continue;
                    }
                    var s = slots[k];
                    if (!s.Seen) mc.ShowUnknown();
                    else
                    {
                        mc.Show(CardCatalog.ById[s.CardId]);
                        mc.SetBadge(0);
                        if (s.Captured) mc.SetMark(UiText.MarkCaptured, new Color(1f, 0.55f, 0.45f));
                        else mc.SetMark(s.Owned ? UiText.MarkOwned : null, Theme.P0);
                    }
                    mc.SetDim(false);
                }
            }
        }
    }
}
