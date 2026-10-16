using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;

namespace Edgewise.Sim
{
    /// <summary>対戦する CPU の指定。"hard"、"hard@200000"（ノード予算の上書き）、"random"。</summary>
    sealed class PlayerSpec
    {
        public readonly string Label;
        public readonly AiProfile Profile; // null = ランダム

        /// <summary>計測専用: 相手の手札を見せる（Open OFF での「情報がない損」の上限を測るため）。</summary>
        public bool Oracle;

        PlayerSpec(string label, AiProfile profile)
        {
            Label = label;
            Profile = profile;
        }

        /// <summary>
        /// "hard"、"hard@200000"、"hard/worlds=16/margin=300/depth=4/noise=0/blind=0/nodes=1000"（調整用の上書き）。
        /// </summary>
        public static PlayerSpec Parse(string s)
        {
            s = s.Trim().ToLowerInvariant();
            if (s == "random") return new PlayerSpec("Random", null);
            var parts = s.Split('/');
            string name = parts[0];
            if (name.StartsWith("opp.", StringComparison.Ordinal))
            {
                // 星図巡りの相手の AI（性格つき）
                var opp = Edgewise.Core.Campaign.CampaignData.OpponentById(name.Substring(4))
                    ?? throw new ArgumentException("unknown campaign opponent " + name);
                if (parts.Length > 1) throw new ArgumentException("options are not supported for opp.*");
                return new PlayerSpec(opp.Id + "(" + opp.Profile.Name + ")", opp.Profile);
            }
            int nodes = -1;
            int at = name.IndexOf('@');
            if (at >= 0)
            {
                nodes = int.Parse(name.Substring(at + 1), CultureInfo.InvariantCulture);
                name = name.Substring(0, at);
            }
            var p = AiProfile.FromName(name);
            string label = p.Name + (nodes > 0 ? "@" + nodes : "");
            if (nodes > 0) p = p.WithMaxNodes(nodes);
            for (int i = 1; i < parts.Length; i++)
            {
                var kv = parts[i].Split('=');
                if (kv.Length != 2) throw new ArgumentException("bad player option " + parts[i]);
                int v = int.Parse(kv[1], CultureInfo.InvariantCulture);
                switch (kv[0])
                {
                    case "nodes": p = p.WithMaxNodes(v); break;
                    case "worlds": p = p.WithWorlds(v); break;
                    case "margin": p = p.WithRootMargin(v); break;
                    case "depth": p = p.WithMaxDepth(v); break;
                    case "noise": p = p.WithNoise(v, p.BlindPercent); break;
                    case "blind": p = p.WithNoise(p.Noise, v); break;
                    case "oracle": label += ""; break;
                    case "tiebreak": p = p.WithTieBreakDepth(v); break;
                    case "truepool": break;
                    case "threat": p = p.WithWeights(p.Weights.With(threat: v)); break;
                    case "threatmove": p = p.WithWeights(p.Weights.With(threatToMove: v)); break;
                    case "tempo": p = p.WithWeights(p.Weights.With(tempo: v)); break;
                    case "hand": p = p.WithWeights(p.Weights.With(handSidePoint: v)); break;
                    case "risk": p = p.WithWeights(p.Weights.With(riskPercent: v)); break;
                    case "pivottoken": p = p.WithWeights(p.Weights.With(pivotToken: v)); break;
                    default: throw new ArgumentException("unknown player option " + kv[0]);
                }
                label += "/" + parts[i];
            }
            var spec = new PlayerSpec(label, p);
            spec.Oracle = label.Contains("/oracle=1");
            return spec;
        }
    }

    /// <summary>集計（1 局分、または足し合わせたもの）。side 0 = 対戦指定の左（A）、1 = 右（B）。</summary>
    sealed class Stats
    {
        public const int Plies = 9;

        public int Games, WinsA, WinsB, Draws, FirstWins, SecondWins;

