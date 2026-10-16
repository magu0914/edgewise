using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Edgewise.Core.AI;

namespace Edgewise.Core.Campaign
{
    /// <summary>
    /// 対戦相手の台詞（DESIGN §12-7 / §12-7-1）。各場面 1〜3 行、1 行は全角 30 字以内が目安。
    /// 登場は初回（Intro。担当のルールに 1 行だけ触れる）と再戦（Rematch。その相手と 1 度でも対戦したことがあれば）で分ける。
    /// </summary>
    public sealed class Dialogue
    {
        /// <summary>登場（初回の対戦前）。</summary>
        public IReadOnlyList<string> Intro { get; }

        /// <summary>登場（再戦。2 回目以降の対戦前）。どちらを出すかは CampaignRules.IntroLines が決める。</summary>
        public IReadOnlyList<string> Rematch { get; }

        /// <summary>プレイヤーが勝ったとき（相手が負けたときの台詞）。</summary>
        public IReadOnlyList<string> PlayerWins { get; }

        /// <summary>プレイヤーが負けたとき（相手が勝ったときの台詞）。</summary>
        public IReadOnlyList<string> PlayerLoses { get; }

        /// <summary>カードを渡すとき。</summary>
        public IReadOnlyList<string> GiveCard { get; }

        /// <summary>原作モードでカードを取るとき。</summary>
        public IReadOnlyList<string> TakeCard { get; }

        /// <summary>主だけ: 星域を抜けたとき（主に初めて勝ったとき）。主以外は空。</summary>
        public IReadOnlyList<string> RegionClear { get; }

        public Dialogue(string[] intro, string[] rematch, string[] playerWins, string[] playerLoses, string[] giveCard, string[] takeCard,
            string[] regionClear = null)
        {
            Intro = Wrap(intro, nameof(intro));
            Rematch = Wrap(rematch, nameof(rematch));
            PlayerWins = Wrap(playerWins, nameof(playerWins));
            PlayerLoses = Wrap(playerLoses, nameof(playerLoses));
            GiveCard = Wrap(giveCard, nameof(giveCard));
            TakeCard = Wrap(takeCard, nameof(takeCard));
            RegionClear = new ReadOnlyCollection<string>(regionClear == null ? Array.Empty<string>() : (string[])regionClear.Clone());
        }

        static IReadOnlyList<string> Wrap(string[] lines, string name)
        {
            if (lines == null || lines.Length == 0) throw new ArgumentException("dialogue needs at least one line", name);
            return new ReadOnlyCollection<string>((string[])lines.Clone());
        }

        /// <summary>すべての行（フォントの焼き込み・テスト用）。</summary>
        public IEnumerable<string> AllLines()
        {
            foreach (var l in Intro) yield return l;
            foreach (var l in Rematch) yield return l;
            foreach (var l in PlayerWins) yield return l;
            foreach (var l in PlayerLoses) yield return l;
            foreach (var l in GiveCard) yield return l;
            foreach (var l in TakeCard) yield return l;
            foreach (var l in RegionClear) yield return l;
        }
    }

    /// <summary>星図巡りの対戦相手（DESIGN §12-2）。不変。</summary>
    public sealed class Opponent
    {
        /// <summary>セーブに使う不変の ID（表示名を変えてもセーブが壊れないよう、名前ではなく役割で付ける）。</summary>
        public string Id { get; }

        /// <summary>表示名（仮）。</summary>
        public string Name { get; }

        /// <summary>肩書き（仮）。</summary>
        public string Title { get; }

        /// <summary>所属する星域の ID。</summary>
        public string RegionId { get; }

        /// <summary>星域の主（ボス）か。主に勝つと次の星域が開く。</summary>
        public bool IsBoss { get; }

        /// <summary>この相手のデッキの合計レベル上限（主は星域の上限 +5）。</summary>
        public int DeckLevelCap { get; }

        /// <summary>AI の強さの表示名（Easy / Easy+ / Normal / Normal+ / Hard- / Hard）。</summary>
        public string StrengthLabel { get; }

        /// <summary>性格の短い説明（表示・ログ用）。</summary>
        public string PersonalityLabel { get; }

        /// <summary>AI の設定（性格つき）。</summary>
        public AiProfile Profile { get; }

        /// <summary>持ちカード（カード ID。重複なし。6〜8 枚）。原作モードで取ったカードは CampaignState 側に持つ。</summary>
        public IReadOnlyList<string> Cards { get; }

        /// <summary>
        /// デッキ選びのゆとり: 上限内で組める最大の合計レベルから、これだけ低いデッキまでを候補にして seed で選ぶ
        /// （0 なら常に最強に近いデッキ、大きいほど対戦ごとのばらつきが増え、平均して弱くなる）。
        /// </summary>
        public int DeckSlack { get; }

        public Dialogue Lines { get; }

