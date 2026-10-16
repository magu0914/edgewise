namespace Edgewise.Game.View
{
    /// <summary>
    /// レベルごとの加工（DESIGN §7-1 の層 4）の段階。Card シェーダーの _LevelFx にこの値（0..3）を渡し、シェーダーが分岐する。
    /// </summary>
    public enum LevelFxTier
    {
        /// <summary>Lv1〜3: 素朴（加工なし）。</summary>
        Plain = 0,

        /// <summary>Lv4〜6: 金の縁。</summary>
        Gold = 1,

        /// <summary>Lv7〜9: 金の縁＋ホロ加工（傾きに連動して虹色の光沢が流れる）。</summary>
        Holo = 2,

        /// <summary>Lv10: 金の縁＋動くオーロラ＋きらめきの粒。</summary>
        Aurora = 3,
    }

    public static class CardLevelFx
    {
        public static LevelFxTier TierOf(int level)
        {
            if (level >= 10) return LevelFxTier.Aurora;
            if (level >= 7) return LevelFxTier.Holo;
            if (level >= 4) return LevelFxTier.Gold;
            return LevelFxTier.Plain;
        }
    }
}
