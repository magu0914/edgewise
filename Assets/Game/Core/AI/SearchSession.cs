using System;
using System.Collections.Generic;

namespace Edgewise.Core.AI
{
    /// <summary>
    /// 探索の作業領域（盤面バッファ・手のバッファ・置換表）。使い回すと 1 手ごとのアロケーションがなくなる。
    /// スレッドセーフではない（1 スレッド・同時に 1 セッションで使うこと）。使い回しても探索結果は変わらない。
    /// </summary>
    public sealed class SearchWorkspace
    {
        internal readonly AlphaBetaSearch Search = new AlphaBetaSearch();
    }

    /// <summary>合法手 1 つの評価（AnalyzeMoves / ヒント / 振り返り用）。</summary>
    public readonly struct MoveEvaluation
    {
        /// <summary>合法手。</summary>
        public readonly Move Move;

        /// <summary>評価値（手番側から見た値、1/100 枚単位。決定化した世界の平均）。終局の値は「スコア差×100 ± WinBonus」。</summary>
        public readonly int Value;

        /// <summary>全世界でラウンドの終局まで読み切ったか（Value が境界値でなく正確な値であることも含む）。</summary>
        public readonly bool Solved;

        /// <summary>読み終えた反復の深さ（世界ごとの最小）。0 なら未評価。</summary>
        public readonly int Depth;

        /// <summary>この手の評価に使った世界の数（Open ON なら 1）。</summary>
        public readonly int Worlds;

        /// <summary>探索した（評価値がある）か。false の手は Value が意味を持たない。</summary>
        public readonly bool Searched;

        public MoveEvaluation(Move move, int value, bool solved, int depth, int worlds, bool searched)
        {
            Move = move;
            Value = value;
            Solved = solved;
            Depth = depth;
            Worlds = worlds;
            Searched = searched;
        }

        /// <summary>
        /// 1 つの世界で読み切った値なら、予想される最終スコア（手番側, 相手）を返す（例: 6-4）。
        /// Open OFF（複数世界の平均）のときは期待値なので false。
        /// </summary>
        public bool TryGetFinalScore(out int mine, out int theirs)
        {
            mine = theirs = 0;
            if (!Solved || Worlds != 1) return false;
            if (!SearchSession.TryDecodeTerminalValue(Value, out int diff)) return false;
            mine = (10 + diff) / 2;
            theirs = 10 - mine;
            return true;
        }

        public override string ToString() =>
            Move + " = " + Value + (Solved ? " (solved)" : " (depth " + Depth + ")") + (Worlds > 1 ? " x" + Worlds : "");
    }

    /// <summary>
    /// Open OFF のとき、相手の伏せ札の候補をどう作るか（決定化。DESIGN §5-1 / §12-6）。
    /// </summary>
    public enum CardPoolModel
    {
        /// <summary>
        /// 両者のカードが重ならない前提（フリー対戦）。候補 = cardPool から自分の手札と盤上のカードを 1 枚ずつ除いたもの。
        /// </summary>
        Distinct = 0,

        /// <summary>
        /// 同じカードを両者が持ちうる（星図巡り）。候補 = cardPool（重複可）から「相手がすでに盤に出したカード」
        /// （PlacedCard.PlacedBy が相手のもの）を 1 枚ずつ除いたもの。1 つの手札の中ではカード ID を重複させない。
        /// cardPool に相手の公開済みの持ちカードを渡せば、見えている情報で候補を絞れる（ズルではない）。
        /// </summary>
        Shared = 1,
    }

    /// <summary>
    /// CPU の思考 1 回分（DESIGN §5-1）。Step(maxNodes) で少しずつ進められる反復深化 αβ。
    /// <code>
    /// var session = new SearchSession(state, me, AiProfile.Hard, seed, CardCatalog.All);
    /// while (!session.Step(4000)) { }   // Unity: 毎フレーム 1 回ずつ呼ぶ
    /// Move move = session.BestMove;      // 常に合法手。途中で打ち切っても有効
    /// </code>
    /// - 予算はノード数（profile.MaxNodes）。Step の刻み方によらず結果は同じ（決定論）。
    /// - Open OFF なら、相手の手札を「自分から見えていないカード」（cardPool から自分の手札・盤上のカードを除いたもの）から
    ///   重複なしで一様にサンプリングした K 個の世界をそれぞれ読み、各手の評価の平均（同点なら最善だった世界の数）で選ぶ。
    ///   相手の本当の手札（state の相手手札と、state.Cards にある相手のカード定義）は一切読まない。
    /// - Sudden Death はラウンドの終局まで読み、引き分けは 0。
    /// - 性格（AiProfile.Personality、DESIGN §12-5）はルートの手の選択にだけ効く。null なら従来とまったく同じ。
    /// </summary>
    public sealed class SearchSession
    {
        const int Inf = SearchConstants.Infinity;

