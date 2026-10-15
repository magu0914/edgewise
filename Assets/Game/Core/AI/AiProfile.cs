using System;

namespace Edgewise.Core.AI
{
    /// <summary>
    /// CPU の難易度ごとのパラメータ（DESIGN.md §5 / §5-1）。不変。
    /// 予算はすべてノード数（Core は時計を使えない）。値はすべて整数（浮動小数点禁止）。
    /// 既定値（Easy / Normal / Hard）の決め方は docs/BALANCE.md を参照。
    /// </summary>
    public sealed class AiProfile
    {
        /// <summary>1 手先の評価にノイズを足して選ぶ。ときどき Same / Plus / Combo を見落とす。</summary>
        public static readonly AiProfile Easy = new AiProfile(
            "Easy", maxNodes: 2000, maxDepth: 1, worlds: 1, noise: 150, blindPercent: 40, rootMargin: SearchConstants.Infinity,
            tableBits: 0);

        /// <summary>浅い αβ（3 手先）。決定化の世界数も少なめ。</summary>
        public static readonly AiProfile Normal = new AiProfile(
            "Normal", maxNodes: 30000, maxDepth: 3, worlds: 3, noise: 40, blindPercent: 0, rootMargin: SearchConstants.Infinity,
            tableBits: 14);

        /// <summary>反復深化 αβ ＋置換表＋手順序付け。残り手数が少なければ終局まで読み切る。</summary>
        public static readonly AiProfile Hard = new AiProfile(
            "Hard", maxNodes: 400000, maxDepth: 0, worlds: 8, noise: 0, blindPercent: 0, rootMargin: 150, tableBits: 18,
            tieBreakDepth: 2);

        /// <summary>表示名。</summary>
        public string Name { get; }

        /// <summary>1 手あたりの最大ノード数（決定化の全世界の合計）。これを超えたらその時点の最善手を返す。</summary>
        public int MaxNodes { get; }

        /// <summary>探索の最大深さ（手数）。0 = 制限なし（ラウンドの終局まで）。</summary>
        public int MaxDepth { get; }

        /// <summary>Open OFF のときの決定化の世界数 K（相手の手札をサンプリングする回数）。Open ON では常に 1。</summary>
        public int Worlds { get; }

        /// <summary>最終的な手の評価に足す一様ノイズの幅（±Noise、評価値の単位 = 1/100 枚）。0 ならノイズなし。</summary>
        public int Noise { get; }

        /// <summary>1 手ごとに、この確率（%）で Same / Plus / Combo を「見落として」通常ルールだけで読む。</summary>
        public int BlindPercent { get; }

        /// <summary>
        /// ルートの各手を読むときの窓の幅。その時点の最善値 − RootMargin より悪い手は「それより悪い」とだけ分かれば打ち切る。
        /// 決定化の平均を取るので、最善手の近くの手は正確な値が要る。Infinity なら全手を正確に評価する（解析用）。
        /// </summary>
        public int RootMargin { get; }

        /// <summary>置換表のサイズ（2^TableBits エントリ、1 エントリ 16 バイト）。0 なら置換表を使わない。</summary>
        public int TableBits { get; }

        /// <summary>
        /// 読み切った結果「引き分け」または「負け」の手が複数あるとき、この深さの（途中局面の評価を使う）読みの値で選ぶ。0 なら使わない。
        /// 相手も最善を尽くす前提のミニマックスでは同じ値の手の区別がつかないので、人間や弱い CPU が間違えやすい手を選ぶための工夫。
        /// </summary>
        public int TieBreakDepth { get; }

        /// <summary>評価関数の重み。</summary>
        public EvalWeights Weights { get; }

