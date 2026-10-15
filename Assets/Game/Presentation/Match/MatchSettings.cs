using System;
using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Game.Juice;
using UnityEngine;

namespace Edgewise.Game.Match
{
    public enum Difficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>カードの強さ帯（DESIGN §8-1 対戦設定）。両者とも同じ帯からランダムに 5 枚。</summary>
    public enum CardBand
    {
        Beginner = 0,
        Intermediate = 1,
        Advanced = 2,
    }

    public enum SeatKind
    {
        Human = 0,
        Cpu = 1,
    }

    /// <summary>
    /// 対戦設定（難易度・ルール・強さ帯）。前回の設定を PlayerPrefs に覚える。
    /// 席 0 がプレイヤー（青緑・◆）、席 1 が CPU（赤橙・●）。テストでは両席 CPU にもできる。
    /// </summary>
    [Serializable]
    public sealed class MatchSettings
    {
        public Difficulty difficulty = Difficulty.Normal;
        public CardBand band = CardBand.Intermediate;
        public bool open = true;
        public bool random;
        public bool same = true;
        public bool sameWall;
        public bool plus = true;
        public bool combo = true;
        public bool elemental;
        public bool suddenDeath;
        public bool pivot = true;

        /// <summary>席ごとの操作者。</summary>
        public SeatKind seat0 = SeatKind.Human;
        public SeatKind seat1 = SeatKind.Cpu;

        /// <summary>席 0 が CPU のときの難易度（CPU 対 CPU のテスト用）。</summary>
        public Difficulty seat0Difficulty = Difficulty.Normal;

        /// <summary>0 なら毎回ランダム。0 以外なら配牌・先攻・CPU の乱数がすべて固定される。</summary>
        public long seed;

        const string Key = "edgewise.matchSettings.v1";

        public MatchSettings Clone() => (MatchSettings)MemberwiseClone();

        public RuleSet ToRuleSet() => new RuleSet(open: open, random: random, same: same, sameWall: same && sameWall, plus: plus,
            combo: combo, elemental: elemental, suddenDeath: suddenDeath, pivot: pivot);

        public SeatKind Seat(Player p) => p == Player.P0 ? seat0 : seat1;

        public AiProfile ProfileFor(Player p) => ToProfile(p == Player.P0 ? seat0Difficulty : difficulty);

        public static AiProfile ToProfile(Difficulty d) =>
            d == Difficulty.Easy ? AiProfile.Easy : d == Difficulty.Normal ? AiProfile.Normal : AiProfile.Hard;

        public static (int Min, int Max) BandLevels(CardBand b) =>
            b == CardBand.Beginner ? (1, 3) : b == CardBand.Intermediate ? (3, 6) : (6, 10);

        public static MatchSettings Load()
        {
            var s = new MatchSettings();
            try
            {
                if (PlayerPrefs.HasKey(Key)) JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(Key), s);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Edgewise] could not read saved settings: " + e.Message);
            }
            // 保存してよいのはプレイヤーが選ぶ項目だけ。席と seed は毎回既定値に戻す
            s.seat0 = SeatKind.Human;
            s.seat1 = SeatKind.Cpu;
            s.seed = 0;
            return s;
        }

        public void Save()
        {
            var copy = Clone();
            copy.seat0 = SeatKind.Human;
            copy.seat1 = SeatKind.Cpu;
            copy.seed = 0;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(copy));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 配牌。強さ帯のカードを seed でシャッフルし、両者に重複なしで配る。
    /// Random ルール OFF なら各 5 枚ちょうど、ON なら各 7 枚のデッキを渡し、エンジンがそこから 5 枚を選ぶ。
    /// </summary>
    public static class Dealer
    {
        public const int RandomDeckSize = 7;

        public static (string[] Deck0, string[] Deck1) Deal(CardBand band, bool randomRule, long seed)
        {
            var (min, max) = MatchSettings.BandLevels(band);
            var pool = new List<string>();
            foreach (var c in CardCatalog.All)
            {
                if (c.Level >= min && c.Level <= max) pool.Add(c.Id);
            }
            int size = randomRule ? RandomDeckSize : GameConstants.HandSize;
            if (pool.Count < size * 2) size = Math.Max(GameConstants.HandSize, pool.Count / 2);

            uint rng = Rng.SeedToState(seed ^ 0x5eed);
            for (int i = 0; i < pool.Count - 1; i++)
            {
                int j = i + Rng.NextInt(rng, pool.Count - i, out rng);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var d0 = pool.GetRange(0, size).ToArray();
            var d1 = pool.GetRange(size, size).ToArray();
            return (d0, d1);
        }
    }
}
