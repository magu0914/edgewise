using System.Collections.Generic;

namespace Edgewise.Core
{
    /// <summary>ApplyMove の結果。</summary>
    public sealed class MoveResult
    {
        public GameState State { get; }
        public IReadOnlyList<GameEvent> Events { get; }

        public MoveResult(GameState state, IReadOnlyList<GameEvent> events)
        {
            State = state;
            Events = events;
        }
    }

    // 1 手の解決（参照実装）。DESIGN.md §2 の判定順序 1〜5 と §3-4（Resonance・Trait）をそのままコードにしている。
    // 高速版は GameEngine.Fast.cs。両者の結果一致はテストで保証する。
    public static partial class GameEngine
    {
        /// <summary>1 手を適用して新しい state とイベント列を返す。入力 state は変更しない。不正な手は IllegalMoveException。</summary>
        public static MoveResult ApplyMove(GameState state, Move move)
        {
            AssertLegalMove(state, move);
            var rules = state.Rules;
            var player = state.Turn;
            int placedCell = move.Cell;
            string cardId = state.HandArray(player)[move.HandIndex];
            bool resonance = rules.Resonance;
            bool traits = rules.Traits;

            var board = (PlacedCard?[])state.BoardArray.Clone();
            var placedDef = LookupCard(state, cardId);
            board[placedCell] = new PlacedCard(placedDef, player, move.Rotation);
            var events = new List<GameEvent> { new PlaceEvent(player, placedCell, cardId, move.Rotation) };

            // Rally: この手の間だけ、置いたカードの全辺 +1（縦横に隣接する自分のカード 1 枚につき、最大 +2）。置いた時点の盤面で数える
            int rally = 0;
            if (traits && placedDef.Trait == TraitId.Rally)
            {
                for (int dir = 0; dir < 4; dir++)
                {
                    int n = Board.Neighbor(placedCell, dir);
                    if (n != GameConstants.Wall && board[n].HasValue && board[n].Value.Owner == player) rally++;
                }
                if (rally > 2) rally = 2;
            }

            // 比較の瞬間の盤面での辺の値 = 素の値 + Elemental + Resonance（共鳴していれば +1）+ Rally（置いたカードだけ）
            int ValueAt(int cell, int dir)
            {
                int v = Board.EffectiveSide(state, board[cell].Value, cell, dir);
                if (resonance && Board.IsResonant(board, cell)) v++;
                if (cell == placedCell) v += rally;
                return v;
            }

            void Flip(int cell, FlipCause cause, int chainDepth, int from)
            {
                board[cell] = board[cell].Value.WithOwner(player);
                events.Add(new FlipEvent(cell, player, cause, chainDepth, from));
            }

            // Guard / Anchor が裏返しを防ぐか。Guard は最初の 1 回だけ（使い切りにして trait イベントを出す）、Anchor は Combo のときだけ
            bool Prevents(int cell, FlipCause cause)
            {
                if (!traits) return false;
                var pc = board[cell].Value;
                if (pc.Card.Trait == TraitId.Guard && !pc.GuardUsed)
                {
                    board[cell] = pc.WithGuardUsed();
                    events.Add(new TraitEvent(cell, TraitId.Guard));
                    return true;
                }
                return pc.Card.Trait == TraitId.Anchor && cause == FlipCause.Combo;
            }

            bool GuardUnused(int cell) =>
                traits && board[cell].Value.Card.Trait == TraitId.Guard && !board[cell].Value.GuardUsed;

            // ---- 手順 1: Same / Plus（同時判定・同時適用）----
            // 成立判定は所有者を問わず数え、裏返すのは相手所有のものだけ。
            var comboQueue = new List<(int Cell, int Depth)>();
            if (rules.Same || rules.Plus)
            {
                var sameCells = new List<int>();
                int sameMatches = 0;
                bool wallMatched = false;
                var sums = new int?[4];

                for (int dir = 0; dir < 4; dir++)
                {
                    int n = Board.Neighbor(placedCell, dir);
                    int mine = ValueAt(placedCell, dir);
                    if (n == GameConstants.Wall)
                    {
                        if (rules.Same && rules.SameWall && mine == GameConstants.WallRank)
                        {
                            sameMatches++;
                            wallMatched = true;
                        }
                        continue;
                    }
                    if (!board[n].HasValue) continue;
                    int theirs = ValueAt(n, Board.Opposite(dir));
                    if (rules.Same && mine == theirs)
                    {
                        sameMatches++;
                        sameCells.Add(n);
                    }
                    if (rules.Plus) sums[dir] = mine + theirs;
                }

                // 集合は挿入順を保つ（rule イベントの cells の並びを TS 版と一致させるため）
                var sameSet = sameMatches >= 2 ? sameCells : new List<int>();
                var plusCells = new List<int>();
                if (rules.Plus)
                {
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int? s = sums[dir];
                        if (s == null) continue;
                        bool matched = false;
                        for (int d = 0; d < 4; d++)
                        {
                            if (d != dir && sums[d] == s)
                            {
                                matched = true;
                                break;
                            }
                        }
                        if (matched) plusCells.Add(Board.Neighbor(placedCell, dir));
                    }
                }

                // Guard が未使用のカードはこの手では裏返らない（成立の数え方は変えない）ので、見出しの「実際に裏返る」には数えない
                bool WillFlip(int c) => board[c].Value.Owner != player && !GuardUnused(c);
                bool anySameFlip = sameSet.Exists(WillFlip);
                bool anyPlusFlip = plusCells.Exists(WillFlip);

                // 演出の見出し。実際に裏返るものがあるときだけ出す。Same → Plus の順
                if (anySameFlip) events.Add(new RuleEvent(wallMatched ? RuleHeading.SameWall : RuleHeading.Same, sameSet.ToArray()));
                if (anyPlusFlip) events.Add(new RuleEvent(RuleHeading.Plus, plusCells.ToArray()));

                // 裏返しは走査順（上→右→下→左）。Same と Plus の両方で成立したマスは Same として 1 回だけ
                for (int dir = 0; dir < 4; dir++)
                {
                    int n = Board.Neighbor(placedCell, dir);
                    if (n == GameConstants.Wall) continue;
                    var other = board[n];
                    if (!other.HasValue || other.Value.Owner == player) continue;
                    FlipCause cause;
                    if (sameSet.Contains(n)) cause = FlipCause.Same;
                    else if (plusCells.Contains(n)) cause = FlipCause.Plus;
                    else continue;
                    if (Prevents(n, cause)) continue;
                    Flip(n, cause, 0, placedCell);
                    comboQueue.Add((n, 0));
                }
            }

