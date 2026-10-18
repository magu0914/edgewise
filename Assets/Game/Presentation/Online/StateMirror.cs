using System.Collections.Generic;
using Edgewise.Core;

namespace Edgewise.Game.Online
{
    /// <summary>
    /// 席を入れ替えた見た目用の写し（DESIGN §9-1「自分は常に手前」）。
    /// 画面は席 0 を手前（自分）に描くので、席 1 で遊ぶ側は局面とイベントを写してから渡す。
    /// エンジンの正の局面（hashState の突き合わせに使う）は写さない。写しは表示と予告にだけ使う。
    /// </summary>
    public static class StateMirror
    {
        public static Player Swap(Player p) => p == Player.P0 ? Player.P1 : Player.P0;

        public static GameResult Swap(GameResult r) => r == GameResult.P0 ? GameResult.P1 : r == GameResult.P1 ? GameResult.P0 : GameResult.Draw;

        public static GameState Mirror(GameState s)
        {
            var board = new PlacedCard?[GameConstants.BoardSize];
            var elems = new Element?[GameConstants.BoardSize];
            for (int i = 0; i < board.Length; i++)
            {
                var c = s.Board[i];
                board[i] = c.HasValue ? new PlacedCard(c.Value.Card, Swap(c.Value.Owner), c.Value.Rotation, Swap(c.Value.PlacedBy), c.Value.GuardUsed) : (PlacedCard?)null;
                elems[i] = s.CellElements[i];
            }
            return GameState.FromParts(s.Rules, s.Cards, board, elems, ToList(s.GetHand(Player.P1)), ToList(s.GetHand(Player.P0)),
                Swap(s.Turn), Swap(s.FirstPlayer), s.GetPivotTokens(Player.P1), s.GetPivotTokens(Player.P0), s.RngState, s.Phase, s.SuddenDeathRound);
        }

        public static IReadOnlyList<GameEvent> Mirror(IReadOnlyList<GameEvent> events)
        {
            var list = new List<GameEvent>(events.Count);
            foreach (var e in events)
            {
                switch (e)
                {
                    case PlaceEvent p:
                        list.Add(new PlaceEvent(Swap(p.Player), p.Cell, p.CardId, p.Rotation));
                        break;
                    case FlipEvent f:
                        list.Add(new FlipEvent(f.Cell, Swap(f.To), f.Cause, f.ChainDepth, f.From));
                        break;
                    case TurnEvent t:
                        list.Add(new TurnEvent(Swap(t.Player)));
                        break;
                    case EndEvent end:
                        list.Add(new EndEvent((end.Score.P1, end.Score.P0), Swap(end.Result)));
                        break;
                    default:
                        list.Add(e);
                        break;
                }
            }
            return list;
        }

        static List<string> ToList(ReadOnlyArray<string> a)
        {
            var l = new List<string>(a.Count);
            for (int i = 0; i < a.Count; i++) l.Add(a[i]);
            return l;
        }
    }
}
