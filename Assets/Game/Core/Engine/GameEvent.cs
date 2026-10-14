using System;
using System.Text;

namespace Edgewise.Core
{
    /// <summary>
    /// イベント列の 1 要素（DESIGN.md §4-3）。UI はエンジン内部を読まず、これだけを再生して演出を組み立てる。
    /// ToJson() は TS 版の JSON.stringify と同じキー順・同じ表記を返す（TS 版との突き合わせに使う）。
    /// </summary>
    public abstract class GameEvent : IEquatable<GameEvent>
    {
        /// <summary>TS 版の type 文字列（place / rule / flip / resonance / trait / turn / end / suddenDeath）。</summary>
        public abstract string Type { get; }

        public abstract string ToJson();

        public bool Equals(GameEvent other) => other is object && other.GetType() == GetType() && other.ToJson() == ToJson();

        public override bool Equals(object obj) => Equals(obj as GameEvent);

        public override int GetHashCode() => ToJson().GetHashCode();

        public override string ToString() => ToJson();

        internal static string IntArrayJson(ReadOnlyArray<int> cells)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(cells[i]);
            }
            return sb.Append(']').ToString();
        }

        internal static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }

    public sealed class PlaceEvent : GameEvent
    {
        public Player Player { get; }
        public int Cell { get; }
        public string CardId { get; }
        public int Rotation { get; }

        public PlaceEvent(Player player, int cell, string cardId, int rotation)
        {
            Player = player;
            Cell = cell;
            CardId = cardId;
            Rotation = rotation;
        }

        public override string Type => "place";

        public override string ToJson() =>
            "{\"type\":\"place\",\"player\":" + (int)Player + ",\"cell\":" + Cell + ",\"cardId\":" + Quote(CardId) + ",\"rotation\":" + Rotation + "}";
    }

    /// <summary>演出の見出し（SAME! / PLUS! など）。実際に 1 枚以上裏返るときだけ出る。</summary>
    public sealed class RuleEvent : GameEvent
    {
        public RuleHeading Rule { get; }
        public ReadOnlyArray<int> Cells { get; }

        public RuleEvent(RuleHeading rule, int[] cells)
        {
            Rule = rule;
            Cells = new ReadOnlyArray<int>((int[])cells.Clone());
        }

        public override string Type => "rule";

        public override string ToJson() =>
            "{\"type\":\"rule\",\"rule\":\"" + Rule.ToId() + "\",\"cells\":" + IntArrayJson(Cells) + "}";
    }

    public sealed class FlipEvent : GameEvent
    {
        public int Cell { get; }
        public Player To { get; }
        public FlipCause Cause { get; }

        /// <summary>Basic / Same / Plus = 0、Combo の n 段目 = n。</summary>
        public int ChainDepth { get; }

        /// <summary>裏返しの起点になったマス。</summary>
        public int From { get; }

        public FlipEvent(int cell, Player to, FlipCause cause, int chainDepth, int from)
        {
            Cell = cell;
            To = to;
            Cause = cause;
            ChainDepth = chainDepth;
            From = from;
        }

        public override string Type => "flip";

        public override string ToJson() =>
            "{\"type\":\"flip\",\"cell\":" + Cell + ",\"to\":" + (int)To + ",\"cause\":\"" + Cause.ToId() + "\",\"chainDepth\":" + ChainDepth
            + ",\"from\":" + From + "}";
    }

    /// <summary>Resonance（Phase 4）。現状は型の置き場のみ。</summary>
    public sealed class ResonanceEvent : GameEvent
    {
        public ReadOnlyArray<int> Cells { get; }

        public ResonanceEvent(int[] cells)
        {
            Cells = new ReadOnlyArray<int>((int[])cells.Clone());
        }

        public override string Type => "resonance";

        public override string ToJson() => "{\"type\":\"resonance\",\"cells\":" + IntArrayJson(Cells) + "}";
    }

    /// <summary>Trait（Phase 4）。現状は型の置き場のみ。</summary>
    public sealed class TraitEvent : GameEvent
    {
        public int Cell { get; }
        public TraitId Trait { get; }

        public TraitEvent(int cell, TraitId trait)
        {
            Cell = cell;
            Trait = trait;
        }

        public override string Type => "trait";

        public override string ToJson() => "{\"type\":\"trait\",\"cell\":" + Cell + ",\"trait\":\"" + Trait.ToId() + "\"}";
    }

    public sealed class TurnEvent : GameEvent
    {
        public Player Player { get; }

        public TurnEvent(Player player)
        {
            Player = player;
        }

        public override string Type => "turn";

        public override string ToJson() => "{\"type\":\"turn\",\"player\":" + (int)Player + "}";
    }

    public sealed class EndEvent : GameEvent
    {
        public (int P0, int P1) Score { get; }
        public GameResult Result { get; }

        public EndEvent((int P0, int P1) score, GameResult result)
        {
            Score = score;
            Result = result;
        }

        public override string Type => "end";

        public override string ToJson() =>
            "{\"type\":\"end\",\"score\":[" + Score.P0 + "," + Score.P1 + "],\"result\":\"" + Result.ToId() + "\"}";
    }

    public sealed class SuddenDeathEvent : GameEvent
    {
        public int Round { get; }

        public SuddenDeathEvent(int round)
        {
            Round = round;
        }

        public override string Type => "suddenDeath";

        public override string ToJson() => "{\"type\":\"suddenDeath\",\"round\":" + Round + "}";
    }
}
