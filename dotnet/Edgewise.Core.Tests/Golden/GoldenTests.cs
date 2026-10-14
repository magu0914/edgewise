using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Edgewise.Core.Data;
using NUnit.Framework;

namespace Edgewise.Core.Tests.Golden
{
    /// <summary>
    /// TS 版との突き合わせ（最重要）。
    /// tripletriad-ts-legacy/tools/export-golden.ts が書き出した golden.json の各局を C# で再生し、
    /// 初期局面と終局の正規化文字列・毎手の hashState・毎手のイベント列（JSON 表記）・合法手の数と並びが TS 版と完全に一致することを確かめる。
    /// ApplyMoveFast の hash も同時に検査する。
    /// </summary>
    [TestFixture]
    public class GoldenTests
    {
        static readonly Lazy<JsonDocument> Doc = new Lazy<JsonDocument>(() =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Golden", "golden.json"))));

        static IEnumerable<JsonElement> Games() => Doc.Value.RootElement.GetProperty("games").EnumerateArray();

        static string GroupOf(JsonElement game)
        {
            var name = game.GetProperty("name").GetString();
            return name.Substring(0, name.IndexOf('#'));
        }

        static IEnumerable<string> Groups() => Games().Select(GroupOf).Distinct();

        static RuleSet ParseRules(JsonElement r) => new RuleSet(
            open: r.GetProperty("open").GetBoolean(),
            random: r.GetProperty("random").GetBoolean(),
            same: r.GetProperty("same").GetBoolean(),
            sameWall: r.GetProperty("sameWall").GetBoolean(),
            plus: r.GetProperty("plus").GetBoolean(),
            combo: r.GetProperty("combo").GetBoolean(),
            elemental: r.GetProperty("elemental").GetBoolean(),
            suddenDeath: r.GetProperty("suddenDeath").GetBoolean(),
            pivot: r.GetProperty("pivot").GetBoolean(),
            resonance: r.GetProperty("resonance").GetBoolean(),
            traits: r.GetProperty("traits").GetBoolean());

        static CardDef ParseCard(JsonElement c)
        {
            var s = c.GetProperty("sides").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            Element? element = null;
            if (c.TryGetProperty("element", out var e))
            {
                Assert.That(Ids.TryParseElement(e.GetString(), out var parsed), Is.True);
                element = parsed;
            }
            Tribe? tribe = null;
            if (c.TryGetProperty("tribe", out var t))
            {
                Assert.That(Ids.TryParseTribe(t.GetString(), out var parsed), Is.True);
                tribe = parsed;
            }
            return new CardDef(c.GetProperty("id").GetString(), c.GetProperty("name").GetString(), c.GetProperty("level").GetInt32(),
                new Sides(s[0], s[1], s[2], s[3]), element, tribe);
        }

        static string[] Strings(JsonElement a) => a.EnumerateArray().Select(v => v.GetString()).ToArray();

        static Move ParseMove(JsonElement m) =>
            new Move(m.GetProperty("handIndex").GetInt32(), m.GetProperty("cell").GetInt32(), m.GetProperty("rotation").GetInt32());

        /// <summary>1 局を再生して、食い違いを文字列で返す（一致すれば null）。</summary>
        static string ReplayGame(JsonElement game, ref int moveCount, ref int eventCount)
        {
            string name = game.GetProperty("name").GetString();
            var rules = ParseRules(game.GetProperty("rules"));
            IEnumerable<CardDef> cards = game.TryGetProperty("cards", out var cs) ? cs.EnumerateArray().Select(ParseCard).ToArray() : CardCatalog.All;
            var decks = game.GetProperty("decks");
            long seed = game.GetProperty("seed").GetInt64();
            var config = new GameConfig(rules, Strings(decks[0]), Strings(decks[1]), seed, cards);

            var state = GameEngine.CreateGame(config);
            var initial = game.GetProperty("initial");
            if (StateHash.Serialize(state) != initial.GetProperty("serialized").GetString())
                return name + " initial: serialized differs\n  C#: " + StateHash.Serialize(state) + "\n  TS: " + initial.GetProperty("serialized").GetString();
            if (GameEngine.HashState(state) != initial.GetProperty("hash").GetString()) return name + " initial: hash differs";

            int stepIndex = 0;
            foreach (var step in game.GetProperty("steps").EnumerateArray())
            {
                string where = name + " step " + stepIndex;
                var legal = GameEngine.LegalMoves(state);
                if (legal.Count != step.GetProperty("legalCount").GetInt32())
                    return where + ": legalMoves count " + legal.Count + " != " + step.GetProperty("legalCount").GetInt32();
                var move = ParseMove(step.GetProperty("move"));
                if (legal[step.GetProperty("moveIndex").GetInt32()] != move) return where + ": legalMoves order differs";

                var result = GameEngine.ApplyMove(state, move);
                var fast = GameEngine.ApplyMoveFast(state, move);

                var expectedEvents = step.GetProperty("events").EnumerateArray().Select(e => e.GetRawText()).ToList();
                var actualEvents = result.Events.Select(e => e.ToJson()).ToList();
                if (!expectedEvents.SequenceEqual(actualEvents))
                    return where + ": events differ\n  C#: " + string.Join(",", actualEvents) + "\n  TS: " + string.Join(",", expectedEvents);

                string expectedHash = step.GetProperty("hash").GetString();
                if (GameEngine.HashState(result.State) != expectedHash)
                    return where + ": hash differs\n  C# serialized: " + StateHash.Serialize(result.State);
                if (GameEngine.HashState(fast) != expectedHash) return where + ": ApplyMoveFast hash differs";
                if (step.TryGetProperty("serialized", out var ser) && StateHash.Serialize(result.State) != ser.GetString())
                    return where + ": serialized differs\n  C#: " + StateHash.Serialize(result.State) + "\n  TS: " + ser.GetString();

                state = result.State;
                stepIndex++;
                moveCount++;
                eventCount += actualEvents.Count;
            }
            if (state.Phase != Phase.Ended) return name + ": game did not end after the recorded moves";
            return null;
        }

        [TestCaseSource(nameof(Groups))]
        public void MatchesTheTypeScriptEngine(string group)
        {
            int games = 0, moves = 0, events = 0;
            var failures = new List<string>();
            foreach (var game in Games().Where(g => GroupOf(g) == group))
            {
                games++;
                var failure = ReplayGame(game, ref moves, ref events);
                if (failure != null) failures.Add(failure);
            }
            TestContext.Out.WriteLine(group + ": " + games + " games, " + moves + " moves, " + events + " events");
            Assert.That(games, Is.GreaterThan(0));
            Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(5)));
        }

        [Test]
        public void CoversTheWholeGoldenFile()
        {
            var summary = Doc.Value.RootElement.GetProperty("summary");
            int games = 0, moves = 0, events = 0;
            foreach (var game in Games())
            {
                games++;
                Assert.That(ReplayGame(game, ref moves, ref events), Is.Null);
            }
            TestContext.Out.WriteLine("golden: " + games + " games, " + moves + " moves, " + events + " events");
            Assert.That(games, Is.EqualTo(summary.GetProperty("games").GetInt32()));
            Assert.That(moves, Is.EqualTo(summary.GetProperty("moves").GetInt32()));
            Assert.That(events, Is.EqualTo(summary.GetProperty("events").GetInt32()));
            // 珍しい分岐も踏んでいること（Sudden Death の上限、Same / Plus / Combo、Same Wall）
            Assert.That(summary.GetProperty("maxSuddenDeathRound").GetInt32(), Is.EqualTo(GameConstants.MaxSuddenDeathRounds));
            var causes = summary.GetProperty("flipCauses");
            foreach (var cause in new[] { "basic", "same", "plus", "combo" }) Assert.That(causes.GetProperty(cause).GetInt32(), Is.GreaterThan(50), cause);
        }
    }
}
