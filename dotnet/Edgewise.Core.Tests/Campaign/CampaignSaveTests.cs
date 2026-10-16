using System.Collections.Generic;
using System.Linq;
using System.Text;
using Edgewise.Core.Campaign;
using NUnit.Framework;

namespace Edgewise.Core.Tests.Campaign
{
    [TestFixture]
    public class CampaignSaveTests
    {
        /// <summary>いろいろ進めた進行（勝ち・負け・原作モードの取られ・解放・最後のデッキ）。</summary>
        static CampaignState Played()
        {
            var s = CampaignRules.NewGame(123456789012L, classicMode: true);
            var harbor = CampaignData.Regions[0];
            foreach (var o in harbor.Opponents)
            {
                var mine = CampaignRules.AutoDeck(s, harbor);
                var theirs = CampaignRules.ChooseOpponentDeck(s, o, CampaignRules.MatchSeed(s, o));
                s = CampaignRules.ApplyMatch(s, o, mine, theirs, new MatchOutcome(MatchResultKind.Win, true), theirs.Take(2).ToArray()).State;
                s = CampaignRules.ApplyMatch(s, o, mine, theirs, new MatchOutcome(MatchResultKind.Loss, false), null).State;
                s = CampaignRules.ApplyMatch(s, o, mine, theirs, new MatchOutcome(MatchResultKind.Draw, false), null).State;
            }
            return s;
        }

        static void AssertSame(CampaignState a, CampaignState b)
        {
            Assert.That(b.Seed, Is.EqualTo(a.Seed));
            Assert.That(b.ClassicMode, Is.EqualTo(a.ClassicMode));
            Assert.That(b.Collection, Is.EquivalentTo(a.Collection));
            Assert.That(b.UnlockedRegions, Is.EqualTo(a.UnlockedRegions));
            Assert.That(b.Records.Keys, Is.EquivalentTo(a.Records.Keys));
            foreach (var kv in a.Records)
            {
                var r = b.Records[kv.Key];
                Assert.That((r.Wins, r.Losses, r.Draws), Is.EqualTo((kv.Value.Wins, kv.Value.Losses, kv.Value.Draws)));
                Assert.That(r.Captured, Is.EquivalentTo(kv.Value.Captured));
                Assert.That(r.Seen, Is.EqualTo(kv.Value.Seen));
            }
            Assert.That(b.LastDecks.Keys, Is.EquivalentTo(a.LastDecks.Keys));
            foreach (var kv in a.LastDecks) Assert.That(b.LastDecks[kv.Key], Is.EqualTo(kv.Value));
        }

        [Test]
        public void RoundTripsThroughJson()
        {
            var s = Played();
            Assert.That(s.UnlockedRegions.Count, Is.EqualTo(2));
            Assert.That(s.Records.Values.Any(r => r.Captured.Count > 0), Is.True);
            var json = CampaignSave.ToJson(s);
            var loaded = CampaignSave.Load(json);
            Assert.That(loaded.Status, Is.EqualTo(SaveLoadStatus.Ok), string.Join("; ", loaded.Warnings));
            Assert.That(loaded.SourceVersion, Is.EqualTo(CampaignSave.CurrentVersion));
            AssertSame(s, loaded.State);
            Assert.That(CampaignSave.ToJson(loaded.State), Is.EqualTo(json), "the output is canonical");
            AssertSame(CampaignRules.NewGame(5), CampaignSave.Load(CampaignSave.ToJson(CampaignRules.NewGame(5))).State);
        }

        [Test]
        public void JsonLooksAsDocumented()
        {
            var json = CampaignSave.ToJson(CampaignRules.NewGame(7));
            Assert.That(json, Does.StartWith("{\"version\":1,\"seed\":7,\"classic\":false,\"collection\":{\"glimmouse\":1,"));
            Assert.That(json, Does.Contain("\"unlocked\":[\"harbor\"]"));
            Assert.That(json, Does.EndWith("\"opponents\":{},\"lastDecks\":{}}"));
        }

