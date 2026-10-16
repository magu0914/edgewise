using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;

namespace Edgewise.Sim
{
    /// <summary>
    /// 星図巡りの進行バランスの計測（DESIGN §12-9、`--campaign`）。
    /// 相手ごとに「その時点の標準的な手持ち」から上限内の最善デッキ（おまかせ）を組み、プレイヤー役の AI（既定 Normal）が
    /// 星域のルールで相手と対戦したときの勝率を測る。
    ///
    /// 標準的な手持ち: はじめの手持ち ＋ それより前の相手（星図の順。同じ星域の主の前の 2 人も含む）に 1 回ずつ勝って
    /// 1 枚ずつ（--picks で変更）もらったもの。もらうカードは、その相手の持ちカードのうちまだ持っていない最も強いもの
    /// （レベル → 辺の合計 → カタログ順）。
    ///
    /// 対戦: 配牌ごとに相手のデッキを seed で選び（CampaignRules.ChooseOpponentDeck）、席を入れ替えて 2 局。
    /// Pivot トークンはゲーム本体の既定（GameConstants.MatchPivotTokens）。Open OFF の決定化は両者とも CardPoolModel.Shared で、
    /// プレイヤー役は相手の持ちカード（相手選択画面で見えるもの）を候補に、相手は全 50 枚を候補にする。
    /// </summary>
    static class CampaignSim
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>DESIGN §12-9 の目標（引き分けを除いた勝率、%）。[星域-1] = (主以外の下限, 上限, 主の下限, 上限)。</summary>
        static readonly (int Lo, int Hi, int BossLo, int BossHi)[] Targets =
        {
            (70, 80, 55, 65), (65, 75, 50, 60), (55, 65, 45, 55), (50, 60, 35, 45),
        };

        public static (int Lo, int Hi) Target(Opponent o)
        {
            var t = Targets[CampaignData.RegionOf(o).Number - 1];
            return o.IsBoss ? (t.BossLo, t.BossHi) : (t.Lo, t.Hi);
        }

        sealed class Row
        {
            public Opponent Opponent;
            public string[] PlayerDeck;
            public int CollectionSize;
            public int Wins, Losses, Draws, Perfect, Games, FirstWins, Decisive;
            public long OppDeckLevels, OppDecks;
            public long SuddenDeathRounds;
            public double Seconds;
        }

