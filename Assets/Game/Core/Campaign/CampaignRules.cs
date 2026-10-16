using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Edgewise.Core.Data;

namespace Edgewise.Core.Campaign
{
    /// <summary>デッキの問題の種類。</summary>
    public enum DeckProblem
    {
        /// <summary>5 枚ではない。</summary>
        WrongCount = 0,

        /// <summary>同じカードが 2 枚以上入っている（1 つのデッキに同じカードは 1 枚まで）。</summary>
        Duplicate = 1,

        /// <summary>存在しないカード ID。</summary>
        UnknownCard = 2,

        /// <summary>手持ちにないカード。</summary>
        NotOwned = 3,

        /// <summary>合計レベルが上限を超えている。</summary>
        OverLevelCap = 4,
    }

    /// <summary>デッキの問題 1 つ（CardId はカードに関する問題のときだけ）。</summary>
    public readonly struct DeckIssue : IEquatable<DeckIssue>
    {
        public readonly DeckProblem Problem;
        public readonly string CardId;

        public DeckIssue(DeckProblem problem, string cardId = null)
        {
            Problem = problem;
            CardId = cardId;
        }

        public bool Equals(DeckIssue other) => Problem == other.Problem && CardId == other.CardId;

        public override bool Equals(object obj) => obj is DeckIssue o && Equals(o);

        public override int GetHashCode() => (int)Problem * 31 + (CardId == null ? 0 : CardId.GetHashCode());

        public override string ToString() => Problem + (CardId == null ? "" : "(" + CardId + ")");
    }

    /// <summary>対戦結果の種類（プレイヤーから見て）。</summary>
    public enum MatchResultKind
    {
        Win = 0,
        Loss = 1,
        Draw = 2,
    }

    /// <summary>終局した対戦の判定（プレイヤーから見た勝敗・完勝・もらえる枚数）。</summary>
    public readonly struct MatchOutcome
    {
        public readonly MatchResultKind Kind;

        /// <summary>完勝: 勝って、終局時に相手の盤上のカードが 0 枚。</summary>
        public readonly bool Perfect;

        /// <summary>相手の 5 枚から選べる枚数（勝ち 1、完勝 2、それ以外 0）。</summary>
        public readonly int RewardCount;

        public MatchOutcome(MatchResultKind kind, bool perfect)
        {
            Kind = kind;
            Perfect = kind == MatchResultKind.Win && perfect;
            RewardCount = kind != MatchResultKind.Win ? 0 : Perfect ? CampaignRules.PerfectRewardCount : CampaignRules.WinRewardCount;
        }

        public override string ToString() => Kind + (Perfect ? " (perfect)" : "") + " x" + RewardCount;
    }

    /// <summary>ApplyMatch の結果。</summary>
    public sealed class MatchApplication
    {
        /// <summary>結果を反映した新しい進行。</summary>
        public CampaignState State { get; }

        /// <summary>原作モードで相手に取られたカード（なければ null）。</summary>
        public string TakenCard { get; }

        /// <summary>原作モードで本来は取られるはずだったが、手持ちが 5 種類を切るので守られたカード（なければ null）。</summary>
        public string ProtectedCard { get; }

        /// <summary>この結果で新しく開いた星域（なければ null）。</summary>
        public Region UnlockedRegion { get; }

        /// <summary>この結果で初めて主に勝った（星域を抜けた）か。台詞 RegionClear を出す。</summary>
        public bool ClearedRegion { get; }

        public MatchApplication(CampaignState state, string takenCard, string protectedCard, Region unlockedRegion, bool clearedRegion)
        {
            State = state;
            TakenCard = takenCard;
            ProtectedCard = protectedCard;
            UnlockedRegion = unlockedRegion;
            ClearedRegion = clearedRegion;
        }
    }

    /// <summary>
    /// 星図巡りのルール（DESIGN §12-3 / §12-4）。すべて純関数（入力を変更せず、同じ入力には同じ結果を返す）。
    /// </summary>
    public static class CampaignRules
    {
        public const int DeckSize = GameConstants.HandSize;
        public const int WinRewardCount = 1;
        public const int PerfectRewardCount = 2;

