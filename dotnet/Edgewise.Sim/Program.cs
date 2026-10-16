using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Edgewise.Sim
{
    /// <summary>
    /// 自己対戦シミュレーター（DESIGN.md §6）。
    ///
    /// 使い方:
    ///   dotnet run -c Release --project dotnet/Edgewise.Sim -- [options]
    ///     --match A:B        対戦（複数可・カンマ区切り可）。A/B = random | easy | normal | hard、"hard@200000" でノード予算を上書き
    ///     --rules LIST       ルールのプリセット（カンマ区切り）。default = basic,spc,all,openoff。他: spc-openoff, all-openoff, same, plus, pivot, all-nopivot, ...
    ///     --pivot LIST       Pivot トークン数の候補「先攻/後攻」（カンマ区切り。例 2/2,1/2,2/3。game = ゲーム本体の既定値）。Pivot ON のプリセットを候補ごとの行にする
    ///     --deals N          配牌の数（各配牌を席を入れ替えて 2 局。既定 500 → 1000 局）
    ///     --games N          局数で指定（--deals N/2 と同じ）
    ///     --seed S           乱数の seed（配牌・先攻・AI の乱数がすべてこれで決まる）
    ///     --threads T        並列数（既定 = 論理コア数）。結果はスレッド数によらず同じ（時間の計測値を除く）
    ///     --out PATH         Markdown を PATH にも書き出す
    ///     --bench            ApplyMove と AI 探索の速度を測る
    ///     --campaign         星図巡りの進行バランス（DESIGN §12-9）。相手ごとに標準的な手持ちの最善デッキで対戦させる（--deals は相手ごとの配牌数）
    ///       --picks K        標準的な手持ち: それより前の相手 1 人につき何枚もらった想定か（既定 1）
    ///       --player SPEC    プレイヤー役の AI（既定 normal。--match と同じ書式）
    ///       --only IDS       計測する相手の ID（カンマ区切り）
    ///       --sudden-death-regions N,M  計測専用: 指定した星域に Sudden Death を足す（ルール変更の提案の効果を測る）
    ///       --tune SPEC      調整用の上書き "ID:ai=normal/noise=60;cards=a+b+c;slack=2|ID2:..."（CampaignSim.Effective）
    ///   --match では "opp.<相手の ID>"（例 opp.harbor-master）で星図巡りの相手の AI（性格つき）を指定できる。
    ///   --rules の region1〜region4 は星図巡りの星域のルール（Pivot トークンはゲーム本体の既定）。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var matches = new List<(PlayerSpec, PlayerSpec)>();
            string rules = "default";
            string pivot = null;
            int deals = 500;
            long seed = 1;
            int threads = Environment.ProcessorCount;
            string outPath = null;
            bool bench = false;
            bool campaign = false;
            int picks = 1;
            string player = "normal";
            string only = null;
            string tune = null;
            string sdRegions = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(a + " needs a value");
                    switch (a)
                    {
                        case "--match":
                            foreach (var m in Next().Split(','))
                            {
                                var parts = m.Split(':');
                                if (parts.Length != 2) throw new ArgumentException("--match expects A:B, got " + m);
                                matches.Add((PlayerSpec.Parse(parts[0]), PlayerSpec.Parse(parts[1])));
                            }
                            break;
                        case "--rules": rules = Next(); break;
                        case "--pivot": pivot = Next(); break;
                        case "--deals": deals = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--games": deals = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture) / 2); break;
                        case "--seed": seed = long.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--threads": threads = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--out": outPath = Next(); break;
                        case "--bench": bench = true; break;
                        case "--campaign": campaign = true; break;
                        case "--picks": picks = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--player": player = Next(); break;
                        case "--only": only = Next(); break;
                        case "--tune": tune = Next(); break;
                        case "--sudden-death-regions": sdRegions = Next(); break;
                        default: throw new ArgumentException("unknown option " + a);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("usage: Edgewise.Sim [--match A:B[,C:D]] [--rules LIST] [--pivot F/S[,F/S]] [--deals N | --games N] [--seed S] [--threads T] [--out PATH] [--bench] [--campaign [--picks K] [--player SPEC] [--only IDS]]");
                return 2;
            }

            if (bench)
            {
                Bench.Run();
                return 0;
            }
            if (campaign)
            {
                try
                {
                    return CampaignSim.Run(deals, seed, threads, picks, player, only, tune, outPath, args, sdRegions);
                }
                catch (ArgumentException e)
                {
                    Console.Error.WriteLine(e.Message);
                    return 2;
                }
            }
            if (matches.Count == 0) matches.Add((PlayerSpec.Parse("random"), PlayerSpec.Parse("random")));

            List<RulePreset> presets;
            try
            {
                presets = RulePreset.ExpandPivot(RulePreset.Parse(rules), pivot);
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }
            var header = new StringBuilder();
            header.AppendLine("## Edgewise.Sim");
            header.AppendLine();
            header.AppendLine("- Command: `dotnet run -c Release --project dotnet/Edgewise.Sim -- " + string.Join(" ", args) + "`");
            header.AppendLine("- Deals: " + deals + " (x2 seats = " + (2 * deals) + " games per row), seed " + seed + ", threads " + threads);
            header.AppendLine("- Machine: " + CpuName() + ", " + Environment.ProcessorCount + " logical cores, " + RuntimeInformation.OSDescription
                + ", " + RuntimeInformation.FrameworkDescription);
            var seen = new HashSet<string>();
            foreach (var (a, b) in matches)
            {
                foreach (var p in new[] { a, b })
                {
                    if (p.Profile != null && seen.Add(p.Label)) header.AppendLine("- " + p.Label + ": " + p.Profile);
                }
            }
#if DEBUG
            header.AppendLine("- WARNING: Debug build. Use -c Release.");
#endif
            Console.WriteLine(header.ToString());

            var results = new List<MatchResult>();
            foreach (var preset in presets)
            {
                foreach (var (a, b) in matches)
                {
                    var r = MatchRunner.Run(preset, a, b, deals, seed, threads);
                    results.Add(r);
                    var s = r.Stats;
                    int decisive = s.WinsA + s.WinsB;
                    var (lo, hi) = Report.Wilson(s.WinsA, decisive);
                    Console.Error.WriteLine(preset.Name + " | " + a.Label + " vs " + b.Label + ": " + s.WinsA + "-" + s.WinsB + "-" + s.Draws
                        + "  A " + (decisive == 0 ? 0 : 100.0 * s.WinsA / decisive).ToString("0.0", CultureInfo.InvariantCulture)
                        + "% [" + (100 * lo).ToString("0.0", CultureInfo.InvariantCulture) + ", " + (100 * hi).ToString("0.0", CultureInfo.InvariantCulture)
                        + "]  first " + (decisive == 0 ? 0 : 100.0 * s.FirstWins / decisive).ToString("0.0", CultureInfo.InvariantCulture)
                        + "% (1/deal " + FirstPerDeal(s) + ")  draws " + (s.Games == 0 ? 0 : 100.0 * s.Draws / s.Games).ToString("0.0", CultureInfo.InvariantCulture)
                        + "%  " + r.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                }
            }

            var md = Report.Build(results, header.ToString());
            Console.WriteLine(md);
            if (outPath != null)
            {
                File.WriteAllText(outPath, md, new UTF8Encoding(false));
                Console.Error.WriteLine("wrote " + Path.GetFullPath(outPath));
            }
            return 0;
        }

        static string FirstPerDeal(Stats s)
        {
            int n = s.FirstWins1 + s.SecondWins1;
            var (lo, hi) = Report.Wilson(s.FirstWins1, n);
            var inv = CultureInfo.InvariantCulture;
            return (n == 0 ? 0 : 100.0 * s.FirstWins1 / n).ToString("0.0", inv) + "% [" + (100 * lo).ToString("0.0", inv) + ", "
                + (100 * hi).ToString("0.0", inv) + "]";
        }

        static string CpuName()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    {
                        var name = key?.GetValue("ProcessorNameString") as string;
                        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                    }
                }
            }
            catch (Exception)
            {
                // 取れなければ下の値を使う
            }
            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
        }
    }
}