            // ---- 手順 2: 通常ルール（大小比較）----
            for (int dir = 0; dir < 4; dir++)
            {
                int n = Board.Neighbor(placedCell, dir);
                if (n == GameConstants.Wall) continue;
                var other = board[n];
                if (!other.HasValue || other.Value.Owner == player) continue;
                if (ValueAt(placedCell, dir) > ValueAt(n, Board.Opposite(dir)))
                {
                    if (!Prevents(n, FlipCause.Basic)) Flip(n, FlipCause.Basic, 0, placedCell);
                }
            }

            // ---- 手順 2.5: Pierce（斜め隣。右上 → 右下 → 左下 → 左上）----
            // 置いたカードの隣り合う 2 辺の和 > 相手の向かい合う 2 辺の和なら裏返す。通常ルールと同じ扱い（Combo の起点にならない）
            if (traits && placedDef.Trait == TraitId.Pierce)
            {
                for (int k = 0; k < 4; k++)
                {
                    int n = Board.Diagonal(placedCell, k);
                    if (n == GameConstants.Wall) continue;
                    var other = board[n];
                    if (!other.HasValue || other.Value.Owner == player) continue;
                    int a = Board.PierceDirA[k];
                    int b = Board.PierceDirB[k];
                    int mineSum = ValueAt(placedCell, a) + ValueAt(placedCell, b);
                    int theirSum = ValueAt(n, Board.Opposite(a)) + ValueAt(n, Board.Opposite(b));
                    if (mineSum > theirSum)
                    {
                        if (!Prevents(n, FlipCause.Basic)) Flip(n, FlipCause.Basic, 0, placedCell);
                    }
                }
            }

