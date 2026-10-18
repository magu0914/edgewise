using System;
using System.Text;
using Edgewise.Core;
using Edgewise.Game.Juice;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>対戦中の右上メニュー: ルール確認・設定（速度・音量・揺れ）・ミュート・降参。</summary>
    public sealed class MenuPanel : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] Image panel;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] Button rulesButton;
        [SerializeField] Button speedButton;
        [SerializeField] Button muteButton;
        [SerializeField] Button resignButton;
        [SerializeField] Button closeButton;
        [SerializeField] Image rulesPanel;
        [SerializeField] TextMeshProUGUI rulesText;

        RuleSet _rules;

        public event Action ResignPressed;
        public event Action SettingsPressed;
        public event Action Closed;

        public bool IsOpen => gameObject.activeSelf;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", Theme.Dim);
            panel = UiKit.Panel(root, "Panel", a, Theme.Panel);
            title = UiKit.Label(panel.transform, "Title", a, UiText.MenuTitle, 32, Theme.Gold);
            rulesButton = UiKit.Button(panel.transform, "RulesButton", a, UiText.MenuRules, 24, out _);
            speedButton = UiKit.Button(panel.transform, "SettingsButton", a, UiText.MenuSettings, 24, out _);
            muteButton = UiKit.Button(panel.transform, "MuteButton", a, UiText.MenuMute, 24, out _);
            resignButton = UiKit.Button(panel.transform, "ResignButton", a, UiText.MenuResign, 24, out _, new Color(0.45f, 0.18f, 0.18f));
            closeButton = UiKit.Button(panel.transform, "CloseButton", a, UiText.MenuClose, 24, out _, new Color(0.25f, 0.42f, 0.40f));
            rulesPanel = UiKit.Panel(root, "RulesPanel", a, new Color(0.05f, 0.06f, 0.13f, 0.97f));
            rulesText = UiKit.Label(rulesPanel.transform, "Text", a, "", 20, Theme.Ink, TextAlignmentOptions.TopLeft);
            UiKit.Stretch(rulesText.rectTransform, 24, 20, 24, 20);
            // 全ルール ON（共鳴・Trait の説明つき）でも収まるように、長いときは文字を縮める
            rulesText.enableAutoSizing = true;
            rulesText.fontSizeMin = 12;
            rulesText.fontSizeMax = 20;
            rulesPanel.gameObject.SetActive(false);
            ApplyLayout(false);
        }

        void Awake()
        {
            rulesButton.onClick.AddListener(() => rulesPanel.gameObject.SetActive(!rulesPanel.gameObject.activeSelf));
            speedButton.onClick.AddListener(() => SettingsPressed?.Invoke());
            muteButton.onClick.AddListener(() =>
            {
                UserPrefs.Mute = !UserPrefs.Mute;
                Refresh();
            });
            resignButton.onClick.AddListener(() =>
            {
                Close();
                ResignPressed?.Invoke();
            });
            closeButton.onClick.AddListener(Close);
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            if (portrait)
            {
                UiKit.Place((RectTransform)panel.transform, c, new Vector2(0f, 250f), new Vector2(560f, 560f));
                UiKit.Place((RectTransform)rulesPanel.transform, c, new Vector2(0f, -330f), new Vector2(660f, 520f));
            }
            else
            {
                UiKit.Place((RectTransform)panel.transform, c, new Vector2(-250f, 0f), new Vector2(420f, 560f));
                UiKit.Place((RectTransform)rulesPanel.transform, c, new Vector2(250f, 0f), new Vector2(560f, 560f));
            }
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(360f, 50f));
            Button[] buttons = { rulesButton, speedButton, muteButton, resignButton, closeButton };
            for (int i = 0; i < buttons.Length; i++)
                UiKit.Place((RectTransform)buttons[i].transform, new Vector2(0.5f, 1f), new Vector2(0f, -90f - i * 88f), new Vector2(340f, 70f));
        }

        public void Open(RuleSet rules)
        {
            _rules = rules;
            gameObject.SetActive(true);
            rulesPanel.gameObject.SetActive(false);
            Refresh();
            EventSystem.current?.SetSelectedGameObject(closeButton.gameObject);
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            Closed?.Invoke();
        }

        void Refresh()
        {

            SetLabel(muteButton, UiText.MenuMute + ": " + (UserPrefs.Mute ? UiText.On : UiText.Off));
            rulesText.text = DescribeRules(_rules);
        }

        static void SetLabel(Button b, string text) => b.GetComponentInChildren<TextMeshProUGUI>().text = text;

        public static string DescribeRules(RuleSet r)
        {
            var sb = new StringBuilder();
            sb.Append("<color=#F5D17A>").Append(UiText.RulesInUse).Append("</color>\n");
            sb.Append("<size=85%>").Append(UiText.BasicRule).Append("</size>\n\n");
            if (r == null) return sb.ToString();
            bool any = false;
            void Line(bool on, string name, string desc)
            {
                if (!on) return;
                any = true;
                sb.Append("<b>").Append(name).Append("</b>  <size=85%>").Append(desc).Append("</size>\n");
            }
            Line(r.Open, UiText.RuleOpen, UiText.RuleOpenDesc);
            Line(r.Random, UiText.RuleRandom, UiText.RuleRandomDesc);
            Line(r.Same, UiText.RuleSame, UiText.RuleSameDesc);
            Line(r.SameWall, UiText.RuleSameWall, UiText.RuleSameWallDesc);
            Line(r.Plus, UiText.RulePlus, UiText.RulePlusDesc);
            Line(r.Combo, UiText.RuleCombo, UiText.RuleComboDesc);
            Line(r.Elemental, UiText.RuleElemental, UiText.RuleElementalDesc);
            Line(r.SuddenDeath, UiText.RuleSuddenDeath, UiText.RuleSuddenDeathDesc);
            Line(r.Pivot, UiText.RulePivot, UiText.PivotDesc(r));
            Line(r.Resonance, UiText.RuleResonance, UiText.RuleResonanceDesc);
            Line(r.Traits, UiText.RuleTraits, UiText.RuleTraitsDesc);
            if (!any) sb.Append(UiText.NoRules);
            if (r.Resonance) sb.Append("\n<size=80%><color=#F5D17A>").Append(UiText.ResonanceHeading).Append("</color>  ").Append(UiText.ResonanceNote).Append("</size>\n");
            if (r.Traits)
            {
                // 4 つの固有能力（右下の漢字のバッジ）
                sb.Append("\n<color=#F5D17A>").Append(UiText.TraitHeading).Append("</color>\n");
                foreach (TraitId t in System.Enum.GetValues(typeof(TraitId)))
                    sb.Append("<b>").Append(Theme.TraitGlyph(t)).Append(' ').Append(UiText.TraitName(t)).Append("</b>  <size=80%>").Append(UiText.TraitDesc(t)).Append("</size>\n");
            }
            return sb.ToString();
        }
    }
}
