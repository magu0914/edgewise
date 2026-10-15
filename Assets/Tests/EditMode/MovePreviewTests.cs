using System.Collections.Generic;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.Match;
using NUnit.Framework;

namespace Edgewise.Tests.EditMode
{
    /// <summary>予告（MovePreview）がエンジンの結果と一致し、局面を変えないこと。</summary>
    public class MovePreviewTests
    {
        [Test]
        public void PreviewMatchesTheEngineForEveryLegalMove()
        {
            var ruleSets = new[]
            {
                new RuleSet(open: true),
                new RuleSet(same: true, plus: true, combo: true),
                new RuleSet(open: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, pivot: true),
            };
            var ids = CardCatalog.All.Select(c => c.Id).ToArray();
            int compared = 0, withRule = 0;
            foreach (var rules in ruleSets)
            {
                for (int seed = 1; seed <= 25; seed++)
                {
                    uint rng = Rng.SeedToState(seed * 7919);
                    var deck = ids.OrderBy(_ => Rng.NextInt(rng, 1 << 20, out rng)).ToArray();
                    var state = GameEngine.CreateGame(new GameConfig(rules, deck.Take(5).ToArray(), deck.Skip(5).Take(5).ToArray(), seed,
                        CardCatalog.ById));
                    while (state.Phase == Phase.Playing)
                    {
                        string hashBefore = GameEngine.HashState(state);
                        var legal = GameEngine.LegalMoves(state);
                        foreach (var move in legal)
                        {
                            var preview = MovePreview.Compute(state, move);
                            Assert.That(preview, Is.Not.Null);
                            var after = GameEngine.ApplyMove(state, move).State;
                            // 盤の所有者の変化（置いたマス以外）＝裏返ったマス
                            var expected = new List<int>();
                            for (int c = 0; c < 9; c++)
                            {
                                if (c == move.Cell || !state.Board[c].HasValue) continue;
                                if (after.Board[c].Value.Owner != state.Board[c].Value.Owner) expected.Add(c);
                            }
                            Assert.That(preview.FlipCells.OrderBy(c => c), Is.EqualTo(expected), "move " + move);
                            var events = GameEngine.ApplyMove(state, move).Events;
                            Assert.That(preview.Same || preview.SameWall, Is.EqualTo(events.OfType<RuleEvent>().Any(r => r.Rule != RuleHeading.Plus)));
                            Assert.That(preview.Plus, Is.EqualTo(events.OfType<RuleEvent>().Any(r => r.Rule == RuleHeading.Plus)));
                            if (preview.Same || preview.Plus || preview.SameWall) withRule++;
                            compared++;
                        }
                        // 予告は局面を変えない
                        Assert.That(GameEngine.HashState(state), Is.EqualTo(hashBefore));
                        uint pick = Rng.NextUInt32(rng, out rng);
                        state = GameEngine.ApplyMove(state, legal[(int)(pick % (uint)legal.Count)]).State;
                    }
                }
            }
            Assert.That(compared, Is.GreaterThan(1000));
            Assert.That(withRule, Is.GreaterThan(0), "some previews must show Same / Plus");
        }

        [Test]
        public void IllegalMoveHasNoPreview()
        {
            var state = GameEngine.CreateGame(new GameConfig(new RuleSet(open: true),
                new[] { "glimmouse", "dustwisp", "coglet", "emberkit", "scalefry" },
                new[] { "pyxmoth", "bolthare", "rivetcrab", "frostnewt", "mirelamp" }, 3, CardCatalog.ById));
            Assert.That(MovePreview.Compute(state, new Move(0, 9)), Is.Null);
            Assert.That(MovePreview.Compute(state, new Move(0, 0, 1)), Is.Null, "rotation needs the Pivot rule");
            Assert.That(MovePreview.Compute(state, new Move(0, 0)), Is.Not.Null);
        }
    }
}
