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

        /// <summary>Pivot トークン数（先攻・後攻）。既定はエンジンの既定（TS 版と同じ 2/2）。--pivot で上書きした行を作る。</summary>
        public readonly int PivotFirst;
        public readonly int PivotSecond;

        RulePreset(string key, string name, RuleSet rules, int pivotFirst = GameConstants.PivotTokens, int pivotSecond = GameConstants.PivotTokens)
        {
            Key = key;
            Name = name;
            Rules = rules;
            PivotFirst = pivotFirst;
            PivotSecond = pivotSecond;
        }

        /// <summary>トークン数だけ変えた行。名前に「[先攻/後攻]」を付ける。</summary>
        public RulePreset WithPivot(int first, int second) =>
            new RulePreset(Key, Name + " [" + first + "/" + second + "]", Rules, first, second);

        /// <summary>
        /// --pivot の候補（カンマ区切りの "先攻/後攻"。"game" はゲーム本体の既定値 GameConstants.MatchPivotTokensFirst/Second）。
        /// Pivot OFF のプリセットはそのまま 1 行、Pivot ON のプリセットは候補ごとに 1 行にする。
        /// </summary>
        public static List<RulePreset> ExpandPivot(List<RulePreset> presets, string list)
        {
            if (string.IsNullOrEmpty(list)) return presets;
            var candidates = new List<(int, int)>();
            const int Game = -1; // ゲーム本体の既定値（ルールで決まる）
            foreach (var raw in list.Split(','))
            {
                var s = raw.Trim().ToLowerInvariant();
                if (s.Length == 0) continue;
                if (s == "game")
                {
                    candidates.Add((Game, Game));
                    continue;
                }
                var parts = s.Split('/');
                if (parts.Length != 2) throw new ArgumentException("--pivot expects FIRST/SECOND, got " + raw);
                candidates.Add((int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
            }
            var result = new List<RulePreset>();
            foreach (var p in presets)
            {
                if (!p.Rules.Pivot)
                {
                    result.Add(p);
                    continue;
                }
                foreach (var (f, s) in candidates)
                {
                    if (f == Game)
                    {
                        var t = GameConstants.MatchPivotTokens(p.Rules);
                        var g = p.WithPivot(t.First, t.Second);
                        result.Add(new RulePreset(g.Key, g.Name.Replace("]", " game]"), g.Rules, t.First, t.Second));
                    }
                    else result.Add(p.WithPivot(f, s));
                }
            }
            return result;
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
            new RulePreset("all-nopivot", "All except Pivot (Open)",
                new RuleSet(open: true, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true)),
            new RulePreset("pivot-openoff", "Pivot (Open OFF)", new RuleSet(open: false, pivot: true)),
            new RulePreset("pivot-sd", "Pivot+Sudden Death (Open)", new RuleSet(open: true, suddenDeath: true, pivot: true)),
            new RulePreset("spc-pivot", "Same+Plus+Combo+Pivot (Open)", new RuleSet(open: true, same: true, plus: true, combo: true, pivot: true)),
            new RulePreset("all-nosd", "All incl. Pivot except Sudden Death (Open)",
                new RuleSet(open: true, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, pivot: true)),
            new RulePreset("all-openoff-nopivot", "All except Pivot (Open OFF)",
                new RuleSet(open: false, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true)),
            new RulePreset("suddendeath", "Sudden Death (Open)", new RuleSet(open: true, suddenDeath: true)),
            Region(1), Region(2), Region(3), Region(4),
        };

        /// <summary>星図巡りの星域のルール（Pivot トークンはゲーム本体の既定値）。</summary>
        static RulePreset Region(int number)
        {
            var r = Edgewise.Core.Campaign.CampaignData.Regions[number - 1];
            var t = GameConstants.MatchPivotTokens(r.Rules);
            return new RulePreset("region" + number, "Region " + number + " " + r.Id, r.Rules, t.First, t.Second);
        }

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