        /// <summary>原作モードで守られる手持ちの種類数（取られて 5 種類を切るなら取られない）。</summary>
        public const int MinimumDistinctCards = DeckSize;

        /// <summary>相手のデッキ選びで列挙する持ちカードの最大種類数（原作モードで増えすぎたときの計算量の上限）。</summary>
        public const int MaxOpponentPool = 16;

        // ------------------------------------------------------------------
        // 開始・解放
        // ------------------------------------------------------------------

        /// <summary>新しく始める（はじめの手持ち 7 枚、星域 1 だけ解放）。seed は相手のデッキ選びなどに使う。</summary>
        public static CampaignState NewGame(long seed = 0, bool classicMode = false)
        {
            var col = new Dictionary<string, int>();
            foreach (var id in CampaignData.StarterCards) col[id] = col.TryGetValue(id, out int n) ? n + 1 : 1;
            return new CampaignState(seed, classicMode, col, new[] { CampaignData.Regions[0].Id }, null, null);
        }

        /// <summary>その相手と対戦できるか（相手の星域が解放済み）。</summary>
        public static bool CanChallenge(CampaignState state, Opponent opponent) =>
            state != null && opponent != null && state.IsUnlocked(opponent.RegionId);

        // ------------------------------------------------------------------
        // デッキ
        // ------------------------------------------------------------------

        /// <summary>カード ID の合計レベル（不明な ID は 0）。</summary>
        public static int TotalLevel(IReadOnlyList<string> deck)
        {
            int total = 0;
            if (deck == null) return 0;
            foreach (var id in deck)
            {
                if (id != null && CardCatalog.ById.TryGetValue(id, out var def)) total += def.Level;
            }
            return total;
        }

        /// <summary>
        /// デッキの検証（DESIGN §12-4）: 5 枚、重複なし、存在するカード、手持ちにある、合計レベルが上限以下。
        /// 問題がなければ空のリスト。問題はデッキの並び順に、種類ごとにまとめずに列挙する。
        /// </summary>
        public static List<DeckIssue> ValidateDeck(IReadOnlyDictionary<string, int> collection, IReadOnlyList<string> deck, int levelCap)
        {
            var issues = new List<DeckIssue>();
            if (deck == null)
            {
                issues.Add(new DeckIssue(DeckProblem.WrongCount));
                return issues;
            }
            if (deck.Count != DeckSize) issues.Add(new DeckIssue(DeckProblem.WrongCount));
            var seen = new List<string>();
            foreach (var id in deck)
            {
                if (id == null || !CardCatalog.ById.ContainsKey(id))
                {
                    issues.Add(new DeckIssue(DeckProblem.UnknownCard, id));
                    continue;
                }
                if (seen.Contains(id))
                {
                    if (!issues.Contains(new DeckIssue(DeckProblem.Duplicate, id))) issues.Add(new DeckIssue(DeckProblem.Duplicate, id));
                    continue;
                }
                seen.Add(id);
                if (collection == null || !collection.TryGetValue(id, out int n) || n <= 0) issues.Add(new DeckIssue(DeckProblem.NotOwned, id));
            }
            if (TotalLevel(deck) > levelCap) issues.Add(new DeckIssue(DeckProblem.OverLevelCap));
            return issues;
        }

        public static List<DeckIssue> ValidateDeck(CampaignState state, Region region, IReadOnlyList<string> deck) =>
            ValidateDeck(state?.Collection, deck, (region ?? throw new ArgumentNullException(nameof(region))).DeckLevelCap);

