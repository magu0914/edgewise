using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Edgewise.Core;

namespace Edgewise.Sim
{
    /// <summary>集計結果を Markdown の表にする。</summary>
    static class Report
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Wilson の 95% 信頼区間（z = 1.96）。n = 0 なら (0, 1)。</summary>
        public static (double Low, double High) Wilson(long k, long n)
        {
            if (n == 0) return (0, 1);
            const double z = 1.96;
            double p = (double)k / n;
            double z2 = z * z;
            double denom = 1 + z2 / n;
            double center = (p + z2 / (2 * n)) / denom;
            double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
            return (Math.Max(0, center - half), Math.Min(1, center + half));
        }

        static string Pct(long k, long n) => n == 0 ? "-" : (100.0 * k / n).ToString("0.0", Inv) + "%";

        static string PctCi(long k, long n)
        {
            if (n == 0) return "-";
            var (lo, hi) = Wilson(k, n);
            return (100.0 * k / n).ToString("0.0", Inv) + "% [" + (100 * lo).ToString("0.0", Inv) + ", " + (100 * hi).ToString("0.0", Inv) + "]";
        }

        static string F(double v, string fmt) => v.ToString(fmt, Inv);

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        public static string Build(IReadOnlyList<MatchResult> results, string header, IReadOnlyList<(MatchResult On, MatchResult Off)> baselines = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header)) sb.AppendLine(header).AppendLine();

            sb.AppendLine("### 勝敗（配牌ごとに席を入れ替えて 2 局。勝率は引き分けを除く。[ ] は Wilson 95% 信頼区間）");
            sb.AppendLine();
            sb.AppendLine("| Rules | A vs B | Games | A wins | B wins | Draws | A win rate (excl. draws) | First-player win rate (excl. draws) | Draw rate |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in results)
            {
                var s = r.Stats;
                int decisive = s.WinsA + s.WinsB;
                sb.Append("| ").Append(r.Preset.Name)
                    .Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label)
                    .Append(" | ").Append(s.Games)
                    .Append(" | ").Append(s.WinsA)
                    .Append(" | ").Append(s.WinsB)
                    .Append(" | ").Append(s.Draws)
                    .Append(" | ").Append(PctCi(s.WinsA, decisive))
                    .Append(" | ").Append(PctCi(s.FirstWins, decisive))
                    .Append(" | ").Append(PctCi(s.Draws, s.Games))
                    .AppendLine(" |");
            }
            sb.AppendLine();

            sb.AppendLine("### 先攻勝率（配牌ごとに 1 局だけ数えた独立な標本。ノイズのない CPU 同士は席交代の 2 局が鏡写しになるので、合否はこの区間で判断する）");
            sb.AppendLine();
            sb.AppendLine("| Rules | A vs B | Deals | First wins | Second wins | Draws | First-player win rate (excl. draws) | Draw rate |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (var r in results)
            {
                var s = r.Stats;
                int decisive1 = s.FirstWins1 + s.SecondWins1;
                int games1 = decisive1 + s.Draws1;
                sb.Append("| ").Append(r.Preset.Name)
                    .Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label)
                    .Append(" | ").Append(games1)
                    .Append(" | ").Append(s.FirstWins1)
                    .Append(" | ").Append(s.SecondWins1)
                    .Append(" | ").Append(s.Draws1)
                    .Append(" | ").Append(PctCi(s.FirstWins1, decisive1))
                    .Append(" | ").Append(PctCi(s.Draws1, games1))
                    .AppendLine(" |");
            }
            sb.AppendLine();

            bool anySd = false;
            foreach (var r in results)
                if (r.Preset.Rules.SuddenDeath) anySd = true;
            if (anySd)
            {
                sb.AppendLine("### 決着したラウンド別の先攻勝率（本戦の先攻から見る。再戦では先攻が入れ替わり、Pivot トークンは持ち越す）");
                sb.AppendLine();
                sb.AppendLine("| Rules | A vs B | Decided in round 1: first-player win rate | Decided in a rematch: first-player win rate |");
                sb.AppendLine("|---|---|---:|---:|");
                foreach (var r in results)
                {
                    if (!r.Preset.Rules.SuddenDeath) continue;
                    var s = r.Stats;
                    int f0 = s.FirstWins - s.FirstWinsRematch, s0 = s.SecondWins - s.SecondWinsRematch;
                    sb.Append("| ").Append(r.Preset.Name).Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label)
                        .Append(" | ").Append(PctCi(f0, f0 + s0)).Append(" (n=").Append(f0 + s0).Append(')')
                        .Append(" | ").Append(PctCi(s.FirstWinsRematch, s.FirstWinsRematch + s.SecondWinsRematch))
                        .Append(" (n=").Append(s.FirstWinsRematch + s.SecondWinsRematch).Append(')')
                        .AppendLine(" |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("### 盤上の出来事（1 局あたり）と思考コスト（dotnet）");
            sb.AppendLine();
            sb.AppendLine("| Rules | A vs B | Flips basic / same / plus / combo | Max chain (max / avg) | Sudden Death rounds | Nodes/move A / B (max) | ms/move A / B (max) | Wall s |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (var r in results)
            {
                var s = r.Stats;
                double g = Math.Max(1, s.Games);
                sb.Append("| ").Append(r.Preset.Name)
                    .Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label)
                    .Append(" | ").Append(F(s.FlipsByCause[0] / g, "0.00")).Append(" / ").Append(F(s.FlipsByCause[1] / g, "0.00"))
                    .Append(" / ").Append(F(s.FlipsByCause[2] / g, "0.00")).Append(" / ").Append(F(s.FlipsByCause[3] / g, "0.00"))
                    .Append(" | ").Append(s.MaxChain).Append(" / ").Append(F(s.SumMaxChain / g, "0.00"))
                    .Append(" | ").Append(F(s.SuddenDeathRounds / g, "0.00"))
                    .Append(" | ").Append(NodesCell(s, 0)).Append(" / ").Append(NodesCell(s, 1))
                    .Append(" | ").Append(MsCell(s, 0)).Append(" / ").Append(MsCell(s, 1))
                    .Append(" | ").Append(F(r.Seconds, "0.0"))
                    .AppendLine(" |");
            }
            sb.AppendLine();

            bool anyPivot = false;
            foreach (var r in results)
            {
                if (r.Preset.Rules.Pivot) anyPivot = true;
            }
            if (anyPivot)
            {
                sb.AppendLine("### Pivot を使った手番（ラウンド内の何手目か。その手番の手のうち回転して置いた割合）");
                sb.AppendLine();
                sb.Append("| Rules | A vs B | Player |");
                for (int p = 0; p < Stats.Plies; p++) sb.Append(' ').Append(p + 1).Append(" |");
                sb.AppendLine(" Tokens used / game |");
                sb.Append("|---|---|---|");
                for (int p = 0; p < Stats.Plies; p++) sb.Append("---:|");
                sb.AppendLine("---:|");
                foreach (var r in results)
                {
                    if (!r.Preset.Rules.Pivot) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        var s = r.Stats;
                        sb.Append("| ").Append(r.Preset.Name).Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label).Append(" | ").Append(side == 0 ? "A" : "B").Append(" |");
                        long used = 0;
                        for (int p = 0; p < Stats.Plies; p++)
                        {
                            used += s.PivotByPly[side, p];
                            sb.Append(' ').Append(s.MovesByPly[side, p] == 0 ? "-" : Pct(s.PivotByPly[side, p], s.MovesByPly[side, p])).Append(" |");
                        }
                        sb.Append(' ').Append(F(used / (double)Math.Max(1, s.Games), "0.00")).AppendLine(" |");
                    }
                }
                sb.AppendLine();

                sb.AppendLine("### Pivot の使用（役割別。先攻 = 本戦の先攻。Sudden Death の再戦を含む 1 局全体）");
                sb.AppendLine();
                sb.AppendLine("| Rules | A vs B | Tokens first / second | First: games with ≥1 pivot | Second: games with ≥1 pivot | Pivots / game first / second |");
                sb.AppendLine("|---|---|---:|---:|---:|---:|");
                foreach (var r in results)
                {
                    if (!r.Preset.Rules.Pivot) continue;
                    var s = r.Stats;
                    double g = Math.Max(1, s.Games);
                    sb.Append("| ").Append(r.Preset.Name).Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label)
                        .Append(" | ").Append(r.Preset.PivotFirst).Append(" / ").Append(r.Preset.PivotSecond)
                        .Append(" | ").Append(Pct(s.GamesRotatedByRole[0], s.Games))
                        .Append(" | ").Append(Pct(s.GamesRotatedByRole[1], s.Games))
                        .Append(" | ").Append(F(s.PivotByRole[0] / g, "0.00")).Append(" / ").Append(F(s.PivotByRole[1] / g, "0.00"))
                        .AppendLine(" |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("### 読み切り率（ラウンド内の何手目か。選んだ手が全世界で終局まで読み切れていた割合）");
            sb.AppendLine();
            sb.Append("| Rules | A vs B | Player |");
            for (int p = 0; p < Stats.Plies; p++) sb.Append(' ').Append(p + 1).Append(" |");
            sb.AppendLine();
            sb.Append("|---|---|---|");
            for (int p = 0; p < Stats.Plies; p++) sb.Append("---:|");
            sb.AppendLine();
            foreach (var r in results)
            {
                for (int side = 0; side < 2; side++)
                {
                    var spec = side == 0 ? r.A : r.B;
                    if (spec.Profile == null || spec.Profile.MaxDepth != 0) continue; // 深さ制限のあるプロファイルは読み切らない
                    var s = r.Stats;
                    sb.Append("| ").Append(r.Preset.Name).Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label).Append(" | ").Append(side == 0 ? "A" : "B").Append(" |");
                    for (int p = 0; p < Stats.Plies; p++)
                        sb.Append(' ').Append(s.MovesByPly[side, p] == 0 ? "-" : Pct(s.SolvedByPly[side, p], s.MovesByPly[side, p])).Append(" |");
                    sb.AppendLine();
                }
            }
            sb.AppendLine();

            sb.AppendLine("### 最初に読み切った時点の予想と実際の結果（Open ON・本戦のみ。予想は「相手も最善を尽くす」前提の値）");
            sb.AppendLine();
            sb.AppendLine("| Rules | A vs B | Player | 予想: 勝ち → 勝 / 分 / 負 | 予想: 引き分け → 勝 / 分 / 負 | 予想: 負け → 勝 / 分 / 負 |");
            sb.AppendLine("|---|---|---|---:|---:|---:|");
            foreach (var r in results)
            {
                for (int side = 0; side < 2; side++)
                {
                    var spec = side == 0 ? r.A : r.B;
                    var s = r.Stats;
                    long any = 0;
                    for (int k = 0; k < 9; k++) any += s.FirstSolved[side * 3 + k / 3, k % 3];
                    if (any == 0 || spec.Profile == null || spec.Profile.MaxDepth != 0) continue;
                    sb.Append("| ").Append(r.Preset.Name).Append(" | ").Append(r.A.Label).Append(" vs ").Append(r.B.Label).Append(" | ").Append(side == 0 ? "A" : "B").Append(" |");
                    for (int k = 0; k < 3; k++)
                    {
                        sb.Append(' ').Append(s.FirstSolved[side * 3 + k, 0]).Append(" / ").Append(s.FirstSolved[side * 3 + k, 1])
                            .Append(" / ").Append(s.FirstSolved[side * 3 + k, 2]).Append(" |");
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine();
            if (baselines != null && baselines.Count > 0) AppendTraitSections(sb, baselines);
            return sb.ToString();
        }

        /// <summary>Trait 付きのカードを含む手札の成績と、カード別の成績。Traits ON と、同じ配牌・同じ seed の Traits OFF（対照）を並べる。</summary>
        static void AppendTraitSections(StringBuilder sb, IReadOnlyList<(MatchResult On, MatchResult Off)> baselines)
        {
            sb.AppendLine("### Trait 付きのカードを含む手札の勝率（ちょうど片方だけが Trait 付きのカードを含む配牌。引き分けを除く。Off = 同じ配牌で Traits だけ OFF の対照）");
            sb.AppendLine();
            sb.AppendLine("Trait 付きのカードは Lv7〜10 なので、含む手札は元々強い。Traits の効果そのものは「ON − OFF」の差で見る。");
            sb.AppendLine();
            sb.AppendLine("| Rules | A vs B | Games with exactly one trait hand (ON / OFF) | Trait hand win rate ON | Trait hand win rate OFF | ON − OFF (points) |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach (var (on, off) in baselines)
            {
                var a = on.Stats;
                var b = off.Stats;
                long nOn = a.TraitHandWins + a.TraitHandLosses;
                long nOff = b.TraitHandWins + b.TraitHandLosses;
                double pOn = nOn == 0 ? 0 : 100.0 * a.TraitHandWins / nOn;
                double pOff = nOff == 0 ? 0 : 100.0 * b.TraitHandWins / nOff;
                sb.Append("| ").Append(on.Preset.Name).Append(" | ").Append(on.A.Label).Append(" vs ").Append(on.B.Label)
                    .Append(" | ").Append(nOn + a.TraitHandDraws).Append(" / ").Append(nOff + b.TraitHandDraws)
                    .Append(" | ").Append(PctCi(a.TraitHandWins, nOn))
                    .Append(" | ").Append(PctCi(b.TraitHandWins, nOff))
                    .Append(" | ").Append(F(pOn - pOff, "+0.0;-0.0;0.0"))
                    .AppendLine(" |");
            }
            sb.AppendLine();

            foreach (var (on, off) in baselines)
            {
                sb.AppendLine("### カード別の勝率（持ち主の勝率。引き分けを除く）: " + on.Preset.Name + " / " + on.A.Label + " vs " + on.B.Label);
                sb.AppendLine();
                sb.AppendLine("| Card | Lv | Trait | Games held | Win rate ON | Win rate OFF | ON − OFF (points) |");
                sb.AppendLine("|---|---:|---|---:|---:|---:|---:|");
                var cards = SimCards.All;
                for (int i = 0; i < cards.Count; i++)
                {
                    var c = cards[i];
                    if (!c.Trait.HasValue) continue;
                    long wOn = on.Stats.CardWins[i], lOn = on.Stats.CardLosses[i];
                    long wOff = off.Stats.CardWins[i], lOff = off.Stats.CardLosses[i];
                    double pOn = wOn + lOn == 0 ? 0 : 100.0 * wOn / (wOn + lOn);
                    double pOff = wOff + lOff == 0 ? 0 : 100.0 * wOff / (wOff + lOff);
                    sb.Append("| ").Append(c.Id).Append(" | ").Append(c.Level).Append(" | ").Append(c.Trait.Value.ToId())
                        .Append(" | ").Append(on.Stats.CardGames[i])
                        .Append(" | ").Append(PctCi(wOn, wOn + lOn))
                        .Append(" | ").Append(PctCi(wOff, wOff + lOff))
                        .Append(" | ").Append(F(pOn - pOff, "+0.0;-0.0;0.0"))
                        .AppendLine(" |");
                }
                // 集計: Trait 付きのカードと、同じ Lv7〜10 で Trait のないカード（Trait の有無だけの比較になる）
                foreach (bool withTrait in new[] { true, false })
                {
                    long wOn = 0, lOn = 0, wOff = 0, lOff = 0, held = 0;
                    int count = 0;
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var c = cards[i];
                        if (c.Level < 7 || c.Trait.HasValue != withTrait) continue;
                        wOn += on.Stats.CardWins[i];
                        lOn += on.Stats.CardLosses[i];
                        wOff += off.Stats.CardWins[i];
                        lOff += off.Stats.CardLosses[i];
                        held += on.Stats.CardGames[i];
                        count++;
                    }
                    double pOn = wOn + lOn == 0 ? 0 : 100.0 * wOn / (wOn + lOn);
                    double pOff = wOff + lOff == 0 ? 0 : 100.0 * wOff / (wOff + lOff);
                    sb.Append("| **").Append(withTrait ? "All Lv7-10 cards with a Trait (" : "All Lv7-10 cards without a Trait (").Append(count).Append(")**")
                        .Append(" | | | ").Append(held)
                        .Append(" | ").Append(PctCi(wOn, wOn + lOn))
                        .Append(" | ").Append(PctCi(wOff, wOff + lOff))
                        .Append(" | ").Append(F(pOn - pOff, "+0.0;-0.0;0.0"))
                        .AppendLine(" |");
                }
                sb.AppendLine();
            }
        }

        static string NodesCell(Stats s, int side)
        {
            if (s.Moves[side] == 0 || s.Nodes[side] == 0) return "-";
            return F(s.Nodes[side] / (double)s.Moves[side], "0") + " (" + s.MaxNodes[side] + ")";
        }

        static string MsCell(Stats s, int side)
        {
            if (s.Moves[side] == 0) return "-";
            return F(Ms(s.Ticks[side]) / s.Moves[side], "0.00") + " (" + F(Ms(s.MaxTicks[side]), "0.0") + ")";
        }
    }
}
