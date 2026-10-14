namespace Edgewise.Core
{
    /// <summary>盤の幾何と辺の値の計算。</summary>
    public static class Board
    {
        /// <summary>NeighborTable[cell * 4 + dir] = 隣のマス、盤外なら Wall。dir は上→右→下→左。</summary>
        static readonly int[] NeighborTable = BuildNeighbors();

        static readonly int[] OppositeTable = { GameConstants.Bottom, GameConstants.Left, GameConstants.Top, GameConstants.Right };

        static int[] BuildNeighbors()
        {
            const int w = GameConstants.BoardWidth;
            var t = new int[GameConstants.BoardSize * 4];
            for (int cell = 0; cell < GameConstants.BoardSize; cell++)
            {
                int row = cell / w;
                int col = cell % w;
                t[cell * 4 + GameConstants.Top] = row > 0 ? cell - w : GameConstants.Wall;
                t[cell * 4 + GameConstants.Right] = col < w - 1 ? cell + 1 : GameConstants.Wall;
                t[cell * 4 + GameConstants.Bottom] = row < w - 1 ? cell + w : GameConstants.Wall;
                t[cell * 4 + GameConstants.Left] = col > 0 ? cell - 1 : GameConstants.Wall;
            }
            return t;
        }

        /// <summary>cell の dir 方向の隣のマス。盤外なら GameConstants.Wall（-1）。</summary>
        public static int Neighbor(int cell, int dir) => NeighborTable[cell * 4 + dir];

        /// <summary>cell の 4 方向の隣（上→右→下→左）。</summary>
        public static int[] NeighborsOf(int cell) =>
            new[] { Neighbor(cell, 0), Neighbor(cell, 1), Neighbor(cell, 2), Neighbor(cell, 3) };

        /// <summary>向かい合う方向（上↔下、右↔左）。</summary>
        public static int Opposite(int dir) => OppositeTable[dir];

        /// <summary>
        /// 回転後の辺を返す。rotation=1 は時計回り 90°:
        /// 新しい top = 元の left、新しい right = 元の top、新しい bottom = 元の right、新しい left = 元の bottom。
        /// </summary>
        public static Sides RotateSides(Sides sides, int rotation)
        {
            if (rotation == 0) return sides;
            return new Sides(
                sides[(GameConstants.Top - rotation + 4) & 3],
                sides[(GameConstants.Right - rotation + 4) & 3],
                sides[(GameConstants.Bottom - rotation + 4) & 3],
                sides[(GameConstants.Left - rotation + 4) & 3]);
        }

        /// <summary>回転を考慮した 1 辺の素の値。</summary>
        public static int RawSide(CardDef def, int rotation, int dir) => def.Sides[(dir - rotation + 4) & 3];

        /// <summary>Elemental 補正値（属性のないマスは 0、一致 +1、不一致・無属性カードは −1）。</summary>
        public static int ElementalModifier(RuleSet rules, Element? cellElement, CardDef def)
        {
            if (!rules.Elemental || cellElement == null) return 0;
            return def.Element == cellElement ? 1 : -1;
        }

        /// <summary>
        /// 盤上カードの補正後の辺の値。比較・Same・Plus はすべてこの値を使う（§2 判定順序 5）。
        /// 上限・下限のクランプはしない（A+1 = 11、1-1 = 0）。
        /// </summary>
        public static int EffectiveSide(GameState state, PlacedCard placed, int cell, int dir) =>
            RawSide(placed.Card, placed.Rotation, dir) + ElementalModifier(state.Rules, state.CellElementArray[cell], placed.Card);
    }
}
