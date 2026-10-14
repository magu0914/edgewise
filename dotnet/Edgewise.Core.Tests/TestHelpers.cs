using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.Data;
using NUnit.Framework;

namespace Edgewise.Core.Tests
{
    /// <summary>盤上の配置の指定（コレクション初期化子で { セル, id, 所有者, 回転 } と書ける）。</summary>
    public sealed class BoardSpec : IEnumerable<(int Cell, string Id, int Owner, int Rotation)>
    {
        readonly List<(int, string, int, int)> _items = new List<(int, string, int, int)>();

        public void Add(int cell, string id, int owner, int rotation = 0) => _items.Add((cell, id, owner, rotation));

        public IEnumerator<(int Cell, string Id, int Owner, int Rotation)> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>任意の局面の指定（TS 版 helpers.ts の Setup）。</summary>
    public sealed class Setup
    {
        public CardDef[] Cards { get; set; } = Array.Empty<CardDef>();
        public RuleSet Rules { get; set; }
        public BoardSpec Board { get; set; } = new BoardSpec();
        public string[][] Hands { get; set; }
        public int Turn { get; set; }
        public int FirstPlayer { get; set; }
        public Dictionary<int, Element> CellElements { get; set; } = new Dictionary<int, Element>();
        public int[] PivotTokens { get; set; }
        public int SuddenDeathRound { get; set; }
    }

    /// <summary>
    /// テスト用の決定論的な乱数源（TS 版 seededRandom と同じ数列: state = seed の下位 32bit、mulberry32）。
    /// TS 版の r() = u / 2^32 なので、pick = floor(r * n) = (u * n) >> 32、coin = r &lt; 0.5 = u &lt; 2^31、floor(r * 2^32) = u。
    /// </summary>
    public sealed class TestRandom
    {
        uint _state;

        public TestRandom(long seed)
        {
            _state = unchecked((uint)seed);
        }

        public uint NextUInt32() => Rng.NextUInt32(_state, out _state);

        public int NextInt(int n) => Rng.NextInt(_state, n, out _state);

        public bool Coin() => NextUInt32() < 0x80000000u;

        public T Pick<T>(IReadOnlyList<T> items) => items[NextInt(items.Count)];
    }

    public static class H
    {
        public const int P0 = 0;
        public const int P1 = 1;

        /// <summary>テスト用のカード定義（TS 版 mkCard）。</summary>
        public static CardDef MkCard(string id, int top, int right, int bottom, int left, Element? element = null) =>
            new CardDef(id, id, 1, new Sides(top, right, bottom, left), element);

        /// <summary>任意の局面を直接組み立てる（TS 版 makeState）。rngState は 12345 固定。</summary>
        public static GameState MakeState(Setup s)
        {
            var rules = s.Rules ?? RuleSet.None;
            var cards = new Dictionary<string, CardDef>();
            foreach (var c in s.Cards) cards[c.Id] = c;

            var board = new PlacedCard?[GameConstants.BoardSize];
            foreach (var (cell, id, owner, rotation) in s.Board) board[cell] = new PlacedCard(cards[id], (Player)owner, rotation);

            var elements = new Element?[GameConstants.BoardSize];
            foreach (var kv in s.CellElements) elements[kv.Key] = kv.Value;

            var hands = s.Hands ?? new[] { new[] { "P" }, new[] { "Q" } };
            var pivot = s.PivotTokens ?? (rules.Pivot ? new[] { 2, 2 } : new[] { 0, 0 });
            return GameState.FromParts(rules, cards, board, elements, hands[0], hands[1], (Player)s.Turn, (Player)s.FirstPlayer,
                pivot[0], pivot[1], 12345u, Phase.Playing, s.SuddenDeathRound);
        }

        /// <summary>局面の完全なコピー（TS 版 structuredClone 相当）。</summary>
        public static GameState Clone(GameState s) =>
            GameState.FromParts(s.Rules, s.Cards, s.Board.ToArray(), s.CellElements.ToArray(), s.GetHand(Player.P0).ToArray(),
                s.GetHand(Player.P1).ToArray(), s.Turn, s.FirstPlayer, s.GetPivotTokens(Player.P0), s.GetPivotTokens(Player.P1),
                s.RngState, s.Phase, s.SuddenDeathRound);