        /// <summary>
        /// 配牌ごとの 1 局目（A が席 0）だけの先攻勝ち・後攻勝ち・引き分け。ノイズのない CPU 同士（Open ON の Hard 同士など）は
        /// 席を入れ替えた 2 局が鏡写しで同じ結果になり、2 局を独立とみなすと信頼区間が狭く出すぎるので、合否はこちらの区間で見る。
        /// </summary>
        public int FirstWins1, SecondWins1, Draws1;

        /// <summary>Sudden Death の再戦まで行った局の、本戦の先攻の勝ち・負け（再戦では先攻が入れ替わり、Pivot トークンは持ち越す）。</summary>
        public int FirstWinsRematch, SecondWinsRematch;
        public readonly long[] FlipsByCause = new long[4];
        public int MaxChain;
        public long SumMaxChain;
        public long SuddenDeathRounds;
        public readonly long[] Moves = new long[2];
        public readonly long[] Nodes = new long[2];
        public readonly long[] Ticks = new long[2];
        public readonly long[] MaxTicks = new long[2];
        public readonly long[] MaxNodes = new long[2];
        public readonly long[,] MovesByPly = new long[2, Plies];
        public readonly long[,] PivotByPly = new long[2, Plies];
        public readonly long[,] SolvedByPly = new long[2, Plies];

        /// <summary>[side*3 + 最初に読み切った時点の予想（0 勝ち, 1 引き分け, 2 負け）, 実際の結果（同）]。Open ON（世界 1 つ）の本戦のみ。</summary>
        public readonly long[,] FirstSolved = new long[6, 3];

        /// <summary>役割別（0 = 本戦の先攻、1 = 本戦の後攻）の Pivot 使用回数（Sudden Death の再戦を含む 1 局全体）。</summary>
        public readonly long[] PivotByRole = new long[2];

        /// <summary>役割別に、1 局で 1 回以上回転した局の数。</summary>
        public readonly long[] GamesRotatedByRole = new long[2];

        public void Add(Stats o)
        {
            for (int r = 0; r < 2; r++)
            {
                PivotByRole[r] += o.PivotByRole[r];
                GamesRotatedByRole[r] += o.GamesRotatedByRole[r];
            }
            Games += o.Games;
            WinsA += o.WinsA;
            WinsB += o.WinsB;
            Draws += o.Draws;
            FirstWins += o.FirstWins;
            SecondWins += o.SecondWins;
            FirstWins1 += o.FirstWins1;
            SecondWins1 += o.SecondWins1;
            Draws1 += o.Draws1;
            FirstWinsRematch += o.FirstWinsRematch;
            SecondWinsRematch += o.SecondWinsRematch;
            for (int i = 0; i < 4; i++) FlipsByCause[i] += o.FlipsByCause[i];
            MaxChain = Math.Max(MaxChain, o.MaxChain);
            SumMaxChain += o.SumMaxChain;
            SuddenDeathRounds += o.SuddenDeathRounds;
            for (int s = 0; s < 2; s++)
            {
                Moves[s] += o.Moves[s];
                Nodes[s] += o.Nodes[s];
                Ticks[s] += o.Ticks[s];
                MaxTicks[s] = Math.Max(MaxTicks[s], o.MaxTicks[s]);
                MaxNodes[s] = Math.Max(MaxNodes[s], o.MaxNodes[s]);
                for (int p = 0; p < Plies; p++)
                {
                    MovesByPly[s, p] += o.MovesByPly[s, p];
                    PivotByPly[s, p] += o.PivotByPly[s, p];
                    SolvedByPly[s, p] += o.SolvedByPly[s, p];
                }
                for (int k = 0; k < 3; k++)
                {
                    FirstSolved[s * 3 + k, 0] += o.FirstSolved[s * 3 + k, 0];
                    FirstSolved[s * 3 + k, 1] += o.FirstSolved[s * 3 + k, 1];
                    FirstSolved[s * 3 + k, 2] += o.FirstSolved[s * 3 + k, 2];
                }
            }
        }
    }

