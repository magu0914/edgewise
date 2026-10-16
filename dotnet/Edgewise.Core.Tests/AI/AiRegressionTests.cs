using System.Text;
using Edgewise.Core.AI;
using NUnit.Framework;

namespace Edgewise.Core.Tests.AI
{
    /// <summary>
    /// AI の選ぶ手と評価値の「ゴールデン相当」の指紋。Phase 5a（性格・決定化の修正）の前の main（a385d4d）で測った値と一致すること
    /// ＝ 性格なし・CardPoolModel.Distinct（既定）の挙動が変わっていないことを保証する。
    /// 指紋を作り直すときは、変更前のコードでこのテストを走らせ、失敗メッセージの値を貼る。
    /// </summary>
    [TestFixture]
    public class AiRegressionTests
    {
        const int Positions = 400;

        public static string Fingerprint()
        {
            var profiles = new[] { AiProfile.Easy, AiProfile.Normal.WithMaxNodes(8000), AiProfile.Hard.WithMaxNodes(20000) };
            var sb = new StringBuilder();
            for (int i = 0; i < Positions; i++)
            {
                var r = new TestRandom(0x5a5a + i);
                var s = AiH.RandomPosition(r, r.NextInt(8));
                if (s == null) continue;
                foreach (var p in profiles)
                {
                    var session = new SearchSession(s, s.Turn, p, 1000 + i, AiH.Pool);
                    var m = session.Run();
                    sb.Append(m.HandIndex).Append(m.Cell).Append(m.Rotation).Append(':').Append(session.NodesSearched).Append(';');
                    foreach (var e in session.GetEvaluations()) sb.Append(e.Value).Append(',');
                    sb.Append('|');
                }
            }
            return Fnv(sb.ToString());
        }

        static string Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }

        [Test]
        public void MovesWithoutPersonalityMatchTheBaseline()
        {
            Assert.That(Fingerprint(), Is.EqualTo(Baseline));
        }

        const string Baseline = "2a5b69210775efd7";
    }
}