        /// <summary>実装済みのルールだけをランダムに ON/OFF（TS 版 randomRules。乱数は 9 回必ず消費し、その後で上書き）。</summary>
        public static RuleSet RandomRules(TestRandom r, bool? suddenDeath = null, bool? same = null, bool? sameWall = null,
            bool? plus = null, bool? combo = null, bool? elemental = null, bool? pivot = null)
        {
            bool open = r.Coin(), random = r.Coin(), sm = r.Coin(), sw = r.Coin(), pl = r.Coin(), co = r.Coin(), el = r.Coin(),
                sd = r.Coin(), pv = r.Coin();
            return new RuleSet(
                open: open,
                random: random,
                same: same ?? sm,
                sameWall: sameWall ?? sw,
                plus: plus ?? pl,
                combo: combo ?? co,
                elemental: elemental ?? el,
                suddenDeath: suddenDeath ?? sd,
                pivot: pivot ?? pv);
        }

        /// <summary>本物のカードデータからランダムなデッキを作る（Random ON なら 5〜10 枚）。TS 版 randomConfig。</summary>
        public static GameConfig RandomConfig(TestRandom r, RuleSet rules, IReadOnlyList<CardDef> cards = null)
        {
            cards = cards ?? CardCatalog.All;
            int deckSize = rules.Random ? 5 + r.NextInt(6) : 5;
            string[] Deck()
            {
                var d = new string[deckSize];
                for (int i = 0; i < deckSize; i++) d[i] = r.Pick(cards).Id;
                return d;
            }
            var deck0 = Deck();
            var deck1 = Deck();
            long seed = r.NextUInt32();
            return new GameConfig(rules, deck0, deck1, seed, cards);
        }

        public static GameState NewRandomGame(TestRandom r, bool? suddenDeath = null, bool? same = null, bool? sameWall = null,
            bool? plus = null, bool? combo = null, bool? elemental = null, bool? pivot = null) =>
            GameEngine.CreateGame(RandomConfig(r, RandomRules(r, suddenDeath, same, sameWall, plus, combo, elemental, pivot)));

        /// <summary>合法手からランダムに選んで終局まで進める（TS 版 playRandom）。</summary>
        public static (GameState State, int Moves) PlayRandom(GameState start, TestRandom r, Func<GameState, Move, GameState> apply)
        {
            var state = start;
            int moves = 0;
            while (state.Phase == Phase.Playing)
            {
                var move = r.Pick(GameEngine.LegalMoves(state));
                state = apply(state, move);
                moves++;
                if (moves > 9 * 10) throw new InvalidOperationException("game did not terminate");
            }
            return (state, moves);
        }

        public static List<(int Cell, FlipCause Cause, int ChainDepth, int From)> Flips(IEnumerable<GameEvent> events) =>
            events.OfType<FlipEvent>().Select(f => (f.Cell, f.Cause, f.ChainDepth, f.From)).ToList();

        public static List<GameEvent> RuleEvents(IEnumerable<GameEvent> events) => events.Where(e => e is RuleEvent).ToList();

        public static int?[] Owners(GameState state) =>
            state.Board.Select(pc => pc.HasValue ? (int?)(int)pc.Value.Owner : null).ToArray();

        public static int?[] Owners(params int?[] owners) => owners;

        public static (int Cell, FlipCause Cause, int ChainDepth, int From) F(int cell, FlipCause cause, int depth, int from) =>
            (cell, cause, depth, from);

        /// <summary>参照実装と高速版の両方で適用し、結果の一致を確かめてから参照実装の結果を返す（TS 版 both）。</summary>
        public static MoveResult Both(GameState state, Move move)
        {
            var r = GameEngine.ApplyMove(state, move);
            Assert.That(GameEngine.ApplyMoveFast(state, move), Is.EqualTo(r.State));
            return r;
        }

        public static readonly Move PlayCenter = new Move(0, 4, 0);

        public static string[] Ids(IEnumerable<CardDef> cards) => cards.Select(c => c.Id).ToArray();
    }
}