        public AiProfile(string name, int maxNodes, int maxDepth, int worlds, int noise, int blindPercent, int rootMargin, int tableBits,
            EvalWeights weights = null, int tieBreakDepth = 0)
        {
            if (maxNodes < 1) throw new ArgumentOutOfRangeException(nameof(maxNodes));
            if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            if (worlds < 1) throw new ArgumentOutOfRangeException(nameof(worlds));
            if (noise < 0) throw new ArgumentOutOfRangeException(nameof(noise));
            if (blindPercent < 0 || blindPercent > 100) throw new ArgumentOutOfRangeException(nameof(blindPercent));
            if (rootMargin < 0) throw new ArgumentOutOfRangeException(nameof(rootMargin));
            if (tableBits < 0 || tableBits > 24) throw new ArgumentOutOfRangeException(nameof(tableBits));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MaxNodes = maxNodes;
            MaxDepth = maxDepth;
            Worlds = worlds;
            Noise = noise;
            BlindPercent = blindPercent;
            RootMargin = rootMargin;
            TableBits = tableBits;
            Weights = weights ?? EvalWeights.Default;
            if (tieBreakDepth < 0) throw new ArgumentOutOfRangeException(nameof(tieBreakDepth));
            TieBreakDepth = tieBreakDepth;
        }

        public AiProfile WithMaxNodes(int maxNodes) =>
            new AiProfile(Name, maxNodes, MaxDepth, Worlds, Noise, BlindPercent, RootMargin, TableBits, Weights, TieBreakDepth);

        public AiProfile WithWorlds(int worlds) =>
            new AiProfile(Name, MaxNodes, MaxDepth, worlds, Noise, BlindPercent, RootMargin, TableBits, Weights, TieBreakDepth);

        public AiProfile WithMaxDepth(int maxDepth) =>
            new AiProfile(Name, MaxNodes, maxDepth, Worlds, Noise, BlindPercent, RootMargin, TableBits, Weights, TieBreakDepth);

        public AiProfile WithNoise(int noise, int blindPercent) =>
            new AiProfile(Name, MaxNodes, MaxDepth, Worlds, noise, blindPercent, RootMargin, TableBits, Weights, TieBreakDepth);

        public AiProfile WithRootMargin(int rootMargin) =>
            new AiProfile(Name, MaxNodes, MaxDepth, Worlds, Noise, BlindPercent, rootMargin, TableBits, Weights, TieBreakDepth);

        public AiProfile WithWeights(EvalWeights weights) =>
            new AiProfile(Name, MaxNodes, MaxDepth, Worlds, Noise, BlindPercent, RootMargin, TableBits, weights, TieBreakDepth);

        public AiProfile WithTieBreakDepth(int depth) =>
            new AiProfile(Name, MaxNodes, MaxDepth, Worlds, Noise, BlindPercent, RootMargin, TableBits, Weights, depth);

        public AiProfile WithName(string name) =>
            new AiProfile(name, MaxNodes, MaxDepth, Worlds, Noise, BlindPercent, RootMargin, TableBits, Weights, TieBreakDepth);

        /// <summary>名前（easy / normal / hard、大文字小文字は無視）から既定のプロファイルを返す。</summary>
        public static AiProfile FromName(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "easy": return Easy;
                case "normal": return Normal;
                case "hard": return Hard;
                default: throw new ArgumentException("unknown AI profile: " + name, nameof(name));
            }
        }

        public override string ToString() =>
            Name + "(nodes " + MaxNodes + ", depth " + (MaxDepth == 0 ? "inf" : MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture))
            + ", worlds " + Worlds + (TieBreakDepth > 0 ? ", tiebreak " + TieBreakDepth : "") + ", noise " + Noise + ", blind " + BlindPercent + "%, margin " + RootMargin + ", tt 2^" + TableBits
            + (ReferenceEquals(Weights, EvalWeights.Default) ? "" : ", eval {" + Weights + "}") + ")";
    }

    /// <summary>探索・評価で共有する定数。</summary>
    public static class SearchConstants
    {
        /// <summary>評価値の無限大。評価値の単位は 1/100 枚。</summary>
        public const int Infinity = 30000;

        /// <summary>勝ち（負け）の終局に足す（引く）点。スコア差 1 枚 = 100。</summary>
        public const int WinBonus = 300;

        /// <summary>1 ラウンドの最大手数（＝探索の最大深さ）。</summary>
        public const int MaxPlies = 9;
    }
}
