using System.Collections.Generic;

namespace Edgewise.Core.Data
{
    /// <summary>カード定義の検証。問題点を文字列で列挙する（TS 版 validate.ts と同じ文言）。</summary>
    public static class CardValidator
    {
        const string IdPatternText = "/^[a-z][a-z0-9_-]*$/";

        /// <summary>^[a-z][a-z0-9_-]*$ と同じ判定（Regex を使わずに書いたもの）。</summary>
        static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id[0] < 'a' || id[0] > 'z') return false;
            for (int i = 1; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>1 枚のカード定義の問題点を列挙する（問題なしなら空）。</summary>
        public static List<string> CardProblems(CardDef card)
        {
            var problems = new List<string>();
            string where = "card \"" + card.Id + "\"";
            if (!IsValidId(card.Id)) problems.Add(where + ": id must match " + IdPatternText);
            if (card.Name.Trim().Length == 0) problems.Add(where + ": empty name");
            if (card.Level < Budget.MinLevel || card.Level > Budget.MaxLevel)
                problems.Add(where + ": level out of range (" + card.Level + ")");
            var sides = card.Sides.ToArray();
            foreach (int s in sides)
            {
                if (s < 1 || s > 10) problems.Add(where + ": side out of range (" + s + ")");
            }
            if (card.Level < Budget.MinLevelForAce && card.Sides.Contains(10))
                problems.Add(where + ": A (10) is only allowed at Lv" + Budget.MinLevelForAce + "+");
            int total = card.Sides.Sum;
            if (Budget.LevelBudgets.TryGetValue(card.Level, out var budget) && (total < budget.Min || total > budget.Max))
                problems.Add(where + ": side total " + total + " is outside Lv" + card.Level + " budget " + budget.Min + "-" + budget.Max);
            if (card.Element.HasValue && ((int)card.Element.Value < 0 || (int)card.Element.Value >= GameConstants.ElementCount))
                problems.Add(where + ": unknown element");
            if (card.Tribe.HasValue && ((int)card.Tribe.Value < 0 || (int)card.Tribe.Value >= GameConstants.TribeCount))
                problems.Add(where + ": unknown tribe");
            return problems;
        }

        /// <summary>カード一覧全体の問題点（個々のカード + id/名前の重複）。</summary>
        public static List<string> CardListProblems(IEnumerable<CardDef> cards)
        {
            var list = new List<CardDef>(cards);
            var problems = new List<string>();
            foreach (var c in list) problems.AddRange(CardProblems(c));
            var ids = new HashSet<string>();
            var names = new HashSet<string>();
            foreach (var c in list)
            {
                if (ids.Contains(c.Id)) problems.Add("duplicate id: " + c.Id);
                if (names.Contains(c.Name)) problems.Add("duplicate name: " + c.Name);
                ids.Add(c.Id);
                names.Add(c.Name);
            }
            return problems;
        }
    }
}
