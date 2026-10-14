namespace Edgewise.Core
{
    /// <summary>
    /// seed 付き PRNG（mulberry32）。状態は uint 1 つで、純関数として扱う。TS 版 rng.ts と同じ数列を返す。
    /// TS 版の nextRandom は [0,1) の小数を返すが、本作の Core は浮動小数点を使わないので 32bit 整数のまま扱う
    /// （TS の value = u / 2^32 なので、「value &lt; 0.5」は「u &lt; 2^31」、floor(value * n) は (u * n) >> 32 と厳密に等しい）。
    /// </summary>
    public static class Rng
    {
        /// <summary>
        /// 任意の整数 seed から初期状態を作る。TS 版 seedToState と同じ畳み込み
        /// （下位 32bit と、seed / 2^32 を 0 方向に切り捨てた値の下位 32bit を混ぜる）。負数・2^32 以上も可。
        /// </summary>
        public static uint SeedToState(long seed)
        {
            unchecked
            {
                uint h = (uint)seed;
                uint hi = (uint)(seed / 4294967296L);
                h = ((h ^ (h >> 16)) * 0x85ebca6bu) ^ hi;
                h = (h ^ (h >> 13)) * 0xc2b2ae35u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>一様な 32bit 乱数と次の状態を返す（TS 版 nextRandom の value * 2^32 に相当）。</summary>
        public static uint NextUInt32(uint state, out uint next)
        {
            unchecked
            {
                next = state + 0x6d2b79f5u;
                uint t = next;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return t ^ (t >> 14);
            }
        }

        /// <summary>[0, n) の整数と次の状態を返す（TS 版 nextInt と同じ値）。</summary>
        public static int NextInt(uint state, int n, out uint next)
        {
            uint u = NextUInt32(state, out next);
            return (int)(((ulong)u * (ulong)n) >> 32);
        }

        /// <summary>確率 numerator/denominator で true（TS 版の nextRandom() &lt; numerator/denominator と同じ判定）。</summary>
        public static bool NextChance(uint state, int numerator, int denominator, out uint next)
        {
            uint u = NextUInt32(state, out next);
            // u / 2^32 < num / den  ⇔  u * den < num * 2^32
            return (ulong)u * (ulong)denominator < ((ulong)numerator << 32);
        }
    }
}
