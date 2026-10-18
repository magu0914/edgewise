using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.AI;
using Edgewise.Core.Data;

namespace Edgewise.Core.Tests.AI
{
    public static class AiH
    {
        /// <summary>state の本当の手札で SearchBoard を作る（Open に関係なく両者の手札を使う。テスト専用）。</summary>
        internal static SearchBoard LoadTrue(GameState s)
        {
            var b = new SearchBoard();
            b.Load(s, Defs(s, Player.P0), Defs(s, Player.P1), false);
            return b;
        }

        public static List<CardDef> Defs(GameState s, Player p) => s.GetHand(p).Select(id => s.Cards[id]).ToList();

        /// <summary>手札の k 番目（エンジンの handIndex）に当たる SearchBoard のスロット。</summary>
        internal static int SlotOf(SearchBoard b, int player, int handIndex)
        {
            int mask = b.HandMask[player];
            for (int slot = 0; slot < SearchBoard.MaxSlots; slot++)
            {
                if ((mask & (1 << slot)) == 0) continue;
                if (handIndex-- == 0) return slot;
            }
            return -1;
        }

        /// <summary>
        /// 参照実装（GameEngine.ApplyMove）だけを使う総当たりのミニマックス。手番側から見たラウンド終局までの値。
        /// 終局の値は SearchBoard と同じ定義（スコア差×100 ± WinBonus、引き分け・Sudden Death の再戦は 0）。
        /// </summary>
        public static int BruteForce(GameState s)
        {
            int best = int.MinValue;
            foreach (var m in GameEngine.LegalMoves(s)) best = System.Math.Max(best, BruteForceChild(s, m));
            return best;
        }

        /// <summary>s で m を指した後の値（s の手番側から見た値）。</summary>
        public static int BruteForceChild(GameState s, Move m)
        {
            var next = GameEngine.ApplyMove(s, m).State;
            if (next.Phase == Phase.Ended)
            {
                var sc = GameEngine.Score(next);
                int diff = s.Turn == Player.P0 ? sc.P0 - sc.P1 : sc.P1 - sc.P0;
                return SearchBoard.TerminalValueOfDiff(diff);
            }
            if (next.SuddenDeathRound != s.SuddenDeathRound) return 0;
            return -BruteForce(next);
        }

        /// <summary>ランダムな局面を作る: ランダムなルール・デッキで始め、ランダムに plies 手進める（終局したら null）。</summary>
        public static GameState RandomPosition(TestRandom r, int plies, IReadOnlyList<CardDef> cards = null, bool? open = null,
            bool? suddenDeath = null, bool? pivot = null, bool? same = null, bool? plus = null, bool? combo = null,
            bool resonance = false, bool traits = false)
        {
            var rules = H.RandomRules(r, suddenDeath: suddenDeath, pivot: pivot, same: same, plus: plus, combo: combo, resonance: resonance, traits: traits);
            if (open.HasValue)
            {
                rules = new RuleSet(open.Value, rules.Random, rules.Same, rules.SameWall, rules.Plus, rules.Combo, rules.Elemental,
                    rules.SuddenDeath, rules.Pivot, rules.Resonance, rules.Traits);
            }
            var state = GameEngine.CreateGame(H.RandomConfig(r, rules, cards));
            for (int i = 0; i < plies && state.Phase == Phase.Playing; i++)
                state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
            return state.Phase == Phase.Playing ? state : null;
        }

        public static IReadOnlyList<CardDef> Pool => CardCatalog.All;

        /// <summary>
        /// 相手（viewer の相手）の手札だけを、viewer から見えていないカードに差し替えた局面。
        /// state.Cards も差し替えたカードの定義に合わせて作り直す（AI がそこから盗み見ていないことを確かめるため）。
        /// </summary>
        public static GameState WithOtherOpponentHand(GameState s, Player viewer, TestRandom r)
        {
            var opp = viewer.Opponent();
            var seen = new HashSet<string>(s.GetHand(viewer));
            foreach (var pc in s.Board)
            {
                if (pc.HasValue) seen.Add(pc.Value.CardId);
            }
            var oldOpp = new HashSet<string>(s.GetHand(opp));
            var candidates = CardCatalog.All.Where(c => !seen.Contains(c.Id) && !oldOpp.Contains(c.Id)).ToList();
            var newHand = new List<string>();
            for (int i = 0; i < s.GetHand(opp).Count; i++)
            {
                int k = r.NextInt(candidates.Count);
                newHand.Add(candidates[k].Id);
                candidates.RemoveAt(k);
            }

            var cards = new Dictionary<string, CardDef>();
            foreach (var id in s.GetHand(viewer)) cards[id] = s.Cards[id];
            foreach (var id in newHand) cards[id] = CardCatalog.ById[id];
            var hand0 = viewer == Player.P0 ? s.GetHand(Player.P0).ToArray() : newHand.ToArray();
            var hand1 = viewer == Player.P1 ? s.GetHand(Player.P1).ToArray() : newHand.ToArray();
            return GameState.FromParts(s.Rules, cards, s.Board.ToArray(), s.CellElements.ToArray(), hand0, hand1, s.Turn, s.FirstPlayer,
                s.GetPivotTokens(Player.P0), s.GetPivotTokens(Player.P1), s.RngState, s.Phase, s.SuddenDeathRound);
        }

        public static AiProfile Small(AiProfile p, int nodes) => p.WithMaxNodes(nodes);
    }
}
