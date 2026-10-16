using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;

namespace Edgewise.Game.Campaign
{
    /// <summary>デッキ編集でカードをタップしたときの結果。</summary>
    public enum DeckToggle
    {
        Added = 0,
        Removed = 1,

        /// <summary>5 枚そろっているので入らない（先に外す）。</summary>
        Full = 2,

        /// <summary>手持ちにないカード。</summary>
        NotOwned = 3,
    }

    /// <summary>
    /// デッキ編集の操作（DESIGN §12-4 / §12-11）。表示に依存しない（EditMode でテストする）。
    /// - タップで入れる・外す（5 枚まで。合計レベルが上限を超えても入れられるが、そのときは赤く表示して挑めない）。
    /// - 「おまかせ」は Core の CampaignRules.AutoDeck（上限内で点数が最大の 5 枚）。
    /// - 一覧はレベル順（同じならカタログ順）。属性・系統で絞り込める。残りの予算で入らないカードは薄くする（Fits = false）。
    /// - 最初の 5 枚は「星域ごとに最後に使ったデッキ」（今の手持ちにないカードは除く）。なければおまかせ。
    /// </summary>
    public sealed class DeckEditModel
    {
        readonly List<string> _selected = new List<string>();
        readonly IReadOnlyDictionary<string, int> _collection;

        public int Cap { get; }
        public RuleSet Rules { get; }

        public Element? ElementFilter { get; set; }
        public Tribe? TribeFilter { get; set; }

        public DeckEditModel(IReadOnlyDictionary<string, int> collection, int cap, RuleSet rules, IReadOnlyList<string> initial)
        {
            _collection = collection ?? new Dictionary<string, int>();
            Cap = cap;
            Rules = rules;
            if (initial != null)
            {
                foreach (var id in initial)
                {
                    if (_selected.Count >= CampaignRules.DeckSize) break;
                    if (Count(id) > 0 && !_selected.Contains(id)) _selected.Add(id);
                }
            }
            if (_selected.Count == 0) Auto();
        }

        /// <summary>星域の画面から: 最後のデッキ（なければおまかせ）で始める。</summary>
        public static DeckEditModel For(CampaignState state, Region region)
        {
            state.LastDecks.TryGetValue(region.Id, out var last);
            return new DeckEditModel(state.Collection, region.DeckLevelCap, region.Rules, last);
        }

        public IReadOnlyList<string> Selected => _selected;

        public int TotalLevel => CampaignRules.TotalLevel(_selected);

        public int Remaining => Cap - TotalLevel;

        public bool IsFull => _selected.Count >= CampaignRules.DeckSize;

        /// <summary>合計レベルが上限を超えている（目盛りを赤くする）。</summary>
        public bool OverCap => TotalLevel > Cap;

        /// <summary>このデッキで挑める（5 枚・重複なし・手持ちにある・上限以下）。</summary>
        public bool CanBegin => CampaignRules.ValidateDeck(_collection, _selected, Cap).Count == 0;

        public List<DeckIssue> Issues => CampaignRules.ValidateDeck(_collection, _selected, Cap);

        public int Count(string id) => id != null && _collection.TryGetValue(id, out int n) ? n : 0;

        public bool Contains(string id) => _selected.Contains(id);

        /// <summary>タップ: 入っていれば外し、入っていなければ入れる。</summary>
        public DeckToggle Toggle(string id)
        {
            if (_selected.Remove(id)) return DeckToggle.Removed;
            if (Count(id) <= 0) return DeckToggle.NotOwned;
            if (IsFull) return DeckToggle.Full;
            _selected.Add(id);
            return DeckToggle.Added;
        }

        /// <summary>デッキの index 番目の枠のカードを外す。</summary>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _selected.Count) return false;
            _selected.RemoveAt(index);
            return true;
        }

        /// <summary>おまかせ（上限内で最も点数の高い 5 枚）。組めなければ false で、今のデッキは変えない。</summary>
        public bool Auto()
        {
            var deck = CampaignRules.AutoDeck(_collection, Rules, Cap);
            if (deck == null) return false;
            _selected.Clear();
            _selected.AddRange(deck);
            return true;
        }

        /// <summary>今の残りの予算で入れられるか（入っているカードは常に true）。false のカードは一覧で薄くする。</summary>
        public bool Fits(string id)
        {
            if (_selected.Contains(id)) return true;
            if (IsFull || !CardCatalog.ById.TryGetValue(id, out var def)) return false;
            return def.Level <= Remaining;
        }

        /// <summary>手持ちの一覧（重複なし）。レベル順、同じならカタログ順。絞り込みを反映する。</summary>
        public List<CardDef> Listing()
        {
            var list = new List<CardDef>();
            foreach (var c in CardCatalog.All)
            {
                if (Count(c.Id) <= 0) continue;
                if (ElementFilter.HasValue && c.Element != ElementFilter) continue;
                if (TribeFilter.HasValue && c.Tribe != TribeFilter) continue;
                list.Add(c);
            }
            // 安定な並べ替え（カタログ順を保ったままレベル順）
            var indexed = new List<KeyValuePair<int, CardDef>>();
            for (int i = 0; i < list.Count; i++) indexed.Add(new KeyValuePair<int, CardDef>(i, list[i]));
            indexed.Sort((a, b) =>
            {
                int c = a.Value.Level.CompareTo(b.Value.Level);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            var sorted = new List<CardDef>();
            foreach (var kv in indexed) sorted.Add(kv.Value);
            return sorted;
        }
    }
}
