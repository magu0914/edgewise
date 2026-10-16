using System.Collections.Generic;
using System.Diagnostics;
using Edgewise.Core;
using Edgewise.Core.AI;

namespace Edgewise.Game.AI
{
    /// <summary>
    /// CPU の思考を 1 フレームあたりのミリ秒予算で少しずつ進める（DESIGN §1・§5-1。WebGL ではスレッドが使えないため）。
    /// Core の探索はノード数でしか止められないので、実測した「1 ミリ秒あたりのノード数」で予算をノード数に換算する。
    /// 換算値はフレームごとの実測で自動調整する（指数移動平均）。Presentation 側なので Stopwatch を使ってよい。
    ///
    /// 使い方: Begin(...) のあと毎フレーム Tick() を呼び、true が返ったら BestMove を使う。
    /// MonoBehaviour ではない普通のクラスなので、EditMode テストからも使える。
    /// </summary>
    public sealed class AiDriver
    {
        public const double DefaultFrameBudgetMs = 6.0;

        readonly SearchWorkspace _workspace = new SearchWorkspace();
        SearchSession _session;
        bool _done;
        long _beginTicks;
        long _lastNodes;
        long _accNodes;
        double _accMs;

        /// <summary>1 フレームで探索に使ってよい時間（ミリ秒）。</summary>
        public double FrameBudgetMs { get; set; } = DefaultFrameBudgetMs;

        /// <summary>実測した 1 ミリ秒あたりのノード数（自動調整される。最初は控えめな推定値）。</summary>
        public double NodesPerMs { get; private set; } = 200.0;

        public SearchSession Session => _session;

        public bool IsThinking => _session != null && !_done;

        public bool IsDone => _session != null && _done;

        /// <summary>思考の結果（Tick が true を返した後に使う。途中でも常に合法手）。</summary>
        public Move BestMove => _session.BestMove;

        /// <summary>このセッションの探索ノード数。</summary>
        public long Nodes => _session == null ? 0 : _session.NodesSearched;

        /// <summary>Begin から思考が終わるまでの経過時間（ミリ秒。思考中は現在までの時間）。</summary>
        public double ThinkMs { get; private set; }

        /// <summary>そのうち実際に Step の中にいた時間（ミリ秒）。</summary>
        public double ComputeMs { get; private set; }

        /// <summary>思考にかかったフレーム数。</summary>
        public int Frames { get; private set; }

        /// <summary>思考を始める。前の思考が残っていれば捨てる。</summary>
        public void Begin(GameState state, Player me, AiProfile profile, long seed, IReadOnlyList<CardDef> cardPool,
            CardPoolModel poolModel = CardPoolModel.Distinct)
        {
            _session = new SearchSession(state, me, profile, seed, cardPool, _workspace, poolModel);
            _done = _session.IsFinished;
            _beginTicks = Stopwatch.GetTimestamp();
            _lastNodes = 0;
            ThinkMs = 0;
            ComputeMs = 0;
            Frames = 0;
        }

        /// <summary>
        /// 1 フレーム分だけ探索を進める。予算の残りに見合うノード数で Step を呼び、時間が余ればもう一度呼ぶ。
        /// 思考が終わったら true。
        /// </summary>
        public bool Tick()
        {
            if (_session == null) return false;
            if (_done) return true;
            Frames++;
            long frameStart = Stopwatch.GetTimestamp();
            while (true)
            {
                double remaining = FrameBudgetMs - Ms(Stopwatch.GetTimestamp() - frameStart);
                if (remaining <= 0.05) break;

                double want = NodesPerMs * remaining * 0.9;
                int chunk = want < 64 ? 64 : want > 4_000_000 ? 4_000_000 : (int)want;
                long t0 = Stopwatch.GetTimestamp();
                bool finished = _session.Step(chunk);
                double dt = Ms(Stopwatch.GetTimestamp() - t0);
                long nodes = _session.NodesSearched;
                long dn = nodes - _lastNodes;
                _lastNodes = nodes;
                ComputeMs += dt;

                // 換算値の更新。1 ミリ秒以上たまってから測る（タイマーの分解能が粗い環境でも暴れないように）
                _accNodes += dn;
                _accMs += dt;
                if (_accMs >= 1.0 && _accNodes >= 256)
                {
                    double rate = _accNodes / _accMs;
                    NodesPerMs = NodesPerMs * 0.6 + rate * 0.4;
                    _accNodes = 0;
                    _accMs = 0;
                }

                if (finished)
                {
                    _done = true;
                    break;
                }
            }
            ThinkMs = Ms(Stopwatch.GetTimestamp() - _beginTicks);
            return _done;
        }

        /// <summary>フレーム分割せずに最後まで読む（計測用）。</summary>
        public Move RunToEnd()
        {
            long t0 = Stopwatch.GetTimestamp();
            while (!_session.Step(1 << 20))
            {
            }
            _done = true;
            ComputeMs += Ms(Stopwatch.GetTimestamp() - t0);
            ThinkMs = Ms(Stopwatch.GetTimestamp() - _beginTicks);
            return _session.BestMove;
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
