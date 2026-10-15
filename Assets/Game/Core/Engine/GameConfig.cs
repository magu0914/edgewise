using System;
using System.Collections.Generic;

namespace Edgewise.Core
{
    /// <summary>CreateGame の入力。</summary>
    public sealed class GameConfig
    {
        public RuleSet Rules { get; }

        /// <summary>Random OFF なら各 5 枚ちょうど。Random ON なら 5 枚以上から無作為に 5 枚。</summary>
        public ReadOnlyArray<string> Deck0 { get; }

        public ReadOnlyArray<string> Deck1 { get; }

        public long Seed { get; }

        /// <summary>カード定義。Core の Engine は Data に依存しないので呼び出し側が渡す。</summary>
        public IReadOnlyDictionary<string, CardDef> Cards { get; }

        /// <summary>
        /// Pivot ON のとき先攻が持つトークン数（0..MaxPivotTokens）。先攻は seed で決まるので、席（P0/P1）ではなく先攻・後攻で指定する。
        /// 既定は TS 版と同じ GameConstants.PivotTokens。Sudden Death の再戦では先攻が入れ替わってもトークンは持ち越す（配り直さない）。
        /// </summary>
        public int PivotTokensFirst { get; }

        /// <summary>Pivot ON のとき後攻が持つトークン数（0..MaxPivotTokens）。</summary>
        public int PivotTokensSecond { get; }

        public GameConfig(RuleSet rules, IReadOnlyList<string> deck0, IReadOnlyList<string> deck1, long seed, IReadOnlyDictionary<string, CardDef> cards,
            int pivotTokensFirst = GameConstants.PivotTokens, int pivotTokensSecond = GameConstants.PivotTokens)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Deck0 = new ReadOnlyArray<string>(CopyDeck(deck0, nameof(deck0)));
            Deck1 = new ReadOnlyArray<string>(CopyDeck(deck1, nameof(deck1)));
            Seed = seed;
            Cards = cards ?? throw new ArgumentNullException(nameof(cards));
            PivotTokensFirst = CheckTokens(pivotTokensFirst, nameof(pivotTokensFirst));
            PivotTokensSecond = CheckTokens(pivotTokensSecond, nameof(pivotTokensSecond));
        }

        /// <summary>カード定義を一覧で渡す版（TS 版の CardDef[] 受け取りに相当。id が重複したら後勝ち）。</summary>
        public GameConfig(RuleSet rules, IReadOnlyList<string> deck0, IReadOnlyList<string> deck1, long seed, IEnumerable<CardDef> cards,
            int pivotTokensFirst = GameConstants.PivotTokens, int pivotTokensSecond = GameConstants.PivotTokens)
            : this(rules, deck0, deck1, seed, ToDictionary(cards), pivotTokensFirst, pivotTokensSecond)
        {
        }

        public ReadOnlyArray<string> Deck(Player player) => player == Player.P0 ? Deck0 : Deck1;

        static int CheckTokens(int n, string name)
        {
            if (n < 0 || n > GameConstants.MaxPivotTokens)
                throw new InvalidConfigException(name + " must be 0.." + GameConstants.MaxPivotTokens + " (got " + n + ")");
            return n;
        }

        static string[] CopyDeck(IReadOnlyList<string> deck, string name)
        {
            if (deck == null) throw new ArgumentNullException(name);
            var a = new string[deck.Count];
            for (int i = 0; i < a.Length; i++) a[i] = deck[i];
            return a;
        }

        static IReadOnlyDictionary<string, CardDef> ToDictionary(IEnumerable<CardDef> cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            var db = new Dictionary<string, CardDef>();
            foreach (var c in cards) db[c.Id] = c;
            return db;
        }
    }
}
