using System.Collections.Generic;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Game.Match;
using UnityEngine;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// E2E のスクリーンショット「COMBO ×2 以上の瞬間」を再現するための seed 探し（撮影用の道具。ゲームには含まれない）。
    /// 既定の対戦設定（CPU Normal・中級・Open/Same/Plus/Combo/Pivot）で、MatchController と同じ配牌・CPU の乱数を使い、
    /// プレイヤー側は E2E スクリプトと同じ方針（回転なしの手で Same/Plus が出る手 → 裏返す枚数が最も多い手）で指して、
    /// 連鎖の段が 2 以上になる seed を探す。
    ///   tools/RunUnity.ps1 -LogName combo.log -Extra '-executeMethod','Edgewise.EditorTools.ComboSeedFinder.Run'
    /// </summary>
    public static class ComboSeedFinder
    {
        public static void Run()
        {
            var settings = new MatchSettings();
            var rules = settings.ToRuleSet();
            int found = 0;
            for (long seed = 1; seed <= 3000 && found < 5; seed++)
            {
                var (d0, d1) = Dealer.Deal(settings.band, settings.random, seed);
                var state = GameEngine.CreateGame(new GameConfig(rules, d0, d1, seed, CardCatalog.ById));
                uint rng = Rng.SeedToState(seed ^ 0x2545F491L);
                int ply = 0;
                while (state.Phase == Phase.Playing)
                {
                    Move move;
                    if (state.Turn == Player.P0) move = PlayerPolicy(state);
                    else
                    {
                        long moveSeed = Rng.NextUInt32(rng, out rng);
                        move = SearchSession.ChooseMove(state, state.Turn, settings.ProfileFor(Player.P1), moveSeed, CardCatalog.All);
                    }
                    var r = GameEngine.ApplyMove(state, move);
                    int depth = r.Events.OfType<FlipEvent>().Select(f => f.ChainDepth).DefaultIfEmpty(0).Max();
                    if (depth >= 2)
                    {
                        Debug.Log("[Edgewise] combo seed=" + seed + " ply=" + ply + " by=P" + (int)state.Turn + " depth=" + depth);
                        found++;
                        break;
                    }
                    state = r.State;
                    ply++;
                }
            }
            Debug.Log("[Edgewise] combo search done, found " + found);
        }

        /// <summary>E2E スクリプトと同じ: 回転なしの合法手の中で、Same/Plus が出る最初の手、なければ裏返す枚数が最も多い最初の手。</summary>
        public static Move PlayerPolicy(GameState state)
        {
            var moves = new List<(Move M, MovePreview P)>();
            foreach (var m in GameEngine.LegalMoves(state))
            {
                if (m.Rotation != 0) continue;
                moves.Add((m, MovePreview.Compute(state, m)));
            }
            foreach (var x in moves)
                if (x.P.Same || x.P.SameWall || x.P.Plus) return x.M;
            var best = moves[0];
            foreach (var x in moves)
                if (x.P.FlipCells.Count > best.P.FlipCells.Count) best = x;
            return best.M;
        }
    }
}
