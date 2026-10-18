using System;

namespace Edgewise.Core
{
    /// <summary>不正な手（埋まったマス、手札外、トークン不足など）。TS 版の IllegalMoveError。</summary>
    public sealed class IllegalMoveException : Exception
    {
        public IllegalMoveException(string message) : base(message)
        {
        }
    }

    /// <summary>不正な対戦設定（存在しないカード、デッキ枚数不足など）。TS 版の InvalidConfigError。</summary>
    public sealed class InvalidConfigException : Exception
    {
        public InvalidConfigException(string message) : base(message)
        {
        }
    }

    /// <summary>未実装のルール修飾（Phase 6 で Resonance / Traits を実装したため、現在エンジンは投げない。TS 版 NotImplementedRuleError の互換のために残す）。</summary>
    public sealed class NotImplementedRuleException : Exception
    {
        public string Rule { get; }

        public NotImplementedRuleException(string rule) : base("Rule \"" + rule + "\" is not implemented yet")
        {
            Rule = rule;
        }
    }
}
