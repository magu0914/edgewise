using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Core.Data;

namespace Edgewise.Game.Debugging
{
    /// <summary>
    /// ランダム手同士の 1 局を最後まで流し、各手のイベント列と最終局面を記録する（Phase 0/1 のデバッグ表示用）。
    /// Unity に依存しないので EditMode テストからも使う。
    /// </summary>
    public sealed class DebugMatch
    {
        public RuleSet Rules { get; }
        public long Seed { get; }
        public GameState Initial { get; }
        public GameState Final { get; }
        public IReadOnlyList<string> Log => _log;
        public int MoveCount { get; private set; }
        public int EventCount { get; private set; }

        readonly List<string> _log = new List<string>();

        public DebugMatch(RuleSet rules, long seed)
        {
            Rules = rules;
            Seed = seed;

            // デッキと手の選択も Core の乱数（mulberry32）で決める。seed が同じなら同じ 1 局になる
            uint rng = Rng.SeedToState(seed ^ 0x5eed);
            var all = CardCatalog.All;
            int deckSize = rules.Random ? 8 : GameConstants.HandSize;
            var deck0 = new string[deckSize];
            var deck1 = new string[deckSize];
            for (int i = 0; i < deckSize; i++) deck0[i] = all[Rng.NextInt(rng, all.Count, out rng)].Id;
            for (int i = 0; i < deckSize; i++) deck1[i] = all[Rng.NextInt(rng, all.Count, out rng)].Id;

            var state = GameEngine.CreateGame(new GameConfig(rules, deck0, deck1, seed, CardCatalog.ById));
            Initial = state;
            _log.Add("seed " + seed + " / first player P" + (int)state.FirstPlayer + " / hash " + GameEngine.HashState(state));

            while (state.Phase == Phase.Playing)
            {
                var moves = GameEngine.LegalMoves(state);
                var move = moves[Rng.NextInt(rng, moves.Count, out rng)];
                var result = GameEngine.ApplyMove(state, move);
                MoveCount++;
                _log.Add("#" + MoveCount + " P" + (int)state.Turn + " " + move + "  hash " + GameEngine.HashState(result.State));
                foreach (var e in result.Events)
                {
                    _log.Add("    " + e.ToJson());
                    EventCount++;
                }
                state = result.State;
            }
            Final = state;
        }
    }
}
