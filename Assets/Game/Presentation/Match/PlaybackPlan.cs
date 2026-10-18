using System.Collections.Generic;
using System.Text;
using Edgewise.Core;

namespace Edgewise.Game.Match
{
    /// <summary>演出ステップの種類。EventPlayer はこの順に再生する。</summary>
    public enum StepKind
    {
        /// <summary>手札からマスへ置く。</summary>
        Place,
        /// <summary>Same / Plus が成立した辺を光の線で結ぶ。</summary>
        RuleLink,
        /// <summary>SAME! / PLUS! / SAME WALL! の文字。</summary>
        RuleBanner,
        /// <summary>連鎖の段の前の待ちと COMBO の文字。</summary>
        ComboBanner,
        /// <summary>同じ段・同じ原因の裏返し（0.05 秒ずつずらして同時に回す）。</summary>
        FlipGroup,
        /// <summary>Guard が裏返しを防いだ（そのマスのカードに盾が光る）。</summary>
        Trait,
        /// <summary>手番の交代。</summary>
        Turn,
        /// <summary>Sudden Death の再戦。</summary>
        SuddenDeath,
        /// <summary>終局。</summary>
        End,
    }

    /// <summary>1 枚の裏返し。</summary>
    public readonly struct FlipStep
    {
        public readonly int Cell;
        public readonly int From;
        public readonly Player To;
        public readonly FlipCause Cause;
        public readonly int ChainDepth;

        /// <summary>この手の中で何枚目の裏返しか（0 始まり。3b の音程に使う）。</summary>
        public readonly int IndexInMove;

        public FlipStep(int cell, int from, Player to, FlipCause cause, int chainDepth, int indexInMove)
        {
            Cell = cell;
            From = from;
            To = to;
            Cause = cause;
            ChainDepth = chainDepth;
            IndexInMove = indexInMove;
        }
    }

    /// <summary>演出の 1 ステップ（データのみ。再生は EventPlayer）。</summary>
    public sealed class PlaybackStep
    {
        public StepKind Kind;
        public Player Player;
        public int Cell = -1;
        public string CardId;
        public int Rotation;
        public RuleHeading Rule;
        public int[] Cells = new int[0];
        public FlipStep[] Flips = new FlipStep[0];
        public int ComboDepth;
        public TraitId Trait;
        public int Round;
        public (int P0, int P1) Score;
        public GameResult Result;

