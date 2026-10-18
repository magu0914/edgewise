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

    /// <summary>Trait（固有能力）の ID（DESIGN.md §3-4。Lv7〜10 の一部のカードが 1 つ持つ）。</summary>
    public enum TraitId
    {
        /// <summary>守護: 盤に置かれた後、最初の 1 回だけ裏返らない（使ったら消える）。</summary>
        Guard = 0,

        /// <summary>貫通: 置いたとき、斜め隣の相手カードとも辺 2 本の和で比べる。</summary>
        Pierce = 1,

        /// <summary>鼓舞: 置いたとき、縦横に隣接する自分のカード 1 枚につき全辺 +1（最大 +2）。その手の比較の間だけ。</summary>
        Rally = 2,

        /// <summary>錨: 盤に置かれた後、Combo では裏返らない。</summary>
        Anchor = 3,
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
        /// <summary>
        /// GameConfig で指定しないときの Pivot トークン数（先攻・後攻とも）。TS 版と同じ 2/2 で、ゴールデン突き合わせはこの値で行う。
        /// ゲーム本体の既定値（先攻・後攻で非対称）は MatchPivotTokensFirst / Second（Phase 3c、docs/BALANCE.md）。
        /// </summary>
        public const int PivotTokens = 2;

        /// <summary>1 人が持てる Pivot トークンの上限（AI の Zobrist 表の大きさがこれで決まる）。</summary>
        public const int MaxPivotTokens = 3;

        /// <summary>ゲーム本体（対戦画面）で使う先攻の Pivot トークン数（Phase 3c の自己対戦で決定。docs/BALANCE.md）。</summary>
        public const int MatchPivotTokensFirst = 1;

        /// <summary>ゲーム本体（対戦画面）で使う後攻の Pivot トークン数（Sudden Death OFF のとき）。</summary>
        public const int MatchPivotTokensSecond = 2;

        /// <summary>
        /// Sudden Death ON（または Resonance ON）のときの後攻の Pivot トークン数。再戦では先攻が入れ替わりトークンは持ち越すので、
        /// 後攻に 2 個渡すと再戦で「先攻＋トークン多め」になって後攻側に偏る（Hard 同士で本戦の先攻 43.5%）。そのため 1 個にする。
        /// </summary>
        public const int MatchPivotTokensSecondSuddenDeath = 1;

        /// <summary>
        /// ゲーム本体で使う Pivot トークン数（先攻, 後攻）。ルールの組み合わせで決まる（docs/BALANCE.md「Phase 3c」「Phase 6」）。
        /// Sudden Death ON または Resonance ON のときは 1・1。Resonance ON で先攻 1・後攻 2 だと Hard 同士の先攻勝率が
        /// Same+Plus+Combo+Pivot+Resonance で 28.7%（1・1 なら 44.3%、Traits も ON なら 51.4%）まで偏るため（Phase 6）。
        /// </summary>
        public static (int First, int Second) MatchPivotTokens(RuleSet rules) =>
            rules != null && (rules.SuddenDeath || rules.Resonance)
                ? (MatchPivotTokensFirst, MatchPivotTokensSecondSuddenDeath)
                : (MatchPivotTokensFirst, MatchPivotTokensSecond);

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
        static readonly string[] TraitIds = { "guard", "pierce", "rally", "anchor" };
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

        public const int TraitCount = 4;

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

        public static bool TryParseTrait(string id, out TraitId trait)
        {
            for (int i = 0; i < TraitIds.Length; i++)
            {
                if (TraitIds[i] == id)
                {
                    trait = (TraitId)i;
                    return true;
                }
            }
            trait = default;
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