        readonly GameState _state;
        readonly AiProfile _profile;
        readonly AlphaBetaSearch _search;
        readonly bool _analyze;
        uint _rng;
        readonly bool _blind;

        // ---- ルートの手 ----
        readonly List<Move> _legal;
        readonly int[] _legalToRoot;
        readonly int _rootCount;
        readonly int[] _rootCodes;
        readonly Move[] _rootMoves;
        readonly int[] _staticOrder;
        readonly int[] _noise;
        readonly int[] _bonus;
        readonly bool _bonusAdditive;
        readonly bool _bonusTieOnly;
        readonly int _rootMarginExtra;
        readonly int _rootEmpties;

        // ---- 世界 ----
        readonly CardDef[] _myHand;
        readonly CardDef[] _oppHand;
        readonly CardDef[] _unseen;
        readonly CardPoolModel _poolModel;
        readonly int _oppCount;
        readonly int _worldCount;
        int _worldIndex;
        bool _worldActive;
        long _worldBudget;
        int _iterDepth;
        int _maxIterDepth;
        readonly int[] _order;
        readonly int[] _lastValue;
        readonly bool[] _lastExact;
        readonly bool[] _lastHas;
        int _lastDepth;
        readonly List<CardDef> _hand0 = new List<CardDef>(SearchBoard.MaxSlots);
        readonly List<CardDef> _hand1 = new List<CardDef>(SearchBoard.MaxSlots);

        // ---- 世界ごと・深さごとの値（深さの違う値を混ぜないため。偶数手読みと奇数手読みは値の傾向が違う） ----
        const int Levels = SearchConstants.MaxPlies + 1;
        readonly int[] _wv;
        readonly bool[] _wx;
        readonly int[] _worldDepth;

        // Open ON（世界 1 つ）で途中まで読んだ反復: 前の最善手を読み終えていれば、読み終えた手の中で比べる（同じ深さどうし）
        readonly int[] _partialValue;
        readonly bool[] _partialDone;
        bool _partialValid;

        // ---- 集計（Aggregate で作り直す） ----
        readonly long[] _sum;
        readonly int[] _cnt;
        readonly int[] _votes;
        readonly int[] _solved;
        int _aggDepth;

        int _solvedWorlds;
        long _nodesAtStart;
        bool _finished;
        Move _best;

        /// <summary>思考する側。</summary>
        public Player Me { get; }

        public AiProfile Profile => _profile;

        /// <summary>探索が終わったか。</summary>
        public bool IsFinished => _finished;

        /// <summary>このセッションで数えたノード数。</summary>
        public long NodesSearched => _search.Nodes - _nodesAtStart;

        /// <summary>読む世界の数（Open ON なら 1）。</summary>
        public int WorldCount => _worldCount;

        /// <summary>読み終えた世界の数。</summary>
        public int WorldsDone => _worldIndex;

        /// <summary>今の世界で読み終えた反復の深さ。</summary>
        public int CurrentDepth => _lastDepth;

        /// <summary>ラウンドの残り手数（読み切るのに必要な深さ）。</summary>
        public int RemainingPlies => _rootEmpties;

        /// <summary>Easy の「見落とし」（Same / Plus / Combo を無視して読む）が起きたか。</summary>
        public bool Blind => _blind;

        public SearchSession(GameState state, Player me, AiProfile profile, long seed, IReadOnlyList<CardDef> cardPool = null,
            SearchWorkspace workspace = null, CardPoolModel poolModel = CardPoolModel.Distinct)
            : this(state, me, profile, seed, cardPool, workspace, false, poolModel)
        {
        }

