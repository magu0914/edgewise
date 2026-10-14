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

        /// <summary>ApplyMove と同じ結果の state を返す（イベントなし）。入力 state は変更しない。不正な手は IllegalMoveException。</summary>
        public static GameState ApplyMoveFast(GameState state, Move move)
        {
            AssertLegalMove(state, move);
            var rules = state.Rules;
            bool elemental = rules.Elemental;
            var elements = state.CellElementArray;
            var player = state.Turn;
            int placedCell = move.Cell;

            var board = (PlacedCard?[])state.BoardArray.Clone();
            var placed = new PlacedCard(LookupCard(state, state.HandArray(player)[move.HandIndex]), player, move.Rotation);
            board[placedCell] = placed;

            // 置いたカードの 4 辺の値は何度も使うので先に計算
            Span<int> mine = stackalloc int[4];
            Span<int> nb = stackalloc int[4];
            for (int dir = 0; dir < 4; dir++)
            {
                mine[dir] = SideValue(elemental, elements, placed, placedCell, dir);
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
                    int t = SideValue(elemental, elements, pc.Value, n, Board.Opposite(dir));
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
                            board[n] = pc.WithOwner(player);
                            queue[queueLength++] = n;
                        }
                    }
                }
            }

            // 手順 2: 通常ルール
            for (int dir = 0; dir < 4; dir++)
            {
                int n = nb[dir];
                if (n < 0) continue;
                var pc = board[n];
                if (!pc.HasValue || pc.Value.Owner == player) continue;
                if (mine[dir] > SideValue(elemental, elements, pc.Value, n, Board.Opposite(dir))) board[n] = pc.Value.WithOwner(player);
            }

            // 手順 3: Combo（FIFO）
            if (rules.Combo)
            {
                for (int head = 0; head < queueLength; head++)
                {
                    int src = queue[head];
                    var srcPc = board[src].Value;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int n = Board.Neighbor(src, dir);
                        if (n < 0) continue;
                        var pc = board[n];
                        if (!pc.HasValue || pc.Value.Owner == player) continue;
                        if (SideValue(elemental, elements, srcPc, src, dir) > SideValue(elemental, elements, pc.Value, n, Board.Opposite(dir)))
                        {
                            board[n] = pc.Value.WithOwner(player);
                            queue[queueLength++] = n;
                        }
                    }
                }
            }

            return FinishMove(state, board, move, null);
        }
    }
}