        [Test]
        public void EmptySaveStartsANewGame()
        {
            foreach (var input in new[] { null, "", "   " })
            {
                var r = CampaignSave.Load(input, 99);
                Assert.That(r.Status, Is.EqualTo(SaveLoadStatus.Empty));
                Assert.That(r.StartedOver, Is.True);
                AssertSame(CampaignRules.NewGame(99), r.State);
            }
        }

        [Test]
        public void MigratesVersionZero()
        {
            // v0: version なし、手持ちは ID の配列（同じ ID を並べて枚数）、解放は星域の数
            const string v0 = "{\"seed\":5,\"classic\":true,\"cards\":[\"glimmouse\",\"glimmouse\",\"dustwisp\",\"coglet\",\"scalefry\",\"pyxmoth\",\"lynxlight\"],"
                + "\"regions\":2,\"opponents\":{\"harbor-master\":{\"wins\":1,\"losses\":2,\"draws\":0,\"captured\":{},\"seen\":[\"lupusfang\"]}}}";
            var r = CampaignSave.Load(v0);
            Assert.That(r.Status, Is.EqualTo(SaveLoadStatus.Migrated));
            Assert.That(r.SourceVersion, Is.EqualTo(0));
            Assert.That(r.StartedOver, Is.False);
            var s = r.State;
            Assert.That(s.Seed, Is.EqualTo(5));
            Assert.That(s.ClassicMode, Is.True);
            Assert.That(s.Count("glimmouse"), Is.EqualTo(2));
            Assert.That(s.Count("lynxlight"), Is.EqualTo(1));
            Assert.That(s.TotalCards, Is.EqualTo(7));
            Assert.That(s.UnlockedRegions, Is.EqualTo(new[] { "harbor", "library" }));
            Assert.That(s.Record("harbor-master").Losses, Is.EqualTo(2));
            // 移行後に書き出すと今の形式になる
            Assert.That(CampaignSave.Load(CampaignSave.ToJson(s)).Status, Is.EqualTo(SaveLoadStatus.Ok));
        }

        [Test]
        public void NewerVersionsAreNotReadAndNotGuessed()
        {
            var json = CampaignSave.ToJson(Played()).Replace("\"version\":1", "\"version\":2");
            var r = CampaignSave.Load(json, 3);
            Assert.That(r.Status, Is.EqualTo(SaveLoadStatus.TooNew));
            Assert.That(r.StartedOver, Is.True);
            Assert.That(r.SourceVersion, Is.EqualTo(2));
            AssertSame(CampaignRules.NewGame(3), r.State);
        }

        [Test]
        public void UnknownEntriesAreDroppedAndReported()
        {
            var json = CampaignSave.ToJson(Played());
            json = json.Replace("\"collection\":{", "\"collection\":{\"nosuchcard\":3,\"orionmane\":-2,\"leomane\":1000,")
                .Replace("\"unlocked\":[", "\"unlocked\":[\"nowhere\",")
                .Replace("\"opponents\":{", "\"opponents\":{\"stranger\":{\"wins\":1},")
                .Replace("\"lastDecks\":{", "\"lastDecks\":{\"void\":[\"a\"],");
            var r = CampaignSave.Load(json);
            Assert.That(r.Status, Is.EqualTo(SaveLoadStatus.Repaired));
            Assert.That(r.Warnings.Count, Is.GreaterThanOrEqualTo(5), string.Join("; ", r.Warnings));
            Assert.That(r.State.Collection.ContainsKey("nosuchcard"), Is.False);
            Assert.That(r.State.Count("orionmane"), Is.Zero);
            Assert.That(r.State.Count("leomane"), Is.EqualTo(CampaignSave.MaxCopies));
            Assert.That(r.State.Records.ContainsKey("stranger"), Is.False);
            Assert.That(r.State.UnlockedRegions, Is.EqualTo(new[] { "harbor", "library" }));
        }

