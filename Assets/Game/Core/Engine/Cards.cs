using System;

namespace Edgewise.Core
{
    /// <summary>辺のランク [上, 右, 下, 左]。10 = A。</summary>
    public readonly struct Sides : IEquatable<Sides>
    {
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
        public readonly int Left;

        public Sides(int top, int right, int bottom, int left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        /// <summary>方向（0=上, 1=右, 2=下, 3=左）で辺を取り出す。</summary>
        public int this[int dir]
        {
            get
            {
                switch (dir)
                {
                    case 0: return Top;
                    case 1: return Right;
                    case 2: return Bottom;
                    case 3: return Left;
                    default: throw new ArgumentOutOfRangeException(nameof(dir), dir, "direction must be 0..3");
                }
            }
        }

        public int Sum => Top + Right + Bottom + Left;

        public bool Contains(int rank) => Top == rank || Right == rank || Bottom == rank || Left == rank;

        public int[] ToArray() => new[] { Top, Right, Bottom, Left };

        public bool Equals(Sides other) =>
            Top == other.Top && Right == other.Right && Bottom == other.Bottom && Left == other.Left;

        public override bool Equals(object obj) => obj is Sides other && Equals(other);

        public override int GetHashCode() => ((Top * 31 + Right) * 31 + Bottom) * 31 + Left;

        public static bool operator ==(Sides a, Sides b) => a.Equals(b);
        public static bool operator !=(Sides a, Sides b) => !a.Equals(b);

        /// <summary>TS 版の sides.join('.') と同じ表記。</summary>
        public override string ToString() => Top + "." + Right + "." + Bottom + "." + Left;
    }

    /// <summary>不変のカード定義。</summary>
    public sealed class CardDef : IEquatable<CardDef>
    {
        public string Id { get; }
        public string Name { get; }

        /// <summary>1..10</summary>
        public int Level { get; }

        public Sides Sides { get; }
        public Element? Element { get; }
        public Tribe? Tribe { get; }
        public TraitId? Trait { get; }

        /// <summary>値の範囲検証はしない（検証は Data の CardValidator で行う）。</summary>
        public CardDef(string id, string name, int level, Sides sides, Element? element = null, Tribe? tribe = null, TraitId? trait = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Level = level;
            Sides = sides;
            Element = element;
            Tribe = tribe;
            Trait = trait;
        }

        public CardDef With(string id = null, string name = null, int? level = null, Sides? sides = null) =>
            new CardDef(id ?? Id, name ?? Name, level ?? Level, sides ?? Sides, Element, Tribe, Trait);

        public bool Equals(CardDef other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            return Id == other.Id && Name == other.Name && Level == other.Level && Sides.Equals(other.Sides)
                && Element == other.Element && Tribe == other.Tribe && Trait == other.Trait;
        }

        public override bool Equals(object obj) => Equals(obj as CardDef);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => Id + "[" + Sides + "]";
    }

    /// <summary>盤上のカード（どのカードを・誰が所有し・どの向きで置いたか）。</summary>
    public readonly struct PlacedCard : IEquatable<PlacedCard>
    {
        /// <summary>カード定義への参照（毎回 id から引かずに済むように持つ）。</summary>
        public readonly CardDef Card;
        public readonly Player Owner;

        /// <summary>時計回りの 90° 単位（0..3）。1 = 90°（元の left が top に来る）。</summary>
        public readonly int Rotation;

        /// <summary>
        /// このカードを盤に置いたプレイヤー（裏返っても変わらない）。両者が見ている公開情報で、
        /// AI の決定化（星図巡りでは同じカードを両者が持ちうる。DESIGN §12-6）が「相手がもう出したカード」を知るために使う。
        /// 局面の同一性（Equals・hashState）には含めない（TS 版とのゴールデン互換のため）。
        /// </summary>
        public readonly Player PlacedBy;

        /// <summary>
        /// Guard（守護）を使い切ったか。Traits ルールが ON のときだけ意味を持つ（OFF では常に false）。
        /// 局面の同一性（Equals・hashState・Zobrist）に含める（Traits ON のときだけ値が変わるので、OFF のハッシュは従来のまま）。
        /// </summary>
        public readonly bool GuardUsed;

        /// <summary>placedBy を省略すると owner（置いた直後の状態）とみなす。</summary>
        public PlacedCard(CardDef card, Player owner, int rotation, Player? placedBy = null, bool guardUsed = false)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            Owner = owner;
            Rotation = rotation;
            PlacedBy = placedBy ?? owner;
            GuardUsed = guardUsed;
        }

        public string CardId => Card.Id;

        public PlacedCard WithOwner(Player owner) => new PlacedCard(Card, owner, Rotation, PlacedBy, GuardUsed);

        /// <summary>Guard を使い切った状態にしたコピー。</summary>
        public PlacedCard WithGuardUsed() => new PlacedCard(Card, Owner, Rotation, PlacedBy, true);

        public bool Equals(PlacedCard other) =>
            Owner == other.Owner && Rotation == other.Rotation && Card.Id == other.Card.Id && GuardUsed == other.GuardUsed;

        public override bool Equals(object obj) => obj is PlacedCard other && Equals(other);

        public override int GetHashCode() => (Card.Id.GetHashCode() * 4 + Rotation) * 2 + (int)Owner;

        public override string ToString() => CardId + ":" + (int)Owner + Rotation + (GuardUsed ? "g" : "");
    }

    /// <summary>1 手。handIndex 番目の手札を cell に rotation で置く。</summary>
    public readonly struct Move : IEquatable<Move>
    {
        public readonly int HandIndex;
        public readonly int Cell;

        /// <summary>0..3。0 以外は Pivot ルールとトークンが必要。</summary>
        public readonly int Rotation;

        public Move(int handIndex, int cell, int rotation = 0)
        {
            HandIndex = handIndex;
            Cell = cell;
            Rotation = rotation;
        }

        public bool Equals(Move other) => HandIndex == other.HandIndex && Cell == other.Cell && Rotation == other.Rotation;

        public override bool Equals(object obj) => obj is Move other && Equals(other);

        public override int GetHashCode() => (HandIndex * 16 + Cell) * 4 + Rotation;

        public static bool operator ==(Move a, Move b) => a.Equals(b);
        public static bool operator !=(Move a, Move b) => !a.Equals(b);

        public override string ToString() => "{handIndex:" + HandIndex + ",cell:" + Cell + ",rotation:" + Rotation + "}";
    }
}