        public override string ToString()
        {
            var sb = new StringBuilder(Kind.ToString());
            switch (Kind)
            {
                case StepKind.Place:
                    sb.Append(" p").Append((int)Player).Append(" cell ").Append(Cell).Append(' ').Append(CardId).Append(" r").Append(Rotation);
                    break;
                case StepKind.RuleLink:
                case StepKind.RuleBanner:
                    sb.Append(' ').Append(Rule.ToId()).Append(" [").Append(string.Join(",", Cells)).Append(']');
                    break;
                case StepKind.ComboBanner:
                    sb.Append(" x").Append(ComboDepth);
                    break;
                case StepKind.FlipGroup:
                    sb.Append(" [");
                    for (int i = 0; i < Flips.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(Flips[i].Cell).Append(':').Append(Flips[i].Cause.ToId()).Append(Flips[i].ChainDepth);
                    }
                    sb.Append(']');
                    break;
                case StepKind.Trait:
                    sb.Append(' ').Append(Trait.ToId()).Append(" cell ").Append(Cell);
                    break;
                case StepKind.Turn:
                    sb.Append(" p").Append((int)Player);
                    break;
                case StepKind.SuddenDeath:
                    sb.Append(" round ").Append(Round);
                    break;
                case StepKind.End:
                    sb.Append(' ').Append(Score.P0).Append('-').Append(Score.P1).Append(' ').Append(Result.ToId());
                    break;
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// エンジンのイベント列を演出ステップの列に変換する（純関数。ルールの判断はしない）。
    /// 順序: Place → (RuleLink → RuleBanner)* → 裏返し（Same/Plus → 通常）→ 段ごとに (ComboBanner → 裏返し) → Turn / SuddenDeath / End。
    /// Guard が防いだときの Trait は、イベントの並びのとおりにその位置へ入る。
    /// </summary>
    public static class PlaybackPlanner
    {
        public static List<PlaybackStep> Build(IReadOnlyList<GameEvent> events)
        {
            var steps = new List<PlaybackStep>();
            var group = new List<FlipStep>();
            int groupDepth = -1;
            int groupClass = -1;
            int lastComboDepth = 0;
            int flipIndex = 0;

            void Flush()
            {
                if (group.Count == 0) return;
                steps.Add(new PlaybackStep { Kind = StepKind.FlipGroup, Flips = group.ToArray() });
                group.Clear();
            }

            foreach (var e in events)
            {
                switch (e)
                {
                    case PlaceEvent p:
                        Flush();
                        lastComboDepth = 0;
                        steps.Add(new PlaybackStep { Kind = StepKind.Place, Player = p.Player, Cell = p.Cell, CardId = p.CardId, Rotation = p.Rotation });
                        break;

                    case RuleEvent r:
                    {
                        Flush();
                        var cells = r.Cells.ToArray();
                        steps.Add(new PlaybackStep { Kind = StepKind.RuleLink, Rule = r.Rule, Cells = cells });
                        steps.Add(new PlaybackStep { Kind = StepKind.RuleBanner, Rule = r.Rule, Cells = cells });
                        break;
                    }

                    case FlipEvent f:
                    {
                        // 原因の区分: 0 = Same/Plus、1 = 通常、2 = Combo（段ごと）
                        int cls = f.Cause == FlipCause.Same || f.Cause == FlipCause.Plus ? 0 : f.Cause == FlipCause.Basic ? 1 : 2;
                        if (cls != groupClass || f.ChainDepth != groupDepth)
                        {
                            Flush();
                            groupClass = cls;
                            groupDepth = f.ChainDepth;
                            if (f.ChainDepth > 0 && f.ChainDepth != lastComboDepth)
                            {
                                lastComboDepth = f.ChainDepth;
                                steps.Add(new PlaybackStep { Kind = StepKind.ComboBanner, ComboDepth = f.ChainDepth });
                            }
                        }
                        group.Add(new FlipStep(f.Cell, f.From, f.To, f.Cause, f.ChainDepth, flipIndex++));
                        break;
                    }

                    case TraitEvent tr:
                        // Guard が防いだ。裏返しの群れの途中でも、盾を見せてから続きを再生する
                        Flush();
                        steps.Add(new PlaybackStep { Kind = StepKind.Trait, Cell = tr.Cell, Trait = tr.Trait });
                        break;

                    case TurnEvent t:
                        Flush();
                        groupClass = -1;
                        steps.Add(new PlaybackStep { Kind = StepKind.Turn, Player = t.Player });
                        break;

                    case SuddenDeathEvent sd:
                        Flush();
                        groupClass = -1;
                        steps.Add(new PlaybackStep { Kind = StepKind.SuddenDeath, Round = sd.Round });
                        break;

                    case EndEvent end:
                        Flush();
                        groupClass = -1;
                        steps.Add(new PlaybackStep { Kind = StepKind.End, Score = end.Score, Result = end.Result });
                        break;

                    default:
                        // resonance イベントは使わない（共鳴の縁は盤の見た目から決める。BoardView.RefreshResonance）
                        break;
                }
            }
            Flush();
            return steps;
        }

        /// <summary>デバッグ・テスト用の 1 行表現。</summary>
        public static string Describe(IReadOnlyList<PlaybackStep> steps)
        {
            var parts = new string[steps.Count];
            for (int i = 0; i < steps.Count; i++) parts[i] = steps[i].ToString();
            return string.Join(" | ", parts);
        }
    }
}
