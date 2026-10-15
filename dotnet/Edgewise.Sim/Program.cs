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
    ///     --rules LIST       ルールのプリセット（カンマ区切り）。default = basic,spc,all,openoff。他: spc-openoff, all-openoff, same, plus, ...
    ///     --deals N          配牌の数（各配牌を席を入れ替えて 2 局。既定 500 → 1000 局）
    ///     --games N          局数で指定（--deals N/2 と同じ）
    ///     --seed S           乱数の seed（配牌・先攻・AI の乱数がすべてこれで決まる）
    ///     --threads T        並列数（既定 = 論理コア数）。結果はスレッド数によらず同じ（時間の計測値を除く）
    ///     --out PATH         Markdown を PATH にも書き出す
    ///     --bench            ApplyMove と AI 探索の速度を測る
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var matches = new List<(PlayerSpec, PlayerSpec)>();
            string rules = "default";
            int deals = 500;
            long seed = 1;
            int threads = Environment.ProcessorCount;
            string outPath = null;
            bool bench = false;
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
                        case "--deals": deals = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--games": deals = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture) / 2); break;
                        case "--seed": seed = long.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--threads": threads = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--out": outPath = Next(); break;
                        case "--bench": bench = true; break;
                        default: throw new ArgumentException("unknown option " + a);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("usage: Edgewise.Sim [--match A:B[,C:D]] [--rules LIST] [--deals N | --games N] [--seed S] [--threads T] [--out PATH] [--bench]");
                return 2;
            }

            if (bench)
            {
                Bench.Run();
                return 0;
            }
            if (matches.Count == 0) matches.Add((PlayerSpec.Parse("random"), PlayerSpec.Parse("random")));

            var presets = RulePreset.Parse(rules);
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
                        + "]  " + r.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
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
