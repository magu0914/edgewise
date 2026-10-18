using System;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.Match;
using UnityEngine;

namespace Edgewise.Game.Online
{
    /// <summary>
    /// 通信するメッセージ 1 通（DESIGN §9-1）。JSON 文字列で送る。type ごとに使う欄だけ埋める。
    ///   hello  ゲスト→ホスト: 入室（protocol, next, round）。切れた後の繋ぎ直しでも最初に送る
    ///   start  ホスト→ゲスト: 対戦の設定（protocol, rules, band, seed, hostIsP0, round）
    ///   move   手（seq, hand, cell, rot, hash = 打った後の hashState）
    ///   sync   繋ぎ直した後の突き合わせ（next = 次に受け取るはずの seq, round）
    ///   rematch / resign / bye / reject / abort
    /// </summary>
    [Serializable]
    public sealed class OnlineMessage
    {
        public const string Hello = "hello";
        public const string Start = "start";
        public const string Move = "move";
        public const string Sync = "sync";
        public const string Rematch = "rematch";
        public const string Resign = "resign";
        public const string Bye = "bye";
        public const string Reject = "reject";
        public const string Abort = "abort";

        public string type;
        public int protocol;
        public int seq;
        public int hand;
        public int cell;
        public int rot;
        public string hash;
        public string rules;
        public int band;
        public long seed;
        public bool hostIsP0;
        public int round;
        public int next;
        public string reason;

        public string ToJson() => JsonUtility.ToJson(this);

        /// <summary>読めなければ null（壊れたメッセージ）。</summary>
        public static OnlineMessage Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var m = JsonUtility.FromJson<OnlineMessage>(json);
                return m != null && !string.IsNullOrEmpty(m.type) ? m : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>ルールと配牌の共有（両端末が同じ設定から同じ局面を作る）。</summary>
    public static class OnlineSetup
    {
        /// <summary>rules / band / seed から開始局面を作る。MatchController のフリー対戦と同じ手順。</summary>
        public static GameState CreateState(RuleSet rules, CardBand band, long seed)
        {
            var (d0, d1) = Dealer.Deal(band, rules.Random, seed);
            var tokens = GameConstants.MatchPivotTokens(rules);
            return GameEngine.CreateGame(new GameConfig(rules, d0, d1, seed, CardCatalog.ById, tokens.First, tokens.Second));
        }

        /// <summary>11 桁の 0/1 文字列（RuleSet.ToBitString）から戻す。形が違う・未実装のルールが入っていれば null。</summary>
        public static RuleSet ParseRules(string bits)
        {
            if (bits == null || bits.Length != 11) return null;
            foreach (char c in bits)
            {
                if (c != '0' && c != '1') return null;
            }
            bool B(int i) => bits[i] == '1';
            if (B(9) || B(10)) return null; // resonance / traits は未実装
            return new RuleSet(open: B(0), random: B(1), same: B(2), sameWall: B(3), plus: B(4), combo: B(5), elemental: B(6),
                suddenDeath: B(7), pivot: B(8));
        }
    }
}