            // ---- 手順 3: Combo（手順 1 で裏返ったカードを起点に幅優先、FIFO）----
            // 連鎖では Same / Plus を再判定しない。手順 2・2.5 の裏返しは起点にならない。
            if (rules.Combo)
            {
                for (int head = 0; head < comboQueue.Count; head++)
                {
                    var (src, depth) = comboQueue[head];
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int n = Board.Neighbor(src, dir);
                        if (n == GameConstants.Wall) continue;
                        var other = board[n];
                        if (!other.HasValue || other.Value.Owner == player) continue;
                        if (ValueAt(src, dir) > ValueAt(n, Board.Opposite(dir)))
                        {
                            if (Prevents(n, FlipCause.Combo)) continue;
                            Flip(n, FlipCause.Combo, depth + 1, src);
                            comboQueue.Add((n, depth + 1));
                        }
                    }
                }
            }

            var next = FinishMove(state, board, move, events);
            return new MoveResult(next, events.AsReadOnly());
        }

        internal static CardDef LookupCard(GameState state, string cardId)
        {
            if (cardId == null || !state.Cards.TryGetValue(cardId, out var def))
                throw new System.InvalidOperationException("Unknown card id: " + cardId);
            return def;
        }

        /// <summary>
        /// 盤面確定後の共通処理（手札・トークン・手番・終局・Sudden Death）。
        /// events が null ならイベントを生成しない（ApplyMoveFast 用）。board の所有権を受け取る。
        /// </summary>
        internal static GameState FinishMove(GameState state, PlacedCard?[] board, Move move, List<GameEvent> events)
        {
            var player = state.Turn;
            var opponent = player.Opponent();

            var oldHand = state.HandArray(player);
            var hand = new string[oldHand.Length - 1];
            for (int i = 0, j = 0; i < oldHand.Length; i++)
            {
                if (i != move.HandIndex) hand[j++] = oldHand[i];
            }
            var hand0 = player == Player.P0 ? hand : state.HandArray(Player.P0);
            var hand1 = player == Player.P1 ? hand : state.HandArray(Player.P1);

            int pivot0 = state.GetPivotTokens(Player.P0);
            int pivot1 = state.GetPivotTokens(Player.P1);
            if (move.Rotation != 0)
            {
                if (player == Player.P0) pivot0--;
                else pivot1--;
            }

            bool full = true;
            for (int i = 0; i < GameConstants.BoardSize; i++)
            {
                if (!board[i].HasValue)
                {
                    full = false;
                    break;
                }
            }

            if (!full)
            {
                events?.Add(new TurnEvent(opponent));
                return new GameState(state.Rules, state.Cards, board, state.CellElementArray, hand0, hand1,
                    opponent, state.FirstPlayer, pivot0, pivot1, state.RngState, state.Phase, state.SuddenDeathRound);
            }

            var ended = new GameState(state.Rules, state.Cards, board, state.CellElementArray, hand0, hand1,
                opponent, state.FirstPlayer, pivot0, pivot1, state.RngState, Phase.Ended, state.SuddenDeathRound);
            var s = Score(ended);
            var res = ResultOfScore(s);

            if (res == GameResult.Draw && state.Rules.SuddenDeath && state.SuddenDeathRound < GameConstants.MaxSuddenDeathRounds)
            {
                // 終局時に自分が所有しているカード（盤上の所有分をマス順 → 残りの手札）を新しい手札にして再戦
                var newHand0 = new List<string>();
                var newHand1 = new List<string>();
                for (int i = 0; i < board.Length; i++)
                {
                    var pc = board[i].Value;
                    if (pc.Owner == Player.P0) newHand0.Add(pc.CardId);
                    else newHand1.Add(pc.CardId);
                }
                newHand0.AddRange(hand0);
                newHand1.AddRange(hand1);
                // 再戦の先攻は前ラウンドの後攻（交互）。Pivot トークンは対戦単位なので持ち越す
                var firstPlayer = state.FirstPlayer.Opponent();
                int round = state.SuddenDeathRound + 1;
                if (events != null)
                {
                    events.Add(new SuddenDeathEvent(round));
                    events.Add(new TurnEvent(firstPlayer));
                }
                return new GameState(state.Rules, state.Cards, new PlacedCard?[GameConstants.BoardSize], state.CellElementArray,
                    newHand0.ToArray(), newHand1.ToArray(), firstPlayer, firstPlayer, pivot0, pivot1, state.RngState,
                    state.Phase, round);
            }

            events?.Add(new EndEvent(s, res));
            return ended;
        }
    }
}