        [Test]
        public void CorruptDataFallsBackToANewGameWithoutThrowing()
        {
            var cases = new[]
            {
                "{", "}", "[]", "null", "42", "\"text\"", "{\"version\":1}", "{\"version\":\"1\",\"collection\":{}}",
                "{\"version\":1,\"collection\":[]}", "{\"version\":1,\"collection\":{\"glimmouse\":1.5}}",
                "{\"version\":1,\"collection\":{\"glimmouse\":1}}",                    // 5 種類未満
                "{\"version\":-1,\"collection\":{}}", "{\"version\":99999999999,\"collection\":{}}",
                "{\"cards\":\"oops\"}", "{\"version\":1,\"collection\":{\"a\":1}} trailing",
                new string('[', 5000), "{\"version\":1,\"seed\":123456789012345678901234567890}",
                "\u0000\u0001", "{\"a\":\"\\uZZZZ\"}", "{\"a\":\"unterminated",
            };
            foreach (var c in cases)
            {
                SaveLoadResult r = null;
                Assert.DoesNotThrow(() => r = CampaignSave.Load(c, 11), c);
                Assert.That(r.StartedOver, Is.True, c);
                Assert.That(r.Status, Is.EqualTo(SaveLoadStatus.Corrupt).Or.EqualTo(SaveLoadStatus.TooNew), c);
                Assert.That(r.Message, Is.Not.Empty, c);
                AssertSame(CampaignRules.NewGame(11), r.State);
            }
        }

        [Test]
        public void RandomlyDamagedSavesNeverThrow()
        {
            var json = CampaignSave.ToJson(Played());
            var r = new TestRandom(0x5a7e);
            const string alphabet = "{}[]\":,0123456789-abcxyz \\tnulfrue";
            int recovered = 0, readable = 0;
            for (int t = 0; t < 3000; t++)
            {
                var sb = new StringBuilder(json);
                int edits = 1 + r.NextInt(4);
                for (int e = 0; e < edits && sb.Length > 0; e++)
                {
                    int pos = r.NextInt(sb.Length);
                    switch (r.NextInt(4))
                    {
                        case 0: sb.Remove(pos, 1); break;
                        case 1: sb.Insert(pos, alphabet[r.NextInt(alphabet.Length)]); break;
                        case 2: sb[pos] = alphabet[r.NextInt(alphabet.Length)]; break;
                        default: sb.Length = pos; break; // 途中で切れた
                    }
                }
                SaveLoadResult res = null;
                Assert.DoesNotThrow(() => res = CampaignSave.Load(sb.ToString()), sb.ToString());
                Assert.That(res.State, Is.Not.Null);
                Assert.That(res.State.Collection.Count, Is.GreaterThanOrEqualTo(5));
                Assert.That(res.State.UnlockedRegions, Does.Contain("harbor"));
                if (res.StartedOver) recovered++;
                else readable++;
            }
            Assert.That(recovered, Is.GreaterThan(0));
            Assert.That(readable, Is.GreaterThan(0));
        }

        [Test]
        public void MiniJsonRoundTripsStringsAndRejectsFloats()
        {
            var o = JsonValue.NewObject()
                .Set("s", JsonValue.String("改行\n\"引用\"\\ タブ\t\u0001 ☆"))
                .Set("n", JsonValue.Integer(-9007199254740993L))
                .Set("a", JsonValue.NewArray().Add(JsonValue.True).Add(JsonValue.Null));
            var text = MiniJson.Write(o);
            var back = MiniJson.Parse(text);
            Assert.That(back.Get("s").AsString, Is.EqualTo("改行\n\"引用\"\\ タブ\t\u0001 ☆"));
            Assert.That(back.Get("n").AsLong, Is.EqualTo(-9007199254740993L));
            Assert.That(back.Get("a").Items.Select(v => v.Kind), Is.EqualTo(new[] { JsonKind.Bool, JsonKind.Null }));
            Assert.That(MiniJson.Write(back), Is.EqualTo(text));
            Assert.That(MiniJson.Parse(" {\"x\" : [ 1 , 2 ] } ").Get("x").Items.Count, Is.EqualTo(2));
            Assert.That(MiniJson.Parse("\"\\u3042\"").AsString, Is.EqualTo("あ"));
            Assert.Throws<JsonException>(() => MiniJson.Parse("1.5"));
            Assert.Throws<JsonException>(() => MiniJson.Parse("1e3"));
            Assert.Throws<JsonException>(() => MiniJson.Parse(new string('[', MiniJson.MaxDepth + 5)));
        }
    }
}
