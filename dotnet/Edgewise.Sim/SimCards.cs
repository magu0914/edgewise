using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Edgewise.Core;
using Edgewise.Core.Data;

namespace Edgewise.Sim
{
    /// <summary>
    /// 対戦で使うカード一覧。既定は CardCatalog そのもの。<c>--traits</c> で Trait の割り当てだけを差し替えて計測できる（Phase 6 の調整用）。
    /// 書式: "none"（全カードの Trait を外す）、"id=trait,id=trait,..."（その他は CardCatalog のまま。trait = guard / pierce / rally / anchor / none）、
    /// "only:id=trait,..."（指定したカード以外の Trait を外す）。
    /// </summary>
    static class SimCards
    {
        public static IReadOnlyList<CardDef> All = CardCatalog.All;
        public static IReadOnlyDictionary<string, CardDef> ById = CardCatalog.ById;

        /// <summary>Trait を持つカードの定義順の番号。</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Id == id) return i;
            }
            return -1;
        }

        public static void Apply(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            spec = spec.Trim().ToLowerInvariant();
            var overrides = new Dictionary<string, TraitId?>();
            bool only = false;
            if (spec == "none")
            {
                only = true;
            }
            else
            {
                if (spec.StartsWith("only:", StringComparison.Ordinal))
                {
                    only = true;
                    spec = spec.Substring(5);
                }
                foreach (var item in spec.Split(','))
                {
                    var kv = item.Split('=');
                    if (kv.Length != 2) throw new ArgumentException("--traits expects id=trait, got " + item);
                    if (!CardCatalog.ById.ContainsKey(kv[0].Trim())) throw new ArgumentException("unknown card " + kv[0]);
                    var t = kv[1].Trim();
                    if (t == "none") overrides[kv[0].Trim()] = null;
                    else if (Ids.TryParseTrait(t, out var trait)) overrides[kv[0].Trim()] = trait;
                    else throw new ArgumentException("unknown trait " + t);
                }
            }

            var list = new List<CardDef>();
            foreach (var c in CardCatalog.All)
            {
                TraitId? trait = only ? null : c.Trait;
                if (overrides.TryGetValue(c.Id, out var o)) trait = o;
                list.Add(new CardDef(c.Id, c.Name, c.Level, c.Sides, c.Element, c.Tribe, trait));
            }
            var db = new Dictionary<string, CardDef>();
            foreach (var c in list) db[c.Id] = c;
            All = new ReadOnlyCollection<CardDef>(list);
            ById = new ReadOnlyDictionary<string, CardDef>(db);
        }
    }
}
