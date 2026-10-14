// ルールエンジンの基本型と定数（DESIGN.md §4-1）。
// Core は UnityEngine に依存しない。System.Random・DateTime・Stopwatch・浮動小数点も使わない。

namespace Edgewise.Core
{
    /// <summary>プレイヤー。数値は配列の添字としても使う。</summary>
    public enum Player
    {
        P0 = 0,
        P1 = 1,
    }

    /// <summary>属性。並び順は乱数でマス属性を選ぶときの添字なので変更しないこと。</summary>
    public enum Element
    {
        Fire = 0,
        Ice = 1,
        Thunder = 2,
        Earth = 3,
        Water = 4,
        Wind = 5,
        Holy = 6,
        Dark = 7,
    }

    /// <summary>カードの系統（Resonance 用）。</summary>
    public enum Tribe
    {
        Beast = 0,
        Machine = 1,
        Spirit = 2,
        Dragon = 3,
    }

    /// <summary>Trait の ID（Phase 4 で確定。現状は型の置き場のみ）。</summary>
    public enum TraitId
    {
        Guard = 0,
        Pierce = 1,
        Echo = 2,
    }

    public enum Phase
    {
        Playing = 0,
        Ended = 1,
    }

    public enum GameResult
    {
        P0 = 0,
        P1 = 1,
        Draw = 2,
    }

    public enum FlipCause
    {
        Basic = 0,
        Same = 1,
        Plus = 2,
        Combo = 3,
    }

    /// <summary>rule イベントの見出し。</summary>
    public enum RuleHeading
    {
        Same = 0,
        Plus = 1,
        SameWall = 2,
    }

    /// <summary>エンジン全体の定数。</summary>
    public static class GameConstants
    {
        public const int BoardSize = 9;
        public const int BoardWidth = 3;
        public const int HandSize = 5;
        public const int PivotTokens = 2;
        public const int MaxSuddenDeathRounds = 5;

        /// <summary>Same Wall で壁を表すランク（A）。</summary>
        public const int WallRank = 10;

        /// <summary>Elemental: 各マスに属性が付く確率 = Numerator / Denominator（浮動小数点を使わないため分数で持つ）。</summary>
        public const int ElementalCellProbabilityNumerator = 1;
        public const int ElementalCellProbabilityDenominator = 4;

        /// <summary>方向。走査順は常にこの順（上→右→下→左）。</summary>
        public const int Top = 0;
        public const int Right = 1;
        public const int Bottom = 2;
        public const int Left = 3;

        /// <summary>盤外（壁）を表すマス番号。</summary>
        public const int Wall = -1;

        public const int ElementCount = 8;
        public const int TribeCount = 4;
    }

    /// <summary>enum ⇔ TS 版と同じ文字列 ID の変換（hashState の正規化文字列・JSON で使う）。</summary>
    public static class Ids
    {
        static readonly string[] ElementIds = { "fire", "ice", "thunder", "earth", "water", "wind", "holy", "dark" };
        static readonly string[] TribeIds = { "beast", "machine", "spirit", "dragon" };
        static readonly string[] TraitIds = { "guard", "pierce", "echo" };
        static readonly string[] CauseIds = { "basic", "same", "plus", "combo" };
        static readonly string[] HeadingIds = { "same", "plus", "sameWall" };
        static readonly string[] ResultIds = { "p0", "p1", "draw" };
        static readonly string[] PhaseIds = { "playing", "ended" };

        public static string ToId(this Element e) => ElementIds[(int)e];
        public static string ToId(this Tribe t) => TribeIds[(int)t];
        public static string ToId(this TraitId t) => TraitIds[(int)t];
        public static string ToId(this FlipCause c) => CauseIds[(int)c];
        public static string ToId(this RuleHeading r) => HeadingIds[(int)r];
        public static string ToId(this GameResult r) => ResultIds[(int)r];
        public static string ToId(this Phase p) => PhaseIds[(int)p];

        public static Player Opponent(this Player p) => p == Player.P0 ? Player.P1 : Player.P0;

        public static bool TryParseElement(string id, out Element element)
        {
            for (int i = 0; i < ElementIds.Length; i++)
            {
                if (ElementIds[i] == id)
                {
                    element = (Element)i;
                    return true;
                }
            }
            element = default;
            return false;
        }

        public static bool TryParseTribe(string id, out Tribe tribe)
        {
            for (int i = 0; i < TribeIds.Length; i++)
            {
                if (TribeIds[i] == id)
                {
                    tribe = (Tribe)i;
                    return true;
                }
            }
            tribe = default;
            return false;
        }
    }
}