    /// <summary>1 つの「ルール × 対戦」の計測。</summary>
    sealed class MatchResult
    {
        public RulePreset Preset;
        public PlayerSpec A, B;
        public int Deals;
        public Stats Stats;
        public double Seconds;
    }

    static class MatchRunner
    {
        /// <summary>
        /// deals 個の配牌を、席を入れ替えて 2 回ずつ対戦させる（計 2×deals 局）。
        /// 配牌・先攻・各手の AI の seed はすべて seed から決まるので、スレッド数によらず結果は同じ（時間の計測値を除く）。
        /// </summary>
        public static MatchResult Run(RulePreset preset, PlayerSpec a, PlayerSpec b, int deals, long seed, int threads)
        {
            var perDeal = new Stats[deals];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, deals, new ParallelOptions { MaxDegreeOfParallelism = threads },
                () => new SearchWorkspace(),
                (i, _, ws) =>
                {
                    perDeal[i] = PlayDeal(preset, a, b, DealSeed(seed, i), ws);
                    return ws;
                },
                _ => { });
            sw.Stop();
            var total = new Stats();
            foreach (var s in perDeal) total.Add(s);
            return new MatchResult { Preset = preset, A = a, B = b, Deals = deals, Stats = total, Seconds = sw.Elapsed.TotalSeconds };
        }

        static long DealSeed(long seed, int deal)
        {
            uint s = Rng.SeedToState(seed * 1000003L + deal);
            return Rng.NextUInt32(s, out _);
        }

