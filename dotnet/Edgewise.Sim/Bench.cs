using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;

namespace Edgewise.Sim
{
    /// <summary>
    /// ApplyMove / ApplyMoveFast の簡易ベンチマーク（TS 版 tools/bench.ts と同じ手順）。
    /// ランダム手の局面と手のペアを事前に集めておき、計測は適用だけにする。
    /// 使い方: dotnet run -c Release --project dotnet/Edgewise.Sim -- --bench
    /// </summary>
    static class Bench
    {
        static List<(GameState State, Move Move)> Collect(RuleSet rules, int games)
        {
            var ids = new List<string>();
            foreach (var c in CardCatalog.All) ids.Add(c.Id);
            var pairs = new List<(GameState, Move)>();
            uint rng = 12345;
            for (int g = 0; g < games; g++)
            {
                var d0 = new string[5];
                var d1 = new string[5];
                for (int i = 0; i < 5; i++) d0[i] = ids[Rng.NextInt(rng, ids.Count, out rng)];
                for (int i = 0; i < 5; i++) d1[i] = ids[Rng.NextInt(rng, ids.Count, out rng)];
                var state = GameEngine.CreateGame(new GameConfig(rules, d0, d1, g, CardCatalog.ById));
                while (state.Phase == Phase.Playing)
                {
                    var moves = GameEngine.LegalMoves(state);
                    var move = moves[Rng.NextInt(rng, moves.Count, out rng)];
                    pairs.Add((state, move));
                    state = GameEngine.ApplyMoveFast(state, move);
                }
            }
            return pairs;
        }

        static void Measure(string label, List<(GameState State, Move Move)> pairs, Func<GameState, Move, GameState> fn)
        {
            int sink = 0;
            for (int rep = 0; rep < 3; rep++)
                foreach (var (s, m) in pairs) sink ^= fn(s, m).Turn == Player.P0 ? 1 : 0; // warm-up
            const int Reps = 10;
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int rep = 0; rep < Reps; rep++)
                foreach (var (s, m) in pairs) sink ^= fn(s, m).Turn == Player.P0 ? 1 : 0;
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            long n = (long)pairs.Count * Reps;
            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
            Console.WriteLine(label.PadRight(46) + us.ToString("0.000", CultureInfo.InvariantCulture) + " us/call  "
                + (alloc / n).ToString(CultureInfo.InvariantCulture) + " B/call  (" + n + " calls, sink " + sink + ")");
        }

        public static void Run()
        {
#if DEBUG
            Console.WriteLine("WARNING: Debug build. Use -c Release for meaningful numbers.");
#endif
            var configs = new (string Name, RuleSet Rules)[]
            {
                ("basic only", new RuleSet()),
                ("same+plus+combo", new RuleSet(same: true, sameWall: true, plus: true, combo: true)),
                ("all (incl. elemental, pivot)", new RuleSet(same: true, sameWall: true, plus: true, combo: true, elemental: true, pivot: true)),
            };
            foreach (var (name, rules) in configs)
            {
                var pairs = Collect(rules, 5000);
                Measure("ApplyMove      [" + name + "]", pairs, (s, m) => GameEngine.ApplyMove(s, m).State);
                Measure("ApplyMoveFast  [" + name + "]", pairs, GameEngine.ApplyMoveFast);
            }
            Console.WriteLine();
            SearchBench();
        }

        /// <summary>
        /// AI 探索の速度（難易度別の秒間ノード数・1 手の思考時間・アロケーション）。
        /// 局面はランダム手で 0〜7 手進めたもの（ルール 4 条件 × 各 60 局面）。1 スレッドで測る。
        /// </summary>
        static void SearchBench()
        {
            var presets = new[] { "basic", "spc", "all", "openoff" };
            var positions = new List<GameState>();
            uint rng = 2026;
            var ids = new List<string>();
            foreach (var c in CardCatalog.All) ids.Add(c.Id);
            foreach (var key in presets)
            {
                var rules = RulePreset.Parse(key)[0].Rules;
                for (int i = 0; i < 60; i++)
                {
                    int deckSize = rules.Random ? 8 : 5;
                    var d0 = new string[deckSize];
                    var d1 = new string[deckSize];
                    for (int k = 0; k < deckSize; k++)
                    {
                        d0[k] = ids[Rng.NextInt(rng, ids.Count, out rng)];
                        d1[k] = ids[Rng.NextInt(rng, ids.Count, out rng)];
                    }
                    var state = GameEngine.CreateGame(new GameConfig(rules, d0, d1, i, CardCatalog.ById));
                    int plies = i % 8;
                    for (int p = 0; p < plies; p++)
                    {
                        var moves = GameEngine.LegalMoves(state);
                        state = GameEngine.ApplyMoveFast(state, moves[Rng.NextInt(rng, moves.Count, out rng)]);
                    }
                    positions.Add(state);
                }
            }

            var ws = new SearchWorkspace();
            Console.WriteLine("AI search (" + positions.Count + " positions: plies 0-7 x rules basic/spc/all/openoff, 1 thread)");
            foreach (var profile in new[] { AiProfile.Easy, AiProfile.Normal, AiProfile.Hard })
            {
                // ウォームアップ
                for (int i = 0; i < 10; i++) SearchSession.ChooseMove(positions[i], positions[i].Turn, profile, i, CardCatalog.All, ws);
                long nodes = 0;
                double maxMs = 0;
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                var total = Stopwatch.StartNew();
                for (int i = 0; i < positions.Count; i++)
                {
                    var s = positions[i];
                    long t0 = Stopwatch.GetTimestamp();
                    var session = new SearchSession(s, s.Turn, profile, i, CardCatalog.All, ws);
                    session.Run();
                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    if (ms > maxMs) maxMs = ms;
                    nodes += session.NodesSearched;
                }
                total.Stop();
                long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                double sec = total.Elapsed.TotalSeconds;
                Console.WriteLine(profile.Name.PadRight(8)
                    + (nodes / sec / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " M nodes/s  "
                    + (nodes / (double)positions.Count).ToString("0", CultureInfo.InvariantCulture) + " nodes/move  "
                    + (total.Elapsed.TotalMilliseconds / positions.Count).ToString("0.00", CultureInfo.InvariantCulture) + " ms/move avg  "
                    + maxMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms max  "
                    + (alloc / positions.Count / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KiB alloc/move");
            }
        }
    }
}
