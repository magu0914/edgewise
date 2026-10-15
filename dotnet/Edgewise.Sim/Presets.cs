using System;
using System.Collections.Generic;
using Edgewise.Core;

namespace Edgewise.Sim
{
    /// <summary>ルールのプリセット（--rules で名前を指定する）。</summary>
    sealed class RulePreset
    {
        public readonly string Key;
        public readonly string Name;
        public readonly RuleSet Rules;

        RulePreset(string key, string name, RuleSet rules)
        {
            Key = key;
            Name = name;
            Rules = rules;
        }

        public static readonly RulePreset[] All =
        {
            new RulePreset("basic", "Basic (Open)", new RuleSet(open: true)),
            new RulePreset("spc", "Same+Plus+Combo (Open)", new RuleSet(open: true, same: true, plus: true, combo: true)),
            new RulePreset("all", "All incl. Pivot (Open)",
                new RuleSet(open: true, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true)),
            new RulePreset("openoff", "Basic (Open OFF)", new RuleSet(open: false)),
            new RulePreset("spc-openoff", "Same+Plus+Combo (Open OFF)", new RuleSet(open: false, same: true, plus: true, combo: true)),
            new RulePreset("all-openoff", "All incl. Pivot (Open OFF)",
                new RuleSet(open: false, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true)),
            new RulePreset("same", "Same (Open)", new RuleSet(open: true, same: true)),
            new RulePreset("plus", "Plus (Open)", new RuleSet(open: true, plus: true)),
            new RulePreset("elemental", "Elemental (Open)", new RuleSet(open: true, elemental: true)),
            new RulePreset("pivot", "Pivot (Open)", new RuleSet(open: true, pivot: true)),
            new RulePreset("suddendeath", "Sudden Death (Open)", new RuleSet(open: true, suddenDeath: true)),
        };

        /// <summary>「basic,spc」のようなカンマ区切り。"default" は BALANCE.md の 4 条件。</summary>
        public static List<RulePreset> Parse(string list)
        {
            var result = new List<RulePreset>();
            foreach (var raw in list.Split(','))
            {
                var key = raw.Trim().ToLowerInvariant();
                if (key.Length == 0) continue;
                if (key == "default")
                {
                    result.Add(Find("basic"));
                    result.Add(Find("spc"));
                    result.Add(Find("all"));
                    result.Add(Find("openoff"));
                    continue;
                }
                result.Add(Find(key));
            }
            return result;
        }

        static RulePreset Find(string key)
        {
            foreach (var p in All)
            {
                if (p.Key == key) return p;
            }
            throw new ArgumentException("unknown rule preset: " + key + " (known: " + string.Join(", ", Keys()) + ")");
        }

        static IEnumerable<string> Keys()
        {
            foreach (var p in All) yield return p.Key;
        }
    }
}
