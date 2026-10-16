using System;
using System.Collections.Generic;

namespace Edgewise.Core.AI
{
    /// <summary>
    /// CPU の性格（DESIGN §12-5）。ルート局面の手の選択にだけ「好み」のボーナスとして効く（探索の中身・評価関数は変えない）。
    /// 値はすべて評価値と同じ単位（1/100 枚）の整数。不変。
    /// - AiProfile.PersonalityAsTieBreak が false（Easy / Normal / 弱めの Hard）なら、ボーナスを各手の評価に足して選ぶ（性格が手に表れる）。
    /// - true（Hard）なら、評価がちょうど同じ手の間の好みとしてだけ使う（強さは変わらない）。
    /// ボーナスは「自分から見える情報」（盤面・自分の手札・マス属性・ルール）だけで決まる。相手の手札は使わない。
    /// </summary>
    public sealed class AiPersonality
    {
        /// <summary>性格なし（すべて 0）と同じ扱いになる値。AiProfile では null を使う。</summary>
        public static readonly AiPersonality Neutral = new AiPersonality("neutral");

        /// <summary>識別子（ログ・Sim 用）。</summary>
        public string Name { get; }

        /// <summary>角（0, 2, 6, 8）に置く手へのボーナス。</summary>
        public int Corner { get; }

        /// <summary>辺の中央（1, 3, 5, 7）に置く手へのボーナス。</summary>
        public int Edge { get; }

        /// <summary>その手ですぐ裏返る 1 枚あたりのボーナス（原因を問わない）。</summary>
        public int Capture { get; }

        /// <summary>Same（Same Wall を含む）が成立して 1 枚以上裏返る手へのボーナス。</summary>
        public int Same { get; }

        /// <summary>Plus が成立して 1 枚以上裏返る手へのボーナス。</summary>
        public int Plus { get; }

        /// <summary>Combo で裏返る 1 枚あたりのボーナス。</summary>
        public int Combo { get; }

        /// <summary>回転して置く手（Pivot を切る手）へのボーナス。</summary>
        public int Pivot { get; }

        /// <summary>Elemental: 属性が一致するマスに置く手へのボーナス（不一致のマスは同じ値の減点）。</summary>
        public int Element { get; }

        /// <summary>
        /// 守り: 置いたカードの辺のうち、空きマスに面した辺 1 つあたり (辺の値 − 5) × Defense、盤外に面した辺は 5 × Defense。
        /// 弱い辺を空きマスにさらす手を嫌い、強い辺で囲う手を好む。
        /// </summary>
        public int Defense { get; }

        /// <summary>揺らぎ: AiProfile.Noise に足すノイズの幅（±）。読みにくさを表す。</summary>
        public int Volatility { get; }

        public AiPersonality(string name, int corner = 0, int edge = 0, int capture = 0, int same = 0, int plus = 0, int combo = 0,
            int pivot = 0, int element = 0, int defense = 0, int volatility = 0)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            if (volatility < 0) throw new ArgumentOutOfRangeException(nameof(volatility));
            Corner = corner;
            Edge = edge;
            Capture = capture;
            Same = same;
            Plus = plus;
            Combo = combo;
            Pivot = pivot;
            Element = element;
            Defense = defense;
            Volatility = volatility;
        }

        /// <summary>手のボーナスがすべて 0 か（揺らぎは含まない）。</summary>
        public bool HasMoveBonus =>
            Corner != 0 || Edge != 0 || Capture != 0 || Same != 0 || Plus != 0 || Combo != 0 || Pivot != 0 || Element != 0 || Defense != 0;

        /// <summary>
        /// state（手番は me）で move を指したときのボーナス。盤面・自分の手札・マス属性・ルールだけを使う
        /// （裏返しの判定は相手の手札に依存しないので、GameEngine.ApplyMove のイベントから数えてよい）。
        /// blind = true なら Same / Plus / Combo を見落とした状態で数える（Easy の見落としと揃える）。
        /// </summary>
        public int MoveBonus(GameState state, Move move, bool blind)
        {
            if (!HasMoveBonus) return 0;
            int bonus = 0;
            int cell = move.Cell;
            if (cell != 4) bonus += (cell & 1) == 0 ? Corner : Edge; // 4 = 中央

            if (move.Rotation != 0) bonus += Pivot;

            var rules = state.Rules;
            var def = GameEngine.LookupCard(state, state.HandArray(state.Turn)[move.HandIndex]);
            var elements = state.CellElementArray;
            int mod = 0;
            if (rules.Elemental && elements[cell].HasValue)
            {
                mod = def.Element == elements[cell].Value ? 1 : -1;
                bonus += mod * Element;
            }

            if (Defense != 0)
            {
                var sides = Board.RotateSides(def.Sides, move.Rotation);
                var board = state.BoardArray;
                for (int dir = 0; dir < 4; dir++)
                {
                    int n = Board.Neighbor(cell, dir);
                    if (n == GameConstants.Wall) bonus += 5 * Defense;
                    else if (!board[n].HasValue) bonus += (sides[dir] + mod - 5) * Defense;
                }
            }

            if (Capture != 0 || Same != 0 || Plus != 0 || Combo != 0)
            {
                var view = state;
                if (blind && (rules.Same || rules.Plus || rules.Combo))
                {
                    view = state.WithRules(new RuleSet(rules.Open, rules.Random, false, false, false, false, rules.Elemental,
                        rules.SuddenDeath, rules.Pivot));
                }
                IReadOnlyList<GameEvent> events = GameEngine.ApplyMove(view, move).Events;
                bool same = false, plus = false;
                for (int i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    if (e is FlipEvent f)
                    {
                        bonus += Capture;
                        if (f.Cause == FlipCause.Combo) bonus += Combo;
                    }
                    else if (e is RuleEvent r)
                    {
                        if (r.Rule == RuleHeading.Plus) plus = true;
                        else same = true;
                    }
                }
                if (same) bonus += Same;
                if (plus) bonus += Plus;
            }
            return bonus;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            void Add(string key, int v)
            {
                if (v != 0) parts.Add(key + " " + v);
            }
            Add("corner", Corner);
            Add("edge", Edge);
            Add("capture", Capture);
            Add("same", Same);
            Add("plus", Plus);
            Add("combo", Combo);
            Add("pivot", Pivot);
            Add("element", Element);
            Add("defense", Defense);
            Add("volatility", Volatility);
            return Name + (parts.Count == 0 ? "" : " {" + string.Join(", ", parts) + "}");
        }
    }
}
