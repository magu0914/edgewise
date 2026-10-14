using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Edgewise.Core;
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
        }
    }
}