        /// <summary>
        /// 調整用の上書き（--tune "ID:ai=SPEC;cards=a+b+c;slack=N"、複数は | 区切り）。上書きした相手は標準的な手持ちの計算にも使う。
        /// ai の SPEC は --match と同じ書式で、相手の性格はそのまま引き継ぐ（persona=none で外す）。
        /// </summary>
        public static List<Opponent> Effective(string tune)
        {
            var list = new List<Opponent>(CampaignData.Opponents);
            if (string.IsNullOrEmpty(tune)) return list;
            foreach (var entry in tune.Split('|'))
            {
                int colon = entry.IndexOf(':');
                if (colon < 0) throw new ArgumentException("--tune expects ID:key=value;...");
                string id = entry.Substring(0, colon).Trim();
                int idx = list.FindIndex(o => o.Id == id);
                if (idx < 0) throw new ArgumentException("unknown opponent " + id);
                var o = list[idx];
                var profile = o.Profile;
                var cards = o.Cards.ToArray();
                int slack = o.DeckSlack;
                foreach (var kv in entry.Substring(colon + 1).Split(';'))
                {
                    var parts = kv.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) throw new ArgumentException("bad --tune entry " + kv);
                    switch (parts[0].Trim())
                    {
                        case "ai":
                            profile = PlayerSpec.Parse(parts[1]).Profile.WithPersonality(o.Profile.Personality, o.Profile.PersonalityAsTieBreak);
                            break;
                        case "persona":
                            profile = parts[1] == "none" ? profile.WithPersonality(null) : profile;
                            break;
                        case "cards": cards = parts[1].Split('+'); break;
                        case "slack": slack = int.Parse(parts[1], Inv); break;
                        default: throw new ArgumentException("unknown --tune key " + parts[0]);
                    }
                }
                list[idx] = new Opponent(o.Id, o.Name, o.Title, o.RegionId, o.IsBoss, o.DeckLevelCap, o.StrengthLabel, o.PersonalityLabel,
                    profile, cards, slack, o.Lines);
            }
            return list;
        }

        /// <summary>その相手と戦う時点の標準的な手持ち。</summary>
        public static Dictionary<string, int> StandardCollection(IReadOnlyList<Opponent> all, Opponent target, int picksPerOpponent)
        {
            var col = new Dictionary<string, int>();
            foreach (var id in CampaignData.StarterCards) col[id] = col.TryGetValue(id, out int n) ? n + 1 : 1;
            foreach (var o in all)
            {
                if (o.Id == target.Id) break;
                var reward = o.Cards.Select(id => CardCatalog.ById[id])
                    .Where(c => !col.ContainsKey(c.Id))
                    .OrderByDescending(c => c.Level).ThenByDescending(c => c.Sides.Sum).ThenBy(c => CardCatalog.All.IndexOf(c))
                    .Take(picksPerOpponent);
                foreach (var c in reward) col[c.Id] = 1;
            }
            return col;
        }

        public static int Run(int deals, long seed, int threads, int picks, string playerName, string only, string tune, string outPath,
            string[] args)
        {
            var playerProfile = PlayerSpec.Parse(playerName).Profile ?? throw new ArgumentException("the player must be an AI");
            var all = Effective(tune);
            var opponents = all.Where(o => string.IsNullOrEmpty(only) || only.Split(',').Contains(o.Id)).ToList();
            if (opponents.Count == 0) throw new ArgumentException("no opponents match --only " + only);

            var header = new StringBuilder();
            header.AppendLine("## Edgewise.Sim --campaign");
            header.AppendLine();
            header.AppendLine("- Command: `dotnet run -c Release --project dotnet/Edgewise.Sim -- " + string.Join(" ", args) + "`");
            header.AppendLine("- Deals: " + deals + " per opponent (x2 seats), seed " + seed + ", threads " + threads + ", picks per beaten opponent " + picks);
            header.AppendLine("- Player: " + playerProfile);
#if DEBUG
            header.AppendLine("- WARNING: Debug build. Use -c Release.");
#endif
            Console.WriteLine(header.ToString());

            var rows = new List<Row>();
            foreach (var o in opponents)
            {
                var row = RunOpponent(all, o, playerProfile, deals, seed, threads, picks);
                rows.Add(row);
                var (lo, hi) = Target(o);
                Console.Error.WriteLine(o.Id.PadRight(24) + " " + row.Wins + "-" + row.Losses + "-" + row.Draws + "  win "
                    + Pct(row.Wins, row.Wins + row.Losses) + " (target " + lo + "-" + hi + ")  draws " + Pct(row.Draws, row.Games)
                    + "  " + row.Seconds.ToString("0.0", Inv) + " s");
            }

            var md = Build(rows, header.ToString(), playerProfile, picks);
            Console.WriteLine(md);
            if (outPath != null)
            {
                System.IO.File.WriteAllText(outPath, md, new UTF8Encoding(false));
                Console.Error.WriteLine("wrote " + System.IO.Path.GetFullPath(outPath));
            }
            return 0;
        }

        static Row RunOpponent(IReadOnlyList<Opponent> all, Opponent o, AiProfile playerProfile, int deals, long seed, int threads, int picks)
        {
            var region = CampaignData.RegionOf(o);
            var col = StandardCollection(all, o, picks);
            var playerDeck = CampaignRules.AutoDeck(col, region.Rules, region.DeckLevelCap)
                ?? throw new InvalidOperationException("cannot build a deck for " + o.Id);
            var playerPool = o.Cards.Select(id => CardCatalog.ById[id]).ToList(); // 相手の持ちカード（公開情報）
            var oppPool = CardCatalog.All;
            var tokens = GameConstants.MatchPivotTokens(region.Rules);
            int oppIndex = CampaignData.Opponents.IndexOf(CampaignData.OpponentById(o.Id));

            var perDeal = new Row[deals];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, deals, new ParallelOptions { MaxDegreeOfParallelism = threads }, () => new SearchWorkspace(), (i, _, ws) =>
            {
                var r = new Row();
                uint rng = Rng.SeedToState(seed * 1000003L + oppIndex * 7919L + i);
                long dealSeed = Rng.NextUInt32(rng, out rng);
                long gameSeed = Rng.NextUInt32(rng, out rng);
                var oppDeck = CampaignRules.ChooseOpponentDeck(o.Cards, o.DeckLevelCap, o.DeckSlack, dealSeed);
                r.OppDeckLevels += CampaignRules.TotalLevel(oppDeck);
                r.OppDecks++;
                for (int playerSeat = 0; playerSeat < 2; playerSeat++)
                {
                    var deck0 = playerSeat == 0 ? playerDeck : oppDeck;
                    var deck1 = playerSeat == 0 ? oppDeck : playerDeck;
                    var state = GameEngine.CreateGame(new GameConfig(region.Rules, deck0, deck1, gameSeed, CardCatalog.ById, tokens.First, tokens.Second));
                    var first = state.FirstPlayer;
                    uint moveRng = Rng.SeedToState(gameSeed ^ (playerSeat * 0x7777L));
                    while (state.Phase == Phase.Playing)
                    {
                        bool playerTurn = (int)state.Turn == playerSeat;
                        long moveSeed = Rng.NextUInt32(moveRng, out moveRng);
                        var move = playerTurn
                            ? new SearchSession(state, state.Turn, playerProfile, moveSeed, playerPool, ws, CardPoolModel.Shared).Run()
                            : new SearchSession(state, state.Turn, o.Profile, moveSeed, oppPool, ws, CardPoolModel.Shared).Run();
                        state = GameEngine.ApplyMoveFast(state, move);
                    }
                    var outcome = CampaignRules.Judge(state, (Player)playerSeat);
                    r.Games++;
                    r.SuddenDeathRounds += state.SuddenDeathRound;
                    if (outcome.Kind == MatchResultKind.Win) r.Wins++;
                    else if (outcome.Kind == MatchResultKind.Loss) r.Losses++;
                    else r.Draws++;
                    if (outcome.Perfect) r.Perfect++;
                    var res = GameEngine.Result(state).Value;
                    if (res != GameResult.Draw)
                    {
                        r.Decisive++;
                        if ((Player)(int)res == first) r.FirstWins++;
                    }
                }
                perDeal[i] = r;
                return ws;
            }, _ => { });
            sw.Stop();

            var total = new Row { Opponent = o, PlayerDeck = playerDeck, CollectionSize = col.Count, Seconds = sw.Elapsed.TotalSeconds };
            foreach (var r in perDeal)
            {
                total.Wins += r.Wins;
                total.Losses += r.Losses;
                total.Draws += r.Draws;
                total.Perfect += r.Perfect;
                total.Games += r.Games;
                total.FirstWins += r.FirstWins;
                total.Decisive += r.Decisive;
                total.OppDeckLevels += r.OppDeckLevels;
                total.OppDecks += r.OppDecks;
                total.SuddenDeathRounds += r.SuddenDeathRounds;
            }
            return total;
        }

        static string Pct(long k, long n) => n == 0 ? "-" : (100.0 * k / n).ToString("0.0", Inv) + "%";

        static string PctCi(long k, long n)
        {
            if (n == 0) return "-";
            var (lo, hi) = Report.Wilson(k, n);
            return (100.0 * k / n).ToString("0.0", Inv) + "% [" + (100 * lo).ToString("0.0", Inv) + ", " + (100 * hi).ToString("0.0", Inv) + "]";
        }

        static string Build(List<Row> rows, string header, AiProfile player, int picks)
        {
            var sb = new StringBuilder(header);
            sb.AppendLine();
            sb.AppendLine("#### 星域・相手ごとの勝率（プレイヤー役から見た値。勝率は引き分けを除く。[ ] は Wilson 95% 信頼区間）");
            sb.AppendLine();
            sb.AppendLine("| 星域 | 相手 | AI / 性格 | 相手デッキ Lv（上限） | プレイヤーのデッキ（手持ち種類） | 勝-敗-分 | 勝率 | 目標 | 判定 | 引き分け | 完勝 | 先攻勝率 |");
            sb.AppendLine("|---|---|---|---:|---|---:|---:|---:|:---:|---:|---:|---:|");
            foreach (var r in rows)
            {
                var o = r.Opponent;
                var region = CampaignData.RegionOf(o);
                var (lo, hi) = Target(o);
                int decisive = r.Wins + r.Losses;
                double rate = decisive == 0 ? 0 : 100.0 * r.Wins / decisive;
                string verdict = rate >= lo && rate <= hi ? "○" : "✕";
                string deck = string.Join(" ", r.PlayerDeck.Select(id => id + "(" + CardCatalog.ById[id].Level + ")"));
                sb.AppendLine("| " + region.Number + ". " + region.Name + " | " + (o.IsBoss ? "**主** " : "") + o.Title + " " + o.Name
                    + " | " + o.Profile.Name + " / " + o.PersonalityLabel
                    + " | " + ((double)r.OppDeckLevels / Math.Max(1, r.OppDecks)).ToString("0.0", Inv) + "（" + o.DeckLevelCap + "）"
                    + " | Lv" + CampaignRules.TotalLevel(r.PlayerDeck) + ": " + deck + "（" + r.CollectionSize + "）"
                    + " | " + r.Wins + "-" + r.Losses + "-" + r.Draws
                    + " | " + PctCi(r.Wins, decisive) + " | " + lo + "〜" + hi + "% | " + verdict
                    + " | " + Pct(r.Draws, r.Games) + " | " + Pct(r.Perfect, r.Games) + " | " + Pct(r.FirstWins, r.Decisive) + " |");
            }
            sb.AppendLine();
            sb.AppendLine("プレイヤー役: " + player.Name + "。標準的な手持ち = はじめの 7 枚 ＋ それより前の相手から " + picks
                + " 枚ずつ（その相手のまだ持っていない最も強いカード）。デッキは「おまかせ」（CampaignRules.AutoDeck）。");
            return sb.ToString();
        }
    }
}
