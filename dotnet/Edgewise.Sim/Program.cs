using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Edgewise.Core;
using Edgewise.Core.Data;

namespace Edgewise.Sim
{
    /// <summary>
    /// 自己対戦シミュレーター（最小版）。ランダム手同士で N 局回し、先攻勝率・後攻勝率・引き分け率を Markdown 表で出す。
    /// Phase 2 で CPU（Easy / Normal / Hard）同士の対戦と、連鎖数・Pivot 使用タイミングなどの集計を足す。
    ///
    /// 使い方: dotnet run --project dotnet/Edgewise.Sim -- [--games N] [--seed S]
    /// </summary>
    public static class Program
    {
        sealed class Preset
        {
            public readonly string Name;
            public readonly RuleSet Rules;

            public Preset(string name, RuleSet rules)
            {
                Name = name;
                Rules = rules;
            }
        }

        static readonly Preset[] Presets =
        {
            new Preset("Basic", new RuleSet()),
            new Preset("Same", new RuleSet(same: true)),
            new Preset("Plus", new RuleSet(plus: true)),
            new Preset("Same+Plus+Combo", new RuleSet(same: true, plus: true, combo: true)),
            new Preset("Elemental", new RuleSet(elemental: true)),
            new Preset("Pivot", new RuleSet(pivot: true)),
            new Preset("Sudden Death", new RuleSet(suddenDeath: true)),
            new Preset("All", new RuleSet(random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true)),
        };

        public static int Main(string[] args)
        {
            int games = 2000;
            long seed = 1;
            bool bench = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--games" && i + 1 < args.Length) games = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (args[i] == "--seed" && i + 1 < args.Length) seed = long.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (args[i] == "--bench") bench = true;
                else
                {
                    Console.Error.WriteLine("usage: Edgewise.Sim [--games N] [--seed S] [--bench]");
                    return 2;
                }
            }
            if (bench)
            {
                Bench.Run();
                return 0;
            }

            var ids = new List<string>();
            foreach (var c in CardCatalog.All) ids.Add(c.Id);

            Console.WriteLine("# Random vs Random (" + games + " games per rule set, seed " + seed + ")");
            Console.WriteLine();
            Console.WriteLine("| Rules | Games | First player wins | Second player wins | Draws | Avg flips / game | Avg Sudden Death rounds | ms |");
            Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (var preset in Presets)
            {
                var sw = Stopwatch.StartNew();
                uint rng = Rng.SeedToState(seed);
                int firstWins = 0, secondWins = 0, draws = 0;
                long flips = 0, rounds = 0;
                for (int g = 0; g < games; g++)
                {
                    int deckSize = preset.Rules.Random ? 8 : GameConstants.HandSize;
                    var deck0 = new string[deckSize];
                    var deck1 = new string[deckSize];
                    for (int i = 0; i < deckSize; i++)
                    {
                        deck0[i] = ids[Rng.NextInt(rng, ids.Count, out rng)];
                        deck1[i] = ids[Rng.NextInt(rng, ids.Count, out rng)];
                    }
                    long gameSeed = Rng.NextUInt32(rng, out rng);
                    var state = GameEngine.CreateGame(new GameConfig(preset.Rules, deck0, deck1, gameSeed, CardCatalog.ById));
                    var first = state.FirstPlayer;

                    while (state.Phase == Phase.Playing)
                    {
                        var moves = GameEngine.LegalMoves(state);
                        var result = GameEngine.ApplyMove(state, moves[Rng.NextInt(rng, moves.Count, out rng)]);
                        foreach (var e in result.Events)
                        {
                            if (e is FlipEvent) flips++;
                        }
                        state = result.State;
                    }

                    rounds += state.SuddenDeathRound;
                    var r = GameEngine.Result(state).Value;
                    if (r == GameResult.Draw) draws++;
                    else if ((r == GameResult.P0) == (first == Player.P0)) firstWins++;
                    else secondWins++;
                }
                sw.Stop();

                Console.WriteLine("| " + preset.Name + " | " + games
                    + " | " + Percent(firstWins, games)
                    + " | " + Percent(secondWins, games)
                    + " | " + Percent(draws, games)
                    + " | " + Ratio(flips, games)
                    + " | " + Ratio(rounds, games)
                    + " | " + sw.ElapsedMilliseconds + " |");
            }
            return 0;
        }

        static string Percent(long count, long total) =>
            (100.0 * count / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        static string Ratio(long sum, long total) =>
            ((double)sum / total).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
