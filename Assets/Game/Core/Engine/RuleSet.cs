using System;

namespace Edgewise.Core
{
    /// <summary>
    /// 対戦前に決まる不変の設定。<c>new RuleSet(same: true, combo: true)</c> のように、ON にしたいものだけ名前付き引数で渡す。
    /// </summary>
    public sealed class RuleSet : IEquatable<RuleSet>
    {
        /// <summary>すべて OFF の RuleSet。</summary>
        public static readonly RuleSet None = new RuleSet();

        public bool Open { get; }
        public bool Random { get; }
        public bool Same { get; }

        /// <summary>Same が ON のときのみ有効。</summary>
        public bool SameWall { get; }

        public bool Plus { get; }
        public bool Combo { get; }
        public bool Elemental { get; }
        public bool SuddenDeath { get; }
        public bool Pivot { get; }

        /// <summary>Resonance（共鳴。DESIGN.md §3-4）。同じ所有者・同じ系統のカードが縦横に隣接していると全辺 +1。</summary>
        public bool Resonance { get; }

        /// <summary>Traits（固有能力。DESIGN.md §3-4）。CardDef.Trait を持つカードの能力が働く。</summary>
        public bool Traits { get; }

        public RuleSet(
            bool open = false,
            bool random = false,
            bool same = false,
            bool sameWall = false,
            bool plus = false,
            bool combo = false,
            bool elemental = false,
            bool suddenDeath = false,
            bool pivot = false,
            bool resonance = false,
            bool traits = false)
        {
            Open = open;
            Random = random;
            Same = same;
            SameWall = sameWall;
            Plus = plus;
            Combo = combo;
            Elemental = elemental;
            SuddenDeath = suddenDeath;
            Pivot = pivot;
            Resonance = resonance;
            Traits = traits;
        }

        /// <summary>TS 版 hashState と同じ並び（open, random, same, sameWall, plus, combo, elemental, suddenDeath, pivot, resonance, traits）の 0/1 文字列。</summary>
        public string ToBitString()
        {
            var c = new char[11];
            c[0] = Open ? '1' : '0';
            c[1] = Random ? '1' : '0';
            c[2] = Same ? '1' : '0';
            c[3] = SameWall ? '1' : '0';
            c[4] = Plus ? '1' : '0';
            c[5] = Combo ? '1' : '0';
            c[6] = Elemental ? '1' : '0';
            c[7] = SuddenDeath ? '1' : '0';
            c[8] = Pivot ? '1' : '0';
            c[9] = Resonance ? '1' : '0';
            c[10] = Traits ? '1' : '0';
            return new string(c);
        }

        public bool Equals(RuleSet other) => other is object && ToBitString() == other.ToBitString();

        public override bool Equals(object obj) => Equals(obj as RuleSet);

        public override int GetHashCode() => ToBitString().GetHashCode();

        public override string ToString() => "RuleSet(" + ToBitString() + ")";
    }
}
