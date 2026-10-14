using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Edgewise.Core.Data
{
    /// <summary>レベルごとの 4 辺合計の範囲（両端を含む）。</summary>
    public readonly struct LevelBudget
    {
        public readonly int Min;
        public readonly int Max;

        public LevelBudget(int min, int max)
        {
            Min = min;
            Max = max;
        }

        public override string ToString() => Min + "-" + Max;
    }

    /// <summary>
    /// カードの強さの予算（DESIGN.md §7）。Lv1 合計 10〜13 から Lv10 合計 30〜33 まで線形。
    /// 下限 = round(10 + (level-1) * 20/9)、上限 = 下限 + 3。
    /// </summary>
    public static class Budget
    {
        public static readonly IReadOnlyDictionary<int, LevelBudget> LevelBudgets =
            new ReadOnlyDictionary<int, LevelBudget>(new Dictionary<int, LevelBudget>
            {
                { 1, new LevelBudget(10, 13) },
                { 2, new LevelBudget(12, 15) },
                { 3, new LevelBudget(14, 17) },
                { 4, new LevelBudget(17, 20) },
                { 5, new LevelBudget(19, 22) },
                { 6, new LevelBudget(21, 24) },
                { 7, new LevelBudget(23, 26) },
                { 8, new LevelBudget(26, 29) },
                { 9, new LevelBudget(28, 31) },
                { 10, new LevelBudget(30, 33) },
            });

        /// <summary>A（10）を持てる最低レベル。</summary>
        public const int MinLevelForAce = 8;

        public const int MinLevel = 1;
        public const int MaxLevel = 10;
        public const int CardsPerLevel = 5;
    }
}
