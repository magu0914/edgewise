using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Edgewise.Core
{
    /// <summary>
    /// 対戦の局面。不変（外から変更する手段はない）。ApplyMove は新しいインスタンスを返す。
    /// 内部配列は局面間で共有することがあるが、作った後に書き換えることはない。
    /// </summary>
    public sealed class GameState : IEquatable<GameState>
    {
        readonly PlacedCard?[] _board;
        readonly Element?[] _cellElements;
        readonly string[] _hand0;
        readonly string[] _hand1;
        readonly int _pivot0;
        readonly int _pivot1;

        public RuleSet Rules { get; }

        /// <summary>この対戦で使うカード定義（手札に入ったカードのみ）。state を自己完結させるために保持する。</summary>
        public IReadOnlyDictionary<string, CardDef> Cards { get; }

        public Player Turn { get; }

        /// <summary>現在のラウンドの先攻（Sudden Death の再戦で交代する）。</summary>
        public Player FirstPlayer { get; }

        /// <summary>mulberry32 の内部状態。</summary>
        public uint RngState { get; }

        public Phase Phase { get; }

        /// <summary>0 = 本戦、1..5 = Sudden Death の再戦回数。</summary>
        public int SuddenDeathRound { get; }

        /// <summary>長さ 9、index = row*3+col。空きマスは null。</summary>
        public ReadOnlyArray<PlacedCard?> Board => new ReadOnlyArray<PlacedCard?>(_board);

        /// <summary>長さ 9。Elemental OFF なら全て null。</summary>
        public ReadOnlyArray<Element?> CellElements => new ReadOnlyArray<Element?>(_cellElements);

        public ReadOnlyArray<string> GetHand(Player player) =>
            new ReadOnlyArray<string>(player == Player.P0 ? _hand0 : _hand1);

        public int GetPivotTokens(Player player) => player == Player.P0 ? _pivot0 : _pivot1;

        internal PlacedCard?[] BoardArray => _board;
        internal Element?[] CellElementArray => _cellElements;
        internal string[] HandArray(Player player) => player == Player.P0 ? _hand0 : _hand1;

        /// <summary>配列の所有権を受け取る（コピーしない）。呼び出し側は渡した配列を以後変更しないこと。</summary>
        internal GameState(
            RuleSet rules,
            IReadOnlyDictionary<string, CardDef> cards,
            PlacedCard?[] board,
            Element?[] cellElements,
            string[] hand0,
            string[] hand1,
            Player turn,
            Player firstPlayer,
            int pivot0,
            int pivot1,
            uint rngState,
            Phase phase,
            int suddenDeathRound)
        {
            Rules = rules;
            Cards = cards;
            _board = board;
            _cellElements = cellElements;
            _hand0 = hand0;
            _hand1 = hand1;
            Turn = turn;
            FirstPlayer = firstPlayer;
            _pivot0 = pivot0;
            _pivot1 = pivot1;
            RngState = rngState;
            Phase = phase;
            SuddenDeathRound = suddenDeathRound;
        }

        /// <summary>
        /// 任意の局面を直接組み立てる（シナリオテスト・再同期用）。引数はすべてコピーされる。
        /// 盤上のカードは PlacedCard が定義を持つので検証しない。手札の id も検証しない（TS 版と同じく、使われた時点で失敗する）。
        /// </summary>
        public static GameState FromParts(
            RuleSet rules,
            IReadOnlyDictionary<string, CardDef> cards,
            IReadOnlyList<PlacedCard?> board,
            IReadOnlyList<Element?> cellElements,
            IReadOnlyList<string> hand0,
            IReadOnlyList<string> hand1,
            Player turn,
            Player firstPlayer,
            int pivotTokens0,
            int pivotTokens1,
            uint rngState,
            Phase phase,
            int suddenDeathRound)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (board == null || board.Count != GameConstants.BoardSize) throw new ArgumentException("board must have 9 cells", nameof(board));
            if (cellElements == null || cellElements.Count != GameConstants.BoardSize)
                throw new ArgumentException("cellElements must have 9 cells", nameof(cellElements));
            if (hand0 == null) throw new ArgumentNullException(nameof(hand0));
            if (hand1 == null) throw new ArgumentNullException(nameof(hand1));

            var cardCopy = new Dictionary<string, CardDef>();
            foreach (var kv in cards) cardCopy[kv.Key] = kv.Value;

            return new GameState(
                rules,
                new ReadOnlyDictionary<string, CardDef>(cardCopy),
                Copy(board),
                Copy(cellElements),
                Copy(hand0),
                Copy(hand1),
                turn,
                firstPlayer,
                pivotTokens0,
                pivotTokens1,
                rngState,
                phase,
                suddenDeathRound);
        }

        static T[] Copy<T>(IReadOnlyList<T> src)
        {
            var a = new T[src.Count];
            for (int i = 0; i < a.Length; i++) a[i] = src[i];
            return a;
        }

        /// <summary>ルールだけ差し替えた局面（テスト・検証用）。</summary>
        public GameState WithRules(RuleSet rules) =>
            new GameState(rules ?? throw new ArgumentNullException(nameof(rules)), Cards, _board, _cellElements, _hand0, _hand1,
                Turn, FirstPlayer, _pivot0, _pivot1, RngState, Phase, SuddenDeathRound);

        /// <summary>フェーズだけ差し替えた局面（テスト・検証用）。</summary>
        public GameState WithPhase(Phase phase) =>
            new GameState(Rules, Cards, _board, _cellElements, _hand0, _hand1,
                Turn, FirstPlayer, _pivot0, _pivot1, RngState, phase, SuddenDeathRound);

        public bool Equals(GameState other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            if (Turn != other.Turn || FirstPlayer != other.FirstPlayer || RngState != other.RngState || Phase != other.Phase
                || SuddenDeathRound != other.SuddenDeathRound || _pivot0 != other._pivot0 || _pivot1 != other._pivot1)
                return false;
            if (!Rules.Equals(other.Rules)) return false;
            if (!Board.Equals(other.Board) || !CellElements.Equals(other.CellElements)) return false;
            if (!GetHand(Player.P0).Equals(other.GetHand(Player.P0)) || !GetHand(Player.P1).Equals(other.GetHand(Player.P1))) return false;
            if (!ReferenceEquals(Cards, other.Cards))
            {
                if (Cards.Count != other.Cards.Count) return false;
                foreach (var kv in Cards)
                {
                    if (!other.Cards.TryGetValue(kv.Key, out var def) || !kv.Value.Equals(def)) return false;
                }
            }
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as GameState);

        public override int GetHashCode() => (int)RngState ^ Board.GetHashCode() ^ GetHand(Player.P0).GetHashCode() * 7;

        /// <summary>デバッグ用。正規化文字列（StateHash.Serialize）と同じ。</summary>
        public override string ToString() => StateHash.Serialize(this);
    }
}
