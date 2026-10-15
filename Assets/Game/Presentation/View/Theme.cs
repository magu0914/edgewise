using Edgewise.Core;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// 色と記号（DESIGN §8-1 見た目の方針）。所有者は色だけでなく枠の模様とマークでも区別する:
    /// プレイヤー＝青緑系＋実線の枠＋◆、CPU＝赤橙系＋斜線模様の枠＋●。
    /// </summary>
    public static class Theme
    {
        public static readonly Color P0 = new Color(0.22f, 0.82f, 0.78f);
        public static readonly Color P1 = new Color(0.98f, 0.46f, 0.24f);
        public static readonly Color Gold = new Color(0.96f, 0.82f, 0.48f);
        public static readonly Color Ink = new Color(0.92f, 0.94f, 1f);
        public static readonly Color InkDim = new Color(0.66f, 0.70f, 0.82f);
        public static readonly Color Night = new Color(0.035f, 0.045f, 0.10f);
        public static readonly Color Panel = new Color(0.07f, 0.09f, 0.18f, 0.94f);
        public static readonly Color PanelLine = new Color(0.36f, 0.42f, 0.66f, 1f);
        public static readonly Color Button = new Color(0.16f, 0.22f, 0.40f, 1f);
        public static readonly Color ButtonOn = new Color(0.86f, 0.70f, 0.32f, 1f);
        public static readonly Color ButtonText = new Color(0.94f, 0.95f, 1f);
        public static readonly Color Dim = new Color(0f, 0f, 0.02f, 0.62f);

        /// <summary>予告で「取れる」カードの光（所有者の色とは別の、金白色）。</summary>
        public static readonly Color CaptureGlow = new Color(1f, 0.88f, 0.55f);

        /// <summary>ヒントの光（水色がかった白）。</summary>
        public static readonly Color HintGlow = new Color(0.6f, 0.95f, 1f);

        /// <summary>プレイヤーのマーク（◆）と CPU のマーク（●）。</summary>
        public const string MarkP0 = "◆";
        public const string MarkP1 = "●";

        public static Color OwnerColor(Player p) => p == Player.P0 ? P0 : P1;

        public static string Mark(Player p) => p == Player.P0 ? MarkP0 : MarkP1;

        /// <summary>枠の模様（0 = 実線、1 = 斜線）。</summary>
        public static float Pattern(Player p) => p == Player.P0 ? 0f : 1f;

        public static Color ElementColor(Element? e)
        {
            if (!e.HasValue) return new Color(0.80f, 0.82f, 0.92f);
            switch (e.Value)
            {
                case Element.Fire: return new Color(1f, 0.46f, 0.20f);
                case Element.Ice: return new Color(0.56f, 0.86f, 1f);
                case Element.Thunder: return new Color(1f, 0.88f, 0.28f);
                case Element.Earth: return new Color(0.80f, 0.60f, 0.34f);
                case Element.Water: return new Color(0.28f, 0.58f, 1f);
                case Element.Wind: return new Color(0.46f, 0.95f, 0.62f);
                case Element.Holy: return new Color(1f, 0.95f, 0.78f);
                case Element.Dark: return new Color(0.72f, 0.42f, 0.98f);
                default: return Color.white;
            }
        }

        /// <summary>属性アイコンの文字。</summary>
        public static string ElementGlyph(Element e)
        {
            switch (e)
            {
                case Element.Fire: return "火";
                case Element.Ice: return "氷";
                case Element.Thunder: return "雷";
                case Element.Earth: return "土";
                case Element.Water: return "水";
                case Element.Wind: return "風";
                case Element.Holy: return "聖";
                case Element.Dark: return "闇";
                default: return "";
            }
        }

        /// <summary>系統アイコンの文字。</summary>
        public static string TribeGlyph(Tribe t)
        {
            switch (t)
            {
                case Tribe.Beast: return "獣";
                case Tribe.Machine: return "機";
                case Tribe.Spirit: return "霊";
                case Tribe.Dragon: return "竜";
                default: return "";
            }
        }

        /// <summary>ランクの表記（10 = A）。補正後は 0 や 11 もありうる。</summary>
        public static string Rank(int v) => v == 10 ? "A" : v.ToString();
    }
}
