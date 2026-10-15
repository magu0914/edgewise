using System.Collections.Generic;
using Edgewise.Core;

namespace Edgewise.Game.Match
{
    /// <summary>
    /// 予告（DESIGN §8 の 4・§8-1 予告）: その手を指したら裏返るマスと、Same / Plus が成立するか。
    /// 表示側でルールを再実装しないため、エンジンの ApplyMove を局面に対して呼ぶだけ（GameState は不変なので元の局面は変わらない）。
    /// </summary>
    public sealed class MovePreview
    {
        public Move Move { get; }

        /// <summary>裏返るマス（イベント順。Same/Plus → 通常 → Combo）。</summary>
        public IReadOnlyList<int> FlipCells { get; }

        /// <summary>Combo（連鎖）で裏返るマス。</summary>
        public IReadOnlyList<int> ComboCells { get; }

        public bool Same { get; }
        public bool SameWall { get; }
        public bool Plus { get; }

        readonly List<int> _from;

        /// <summary>cell を裏返す起点のマス（置くマス、または Combo の連鎖元）。裏返らないなら -1。</summary>
        public int FromOf(int cell)
        {
            for (int i = 0; i < FlipCells.Count; i++)
            {
                if (FlipCells[i] == cell) return _from[i];
            }
            return -1;
        }

        MovePreview(Move move, List<int> flips, List<int> from, List<int> combo, bool same, bool sameWall, bool plus)
        {
            _from = from;
            Move = move;
            FlipCells = flips;
            ComboCells = combo;
            Same = same;
            SameWall = sameWall;
            Plus = plus;
        }

        public bool Flips(int cell)
        {
            for (int i = 0; i < FlipCells.Count; i++)
            {
                if (FlipCells[i] == cell) return true;
            }
            return false;
        }

        /// <summary>不正な手なら null。</summary>
        public static MovePreview Compute(GameState state, Move move)
        {
            if (state == null || !GameEngine.IsLegalMove(state, move)) return null;
            var result = GameEngine.ApplyMove(state, move);
            var flips = new List<int>();
            var from = new List<int>();
            var combo = new List<int>();
            bool same = false, sameWall = false, plus = false;
            foreach (var e in result.Events)
            {
                if (e is FlipEvent f)
                {
                    flips.Add(f.Cell);
                    from.Add(f.From);
                    if (f.Cause == FlipCause.Combo) combo.Add(f.Cell);
                }
                else if (e is RuleEvent r)
                {
                    if (r.Rule == RuleHeading.Same) same = true;
                    else if (r.Rule == RuleHeading.SameWall) sameWall = true;
                    else if (r.Rule == RuleHeading.Plus) plus = true;
                }
            }
            return new MovePreview(move, flips, from, combo, same, sameWall, plus);
        }
    }
}
