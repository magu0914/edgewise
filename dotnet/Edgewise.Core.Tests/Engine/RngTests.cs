using NUnit.Framework;

namespace Edgewise.Core.Tests.Engine
{
    /// <summary>mulberry32 と seed の畳み込みが TS 版と同じ値を返すこと（期待値は TS 版 rng.ts で計算した既知の値）。</summary>
    [TestFixture]
    public class RngTests
    {
        [TestCase(0L, 0u)]
        [TestCase(1L, 1364076727u)]
        [TestCase(42L, 142593372u)]
        [TestCase(-1L, 2180083513u)]
        [TestCase(-123456789L, 2059234239u)]
        [TestCase(4294967295L, 2180083513u)]
        [TestCase(4294967296L, 3266473095u)]
        [TestCase(1099511640121L, 4009084015u)] // 2^40 + 12345
        [TestCase(-2199023255559L, 960281884u)] // -(2^41) - 7
        [TestCase(9007199254740991L, 1132773964u)] // 2^53 - 1
        public void SeedToStateMatchesTypeScript(long seed, uint expected)
        {
            Assert.That(Rng.SeedToState(seed), Is.EqualTo(expected));
        }

        [Test]
        public void NextUInt32MatchesTypeScriptSequence()
        {
            // TS: nextRandom を seedToState(2026) から 8 回。value * 2^32 と次の状態
            var expected = new (uint Next, uint Value)[]
            {
                (1188261623, 2499603679), (3019827436, 3302264390), (556425953, 1643815993), (2387991766, 2397110622),
                (4219557579, 637584450), (1756156096, 1528376030), (3587721909, 3995247800), (1124320426, 1585182846),
            };
            uint st = Rng.SeedToState(2026);
            foreach (var (next, value) in expected)
            {
                Assert.That(Rng.NextUInt32(st, out var n), Is.EqualTo(value));
                Assert.That(n, Is.EqualTo(next));
                st = n;
            }
        }

        [Test]
        public void NextIntMatchesTypeScriptFloorOfValueTimesN()
        {
            var ns = new[] { 1, 2, 3, 6, 9, 10, 45, 180, 50 };
            var expected = new[] { 0, 0, 2, 3, 2, 2, 0, 81, 0 };
            uint st = Rng.SeedToState(7);
            for (int i = 0; i < ns.Length; i++) Assert.That(Rng.NextInt(st, ns[i], out st), Is.EqualTo(expected[i]), "n=" + ns[i]);
        }

        [Test]
        public void NextChanceIsExactlyValueLessThanFraction()
        {
            // u / 2^32 < 1/4 ⇔ u < 2^30。境界の前後で確かめる（NextUInt32 の逆算はできないので、判定式だけを状態ごとに比較）
            uint st = 12345;
            for (int i = 0; i < 20000; i++)
            {
                uint u = Rng.NextUInt32(st, out _);
                bool chance = Rng.NextChance(st, 1, 4, out var next);
                Assert.That(chance, Is.EqualTo(u < (1u << 30)));
                Assert.That(Rng.NextChance(st, 1, 2, out _), Is.EqualTo(u < (1u << 31)));
                st = next;
            }
        }
    }
}