        /// <summary>
        /// 「おまかせ」用のカードの点数（DESIGN §12-4）: 辺の合計 ×10 ＋ 最も弱い辺 ×4（弱点の少なさ）＋ 星域ルールとの相性
        /// （Same Wall: A の辺 1 つにつき +6、Elemental: 属性つき +12、Pivot: 辺の最大値 ×2）。
        /// </summary>
        public static int CardScore(CardDef card, RuleSet rules)
        {
            var s = card.Sides;
            int min = s.Top, max = s.Top;
            for (int d = 1; d < 4; d++)
            {
                if (s[d] < min) min = s[d];
                if (s[d] > max) max = s[d];
            }
            int score = s.Sum * 10 + min * 4;
            if (rules != null)
            {
                if (rules.Same && rules.SameWall)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        if (s[d] == GameConstants.WallRank) score += 6;
                    }
                }
                if (rules.Elemental && card.Element.HasValue) score += 12;
                if (rules.Pivot) score += max * 2;
            }
            return score;
        }

        /// <summary>
        /// 「おまかせ」: 手持ちから、合計レベルが levelCap 以下で CardScore の合計が最大の 5 枚を選ぶ（ナップサック問題を DP で厳密に解く）。
        /// 同点なら合計レベルの低い方、さらに同点ならカタログ順で前のカードを含む方。組めなければ null。結果はカタログ順。
        /// </summary>
        public static string[] AutoDeck(IReadOnlyDictionary<string, int> collection, RuleSet rules, int levelCap)
        {
            if (collection == null || levelCap < 0) return null;
            var items = new List<CardDef>();
            foreach (var c in CardCatalog.All)
            {
                if (collection.TryGetValue(c.Id, out int n) && n > 0) items.Add(c);
            }
            if (items.Count < DeckSize) return null;

            // best[k, L] = k 枚で合計レベルちょうど L のときの最大点（未到達は -1）。後ろのカードから詰め、前のカードを優先して取る
            int m = items.Count;
            int cap = levelCap > DeckSize * Budget.MaxLevel ? DeckSize * Budget.MaxLevel : levelCap;
            const int None = -1;
            var best = new int[m + 1, DeckSize + 1, cap + 1];
            for (int i = 0; i <= m; i++)
                for (int k = 0; k <= DeckSize; k++)
                    for (int l = 0; l <= cap; l++)
                        best[i, k, l] = None;
            // best[i, k, L]: items[i..] から k 枚
            for (int i = m; i >= 0; i--) best[i, 0, 0] = 0;
            for (int i = m - 1; i >= 0; i--)
            {
                int lv = items[i].Level;
                int sc = CardScore(items[i], rules);
                for (int k = 1; k <= DeckSize; k++)
                {
                    for (int l = 0; l <= cap; l++)
                    {
                        int skip = best[i + 1, k, l];
                        int take = l >= lv && best[i + 1, k - 1, l - lv] != None ? best[i + 1, k - 1, l - lv] + sc : None;
                        best[i, k, l] = take >= skip ? take : skip;
                    }
                }
            }
            int bestL = -1;
            for (int l = 0; l <= cap; l++)
            {
                if (best[0, DeckSize, l] == None) continue;
                if (bestL < 0 || best[0, DeckSize, l] > best[0, DeckSize, bestL]) bestL = l;
            }
            if (bestL < 0) return null;

            var deck = new List<string>();
            int kk = DeckSize, ll = bestL;
            for (int i = 0; i < m && kk > 0; i++)
            {
                int lv = items[i].Level;
                if (ll >= lv && best[i + 1, kk - 1, ll - lv] != None && best[i + 1, kk - 1, ll - lv] + CardScore(items[i], rules) == best[i, kk, ll])
                {
                    deck.Add(items[i].Id);
                    kk--;
                    ll -= lv;
                }
            }
            return deck.ToArray();
        }

        public static string[] AutoDeck(CampaignState state, Region region) =>
            AutoDeck(state?.Collection, (region ?? throw new ArgumentNullException(nameof(region))).Rules, region.DeckLevelCap);

        // ------------------------------------------------------------------
        // 相手のデッキ
        // ------------------------------------------------------------------

        /// <summary>相手の今の持ちカード（元の持ちカード → 原作モードで取ったカード（カタログ順）。重複なし）。</summary>
        public static List<string> OpponentPool(CampaignState state, Opponent opponent)
        {
            var pool = new List<string>(opponent.Cards);
            if (state != null)
            {
                var rec = state.Record(opponent.Id);
                foreach (var id in CampaignUtil.SortedKeys(rec.Captured))
                {
                    if (!pool.Contains(id) && CardCatalog.ById.ContainsKey(id)) pool.Add(id);
                }
            }
            return pool;
        }

        /// <summary>
        /// 相手のデッキを選ぶ（DESIGN §12-3）: pool の 5 枚の組のうち合計レベルが levelCap 以下のものを列挙し、
        /// 組める最大の合計レベルから slack 以内のものの中から seed で一様に 1 つ選ぶ（同じ入力なら常に同じ）。
        /// pool が MaxOpponentPool 種類を超えるときは、前から MaxOpponentPool 種類だけを使う。組めなければ null。
        /// </summary>
        public static string[] ChooseOpponentDeck(IReadOnlyList<string> pool, int levelCap, int slack, long seed)
        {
            if (pool == null) return null;
            var ids = new List<string>();
            foreach (var id in pool)
            {
                if (id != null && CardCatalog.ById.ContainsKey(id) && !ids.Contains(id)) ids.Add(id);
                if (ids.Count >= MaxOpponentPool) break;
            }
            int n = ids.Count;
            if (n < DeckSize) return null;
            var levels = new int[n];
            for (int i = 0; i < n; i++) levels[i] = CardCatalog.ById[ids[i]].Level;

            var combos = new List<int[]>();
            var totals = new List<int>();
            int maxTotal = -1;
            var idx = new int[DeckSize];
            for (int i = 0; i < DeckSize; i++) idx[i] = i;
            while (true)
            {
                int total = 0;
                for (int i = 0; i < DeckSize; i++) total += levels[idx[i]];
                if (total <= levelCap)
                {
                    combos.Add((int[])idx.Clone());
                    totals.Add(total);
                    if (total > maxTotal) maxTotal = total;
                }
                // 次の組（辞書順）
                int p = DeckSize - 1;
                while (p >= 0 && idx[p] == n - DeckSize + p) p--;
                if (p < 0) break;
                idx[p]++;
                for (int q = p + 1; q < DeckSize; q++) idx[q] = idx[q - 1] + 1;
            }
            if (combos.Count == 0) return null;

            var candidates = new List<int[]>();
            for (int i = 0; i < combos.Count; i++)
            {
                if (totals[i] >= maxTotal - slack) candidates.Add(combos[i]);
            }
            uint rng = Rng.SeedToState(seed);
            var pick = candidates[Rng.NextInt(rng, candidates.Count, out _)];
            var deck = new string[DeckSize];
            for (int i = 0; i < DeckSize; i++) deck[i] = ids[pick[i]];
            return deck;
        }

        public static string[] ChooseOpponentDeck(CampaignState state, Opponent opponent, long seed) =>
            ChooseOpponentDeck(OpponentPool(state, opponent), opponent.DeckLevelCap, opponent.DeckSlack, seed);

        /// <summary>
        /// 次の対戦の seed（相手のデッキ選び・先攻・CPU の乱数の元）。進行の Seed・相手・その相手との対戦数から決まる。
        /// </summary>
        public static long MatchSeed(CampaignState state, Opponent opponent)
        {
            int index = CampaignData.Opponents.IndexOf(opponent);
            long games = state.Record(opponent.Id).Games;
            unchecked
            {
                uint s = Rng.SeedToState(state.Seed * 1000003L + index * 7919L + games * 104729L);
                uint a = Rng.NextUInt32(s, out s);
                uint b = Rng.NextUInt32(s, out _);
                return (long)(((ulong)a << 21) ^ b);
            }
        }

        /// <summary>対戦の設定（星域のルール、ゲーム本体の Pivot トークン数）。プレイヤーは P0、相手は P1。</summary>
        public static GameConfig MatchConfig(Region region, IReadOnlyList<string> playerDeck, IReadOnlyList<string> opponentDeck, long seed)
        {
            var tokens = GameConstants.MatchPivotTokens(region.Rules);
            return new GameConfig(region.Rules, playerDeck, opponentDeck, seed, CardCatalog.ById, tokens.First, tokens.Second);
        }

        // ------------------------------------------------------------------
        // 結果と報酬
        // ------------------------------------------------------------------

        /// <summary>終局した局面を、player から見た勝敗・完勝・もらえる枚数に変換する。終局していなければ例外。</summary>
        public static MatchOutcome Judge(GameState final, Player player)
        {
            var result = GameEngine.Result(final ?? throw new ArgumentNullException(nameof(final)));
            if (!result.HasValue) throw new ArgumentException("the game has not ended", nameof(final));
            if (result.Value == GameResult.Draw) return new MatchOutcome(MatchResultKind.Draw, false);
            bool won = (int)result.Value == (int)player;
            if (!won) return new MatchOutcome(MatchResultKind.Loss, false);
            int theirs = 0;
            foreach (var pc in final.Board)
            {
                if (pc.HasValue && pc.Value.Owner != player) theirs++;
            }
            return new MatchOutcome(MatchResultKind.Win, theirs == 0);
        }

        /// <summary>
        /// 原作モードで負けたときに相手が持っていくカード: プレイヤーの 5 枚のうち最もレベルの高いもの
        /// （同じなら辺の合計が大きいもの、さらに同じならデッキの前のもの）。手持ちにないカードは対象外。
        /// 取ると手持ちが MinimumDistinctCards 種類を切るなら取られない（protectedCard にそのカードを返す）。
        /// </summary>
        public static string ClassicTake(IReadOnlyDictionary<string, int> collection, IReadOnlyList<string> playerDeck, out string protectedCard)
        {
            protectedCard = null;
            if (collection == null || playerDeck == null) return null;
            string best = null;
            CardDef bestDef = null;
            foreach (var id in playerDeck)
            {
                if (id == null || !CardCatalog.ById.TryGetValue(id, out var def)) continue;
                if (!collection.TryGetValue(id, out int n) || n <= 0) continue;
                if (bestDef == null || def.Level > bestDef.Level || (def.Level == bestDef.Level && def.Sides.Sum > bestDef.Sides.Sum))
                {
                    best = id;
                    bestDef = def;
                }
            }
            if (best == null) return null;
            int distinct = 0;
            foreach (var kv in collection)
            {
                if (kv.Value > 0) distinct++;
            }
            bool keepsKind = collection[best] >= 2;
            if (!keepsKind && distinct - 1 < MinimumDistinctCards)
            {
                protectedCard = best;
                return null;
            }
            return best;
        }

        /// <summary>
        /// 対戦の結果を進行に反映する（DESIGN §12-3）。
        /// - 勝ち: picks（相手が使った 5 枚から outcome.RewardCount 枚以下、重複なし）を手持ちに加える。相手が原作モードで取っていたカードなら取り返す。
        ///   主に勝てば次の星域を開く。
        /// - 負け: 原作モードなら ClassicTake のカードを相手に渡す。
        /// - 引き分け: 勝敗数だけ数える。
        /// どの結果でも、相手の 5 枚を「見たことのあるカード」に加え、プレイヤーのデッキを星域の最後のデッキとして覚える。
        /// picks が不正なら ArgumentException（UI 側の誤り）。
        /// </summary>
        public static MatchApplication ApplyMatch(CampaignState state, Opponent opponent, IReadOnlyList<string> playerDeck,
            IReadOnlyList<string> opponentDeck, MatchOutcome outcome, IReadOnlyList<string> picks)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (opponent == null) throw new ArgumentNullException(nameof(opponent));
            if (opponentDeck == null) throw new ArgumentNullException(nameof(opponentDeck));
            picks = picks ?? Array.Empty<string>();
            if (outcome.Kind != MatchResultKind.Win && picks.Count > 0) throw new ArgumentException("picks are only allowed after a win", nameof(picks));
            if (picks.Count > outcome.RewardCount) throw new ArgumentException("too many picks: " + picks.Count, nameof(picks));
            for (int i = 0; i < picks.Count; i++)
            {
                var id = picks[i];
                if (id == null || !Contains(opponentDeck, id)) throw new ArgumentException("pick is not in the opponent's deck: " + id, nameof(picks));
                for (int j = 0; j < i; j++)
                {
                    if (picks[j] == id) throw new ArgumentException("duplicate pick: " + id, nameof(picks));
                }
            }

            var region = CampaignData.RegionOf(opponent);
            var collection = Copy(state.Collection);
            var rec = state.Record(opponent.Id);
            var captured = Copy(rec.Captured);
            var seen = new List<string>(rec.Seen);
            foreach (var id in opponentDeck)
            {
                if (id != null && CardCatalog.ById.ContainsKey(id)) seen.Add(id);
            }

            string taken = null, guarded = null;
            Region unlocked = null;
            bool cleared = false;
            int wins = rec.Wins, losses = rec.Losses, draws = rec.Draws;
            var unlockedRegions = new List<string>(state.UnlockedRegions);

            switch (outcome.Kind)
            {
                case MatchResultKind.Win:
                    wins++;
                    foreach (var id in picks)
                    {
                        collection[id] = (collection.TryGetValue(id, out int n) ? n : 0) + 1;
                        if (captured.TryGetValue(id, out int c))
                        {
                            if (c <= 1) captured.Remove(id);
                            else captured[id] = c - 1;
                        }
                    }
                    if (opponent.IsBoss)
                    {
                        cleared = rec.Wins == 0;
                        var next = CampaignData.NextRegion(region);
                        if (next != null && !unlockedRegions.Contains(next.Id))
                        {
                            unlockedRegions.Add(next.Id);
                            unlocked = next;
                        }
                    }
                    break;
                case MatchResultKind.Loss:
                    losses++;
                    if (state.ClassicMode)
                    {
                        taken = ClassicTake(collection, playerDeck, out guarded);
                        if (taken != null)
                        {
                            int n = collection[taken];
                            if (n <= 1) collection.Remove(taken);
                            else collection[taken] = n - 1;
                            captured[taken] = (captured.TryGetValue(taken, out int c) ? c : 0) + 1;
                        }
                    }
                    break;
                default:
                    draws++;
                    break;
            }

            var next0 = new CampaignState(state.Seed, state.ClassicMode, collection, unlockedRegions, state.Records, state.LastDecks)
                .WithRecord(opponent.Id, new OpponentRecord(wins, losses, draws, captured, seen));
            if (region != null && playerDeck != null && playerDeck.Count == DeckSize)
                next0 = next0.WithLastDeck(region.Id, new ReadOnlyCollection<string>(new List<string>(playerDeck)));
            return new MatchApplication(next0, taken, guarded, unlocked, cleared);
        }

        /// <summary>
        /// 相手選択画面の「持っているカード」: 相手の今の持ちカードのうち、見たことのあるカードは ID、まだのものは null（「？」で表示）。
        /// </summary>
        public static string[] VisibleOpponentCards(CampaignState state, Opponent opponent)
        {
            var pool = OpponentPool(state, opponent);
            var seen = state.Record(opponent.Id).Seen;
            var result = new string[pool.Count];
            for (int i = 0; i < pool.Count; i++) result[i] = Contains(seen, pool[i]) ? pool[i] : null;
            return result;
        }

        static Dictionary<string, int> Copy(IReadOnlyDictionary<string, int> src)
        {
            var d = new Dictionary<string, int>();
            foreach (var kv in src) d[kv.Key] = kv.Value;
            return d;
        }

        static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id) return true;
            }
            return false;
        }
    }
}
