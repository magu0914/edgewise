using System;

namespace Edgewise.Core
{
    // ApplyMove の高速版（AI 探索用）。イベントを生成せず、List・クロージャ・タプルも使わない。
    // 判定ロジックは参照実装（GameEngine.Apply.cs）と独立に書いてあり、結果一致をテストで保証する。
    // アロケーションは新しい盤面配列・手札配列・GameState の 3 つだけ（連鎖キューは stackalloc）。
    public static partial class GameEngine
    {
        static int SideValue(bool elemental, Element?[] cellElements, PlacedCard pc, int cell, int dir)
        {
            var def = pc.Card;
            int v = def.Sides[(dir - pc.Rotation + 4) & 3];
            if (elemental)
            {
                var e = cellElements[cell];
                if (e.HasValue) v += def.Element == e.Value ? 1 : -1;
            }
            return v;
        }

        /// <summary>
        /// 比較の瞬間の盤面での辺の値（素の値 + Elemental + Resonance）。rallyBonus は置いたカードだけに足す（それ以外は 0 を渡す）。
        /// Resonance OFF のときは SideValue と同じ。
        /// </summary>
        static int LiveSide(PlacedCard?[] board, bool elemental, bool resonance, Element?[] cellElements, int cell, int dir, int rallyBonus)
        {
            var pc = board[cell].Value;
            int v = SideValue(elemental, cellElements, pc, cell, dir) + rallyBonus;
            if (resonance)
            {
                var tribe = pc.Card.Tribe;
                if (tribe.HasValue)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        int n = Board.Neighbor(cell, d);
                        if (n < 0) continue;
                        var o = board[n];
                        if (o.HasValue && o.Value.Owner == pc.Owner && o.Value.Card.Tribe == tribe)
                        {
                            v++;
                            break;
                        }
                    }
                }
            }
            return v;
        }

        /// <summary>
        /// Guard / Anchor が cell のカードの裏返しを防ぐか。防ぐなら true（Guard は使い切りにして board を更新する）。
        /// Traits OFF のときは呼ばない。
        /// </summary>
        static bool PreventsFlip(PlacedCard?[] board, int cell, bool isCombo)
        {
            var pc = board[cell].Value;
            var trait = pc.Card.Trait;
            if (trait == TraitId.Guard && !pc.GuardUsed)
            {
                board[cell] = pc.WithGuardUsed();
                return true;
            }
            return isCombo && trait == TraitId.Anchor;
        }

        /// <summary>ApplyMove と同じ結果の state を返す（イベントなし）。入力 state は変更しない。不正な手は IllegalMoveException。</summary>
        public static GameState ApplyMoveFast(GameState state, Move move)
        {
            AssertLegalMove(state, move);
            var rules = state.Rules;
            bool elemental = rules.Elemental;
            bool resonance = rules.Resonance;
            bool traits = rules.Traits;
            var elements = state.CellElementArray;
            var player = state.Turn;
            int placedCell = move.Cell;

            var board = (PlacedCard?[])state.BoardArray.Clone();
            var placed = new PlacedCard(LookupCard(state, state.HandArray(player)[move.HandIndex]), player, move.Rotation);
            board[placedCell] = placed;
            var placedTrait = traits ? placed.Card.Trait : null;

            // Rally: 置いた時点の盤面で、縦横に隣接する自分のカード 1 枚につき +1（最大 +2）
            int rally = 0;
            if (placedTrait == TraitId.Rally)
            {
                for (int dir = 0; dir < 4; dir++)
                {
                    int n = Board.Neighbor(placedCell, dir);
                    if (n >= 0 && board[n].HasValue && board[n].Value.Owner == player) rally++;
                }
                if (rally > 2) rally = 2;
            }

            // 置いたカードの 4 辺の値は何度も使うので先に計算（手順 1 の時点の盤面。Resonance ON のときは手順 2 以降で計算し直す）
            Span<int> mine = stackalloc int[4];
            Span<int> nb = stackalloc int[4];
            for (int dir = 0; dir < 4; dir++)
            {
                mine[dir] = LiveSide(board, elemental, resonance, elements, placedCell, dir, rally);
                nb[dir] = Board.Neighbor(placedCell, dir);
            }

            // 連鎖キュー。1 マスは高々 1 回しか裏返らないので 9 で足りる
            Span<int> queue = stackalloc int[GameConstants.BoardSize];
            int queueLength = 0;

            // 手順 1: Same / Plus
            if (rules.Same || rules.Plus)
            {
                int sameCount = 0;
                int sameMask = 0;
                Span<int> sums = stackalloc int[4];
                for (int dir = 0; dir < 4; dir++)
                {
                    sums[dir] = -1;
                    int n = nb[dir];
                    int v = mine[dir];
                    if (n < 0)
                    {
                        if (rules.Same && rules.SameWall && v == GameConstants.WallRank) sameCount++;
                        continue;
                    }
                    var pc = board[n];
                    if (!pc.HasValue) continue;
                    int t = LiveSide(board, elemental, resonance, elements, n, Board.Opposite(dir), 0);
                    if (rules.Same && v == t)
                    {
                        sameCount++;
                        sameMask |= 1 << dir;
                    }
                    if (rules.Plus) sums[dir] = v + t;
                }
                int flipMask = sameCount >= 2 ? sameMask : 0;
                if (rules.Plus)
                {
                    for (int a = 0; a < 4; a++)
                    {
                        int sa = sums[a];
                        if (sa < 0) continue;
                        for (int b = a + 1; b < 4; b++)
                        {
                            if (sums[b] == sa) flipMask |= (1 << a) | (1 << b);
                        }
                    }
                }
                if (flipMask != 0)
                {
                    for (int dir = 0; dir < 4; dir++)
                    {
                        if ((flipMask & (1 << dir)) == 0) continue;
                        int n = nb[dir];
                        var pc = board[n].Value;
                        if (pc.Owner != player)
                        {
                            if (traits && PreventsFlip(board, n, false)) continue;
                            board[n] = board[n].Value.WithOwner(player);
                            queue[queueLength++] = n;
                        }
                    }
                }
            }

            // 手順 2: 通常ルール（Resonance ON のときは、先に裏返したカードの影響を受けるので毎回その時点の値で比べる）
            for (int dir = 0; dir < 4; dir++)
            {
                int n = nb[dir];
                if (n < 0) continue;
                var pc = board[n];
                if (!pc.HasValue || pc.Value.Owner == player) continue;
                int m = resonance ? LiveSide(board, elemental, true, elements, placedCell, dir, rally) : mine[dir];
                if (m > LiveSide(board, elemental, resonance, elements, n, Board.Opposite(dir), 0))
                {
                    if (traits && PreventsFlip(board, n, false)) continue;
                    board[n] = board[n].Value.WithOwner(player);
                }
            }

            // 手順 2.5: Pierce（斜め 4 方向。右上 → 右下 → 左下 → 左上）。Combo の起点にならない
            if (placedTrait == TraitId.Pierce)
            {
                for (int k = 0; k < 4; k++)
                {
                    int n = Board.Diagonal(placedCell, k);
                    if (n < 0) continue;
                    var pc = board[n];
                    if (!pc.HasValue || pc.Value.Owner == player) continue;
                    int a = Board.PierceDirA[k];
                    int b = Board.PierceDirB[k];
                    int mineSum = LiveSide(board, elemental, resonance, elements, placedCell, a, rally)
                        + LiveSide(board, elemental, resonance, elements, placedCell, b, rally);
                    int theirSum = LiveSide(board, elemental, resonance, elements, n, Board.Opposite(a), 0)
                        + LiveSide(board, elemental, resonance, elements, n, Board.Opposite(b), 0);
                    if (mineSum > theirSum)
                    {
                        if (PreventsFlip(board, n, false)) continue;
                        board[n] = board[n].Value.WithOwner(player);
                    }
                }
            }

            // 手順 3: Combo（FIFO）
            if (rules.Combo)
            {
                for (int head = 0; head < queueLength; head++)
                {
                    int src = queue[head];
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int n = Board.Neighbor(src, dir);
                        if (n < 0) continue;
                        var pc = board[n];
                        if (!pc.HasValue || pc.Value.Owner == player) continue;
                        if (LiveSide(board, elemental, resonance, elements, src, dir, 0)
                            > LiveSide(board, elemental, resonance, elements, n, Board.Opposite(dir), 0))
                        {
                            if (traits && PreventsFlip(board, n, true)) continue;
                            board[n] = board[n].Value.WithOwner(player);
                            queue[queueLength++] = n;
                        }
                    }
                }
            }

            return FinishMove(state, board, move, null);
        }
    }
}
