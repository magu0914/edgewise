using System;
using Edgewise.Game.Match;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 対戦設定: CPU の強さ（Easy / Normal / Hard）、カードの強さ帯、ルールの ON/OFF（短い説明つき）。
    /// 前回の設定は MatchSettings が PlayerPrefs に覚える。
    /// </summary>
    public sealed class SetupScreen : MonoBehaviour, IResponsiveUi
    {
        public const int RuleCount = 9;

        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI difficultyLabel;
        [SerializeField] TextMeshProUGUI bandLabel;
        [SerializeField] TextMeshProUGUI rulesLabel;
        [SerializeField] Button[] difficultyButtons = new Button[3];
        [SerializeField] Button[] bandButtons = new Button[3];
        [SerializeField] Button[] ruleButtons = new Button[RuleCount];
        [SerializeField] TextMeshProUGUI[] ruleNames = new TextMeshProUGUI[RuleCount];
        [SerializeField] TextMeshProUGUI[] ruleDescs = new TextMeshProUGUI[RuleCount];
        [SerializeField] TextMeshProUGUI[] ruleStates = new TextMeshProUGUI[RuleCount];
        [SerializeField] Button beginButton;
        [SerializeField] Button backButton;

        MatchSettings _settings;

        public event Action<MatchSettings> BeginPressed;
        public event Action BackPressed;

        public Button BeginButton => beginButton;
        public Button RuleButton(int i) => ruleButtons[i];
        public Button DifficultyButton(int i) => difficultyButtons[i];
        public Button BandButton(int i) => bandButtons[i];

        static readonly string[] RuleNames =
        {
            UiText.RuleOpen, UiText.RuleRandom, UiText.RuleSame, UiText.RuleSameWall, UiText.RulePlus, UiText.RuleCombo,
            UiText.RuleElemental, UiText.RuleSuddenDeath, UiText.RulePivot,
        };

        static readonly string[] RuleDescs =
        {
            UiText.RuleOpenDesc, UiText.RuleRandomDesc, UiText.RuleSameDesc, UiText.RuleSameWallDesc, UiText.RulePlusDesc,
            UiText.RuleComboDesc, UiText.RuleElementalDesc, UiText.RuleSuddenDeathDesc, UiText.RulePivotDesc,
        };

        /// <summary>ルール i の名前（ログ・テスト用）。</summary>
        public static string RuleName(int i) => RuleNames[i];

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", new Color(0f, 0f, 0.03f, 0.45f), false);
            title = UiKit.Label(root, "Title", a, UiText.SetupTitle, 40, Theme.Gold, TextAlignmentOptions.Center, true);
            difficultyLabel = UiKit.Label(root, "DifficultyLabel", a, UiText.DifficultyLabel, 24, Theme.InkDim, TextAlignmentOptions.Left);
            bandLabel = UiKit.Label(root, "BandLabel", a, UiText.BandLabel, 24, Theme.InkDim, TextAlignmentOptions.Left);
            rulesLabel = UiKit.Label(root, "RulesLabel", a, UiText.RulesLabel, 24, Theme.InkDim, TextAlignmentOptions.Left);
            string[] diff = { UiText.Easy, UiText.Normal, UiText.Hard };
            string[] band = { UiText.BandBeginner, UiText.BandIntermediate, UiText.BandAdvanced };
            difficultyButtons = new Button[3];
            bandButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                difficultyButtons[i] = UiKit.Button(root, "Difficulty" + i, a, diff[i], 24, out _);
                bandButtons[i] = UiKit.Button(root, "Band" + i, a, band[i], 21, out _);
            }
            ruleButtons = new Button[RuleCount];
            ruleNames = new TextMeshProUGUI[RuleCount];
            ruleDescs = new TextMeshProUGUI[RuleCount];
            ruleStates = new TextMeshProUGUI[RuleCount];
            for (int i = 0; i < RuleCount; i++)
            {
                var b = UiKit.Button(root, "Rule_" + RuleNames[i].Replace(" ", ""), a, "", 20, out var label);
                DestroyImmediate(label.gameObject);
                ruleButtons[i] = b;
                ruleNames[i] = UiKit.Label(b.transform, "Name", a, RuleNames[i], 22, Theme.Ink, TextAlignmentOptions.Left);
                ruleNames[i].fontStyle = FontStyles.Bold;
                ruleNames[i].textWrappingMode = TextWrappingModes.NoWrap;
                ruleNames[i].enableAutoSizing = true;
                ruleNames[i].fontSizeMin = 14;
                ruleNames[i].fontSizeMax = 22;
                ruleDescs[i] = UiKit.Label(b.transform, "Desc", a, RuleDescs[i], 16, Theme.InkDim, TextAlignmentOptions.Left);
                ruleDescs[i].textWrappingMode = TextWrappingModes.NoWrap;
                ruleDescs[i].enableAutoSizing = true;
                ruleDescs[i].fontSizeMin = 11;
                ruleDescs[i].fontSizeMax = 16;
                ruleStates[i] = UiKit.Label(b.transform, "State", a, UiText.Off, 20, Theme.InkDim, TextAlignmentOptions.Center);
                ruleStates[i].fontStyle = FontStyles.Bold;
            }
            beginButton = UiKit.Button(root, "BeginButton", a, UiText.BeginMatch, 30, out _, new Color(0.25f, 0.42f, 0.40f));
            backButton = UiKit.Button(root, "BackButton", a, UiText.Back, 24, out _);
            ApplyLayout(false);
        }

        void Awake()
        {
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                difficultyButtons[i].onClick.AddListener(() =>
                {
                    _settings.difficulty = (Difficulty)k;
                    Refresh();
                });
                bandButtons[i].onClick.AddListener(() =>
                {
                    _settings.band = (CardBand)k;
                    Refresh();
                });
            }
            for (int i = 0; i < RuleCount; i++)
            {
                int k = i;
                ruleButtons[i].onClick.AddListener(() =>
                {
                    SetRule(k, !GetRule(k));
                    Refresh();
                });
            }
            beginButton.onClick.AddListener(() =>
            {
                _settings.Save();
                BeginPressed?.Invoke(_settings.Clone());
            });
            backButton.onClick.AddListener(() => BackPressed?.Invoke());
        }

        bool GetRule(int i)
        {
            switch (i)
            {
                case 0: return _settings.open;
                case 1: return _settings.random;
                case 2: return _settings.same;
                case 3: return _settings.sameWall;
                case 4: return _settings.plus;
                case 5: return _settings.combo;
                case 6: return _settings.elemental;
                case 7: return _settings.suddenDeath;
                default: return _settings.pivot;
            }
        }

        void SetRule(int i, bool v)
        {
            switch (i)
            {
                case 0: _settings.open = v; break;
                case 1: _settings.random = v; break;
                case 2: _settings.same = v; break;
                case 3: _settings.sameWall = v; break;
                case 4: _settings.plus = v; break;
                case 5: _settings.combo = v; break;
                case 6: _settings.elemental = v; break;
                case 7: _settings.suddenDeath = v; break;
                default: _settings.pivot = v; break;
            }
        }

        public void ApplyLayout(bool portrait)
        {
            var top = new Vector2(0.5f, 1f);
            if (!portrait)
            {
                UiKit.Place(title.rectTransform, top, new Vector2(0f, -22f), new Vector2(600f, 56f));
                float lx = -330f;
                UiKit.Place(difficultyLabel.rectTransform, top, new Vector2(lx, -100f), new Vector2(500f, 34f));
                for (int i = 0; i < 3; i++)
                    UiKit.Place((RectTransform)difficultyButtons[i].transform, top, new Vector2(lx - 170f + i * 170f, -140f), new Vector2(158f, 58f));
                UiKit.Place(bandLabel.rectTransform, top, new Vector2(lx, -230f), new Vector2(500f, 34f));
                for (int i = 0; i < 3; i++)
                    UiKit.Place((RectTransform)bandButtons[i].transform, top, new Vector2(lx - 170f + i * 170f, -270f), new Vector2(162f, 58f));
                UiKit.Place((RectTransform)beginButton.transform, top, new Vector2(lx + 80f, -560f), new Vector2(300f, 80f));
                UiKit.Place((RectTransform)backButton.transform, top, new Vector2(lx - 170f, -570f), new Vector2(160f, 60f));
                float rx = 310f;
                UiKit.Place(rulesLabel.rectTransform, top, new Vector2(rx, -100f), new Vector2(640f, 34f));
                for (int i = 0; i < RuleCount; i++) LayoutRule(i, top, new Vector2(rx, -138f - i * 60f), new Vector2(640f, 54f));
            }
            else
            {
                // 縦画面（最小 360×640 では 0.5 倍で表示される）: 説明を 2 行目に回して文字を大きくする
                UiKit.Place(title.rectTransform, top, new Vector2(0f, -22f), new Vector2(600f, 56f));
                UiKit.Place(difficultyLabel.rectTransform, top, new Vector2(0f, -84f), new Vector2(660f, 34f));
                for (int i = 0; i < 3; i++)
                    UiKit.Place((RectTransform)difficultyButtons[i].transform, top, new Vector2(-220f + i * 220f, -118f), new Vector2(206f, 62f));
                UiKit.Place(bandLabel.rectTransform, top, new Vector2(0f, -194f), new Vector2(660f, 34f));
                for (int i = 0; i < 3; i++)
                    UiKit.Place((RectTransform)bandButtons[i].transform, top, new Vector2(-220f + i * 220f, -228f), new Vector2(206f, 62f));
                UiKit.Place(rulesLabel.rectTransform, top, new Vector2(0f, -304f), new Vector2(660f, 34f));
                for (int i = 0; i < RuleCount; i++) LayoutRule(i, top, new Vector2(0f, -340f - i * 78f), new Vector2(680f, 72f), true);
                UiKit.Place((RectTransform)backButton.transform, top, new Vector2(-210f, -1062f), new Vector2(220f, 80f));
                UiKit.Place((RectTransform)beginButton.transform, top, new Vector2(110f, -1056f), new Vector2(380f, 92f));
            }
        }

        void LayoutRule(int i, Vector2 anchor, Vector2 pos, Vector2 size, bool twoLines = false)
        {
            UiKit.Place((RectTransform)ruleButtons[i].transform, anchor, pos, size);
            var left = new Vector2(0f, 0.5f);
            if (twoLines)
            {
                var tl = new Vector2(0f, 1f);
                ruleNames[i].fontSizeMax = 26;
                ruleDescs[i].fontSizeMax = 21;
                ruleDescs[i].fontSizeMin = 15;
                UiKit.Place(ruleNames[i].rectTransform, tl, tl, new Vector2(18f, -2f), new Vector2(size.x - 120f, 36f));
                UiKit.Place(ruleDescs[i].rectTransform, tl, tl, new Vector2(18f, -36f), new Vector2(size.x - 120f, 32f));
            }
            else
            {
                ruleNames[i].fontSizeMax = 22;
                ruleDescs[i].fontSizeMax = 16;
                ruleDescs[i].fontSizeMin = 11;
                // 名前の列は一番長い「Sudden Death」が入る幅にし、説明はその右から（重ならない）
                UiKit.Place(ruleNames[i].rectTransform, left, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(196f, size.y));
                UiKit.Place(ruleDescs[i].rectTransform, left, new Vector2(0f, 0.5f), new Vector2(222f, 0f), new Vector2(size.x - 222f - 90f, size.y));
            }
            ruleStates[i].fontSize = twoLines ? 26 : 20;
            UiKit.Place(ruleStates[i].rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(80f, size.y - 14f));
        }

        public void Show(MatchSettings settings)
        {
            _settings = settings;
            gameObject.SetActive(true);
            Refresh();
            EventSystem.current?.SetSelectedGameObject(beginButton.gameObject);
        }

        public void Hide() => gameObject.SetActive(false);

        public MatchSettings Current => _settings;

        void Refresh()
        {
            for (int i = 0; i < 3; i++)
            {
                Mark(difficultyButtons[i], (int)_settings.difficulty == i);
                Mark(bandButtons[i], (int)_settings.band == i);
            }
            for (int i = 0; i < RuleCount; i++)
            {
                bool on = GetRule(i);
                bool enabled = i != 3 || _settings.same; // Same Wall は Same が ON のときだけ有効
                var img = (Image)ruleButtons[i].targetGraphic;
                img.color = on && enabled ? new Color(0.20f, 0.30f, 0.50f) : new Color(0.10f, 0.13f, 0.24f);
                ruleStates[i].text = on ? UiText.On : UiText.Off;
                ruleStates[i].color = on && enabled ? Theme.Gold : Theme.InkDim;
                ruleNames[i].color = enabled ? Theme.Ink : Theme.InkDim * 0.8f;
                ruleDescs[i].alpha = enabled ? 1f : 0.5f;
            }
            // Pivot のトークン数は Sudden Death の ON/OFF で変わる（先攻 1・後攻 2 / 1・1）
            ruleDescs[RuleCount - 1].text = UiText.PivotDesc(_settings.suddenDeath);
        }

        static void Mark(Button b, bool on)
        {
            ((Image)b.targetGraphic).color = on ? Theme.ButtonOn : Theme.Button;
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = on ? new Color(0.08f, 0.06f, 0.02f) : Theme.ButtonText;
        }
    }
}