        public Opponent(string id, string name, string title, string regionId, bool isBoss, int deckLevelCap, string strengthLabel,
            string personalityLabel, AiProfile profile, string[] cards, int deckSlack, Dialogue lines)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            RegionId = regionId ?? throw new ArgumentNullException(nameof(regionId));
            IsBoss = isBoss;
            DeckLevelCap = deckLevelCap;
            StrengthLabel = strengthLabel ?? throw new ArgumentNullException(nameof(strengthLabel));
            PersonalityLabel = personalityLabel ?? throw new ArgumentNullException(nameof(personalityLabel));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Cards = new ReadOnlyCollection<string>((string[])(cards ?? throw new ArgumentNullException(nameof(cards))).Clone());
            if (deckSlack < 0) throw new ArgumentOutOfRangeException(nameof(deckSlack));
            DeckSlack = deckSlack;
            Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        }

        public override string ToString() => Title + " " + Name + " (" + Id + ")";
    }

    /// <summary>星域（DESIGN §12-2）。固定のルールとデッキの合計レベル上限を持つ。不変。</summary>
    public sealed class Region
    {
        public string Id { get; }

        /// <summary>1 始まりの番号（星図の順）。</summary>
        public int Number { get; }

        public string Name { get; }

        /// <summary>この星域の固定ルール。Pivot のトークン数は GameConstants.MatchPivotTokens(Rules) で決める。</summary>
        public RuleSet Rules { get; }

        /// <summary>プレイヤーと主以外の相手のデッキの合計レベル上限。</summary>
        public int DeckLevelCap { get; }

        /// <summary>相手（主以外の 2 人 → 主の順）。</summary>
        public IReadOnlyList<Opponent> Opponents { get; }

        public Opponent Boss => Opponents[Opponents.Count - 1];

        public Region(string id, int number, string name, RuleSet rules, int deckLevelCap, Opponent[] opponents)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Number = number;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            DeckLevelCap = deckLevelCap;
            if (opponents == null || opponents.Length == 0) throw new ArgumentException("a region needs opponents", nameof(opponents));
            Opponents = new ReadOnlyCollection<Opponent>((Opponent[])opponents.Clone());
        }

        public override string ToString() => Number + ". " + Name + " (" + Id + ")";
    }

    /// <summary>1 人の相手との記録（勝敗数・原作モードで取られたカード・見たことのあるカード）。不変。</summary>
    public sealed class OpponentRecord
    {
        public static readonly OpponentRecord Empty = new OpponentRecord(0, 0, 0, null, null);

        /// <summary>プレイヤーの勝ち数。</summary>
        public int Wins { get; }

        public int Losses { get; }

        public int Draws { get; }

        /// <summary>原作モードで相手が取ったカード（カード ID → 枚数）。相手の持ちカードに加わり、勝てば取り返せる。</summary>
        public IReadOnlyDictionary<string, int> Captured { get; }

        /// <summary>対戦で見たことのある相手のカード（相手選択画面で「？」にしないもの）。カタログ順。</summary>
        public IReadOnlyList<string> Seen { get; }

        public int Games => Wins + Losses + Draws;

        public bool IsEmpty => Games == 0 && Captured.Count == 0 && Seen.Count == 0;

        public OpponentRecord(int wins, int losses, int draws, IReadOnlyDictionary<string, int> captured, IEnumerable<string> seen)
        {
            if (wins < 0 || losses < 0 || draws < 0) throw new ArgumentOutOfRangeException(nameof(wins));
            Wins = wins;
            Losses = losses;
            Draws = draws;
            var cap = new Dictionary<string, int>();
            if (captured != null)
            {
                foreach (var kv in captured)
                {
                    if (kv.Value > 0) cap[kv.Key] = kv.Value;
                }
            }
            Captured = new ReadOnlyDictionary<string, int>(cap);
            Seen = new ReadOnlyCollection<string>(CampaignUtil.SortedDistinct(seen));
        }

        public OpponentRecord With(int? wins = null, int? losses = null, int? draws = null, IReadOnlyDictionary<string, int> captured = null,
            IEnumerable<string> seen = null) =>
            new OpponentRecord(wins ?? Wins, losses ?? Losses, draws ?? Draws, captured ?? Captured, seen ?? Seen);
    }

    /// <summary>
    /// 星図巡りの進行（セーブの中身。DESIGN §12-8）。不変: 変更はすべて新しいインスタンスを返す（CampaignRules の純関数で行う）。
    /// </summary>
    public sealed class CampaignState
    {
        /// <summary>この進行の乱数の元（相手のデッキ選びなど）。新しく始めるときに UI 側が決める。</summary>
        public long Seed { get; }

        /// <summary>原作モード（負けると相手にカードを 1 枚取られる）。</summary>
        public bool ClassicMode { get; }

        /// <summary>手持ち（カード ID → 枚数。0 枚のカードは含まない）。</summary>
        public IReadOnlyDictionary<string, int> Collection { get; }

        /// <summary>解放済みの星域の ID（星図の順）。</summary>
        public IReadOnlyList<string> UnlockedRegions { get; }

        /// <summary>相手ごとの記録（記録のない相手は含まない。Record(id) で引く）。</summary>
        public IReadOnlyDictionary<string, OpponentRecord> Records { get; }

        /// <summary>星域ごとに最後に使ったデッキ（星域 ID → カード ID 5 枚）。</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> LastDecks { get; }

        public CampaignState(long seed, bool classicMode, IReadOnlyDictionary<string, int> collection, IEnumerable<string> unlockedRegions,
            IReadOnlyDictionary<string, OpponentRecord> records, IReadOnlyDictionary<string, IReadOnlyList<string>> lastDecks)
        {
            Seed = seed;
            ClassicMode = classicMode;
            var col = new Dictionary<string, int>();
            if (collection != null)
            {
                foreach (var kv in collection)
                {
                    if (kv.Value > 0) col[kv.Key] = kv.Value;
                }
            }
            Collection = new ReadOnlyDictionary<string, int>(col);

            var unlocked = new List<string>();
            foreach (var r in CampaignData.Regions)
            {
                if (unlockedRegions != null && Contains(unlockedRegions, r.Id)) unlocked.Add(r.Id);
            }
            UnlockedRegions = new ReadOnlyCollection<string>(unlocked);

            var rec = new Dictionary<string, OpponentRecord>();
            if (records != null)
            {
                foreach (var kv in records)
                {
                    if (kv.Value != null && !kv.Value.IsEmpty) rec[kv.Key] = kv.Value;
                }
            }
            Records = new ReadOnlyDictionary<string, OpponentRecord>(rec);

            var decks = new Dictionary<string, IReadOnlyList<string>>();
            if (lastDecks != null)
            {
                foreach (var kv in lastDecks)
                {
                    if (kv.Value == null) continue;
                    decks[kv.Key] = new ReadOnlyCollection<string>(new List<string>(kv.Value));
                }
            }
            LastDecks = new ReadOnlyDictionary<string, IReadOnlyList<string>>(decks);
        }

        static bool Contains(IEnumerable<string> list, string id)
        {
            foreach (var s in list)
            {
                if (s == id) return true;
            }
            return false;
        }

        /// <summary>相手の記録（なければ空の記録）。</summary>
        public OpponentRecord Record(string opponentId) =>
            opponentId != null && Records.TryGetValue(opponentId, out var r) ? r : OpponentRecord.Empty;

        /// <summary>手持ちの枚数（持っていなければ 0）。</summary>
        public int Count(string cardId) => cardId != null && Collection.TryGetValue(cardId, out int n) ? n : 0;

        /// <summary>手持ちの合計枚数（重複を数える）。</summary>
        public int TotalCards
        {
            get
            {
                int n = 0;
                foreach (var kv in Collection) n += kv.Value;
                return n;
            }
        }

        public bool IsUnlocked(string regionId)
        {
            foreach (var id in UnlockedRegions)
            {
                if (id == regionId) return true;
            }
            return false;
        }

        public CampaignState WithCollection(IReadOnlyDictionary<string, int> collection) =>
            new CampaignState(Seed, ClassicMode, collection, UnlockedRegions, Records, LastDecks);

        public CampaignState WithClassicMode(bool on) => new CampaignState(Seed, on, Collection, UnlockedRegions, Records, LastDecks);

        public CampaignState WithUnlocked(IEnumerable<string> regions) =>
            new CampaignState(Seed, ClassicMode, Collection, regions, Records, LastDecks);

        public CampaignState WithRecord(string opponentId, OpponentRecord record)
        {
            var rec = new Dictionary<string, OpponentRecord>();
            foreach (var kv in Records) rec[kv.Key] = kv.Value;
            rec[opponentId ?? throw new ArgumentNullException(nameof(opponentId))] = record ?? OpponentRecord.Empty;
            return new CampaignState(Seed, ClassicMode, Collection, UnlockedRegions, rec, LastDecks);
        }

        /// <summary>星域の最後のデッキを覚える（検証はしない。CampaignRules.ValidateDeck を通したものを渡すこと）。</summary>
        public CampaignState WithLastDeck(string regionId, IReadOnlyList<string> deck)
        {
            var decks = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var kv in LastDecks) decks[kv.Key] = kv.Value;
            decks[regionId ?? throw new ArgumentNullException(nameof(regionId))] = deck ?? throw new ArgumentNullException(nameof(deck));
            return new CampaignState(Seed, ClassicMode, Collection, UnlockedRegions, Records, decks);
        }
    }

    static class CampaignUtil
    {
        /// <summary>カードのカタログ順（不明な ID は後ろに ID 順）。</summary>
        public static int CatalogIndex(string id)
        {
            var all = Data.CardCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id) return i;
            }
            return int.MaxValue;
        }

        public static int CompareCatalog(string a, string b)
        {
            int c = CatalogIndex(a).CompareTo(CatalogIndex(b));
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        public static List<string> SortedDistinct(IEnumerable<string> ids)
        {
            var list = new List<string>();
            if (ids == null) return list;
            foreach (var id in ids)
            {
                if (id != null && !list.Contains(id)) list.Add(id);
            }
            list.Sort(CompareCatalog);
            return list;
        }

        public static List<string> SortedKeys<T>(IReadOnlyDictionary<string, T> dict)
        {
            var keys = new List<string>();
            foreach (var kv in dict) keys.Add(kv.Key);
            keys.Sort(CompareCatalog);
            return keys;
        }
    }
}