        SearchSession(GameState state, Player me, AiProfile profile, long seed, IReadOnlyList<CardDef> cardPool,
            SearchWorkspace workspace, bool analyze, CardPoolModel poolModel)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            if (state.Phase != Phase.Playing) throw new ArgumentException("the game has already ended", nameof(state));
            if (state.Turn != me) throw new ArgumentException("it is not " + me + "'s turn", nameof(me));
            Me = me;
            _analyze = analyze;
            _search = (workspace ?? new SearchWorkspace()).Search;
            _nodesAtStart = _search.Nodes;
            _rng = Rng.SeedToState(seed);
            _poolModel = poolModel;

            // 乱数の消費順: 見落とし判定 → 各手のノイズ → 世界のサンプリング
            _blind = profile.BlindPercent > 0 && Rng.NextInt(_rng, 100, out _rng) < profile.BlindPercent;

            var opp = me.Opponent();
            var myIds = state.HandArray(me);
            _myHand = new CardDef[myIds.Length];
            for (int i = 0; i < myIds.Length; i++) _myHand[i] = GameEngine.LookupCard(state, myIds[i]);
            if (_myHand.Length > SearchBoard.MaxSlots) throw new ArgumentException("hand too large: " + _myHand.Length);

            // 相手の手札: 見える情報だけを使う（Open OFF なら枚数だけ）
            var visible = GameEngine.VisibleHand(state, me, opp);
            _oppCount = visible.Length;
            bool hidden = !state.Rules.Open && _oppCount > 0;
            if (!hidden)
            {
                _oppHand = new CardDef[_oppCount];
                for (int i = 0; i < _oppCount; i++) _oppHand[i] = GameEngine.LookupCard(state, visible[i]);
                _unseen = Array.Empty<CardDef>();
                _worldCount = 1;
            }
            else
            {
                if (cardPool == null) throw new ArgumentNullException(nameof(cardPool), "cardPool is required when Open is off");
                _oppHand = null;
                _unseen = poolModel == CardPoolModel.Shared ? UnseenCardsShared(state, me, cardPool) : UnseenCards(state, me, cardPool);
                _worldCount = profile.Worlds;
            }

            // ---- ルートの手（同じ定義のカードの重複はまとめる。対局では無駄な回転も読まない） ----
            _legal = GameEngine.LegalMoves(state);
            if (_legal.Count == 0) throw new ArgumentException("no legal moves", nameof(state));
            _legalToRoot = new int[_legal.Count];
            var codes = new List<int>();
            for (int i = 0; i < _legal.Count; i++)
            {
                var m = _legal[i];
                int rep = -1;
                for (int j = 0; j < i; j++)
                {
                    var o = _legal[j];
                    if (o.Cell == m.Cell && o.Rotation == m.Rotation && SameDef(_myHand[o.HandIndex], _myHand[m.HandIndex]))
                    {
                        rep = _legalToRoot[j];
                        break;
                    }
                }
                if (rep < 0 && !analyze && m.Rotation != 0 && !UsefulRotation(_myHand[m.HandIndex].Sides, m.Rotation))
                {
                    _legalToRoot[i] = -1;
                    continue;
                }
                if (rep < 0)
                {
                    rep = codes.Count;
                    codes.Add(SearchBoard.Code(m.HandIndex, m.Cell, m.Rotation));
                }
                _legalToRoot[i] = rep;
            }
            _rootCount = codes.Count;
            _rootCodes = codes.ToArray();
            _rootMoves = new Move[_rootCount];
            for (int i = 0; i < _rootCount; i++)
                _rootMoves[i] = new Move(SearchBoard.SlotOf(_rootCodes[i]), SearchBoard.CellOf(_rootCodes[i]), SearchBoard.RotOf(_rootCodes[i]));

            // 性格（解析のときは使わない）。null なら乱数の消費も含めて従来どおり
            var personality = analyze ? null : profile.Personality;
            int noiseWidth = profile.Noise + (personality != null ? personality.Volatility : 0);
            _noise = new int[_rootCount];
            if (noiseWidth > 0)
            {
                for (int i = 0; i < _rootCount; i++) _noise[i] = Rng.NextInt(_rng, 2 * noiseWidth + 1, out _rng) - noiseWidth;
            }
            _bonus = new int[_rootCount];
            if (personality != null && personality.HasMoveBonus)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                for (int i = 0; i < _rootCount; i++)
                {
                    _bonus[i] = personality.MoveBonus(state, _rootMoves[i], _blind);
                    if (_bonus[i] < lo) lo = _bonus[i];
                    if (_bonus[i] > hi) hi = _bonus[i];
                }
                _bonusTieOnly = profile.PersonalityAsTieBreak;
                _bonusAdditive = !_bonusTieOnly;
                // 足し算で選ぶときは、ボーナスで逆転しうる手の値が境界値（窓の外）にならないよう、ルートの窓を広げる
                if (_bonusAdditive) _rootMarginExtra = hi - lo;
            }
            if (personality != null && personality.Volatility > 0) _rootMarginExtra += 2 * personality.Volatility;

