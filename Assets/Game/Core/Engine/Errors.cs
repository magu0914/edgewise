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

    /// <summary>まだ実装していないルール修飾（Resonance / Trait）。TS 版の NotImplementedRuleError。</summary>
    public sealed class NotImplementedRuleException : Exception
    {
        public string Rule { get; }

        public NotImplementedRuleException(string rule) : base("Rule \"" + rule + "\" is not implemented yet")
        {
            Rule = rule;
        }
    }
}
