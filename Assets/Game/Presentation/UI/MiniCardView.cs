using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 小さなカード（uGUI）。星域の画面の「持っているカード」とデッキ編集の一覧・枠に使う。
    /// 盤のカード（CardView）と同じ並び（左上に上・右・下・左のランクの菱形）と属性の色で、数字が読めることを優先する。
    /// 未見のカードは「？」、手に入れたカードには ◆、相手に取られたカードには「取」の印。枚数のバッジ（×2）と、薄く表示（入らない）もできる。
    /// </summary>
    public sealed class MiniCardView : MonoBehaviour
    {
        [SerializeField] Image border;
        [SerializeField] Image fill;
        [SerializeField] TextMeshProUGUI[] ranks = new TextMeshProUGUI[4];
        [SerializeField] TextMeshProUGUI level;
        [SerializeField] TextMeshProUGUI nameText;
        [SerializeField] TextMeshProUGUI element;
        [SerializeField] TextMeshProUGUI tribe;
        [SerializeField] TextMeshProUGUI unknown;
        [SerializeField] TextMeshProUGUI mark;
        [SerializeField] Image badge;
        [SerializeField] TextMeshProUGUI badgeText;
        [SerializeField] CanvasGroup group;
        [SerializeField] Button button;

        public string CardId { get; private set; }
        public Button Button => button;
        public bool Dimmed { get; private set; }
        public string BadgeText => badge != null && badge.gameObject.activeSelf ? badgeText.text : "";
        public string MarkText => mark != null && mark.gameObject.activeSelf ? mark.text : "";
        public bool IsUnknown => unknown != null && unknown.gameObject.activeSelf;

        public static MiniCardView Create(Transform parent, string name, GameAssets a, bool clickable)
        {
            var root = UiKit.Rect(parent, name);
            var v = root.gameObject.AddComponent<MiniCardView>();
            v.group = root.gameObject.AddComponent<CanvasGroup>();
            v.border = root.gameObject.AddComponent<Image>();
            v.border.sprite = a.roundRect;
            v.border.type = Image.Type.Sliced;
            v.border.color = Theme.PanelLine;
            v.border.raycastTarget = clickable;
            if (clickable)
            {
                v.button = root.gameObject.AddComponent<Button>();
                v.button.targetGraphic = v.border;
                var colors = v.button.colors;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.3f, 1f);
                colors.selectedColor = new Color(1.35f, 1.3f, 1.05f, 1f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
                colors.colorMultiplier = 1.4f;
                colors.fadeDuration = 0.06f;
                v.button.colors = colors;
            }
            v.fill = UiKit.Panel(root, "Fill", a, new Color(0.08f, 0.10f, 0.20f), false);
            Anchor(v.fill.rectTransform, 0f, 0f, 1f, 1f, 3f);

            // ランクの菱形（上・右・下・左）。カードの上 6 割に置く
            Vector2 c = new Vector2(0.36f, 0.68f);
            const float dx = 0.17f, dy = 0.13f, hw = 0.15f, hh = 0.13f;
            Vector2[] at = { c + new Vector2(0f, dy), c + new Vector2(dx, 0f), c + new Vector2(0f, -dy), c + new Vector2(-dx, 0f) };
            string[] names = { "RankTop", "RankRight", "RankBottom", "RankLeft" };
            v.ranks = new TextMeshProUGUI[4];
            for (int i = 0; i < 4; i++)
            {
                var t = UiKit.Label(root, names[i], a, "", 20, Color.white, TextAlignmentOptions.Center, true);
                Anchor(t.rectTransform, at[i].x - hw, at[i].y - hh, at[i].x + hw, at[i].y + hh, 0f);
                AutoSize(t, 6, 40);
                t.fontStyle = FontStyles.Bold;
                v.ranks[i] = t;
            }
            v.element = UiKit.Label(root, "Element", a, "", 16, Theme.Ink);
            Anchor(v.element.rectTransform, 0.70f, 0.74f, 0.96f, 0.96f, 0f);
            AutoSize(v.element, 5, 30);
            v.tribe = UiKit.Label(root, "Tribe", a, "", 14, Theme.InkDim);
            Anchor(v.tribe.rectTransform, 0.70f, 0.52f, 0.96f, 0.72f, 0f);
            AutoSize(v.tribe, 5, 26);
            v.nameText = UiKit.Label(root, "Name", a, "", 14, Theme.Ink);
            Anchor(v.nameText.rectTransform, 0.06f, 0.22f, 0.94f, 0.38f, 0f);
            AutoSize(v.nameText, 5, 22);
            v.nameText.textWrappingMode = TextWrappingModes.NoWrap;
            v.level = UiKit.Label(root, "Level", a, "", 16, Theme.Gold);
            Anchor(v.level.rectTransform, 0.10f, 0.04f, 0.90f, 0.22f, 0f);
            AutoSize(v.level, 5, 28);
            v.unknown = UiKit.Label(root, "Unknown", a, "？", 40, Theme.InkDim);
            Anchor(v.unknown.rectTransform, 0.1f, 0.15f, 0.9f, 0.85f, 0f);
            AutoSize(v.unknown, 8, 80);
            v.mark = UiKit.Label(root, "Mark", a, "", 16, Theme.P0, TextAlignmentOptions.Center, true);
            Anchor(v.mark.rectTransform, -0.06f, -0.06f, 0.26f, 0.20f, 0f);
            AutoSize(v.mark, 5, 30);
            v.badge = UiKit.Panel(root, "Badge", a, new Color(0.02f, 0.03f, 0.08f, 0.92f), false);
            Anchor(v.badge.rectTransform, 0.56f, -0.06f, 1.06f, 0.16f, 0f);
            v.badgeText = UiKit.Label(v.badge.transform, "Text", a, "", 14, Theme.Gold);
            UiKit.Stretch(v.badgeText.rectTransform);
            AutoSize(v.badgeText, 5, 26);
            v.badge.gameObject.SetActive(false);
            v.mark.gameObject.SetActive(false);
            v.unknown.gameObject.SetActive(false);
            return v;
        }

        static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1, float inset)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        static void AutoSize(TextMeshProUGUI t, float min, float max)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.margin = Vector4.zero;
        }

        /// <summary>表のカードを出す。</summary>
        public void Show(CardDef def, bool showName = false)
        {
            CardId = def?.Id;
            gameObject.SetActive(true);
            unknown.gameObject.SetActive(def == null);
            bool face = def != null;
            foreach (var r in ranks) r.gameObject.SetActive(face);
            level.gameObject.SetActive(face);
            nameText.gameObject.SetActive(face && showName);
            element.gameObject.SetActive(face && def.Element.HasValue);
            tribe.gameObject.SetActive(face && def.Tribe.HasValue);
            if (!face)
            {
                fill.color = new Color(0.05f, 0.06f, 0.12f);
                border.color = new Color(0.25f, 0.28f, 0.42f);
                return;
            }
            for (int d = 0; d < 4; d++) ranks[d].text = Theme.Rank(def.Sides[d]);
            level.text = UiText.DexLevel + def.Level;
            nameText.text = def.Name;
            var ec = Theme.ElementColor(def.Element);
            fill.color = Color.Lerp(new Color(0.07f, 0.08f, 0.17f), ec, def.Element.HasValue ? 0.30f : 0.12f);
            border.color = def.Level >= 7 ? Theme.Gold : def.Level >= 4 ? Color.Lerp(Theme.PanelLine, Theme.Gold, 0.55f) : Theme.PanelLine;
            if (def.Element.HasValue)
            {
                element.text = Theme.ElementGlyph(def.Element.Value);
                element.color = Color.Lerp(ec, Color.white, 0.45f);
            }
            if (def.Tribe.HasValue) tribe.text = Theme.TribeGlyph(def.Tribe.Value);
        }

        /// <summary>まだ見ていないカード（「？」）。</summary>
        public void ShowUnknown()
        {
            Show(null);
            SetBadge(0);
            SetMark(null, Color.white);
        }

        /// <summary>空の枠（デッキの空き）。</summary>
        public void ShowEmpty()
        {
            CardId = null;
            gameObject.SetActive(true);
            foreach (var r in ranks) r.gameObject.SetActive(false);
            level.gameObject.SetActive(false);
            nameText.gameObject.SetActive(false);
            element.gameObject.SetActive(false);
            tribe.gameObject.SetActive(false);
            unknown.gameObject.SetActive(false);
            fill.color = new Color(0.03f, 0.04f, 0.09f, 0.85f);
            border.color = new Color(0.25f, 0.28f, 0.42f, 0.8f);
            SetBadge(0);
            SetMark(null, Color.white);
            SetDim(false);
        }

        /// <summary>枚数のバッジ（2 枚以上のときだけ「×2」）。</summary>
        public void SetBadge(int copies)
        {
            bool show = copies >= 2;
            badge.gameObject.SetActive(show);
            if (show) badgeText.text = UiText.DeckCount + copies;
        }

        public void SetMark(string text, Color color)
        {
            bool show = !string.IsNullOrEmpty(text);
            mark.gameObject.SetActive(show);
            if (!show) return;
            mark.text = text;
            mark.color = color;
        }

        /// <summary>薄く表示する（残りの予算で入らないカード）。押せることは変えない。</summary>
        public void SetDim(bool dim)
        {
            Dimmed = dim;
            group.alpha = dim ? 0.35f : 1f;
        }

        /// <summary>選ばれている（デッキに入っている）印として枠を光らせる。</summary>
        public void SetSelected(bool on)
        {
            if (on) border.color = Theme.P0;
        }
    }
}