            _order = new int[_rootCount];
            _lastValue = new int[_rootCount];
            _lastExact = new bool[_rootCount];
            _lastHas = new bool[_rootCount];
            _sum = new long[_rootCount];
            _cnt = new int[_rootCount];
            _votes = new int[_rootCount];
            _solved = new int[_rootCount];
            _wv = new int[_worldCount * Levels * _rootCount];
            _wx = new bool[_worldCount * Levels * _rootCount];
            _worldDepth = new int[_worldCount];
            _partialValue = new int[_rootCount];
            _partialDone = new bool[_rootCount];

            // 静的な順序（すぐ裏返せる枚数が多い順 → 回転なし → 生成順）。予算 0 のときの手もこれで決まる
            _staticOrder = new int[_rootCount];
            var board = _search.Board;
            _hand0.Clear();
            _hand1.Clear();
            var mine = me == Player.P0 ? _hand0 : _hand1;
            mine.AddRange(_myHand);
            board.Load(state, _hand0, _hand1, _blind);
            _rootEmpties = board.EmptyCount;
            var gain = new int[_rootCount];
            for (int i = 0; i < _rootCount; i++)
            {
                int f = board.Make(_rootCodes[i]);
                board.Unmake(_rootCodes[i], f);
                gain[i] = SearchBoard.PopCount(f) * 2 + (SearchBoard.RotOf(_rootCodes[i]) == 0 ? 1 : 0);
                _staticOrder[i] = i;
            }
            StableSortDescending(_staticOrder, gain, _rootCount);
            _best = _rootMoves[_staticOrder[0]];

