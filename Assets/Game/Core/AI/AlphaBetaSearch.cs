namespace Edgewise.Core.AI
{
    /// <summary>
    /// 1 つの世界（相手の手札が確定した局面）に対する αβ 探索（negamax, fail-soft）。
    /// 再帰を使わず明示的なスタックで書いてあるので、任意のノードで中断（Paused）して次の呼び出しで再開できる。
    /// これにより「Step(maxNodes) をどう刻んでも結果が同じ」になる（中断は探索の判断に影響しない）。
    /// アロケーションは構築時のみ。
    /// </summary>
    internal sealed class AlphaBetaSearch
    {
        public const int Paused = 0;
        public const int Completed = 1;
        public const int Aborted = 2;

        const int Inf = SearchConstants.Infinity;
        const int MaxFrames = SearchConstants.MaxPlies + 1;
        const int MaxMoves = SearchBoard.MoveCodeCount;
        const int StageEnter = 0;
        const int StageIterate = 1;

        public readonly SearchBoard Board = new SearchBoard();
        public readonly TranspositionTable Table = new TranspositionTable();

        readonly int[] _alpha = new int[MaxFrames];
        readonly int[] _beta = new int[MaxFrames];
        readonly int[] _alphaOrig = new int[MaxFrames];
        readonly int[] _best = new int[MaxFrames];
        readonly int[] _bestMove = new int[MaxFrames];
        readonly int[] _depth = new int[MaxFrames];
        readonly int[] _count = new int[MaxFrames];
        readonly int[] _next = new int[MaxFrames];
        readonly int[] _cur = new int[MaxFrames];
        readonly int[] _flipped = new int[MaxFrames];
        readonly int[] _stage = new int[MaxFrames];
        readonly int[] _moves = new int[MaxFrames * MaxMoves];
        readonly int[] _scores = new int[MaxFrames * MaxMoves];
        readonly int[] _history = new int[2 * MaxMoves];
        int _sp;
        bool _useTable;

        // ---- ルート ----
        int _rootCount;
        readonly int[] _rootCodes = new int[MaxMoves];
        readonly int[] _rootOrder = new int[MaxMoves];
        int _rootMargin;
        int _rootBest;
        int _rootAlphaUsed;
        int _curRootId;

        /// <summary>今の反復でのルートの各手（id 順）の評価値。</summary>
        public readonly int[] IterValue = new int[MaxMoves];

        /// <summary>IterValue が正確な値か（false なら上界。窓の下に外れた手）。</summary>
        public readonly bool[] IterExact = new bool[MaxMoves];

        /// <summary>今の反復で読み終わった手か。</summary>
        public readonly bool[] IterDone = new bool[MaxMoves];

        /// <summary>数えたノード数（局面に入った回数）。</summary>
        public long Nodes;

        /// <summary>新しい世界を始める（置換表の世代を進め、history を消す）。Board は呼び出し側で Load 済みであること。</summary>
        public void BeginWorld(int tableBits, int[] rootCodes, int rootCount)
        {
            Table.Prepare(tableBits);
            _useTable = tableBits > 0;
            for (int i = 0; i < _history.Length; i++) _history[i] = 0;
            _rootCount = rootCount;
            for (int i = 0; i < rootCount; i++) _rootCodes[i] = rootCodes[i];
        }

        /// <summary>深さ depth の反復を始める。order はルートの手 id を読む順に並べたもの。</summary>
        public void BeginIteration(int depth, int[] order, int rootMargin)
        {
            for (int i = 0; i < _rootCount; i++)
            {
                _rootOrder[i] = order[i];
                IterDone[i] = false;
                IterExact[i] = false;
                IterValue[i] = -Inf;
            }
            _rootMargin = rootMargin;
            _rootBest = -Inf;
            _sp = 0;
            _stage[0] = StageIterate;
            _count[0] = _rootCount;
            _next[0] = 0;
            _depth[0] = depth;
            _alpha[0] = -Inf;
            _beta[0] = Inf;
        }

        /// <summary>
        /// 探索を進める。stepBudget（この呼び出しで使ってよいノード数）が尽きたら Paused、
        /// worldBudget（この世界で使ってよい残りノード数）が尽きたら盤面を元に戻して Aborted、反復が終われば Completed。
        /// </summary>
        public int Run(ref int stepBudget, ref long worldBudget)
        {
            var b = Board;
            while (true)
            {
                int ply = _sp;
                if (_stage[ply] == StageEnter)
                {
                    if (worldBudget <= 0)
                    {
                        Unwind();
                        return Aborted;
                    }
                    if (stepBudget <= 0) return Paused;
                    stepBudget--;
                    worldBudget--;
                    Nodes++;

                    int empties = b.EmptyCount;
                    if (empties == 0)
                    {
                        Return(b.TerminalValue());
                        continue;
                    }
                    int d = _depth[ply];
                    if (d > empties)
                    {
                        d = empties;
                        _depth[ply] = d;
                    }
                    if (d <= 0)
                    {
                        Return(b.Evaluate());
                        continue;
                    }

                    int ttMove = -1;
                    if (_useTable && Table.Probe(b.Hash, out int tv, out int td, out int tf, out ttMove) && td >= d)
                    {
                        if (tf == TranspositionTable.FlagExact
                            || (tf == TranspositionTable.FlagLower && tv >= _beta[ply])
                            || (tf == TranspositionTable.FlagUpper && tv <= _alpha[ply]))
                        {
                            Return(tv);
                            continue;
                        }
                    }

                    int offset = ply * MaxMoves;
                    int count = b.GenerateMoves(_moves, offset);
                    if (count == 0)
                    {
                        Return(b.Evaluate());
                        continue;
                    }
                    ScoreMoves(offset, count, ttMove, d);
                    _count[ply] = count;
                    _next[ply] = 0;
                    _best[ply] = -Inf;
                    _bestMove[ply] = -1;
                    _alphaOrig[ply] = _alpha[ply];
                    _stage[ply] = StageIterate;
                }

                // ---- 子へ進む ----
                if (ply == 0)
                {
                    if (_next[0] >= _count[0]) return Completed;
                    int id = _rootOrder[_next[0]++];
                    _curRootId = id;
                    int code = _rootCodes[id];
                    int rootAlpha = -Inf;
                    if (_rootBest > -Inf && _rootMargin < Inf)
                    {
                        rootAlpha = _rootBest - _rootMargin;
                        if (rootAlpha < -Inf) rootAlpha = -Inf;
                    }
                    _rootAlphaUsed = rootAlpha;
                    _cur[0] = code;
                    _flipped[0] = b.Make(code);
                    _alpha[1] = -Inf;
                    _beta[1] = -rootAlpha;
                    _depth[1] = _depth[0] - 1;
                    _stage[1] = StageEnter;
                    _sp = 1;
                    continue;
                }

                if (_next[ply] < _count[ply])
                {
                    int code = PickNext(ply);
                    _cur[ply] = code;
                    _flipped[ply] = b.Make(code);
                    int child = ply + 1;
                    _alpha[child] = -_beta[ply];
                    _beta[child] = -_alpha[ply];
                    _depth[child] = _depth[ply] - 1;
                    _stage[child] = StageEnter;
                    _sp = child;
                    continue;
                }

                // ---- このノードの読み終わり ----
                int best = _best[ply];
                if (_useTable)
                {
                    int flag = best <= _alphaOrig[ply] ? TranspositionTable.FlagUpper
                        : best >= _beta[ply] ? TranspositionTable.FlagLower
                        : TranspositionTable.FlagExact;
                    Table.Store(b.Hash, best, _depth[ply], flag, _bestMove[ply]);
                }
                Return(best);
            }
        }

        /// <summary>現在のノードの値 v を親に返す（親の手を戻し、αβ を更新する）。</summary>
        void Return(int v)
        {
            _sp--;
            int parent = _sp;
            Board.Unmake(_cur[parent], _flipped[parent]);
            int score = -v;
            if (parent == 0)
            {
                int id = _curRootId;
                IterValue[id] = score;
                IterExact[id] = _rootAlphaUsed == -Inf || score > _rootAlphaUsed;
                IterDone[id] = true;
                if (IterExact[id] && score > _rootBest) _rootBest = score;
                return;
            }
            if (score > _best[parent])
            {
                _best[parent] = score;
                _bestMove[parent] = _cur[parent];
            }
            if (score > _alpha[parent]) _alpha[parent] = score;
            if (_alpha[parent] >= _beta[parent])
            {
                _next[parent] = _count[parent];
                int h = Board.Turn * MaxMoves + _cur[parent];
                int dd = _depth[parent];
                int nh = _history[h] + dd * dd;
                _history[h] = nh > HistoryCap ? HistoryCap : nh;
            }
        }

        const int HistoryCap = (1 << 20) - 1;

        /// <summary>中断した探索の手をすべて戻す（盤面をルートに戻す）。</summary>
        void Unwind()
        {
            while (_sp > 0)
            {
                _sp--;
                Board.Unmake(_cur[_sp], _flipped[_sp]);
            }
        }

        /// <summary>
        /// 手順序付けの点数: 置換表の手 ＞ 裏返す枚数（残り深さ 3 以上のときだけ実際に指して数える）＞ 回転なし ＞ history。
        /// </summary>
        void ScoreMoves(int offset, int count, int ttMove, int depth)
        {
            var b = Board;
            int turn = b.Turn;
            bool measureGain = depth >= 3;
            for (int i = 0; i < count; i++)
            {
                int code = _moves[offset + i];
                int s;
                if (code == ttMove)
                {
                    s = 1 << 30;
                }
                else
                {
                    s = _history[turn * MaxMoves + code];
                    if ((code & 3) == 0) s += 1 << 21;
                    if (measureGain)
                    {
                        int f = b.Make(code);
                        b.Unmake(code, f);
                        s += SearchBoard.PopCount(f) << 22;
                    }
                }
                _scores[offset + i] = s;
            }
        }

        /// <summary>残りの手から点数最大のものを選んで _next の位置に入れ替え、その手を返す（同点なら生成順）。</summary>
        int PickNext(int ply)
        {
            int offset = ply * MaxMoves;
            int start = offset + _next[ply];
            int end = offset + _count[ply];
            int bestI = start;
            int bestS = _scores[start];
            for (int i = start + 1; i < end; i++)
            {
                if (_scores[i] > bestS)
                {
                    bestS = _scores[i];
                    bestI = i;
                }
            }
            if (bestI != start)
            {
                int tm = _moves[start];
                _moves[start] = _moves[bestI];
                _moves[bestI] = tm;
                int ts = _scores[start];
                _scores[start] = _scores[bestI];
                _scores[bestI] = ts;
            }
            _next[ply]++;
            return _moves[start];
        }
    }
}