        static Stats PlayDeal(RulePreset preset, PlayerSpec a, PlayerSpec b, long dealSeed, SearchWorkspace ws)
        {
            var rules = preset.Rules;
            // 配牌: 50 枚から重複なしで両者のデッキを作る（Random ON なら 8 枚ずつ、OFF なら 5 枚ずつ）
            var all = CardCatalog.All;
            int deckSize = rules.Random ? 8 : GameConstants.HandSize;
            var ids = new string[all.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = all[i].Id;
            uint rng = Rng.SeedToState(dealSeed);
            for (int i = 0; i < 2 * deckSize; i++)
            {
                int j = i + Rng.NextInt(rng, ids.Length - i, out rng);
                var t = ids[i];
                ids[i] = ids[j];
                ids[j] = t;
            }
            var deck0 = new string[deckSize];
            var deck1 = new string[deckSize];
            Array.Copy(ids, 0, deck0, 0, deckSize);
            Array.Copy(ids, deckSize, deck1, 0, deckSize);
            long gameSeed = Rng.NextUInt32(rng, out rng);

            var stats = new Stats();
            for (int aSeat = 0; aSeat < 2; aSeat++)
            {
                var state = GameEngine.CreateGame(new GameConfig(rules, deck0, deck1, gameSeed, CardCatalog.ById,
                    preset.PivotFirst, preset.PivotSecond));
                var first = state.FirstPlayer;
                var rotatedByRole = new int[2];
                uint moveRng = Rng.SeedToState(gameSeed ^ (aSeat * 0x7777L));
                int ply = 0;
                int maxChain = 0;
                var firstPred = new[] { -1, -1 };
                while (state.Phase == Phase.Playing)
                {
                    int side = (int)state.Turn == aSeat ? 0 : 1;
                    var spec = side == 0 ? a : b;
                    int roundPly = 0;
                    for (int c = 0; c < GameConstants.BoardSize; c++)
                    {
                        if (state.Board[c].HasValue) roundPly++;
                    }

                    Move move;
                    long t0 = Stopwatch.GetTimestamp();
                    if (spec.Profile == null)
                    {
                        var legal = GameEngine.LegalMoves(state);
                        move = legal[Rng.NextInt(moveRng, legal.Count, out moveRng)];
                    }
                    else
                    {
                        long moveSeed = Rng.NextUInt32(moveRng, out moveRng);
                        var view = spec.Oracle && !state.Rules.Open
                            ? state.WithRules(new RuleSet(true, state.Rules.Random, state.Rules.Same, state.Rules.SameWall, state.Rules.Plus,
                                state.Rules.Combo, state.Rules.Elemental, state.Rules.SuddenDeath, state.Rules.Pivot))
                            : state;
                        IReadOnlyList<CardDef> pool = all;
                        if (spec.Label.Contains("/truepool=1"))
                        {
                            // 計測専用: 見えていないカードの候補を相手の本当の手札だけにする（決定化の仕組みが正しいかの確認）
                            var tp = new List<CardDef>();
                            foreach (var id in state.GetHand(state.Turn.Opponent())) tp.Add(CardCatalog.ById[id]);
                            pool = tp;
                        }
                        var session = new SearchSession(view, state.Turn, spec.Profile, moveSeed, pool, ws);
                        move = session.Run();
                        stats.Nodes[side] += session.NodesSearched;
                        stats.MaxNodes[side] = Math.Max(stats.MaxNodes[side], session.NodesSearched);
                        if (session.BestMoveSolved) stats.SolvedByPly[side, roundPly]++;
                        if (session.BestMoveSolved && firstPred[side] < 0 && session.WorldCount == 1 && state.SuddenDeathRound == 0)
                        {
                            foreach (var ev in session.GetEvaluations())
                            {
                                if (ev.Move == move && ev.Searched) firstPred[side] = ev.Value > 0 ? 0 : ev.Value == 0 ? 1 : 2;
                            }
                        }
                    }
                    long dt = Stopwatch.GetTimestamp() - t0;
                    stats.Ticks[side] += dt;
                    stats.MaxTicks[side] = Math.Max(stats.MaxTicks[side], dt);
                    stats.Moves[side]++;
                    stats.MovesByPly[side, roundPly]++;
                    if (move.Rotation != 0)
                    {
                        stats.PivotByPly[side, roundPly]++;
                        rotatedByRole[state.Turn == first ? 0 : 1]++;
                    }

                    var result = GameEngine.ApplyMove(state, move);
                    foreach (var e in result.Events)
                    {
                        if (e is FlipEvent f)
                        {
                            stats.FlipsByCause[(int)f.Cause]++;
                            if (f.ChainDepth > maxChain) maxChain = f.ChainDepth;
                        }
                    }
                    state = result.State;
                    ply++;
                }

                stats.Games++;
                for (int role = 0; role < 2; role++)
                {
                    stats.PivotByRole[role] += rotatedByRole[role];
                    if (rotatedByRole[role] > 0) stats.GamesRotatedByRole[role]++;
                }
                stats.SuddenDeathRounds += state.SuddenDeathRound;
                stats.MaxChain = Math.Max(stats.MaxChain, maxChain);
                stats.SumMaxChain += maxChain;
                var final = GameEngine.Result(state).Value;
                for (int sd = 0; sd < 2; sd++)
                {
                    if (firstPred[sd] < 0) continue;
                    int seat = sd == 0 ? aSeat : 1 - aSeat;
                    int actual = final == GameResult.Draw ? 1 : (int)final == seat ? 0 : 2;
                    stats.FirstSolved[sd * 3 + firstPred[sd], actual]++;
                }
                var res = GameEngine.Result(state).Value;
                if (res == GameResult.Draw)
                {
                    stats.Draws++;
                    if (aSeat == 0) stats.Draws1++;
                }
                else
                {
                    if ((int)res == aSeat) stats.WinsA++;
                    else stats.WinsB++;
                    bool firstWon = (Player)(int)res == first;
                    if (firstWon) stats.FirstWins++;
                    else stats.SecondWins++;
                    if (state.SuddenDeathRound > 0)
                    {
                        if (firstWon) stats.FirstWinsRematch++;
                        else stats.SecondWinsRematch++;
                    }
                    if (aSeat == 0)
                    {
                        if (firstWon) stats.FirstWins1++;
                        else stats.SecondWins1++;
                    }
                }
            }
            return stats;
        }
    }
}