            // 手が 1 つしかなければ読まない（解析のときは値が要るので読む）
            if (_rootCount == 1 && !analyze) _finished = true;
        }

        static bool SameDef(CardDef a, CardDef b) => ReferenceEquals(a, b) || (a.Sides == b.Sides && a.Element == b.Element);

        static bool UsefulRotation(Sides s, int rot)
        {
            var rs = Board.RotateSides(s, rot);
            for (int q = 0; q < rot; q++)
            {
                if (Board.RotateSides(s, q) == rs) return false;
            }
            return true;
        }

        /// <summary>me から見えていないカード（pool から、自分の手札と盤上のカードを 1 枚ずつ除いたもの。pool の順を保つ）。</summary>
        internal static CardDef[] UnseenCards(GameState state, Player me, IReadOnlyList<CardDef> pool)
        {
            var seen = new List<string>();
            foreach (var id in state.HandArray(me)) seen.Add(id);
            foreach (var pc in state.BoardArray)
            {
                if (pc.HasValue) seen.Add(pc.Value.CardId);
            }
            var result = new List<CardDef>(pool.Count);
            foreach (var def in pool)
            {
                int k = seen.IndexOf(def.Id);
                if (k >= 0)
                {
                    seen.RemoveAt(k);
                    continue;
                }
                result.Add(def);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 探索を最大 maxNodes ノード進める。終わったら true（以後 BestMove は変わらない）。
        /// maxNodes の刻み方は結果に影響しない。
        /// </summary>
        public bool Step(int maxNodes)
        {
            if (_finished) return true;
            int budget = maxNodes < 1 ? 1 : maxNodes;
            while (!_finished)
            {
                if (!_worldActive) BeginWorld();
                int r = _search.Run(ref budget, ref _worldBudget);
                if (r == AlphaBetaSearch.Paused) return false;
                if (r == AlphaBetaSearch.Completed) IterationCompleted();
                else IterationAborted();
            }
            return true;
        }

        /// <summary>終わるまで探索する（dotnet・テスト用）。</summary>
        public Move Run()
        {
            while (!Step(1 << 20))
            {
            }
            return BestMove;
        }

        void BeginWorld()
        {
            int w = _worldIndex;
            _hand0.Clear();
            _hand1.Clear();
            var mine = Me == Player.P0 ? _hand0 : _hand1;
            var theirs = Me == Player.P0 ? _hand1 : _hand0;
            mine.AddRange(_myHand);
            if (_oppHand != null) theirs.AddRange(_oppHand);
            else SampleOpponentHand(theirs);

            var search = _search;
            search.Board.Load(_state, _hand0, _hand1, _blind);
            search.Board.Weights = _profile.Weights;
            search.BeginWorld(_profile.TableBits, _rootCodes, _rootCount);

            long used = search.Nodes - _nodesAtStart;
            long left = _profile.MaxNodes - used;
            _worldBudget = left <= 0 ? 0 : left / (_worldCount - w);

            int maxDepth = _profile.MaxDepth == 0 ? SearchConstants.MaxPlies : _profile.MaxDepth;
            _maxIterDepth = maxDepth < _rootEmpties ? maxDepth : _rootEmpties;
            _iterDepth = 1;
            _lastDepth = 0;
            for (int i = 0; i < _rootCount; i++)
            {
                _order[i] = _staticOrder[i];
                _lastHas[i] = false;
                _lastExact[i] = false;
                _lastValue[i] = -Inf;
            }
            search.BeginIteration(_iterDepth, _order, RootMargin);
            _worldActive = true;
        }

        int RootMargin => _analyze || _profile.RootMargin >= Inf - _rootMarginExtra ? Inf : _profile.RootMargin + _rootMarginExtra;

        /// <summary>
        /// 星図巡り用の候補（CardPoolModel.Shared）: pool から、相手（me の相手）が盤に置いたカードを 1 枚ずつ除いたもの。
        /// 自分の手札・自分が置いたカードは除かない（相手も同じカードを持ちうる）。pool の順を保つ。
        /// </summary>
        internal static CardDef[] UnseenCardsShared(GameState state, Player me, IReadOnlyList<CardDef> pool)
        {
            var opp = me.Opponent();
            var placed = new List<string>();
            foreach (var pc in state.BoardArray)
            {
                if (pc.HasValue && pc.Value.PlacedBy == opp) placed.Add(pc.Value.CardId);
            }
            var result = new List<CardDef>(pool.Count);
            foreach (var def in pool)
            {
                int k = placed.IndexOf(def.Id);
                if (k >= 0)
                {
                    placed.RemoveAt(k);
                    continue;
                }
                result.Add(def);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 見えていないカードから相手の手札を重複なしで一様に抜き出す（部分 Fisher–Yates）。
        /// Shared のときは、手札にすでにあるカード ID を引いたら候補から外して引き直す（1 つの手札の中で ID を重複させない）。
        /// </summary>
        void SampleOpponentHand(List<CardDef> into)
        {
            var pool = (CardDef[])_unseen.Clone();
            int n = pool.Length;
            bool distinctIds = _poolModel == CardPoolModel.Shared;
            int start = 0;
            for (int i = 0; i < _oppCount; i++)
            {
                bool drawn = false;
                while (distinctIds && start < n)
                {
                    int j = start + Rng.NextInt(_rng, n - start, out _rng);
                    var t = pool[start];
                    pool[start] = pool[j];
                    pool[j] = t;
                    var pick = pool[start++];
                    if (ContainsId(into, pick.Id)) continue;
                    into.Add(pick);
                    drawn = true;
                    break;
                }
                if (distinctIds)
                {
                    if (!drawn) into.Add(pool.Length > 0 ? pool[Rng.NextInt(_rng, pool.Length, out _rng)] : _myHand[0]);
                    continue;
                }
                if (i < n)
                {
                    int j = i + Rng.NextInt(_rng, n - i, out _rng);
                    var t = pool[i];
                    pool[i] = pool[j];
                    pool[j] = t;
                    into.Add(pool[i]);
                }
                else
                {
                    // 見えていないカードが足りない（小さな pool を渡したとき）。重複を許して補う
                    into.Add(pool.Length > 0 ? pool[Rng.NextInt(_rng, pool.Length, out _rng)] : _myHand[0]);
                }
            }
        }

        static bool ContainsId(List<CardDef> cards, string id)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Id == id) return true;
            }
            return false;
        }

        void IterationCompleted()
        {
            var s = _search;
            int baseIdx = (_worldIndex * Levels + _iterDepth) * _rootCount;
            for (int i = 0; i < _rootCount; i++)
            {
                _lastValue[i] = s.IterValue[i];
                _lastExact[i] = s.IterExact[i];
                _lastHas[i] = true;
                _wv[baseIdx + i] = s.IterValue[i];
                _wx[baseIdx + i] = s.IterExact[i];
            }
            _lastDepth = _iterDepth;
            if (_iterDepth >= _maxIterDepth)
            {
                FinishWorld();
                return;
            }
            // 次の反復は前の反復の値の良い順に読む（同点なら前の順）
            StableSortDescending(_order, _lastValue, _rootCount);
            _iterDepth++;
            s.BeginIteration(_iterDepth, _order, RootMargin);
        }

        void IterationAborted()
        {
            // 途中で打ち切った反復は、世界が 1 つのときだけ使う（前の最善手を読み終えていれば、読み終えた手どうしで比べる）
            var s = _search;
            _partialValid = _worldCount == 1 && _rootCount > 0 && _lastDepth > 0 && s.IterDone[_order[0]];
            if (_partialValid)
            {
                for (int i = 0; i < _rootCount; i++)
                {
                    _partialDone[i] = s.IterDone[i];
                    _partialValue[i] = s.IterValue[i];
                }
            }
            FinishWorld();
        }

        void FinishWorld()
        {
            _worldDepth[_worldIndex] = _lastDepth;
            if (_lastDepth >= _rootEmpties) _solvedWorlds++;
            _worldActive = false;
            _worldIndex++;
            if (_worldIndex >= _worldCount)
            {
                Aggregate();
                _best = _rootMoves[ChooseRoot(true)];
                _finished = true;
            }
        }

        /// <summary>
        /// 読み終えた世界の値を集計する。全世界が読み終えた深さのうち最も深いもの（＝世界ごとの完了深さの最小値）の値だけを使い、
        /// 手ごとの平均（_sum / _cnt）と「その世界で最善だった回数」（_votes）を作る。
        /// </summary>
        void Aggregate()
        {
            int depth = int.MaxValue;
            for (int w = 0; w < _worldIndex; w++)
            {
                if (_worldDepth[w] > 0 && _worldDepth[w] < depth) depth = _worldDepth[w];
            }
            for (int i = 0; i < _rootCount; i++)
            {
                _sum[i] = 0;
                _cnt[i] = 0;
                _votes[i] = 0;
                _solved[i] = 0;
            }
            _aggDepth = depth == int.MaxValue ? 0 : depth;
            if (_aggDepth == 0) return;
            for (int w = 0; w < _worldIndex; w++)
            {
                if (_worldDepth[w] < _aggDepth) continue;
                int baseIdx = (w * Levels + _aggDepth) * _rootCount;
                int bestId = -1;
                for (int k = 0; k < _rootCount; k++)
                {
                    int i = _staticOrder[k];
                    int v = _wv[baseIdx + i];
                    _sum[i] += v;
                    _cnt[i]++;
                    if (_aggDepth >= _rootEmpties && _wx[baseIdx + i]) _solved[i]++;
                    if (bestId < 0 || v > _wv[baseIdx + bestId]) bestId = i;
                }
                _votes[bestId]++;
            }
        }

        /// <summary>集計から手を選ぶ: 平均（＋ノイズ）が最大 → 最善だった世界の数 → 静的な順序。</summary>
        int ChooseRoot(bool withNoise)
        {
            if (_partialValid)
            {
                int pb = -1;
                for (int k = 0; k < _rootCount; k++)
                {
                    int i = _order[k];
                    if (!_partialDone[i]) continue;
                    if (pb < 0) pb = i;
                    else
                    {
                        int vi = _partialValue[i] + (withNoise ? _noise[i] + Bonus(i) : 0);
                        int vb = _partialValue[pb] + (withNoise ? _noise[pb] + Bonus(pb) : 0);
                        if (vi > vb || (vi == vb && withNoise && _bonusTieOnly && _bonus[i] > _bonus[pb])) pb = i;
                    }
                }
                if (pb >= 0) return pb;
            }
            int best = -1;
            int bestValue = 0;
            for (int k = 0; k < _rootCount; k++)
            {
                int i = _staticOrder[k];
                if (_cnt[i] == 0) continue;
                int v = Average(i) + (withNoise ? _noise[i] + Bonus(i) : 0);
                bool better = best < 0 || v > bestValue;
                if (!better && v == bestValue)
                {
                    // 性格（Hard）: 評価がちょうど同じ手の間では性格の好みを優先する（世界の票より先。Open ON では票は 1 手にしか入らないため）
                    if (withNoise && _bonusTieOnly && _bonus[i] != _bonus[best]) better = _bonus[i] > _bonus[best];
                    else better = _votes[i] > _votes[best];
                }
                if (better)
                {
                    best = i;
                    bestValue = v;
                }
            }
            if (best < 0) return _staticOrder[0];
            return TieBreak(best);
        }

        /// <summary>
        /// 世界が 1 つで読み切れているとき、最善値が「引き分け」か「負け」なら、同じ結果になる手の中から
        /// 浅い読み（TieBreakDepth 手、途中局面の評価を使う）の値が最も良い手を選ぶ。
        /// 完全な相手を前提にすると区別できない手のうち、相手が間違えやすい（見た目に有利な）手を選ぶため。勝ちのときは何もしない。
        /// </summary>
        int TieBreak(int best)
        {
            int t = _profile.TieBreakDepth;
            if (t <= 0 || _worldIndex < _worldCount || _aggDepth < _rootEmpties || _aggDepth < 2) return best;
            if (t > _aggDepth - 1) t = _aggDepth - 1;
            int choice = best;
            if (_worldCount == 1)
            {
                int solvedBase = _aggDepth * _rootCount;
                int shallowBase = t * _rootCount;
                int bestValue = _wv[solvedBase + best];
                if (bestValue > 0) return best;
                for (int k = 0; k < _rootCount; k++)
                {
                    int i = _staticOrder[k];
                    int v = _wv[solvedBase + i];
                    // 引き分けなら正確に 0 の手だけ。負けなら負けが確定している手（上界が負でもよい）
                    bool sameClass = bestValue == 0 ? v == 0 && _wx[solvedBase + i] : v < 0;
                    if (!sameClass) continue;
                    if (_wv[shallowBase + i] > _wv[shallowBase + choice]
                        || (_bonusTieOnly && _wv[shallowBase + i] == _wv[shallowBase + choice] && _bonus[i] > _bonus[choice])) choice = i;
                }
                return choice;
            }

            // 複数の世界: 平均がちょうど同じ（多くは全世界で引き分け）手どうしを、浅い読みの値の合計で比べる
            int bestAvg = Average(best);
            if (bestAvg > 0) return best;
            long choiceShallow = ShallowSum(choice, t);
            for (int k = 0; k < _rootCount; k++)
            {
                int i = _staticOrder[k];
                if (_cnt[i] != _cnt[best] || Average(i) != bestAvg) continue;
                long s = ShallowSum(i, t);
                if (s > choiceShallow || (_bonusTieOnly && s == choiceShallow && _bonus[i] > _bonus[choice]))
                {
                    choice = i;
                    choiceShallow = s;
                }
            }
            return choice;
        }

        int Average(int i) => FloorDiv(_sum[i], _cnt[i]);

        /// <summary>性格のボーナスのうち評価に足す分（足し算で選ぶ性格のときだけ）。</summary>
        int Bonus(int i) => _bonusAdditive ? _bonus[i] : 0;

        long ShallowSum(int i, int depth)
        {
            long s = 0;
            for (int w = 0; w < _worldIndex; w++)
            {
                if (_worldDepth[w] >= depth) s += _wv[(w * Levels + depth) * _rootCount + i];
            }
            return s;
        }

        static int FloorDiv(long a, int b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return (int)q;
        }

        /// <summary>
        /// 選んだ手（常に合法手）。探索の途中でも、その時点の最善手を返す
        /// （読み終えた世界の集計 → 今の世界の最後の反復 → 静的な順序の順に使う）。
        /// </summary>
        public Move BestMove
        {
            get
            {
                if (_finished) return _best;
                if (_worldIndex > 0)
                {
                    Aggregate();
                    return _rootMoves[ChooseRoot(true)];
                }
                int bestId = -1;
                for (int k = 0; k < _rootCount; k++)
                {
                    int i = _order[k];
                    if (!_lastHas[i]) continue;
                    if (bestId < 0 || _lastValue[i] > _lastValue[bestId]) bestId = i;
                }
                return _rootMoves[bestId >= 0 ? bestId : _staticOrder[0]];
            }
        }

        /// <summary>合法手ごとの評価（GameEngine.LegalMoves の順）。読み終えた世界の集計を使う。</summary>
        public MoveEvaluation[] GetEvaluations()
        {
            if (!_finished) Aggregate();
            var result = new MoveEvaluation[_legal.Count];
            for (int k = 0; k < _legal.Count; k++)
            {
                int i = _legalToRoot[k];
                if (i < 0 || _cnt[i] == 0)
                {
                    result[k] = new MoveEvaluation(_legal[k], -Inf, false, 0, 0, false);
                    continue;
                }
                bool solved = _solved[i] == _worldCount && _cnt[i] == _worldCount;
                result[k] = new MoveEvaluation(_legal[k], Average(i), solved, _aggDepth, _cnt[i], true);
            }
            return result;
        }

        /// <summary>
        /// 全世界でラウンドの終局まで読み切ったうえで手を選んだか（最後の 1 手のように手が 1 つしかなく結果が決まっている場合も true）。
        /// 選んだ手の値そのものが正確かどうかは GetEvaluations の Solved を見る。
        /// </summary>
        public bool BestMoveSolved => _finished && (_solvedWorlds == _worldCount || (_rootCount == 1 && _rootEmpties == 1));
        // ------------------------------------------------------------------
        // 静的 API
        // ------------------------------------------------------------------

        /// <summary>終わるまで読んで手を返す（Sim・テスト用）。</summary>
        public static Move ChooseMove(GameState state, Player me, AiProfile profile, long seed, IReadOnlyList<CardDef> cardPool = null,
            SearchWorkspace workspace = null, CardPoolModel poolModel = CardPoolModel.Distinct) =>
            new SearchSession(state, me, profile, seed, cardPool, workspace, poolModel).Run();

        /// <summary>
        /// 解析 API（DESIGN §5-1）: 合法手ごとの評価値と「読み切ったか」を返す（GameEngine.LegalMoves の順）。
        /// Hard と同じ探索で、ルートの全手を正確に評価する。viewer は手番のプレイヤー。Open OFF なら決定化を使い、相手の手札を盗み見ない。
        /// </summary>
        public static MoveEvaluation[] AnalyzeMoves(GameState state, Player viewer, int budgetNodes, IReadOnlyList<CardDef> cardPool = null,
            long seed = 0, SearchWorkspace workspace = null, CardPoolModel poolModel = CardPoolModel.Distinct)
        {
            var session = CreateAnalysis(state, viewer, AiProfile.Hard.WithMaxNodes(budgetNodes), seed, cardPool, workspace, poolModel);
            session.Run();
            return session.GetEvaluations();
        }

        /// <summary>解析用のセッション（全手を正確に評価する。Step で少しずつ進められる）。</summary>
        public static SearchSession CreateAnalysis(GameState state, Player viewer, AiProfile profile, long seed,
            IReadOnlyList<CardDef> cardPool = null, SearchWorkspace workspace = null, CardPoolModel poolModel = CardPoolModel.Distinct) =>
            new SearchSession(state, viewer, profile, seed, cardPool, workspace, true, poolModel);

        /// <summary>終局の評価値からスコア差（手番側 − 相手）を復元する。途中局面の評価値なら false。</summary>
        public static bool TryDecodeTerminalValue(int value, out int diff)
        {
            diff = 0;
            if (value == 0) return true;
            int raw = value > 0 ? value - SearchConstants.WinBonus : value + SearchConstants.WinBonus;
            if (raw % 100 != 0 || (value > 0) != (raw > 0)) return false;
            diff = raw / 100;
            return true;
        }

        static void StableSortDescending(int[] ids, int[] key, int count)
        {
            // 挿入ソート（n ≤ 180、安定）
            for (int i = 1; i < count; i++)
            {
                int id = ids[i];
                int k = key[id];
                int j = i - 1;
                while (j >= 0 && key[ids[j]] < k)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }
                ids[j + 1] = id;
            }
        }
    }
}
