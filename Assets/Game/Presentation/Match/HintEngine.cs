using System.Collections.Generic;
using System.Diagnostics;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;

namespace Edgewise.Game.Match
{
    /// <summary>ヒントの結果: 最善手と予想スコア（手番側から見た「自分 - 相手」）。</summary>
    public readonly struct HintResult
    {
        public readonly Move Move;
        public readonly int Mine;
        public readonly int Theirs;

        /// <summary>終局まで読み切った正確な予想か（false なら評価値からの目安）。</summary>
        public readonly bool Exact;

        public HintResult(Move move, int mine, int theirs, bool exact)
        {
            Move = move;
            Mine = mine;
            Theirs = theirs;
            Exact = exact;
        }

        public override string ToString() => Move + " " + Mine + "-" + Theirs + (Exact ? " exact" : " approx");
    }

    /// <summary>
    /// ヒント（DESIGN §5-1 / §8-1）: Core の解析 API（SearchSession.CreateAnalysis ＝ AnalyzeMoves と同じ探索）を
    /// 1 フレームの時間予算で少しずつ進め、評価値が最も高い手を返す。
    /// 相手の手札は盗み見ない: Open OFF なら Core が「見えていないカード」からの決定化で読む（state の相手の手札を使わない）。
    /// </summary>
    public sealed class HintEngine
    {
        SearchSession _session;
        Player _viewer;
        GameState _state;

        public bool IsRunning => _session != null && !IsDone;
        public bool IsDone { get; private set; }
        public HintResult Result { get; private set; }

        /// <summary>このヒントの対象の局面（手番が変わったら古いヒントを捨てるのに使う）。</summary>
        public GameState State => _state;

        public void Begin(GameState state, Player viewer, int budgetNodes, long seed, IReadOnlyList<CardDef> cardPool = null,
            CardPoolModel poolModel = CardPoolModel.Distinct)
        {
            _state = state;
            _viewer = viewer;
            IsDone = false;
            var profile = AiProfile.Hard.WithMaxNodes(budgetNodes < 1 ? 1 : budgetNodes);
            _session = SearchSession.CreateAnalysis(state, viewer, profile, seed, cardPool ?? CardCatalog.All, null, poolModel);
        }

        public void Cancel()
        {
            _session = null;
            IsDone = false;
        }

        /// <summary>最大 frameBudgetMs ミリ秒だけ進める。終わったら true。</summary>
        public bool Tick(double frameBudgetMs)
        {
            if (_session == null) return false;
            if (IsDone) return true;
            long t0 = Stopwatch.GetTimestamp();
            while (true)
            {
                if (_session.Step(2000))
                {
                    Finish();
                    return true;
                }
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                if (ms >= frameBudgetMs) return false;
            }
        }

        /// <summary>フレーム分割せずに最後まで読む（テスト用）。</summary>
        public HintResult RunToEnd()
        {
            while (!Tick(1000.0))
            {
            }
            return Result;
        }

        void Finish()
        {
            var evals = _session.GetEvaluations();
            int best = -1;
            for (int i = 0; i < evals.Length; i++)
            {
                if (!evals[i].Searched) continue;
                // 同じ評価なら回転なしの手（分かりやすい方）を優先
                if (best < 0 || evals[i].Value > evals[best].Value
                    || (evals[i].Value == evals[best].Value && evals[best].Move.Rotation != 0 && evals[i].Move.Rotation == 0))
                    best = i;
            }
            if (best < 0) best = 0;
            var e = evals[best];
            int total = TotalCards(_state);
            if (e.TryGetFinalScore(out int mine, out int theirs))
            {
                Result = new HintResult(e.Move, mine, theirs, true);
            }
            else
            {
                // 途中局面の評価値（1/100 枚単位の差）からの目安。合計枚数は 10（Sudden Death の再戦中も 10）
                int diff = SearchSession.TryDecodeTerminalValue(e.Value, out int d) ? d : e.Value / 100;
                if (diff > total) diff = total;
                if (diff < -total) diff = -total;
                int m = (total + diff + 1) / 2;
                Result = new HintResult(e.Move, m, total - m, false);
            }
            IsDone = true;
        }

        static int TotalCards(GameState s)
        {
            var sc = GameEngine.Score(s);
            return sc.P0 + sc.P1;
        }
    }
}
