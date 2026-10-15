using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Game.AI;

namespace Edgewise.Game.Debugging
{
    /// <summary>
    /// 実機（特に WebGL）での CPU の計測。結果は画面とコンソール（[Edgewise] BENCH ...）に出す。
    /// 1) RAW: フレーム分割せずに探索だけを回した秒間ノード数（難易度別。1 フレームに 1 局面ずつ）
    /// 2) SLICED: AiDriver（1 フレームの予算つき）で CPU 対 CPU を指させ、1 手の思考時間・秒間ノード数・思考中の最低 FPS を測る
    /// </summary>
    sealed class DebugBench
    {
        static readonly AiProfile[] Profiles = { AiProfile.Easy, AiProfile.Normal, AiProfile.Hard };

        // (難易度, ルールのプリセット番号, 局数)
        static readonly (int Profile, int Preset, int Games)[] SlicedPlan =
        {
            (0, 4, 2),
            (1, 4, 2),
            (2, 4, 2),
            (2, 2, 1),
        };

        readonly DebugBoardView _view;
        readonly List<GameState> _positions = new List<GameState>();
        readonly SearchWorkspace _ws = new SearchWorkspace();
        int _phase;
        int _profile;
        int _index;
        long _rawNodes;
        double _rawMs;
        double _rawMaxMs;

        int _plan;
        int _gamesLeft;
        readonly List<MoveRecord> _records = new List<MoveRecord>();
        long _frames;
        double _seconds;

        public DebugBench(DebugBoardView view)
        {
            _view = view;
            BuildPositions();
            _view.AddBenchLine("BENCH start: " + _positions.Count + " positions, frame budget " + _view.Driver.FrameBudgetMs + " ms");
            foreach (var p in Profiles) _view.AddBenchLine("BENCH profile " + p);
        }

        void BuildPositions()
        {
            // Sim の --bench と同じ作り方（ルール 4 条件 × ランダム手で 0〜7 手進めた局面）。数は WebGL 向けに少なめ
            var presets = new[]
            {
                new RuleSet(open: true),
                new RuleSet(open: true, same: true, plus: true, combo: true),
                new RuleSet(open: true, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true),
                new RuleSet(),
            };
            uint rng = 2026;
            var all = CardCatalog.All;
            foreach (var rules in presets)
            {
                for (int i = 0; i < 16; i++)
                {
                    int deckSize = rules.Random ? 8 : 5;
                    var d0 = new string[deckSize];
                    var d1 = new string[deckSize];
                    for (int k = 0; k < deckSize; k++)
                    {
                        d0[k] = all[Rng.NextInt(rng, all.Count, out rng)].Id;
                        d1[k] = all[Rng.NextInt(rng, all.Count, out rng)].Id;
                    }
                    var state = GameEngine.CreateGame(new GameConfig(rules, d0, d1, i, CardCatalog.ById));
                    for (int p = 0; p < i % 8; p++)
                    {
                        var moves = GameEngine.LegalMoves(state);
                        state = GameEngine.ApplyMoveFast(state, moves[Rng.NextInt(rng, moves.Count, out rng)]);
                    }
                    _positions.Add(state);
                }
            }
        }

        /// <summary>毎フレーム呼ぶ。終わったら true。</summary>
        public bool Update()
        {
            if (_phase == 0) return UpdateRaw();
            if (_phase == 1) return UpdateSliced();
            return true;
        }

        bool UpdateRaw()
        {
            var profile = Profiles[_profile];
            var s = _positions[_index];
            long t0 = Stopwatch.GetTimestamp();
            var session = new SearchSession(s, s.Turn, profile, _index, CardCatalog.All, _ws);
            session.Run();
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            _rawNodes += session.NodesSearched;
            _rawMs += ms;
            if (ms > _rawMaxMs) _rawMaxMs = ms;
            _index++;
            if (_index < _positions.Count) return false;

            _view.AddBenchLine("BENCH RAW " + profile.Name + ": " + F(_rawNodes / _rawMs / 1000.0, "0.000") + " M nodes/s, "
                + F(_rawNodes / (double)_positions.Count, "0") + " nodes/move, " + F(_rawMs / _positions.Count, "0.0") + " ms/move avg (unsliced), "
                + F(_rawMaxMs, "0.0") + " ms max");
            _index = 0;
            _rawNodes = 0;
            _rawMs = 0;
            _rawMaxMs = 0;
            _profile++;
            if (_profile < Profiles.Length) return false;

            _phase = 1;
            _plan = 0;
            StartPlan();
            return false;
        }

        void StartPlan()
        {
            var (profile, preset, games) = SlicedPlan[_plan];
            _gamesLeft = games;
            _records.Clear();
            _frames = 0;
            _seconds = 0;
            _view.WorstThinkingFrameDt = 0;
            _view.ThinkingFrames = 0;
            _view.SlowThinkingFrames = 0;
            _view.SetUp(preset, profile, profile, 1000 + _plan * 10, true);
        }

        bool UpdateSliced()
        {
            _frames++;
            _seconds += UnityEngine.Time.unscaledDeltaTime;
            var rec = _view.Advance(UnityEngine.Time.unscaledDeltaTime);
            if (rec != null) _records.Add(rec);
            if (!_view.Match.IsOver) return false;

            _gamesLeft--;
            var (profile, preset, games) = SlicedPlan[_plan];
            if (_gamesLeft > 0)
            {
                _view.SetUp(preset, profile, profile, 1000 + _plan * 10 + (games - _gamesLeft), true);
                return false;
            }

            double sumThink = 0, maxThink = 0, sumCpu = 0, minFps = double.MaxValue;
            long nodes = 0;
            int solved = 0;
            foreach (var r in _records)
            {
                sumThink += r.ThinkMs;
                if (r.ThinkMs > maxThink) maxThink = r.ThinkMs;
                sumCpu += r.ComputeMs;
                nodes += r.Nodes;
                if (r.MinFps > 0 && r.MinFps < minFps) minFps = r.MinFps;
                if (r.Solved) solved++;
            }
            int n = _records.Count == 0 ? 1 : _records.Count;
            _view.AddBenchLine("BENCH SLICED " + Profiles[profile].Name + " / " + DebugBoardView.PresetName(preset) + ": " + _records.Count + " moves, think "
                + F(sumThink / n, "0") + " ms avg / " + F(maxThink, "0") + " ms max, " + F(nodes / (double)n, "0") + " nodes/move, "
                + F(sumCpu > 0 ? nodes / sumCpu / 1000.0 : 0, "0.000") + " M nodes/s, worst frame while thinking " + F(_view.WorstThinkingFrameDt * 1000, "0.0") + " ms (" + F(_view.WorstThinkingFrameDt > 0 ? 1 / _view.WorstThinkingFrameDt : 0, "0")
                + " fps), frames under 30 fps " + _view.SlowThinkingFrames + "/" + _view.ThinkingFrames + ", avg " + F(_seconds > 0 ? _frames / _seconds : 0, "0") + " fps, solved " + solved + "/" + _records.Count
                + ", budget " + _view.Driver.FrameBudgetMs + " ms/frame");

            _plan++;
            if (_plan < SlicedPlan.Length)
            {
                StartPlan();
                return false;
            }
            _view.AddBenchLine("BENCH done");
            _phase = 2;
            return true;
        }

        static string F(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
    }
}
