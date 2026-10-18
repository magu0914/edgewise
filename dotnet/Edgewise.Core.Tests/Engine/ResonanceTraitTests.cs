using System.Collections.Generic;
using System.Linq;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using NUnit.Framework;
using AiTests = Edgewise.Core.Tests.AI;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // DESIGN.md §3-4（Resonance と Trait の確定仕様）。盤のマス番号:
    // 0 1 2
    // 3 4 5
    // 6 7 8
    // 手番は P0（プレイヤー 0）。PlayCenter = 手札 0 番をマス 4 に置く。

    static class RT
    {
        public static readonly RuleSet Traits = new RuleSet(traits: true);
        public static readonly RuleSet Res = new RuleSet(resonance: true);

        public static List<TraitEvent> TraitEvents(IEnumerable<GameEvent> events) => events.OfType<TraitEvent>().ToList();

        public static GameState S(RuleSet rules, CardDef[] cards, BoardSpec board, Setup extra = null)
        {
            var s = extra ?? new Setup();
            s.Cards = cards;
            s.Rules = rules;
            s.Board = board;
            return MakeState(s);
        }
    }

    [TestFixture]
    public class GuardTests
    {
        static readonly CardDef P = MkCard("P", 9, 1, 1, 1);
        static readonly CardDef G = MkCard("G", 1, 1, 1, 1, trait: TraitId.Guard);

        [Test]
        public void BlocksTheFirstFlipOnlyAndEmitsATraitEvent()
        {
            var cards = new[] { P, G };
            var s = RT.S(RT.Traits, cards, new BoardSpec { { 1, "G", 1 } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State)[1], Is.EqualTo(1), "guarded card is not flipped");
            Assert.That(Flips(r.Events), Is.Empty);
            Assert.That(RT.TraitEvents(r.Events), Is.EqualTo(new[] { new TraitEvent(1, TraitId.Guard) }));
            Assert.That(r.State.Board[1].Value.GuardUsed, Is.True);

            // 使い切った後は普通に裏返る（trait イベントも出ない）
            var used = RT.S(RT.Traits, cards, new BoardSpec { { 1, "G", 1 } }, new Setup { GuardUsedCells = { 1 } });
            var r2 = Both(used, PlayCenter);
            Assert.That(Owners(r2.State)[1], Is.EqualTo(0));
            Assert.That(RT.TraitEvents(r2.Events), Is.Empty);
        }

        [Test]
        public void IsIgnoredWhenTheTraitsRuleIsOff()
        {
            var s = RT.S(RuleSet.None, new[] { P, G }, new BoardSpec { { 1, "G", 1 } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State)[1], Is.EqualTo(0));
            Assert.That(RT.TraitEvents(r.Events), Is.Empty);
            Assert.That(r.State.Board[1].Value.GuardUsed, Is.False);
        }

        [Test]
        public void ThePlacedGuardCardIsAlsoProtectedLater()
        {
            // P0 が Guard カードを置き、P1 が攻撃しても 1 回は耐える
            var guard = MkCard("GP", 1, 1, 1, 1, trait: TraitId.Guard);
            var attacker = MkCard("Q", 9, 9, 9, 9);
            var s = RT.S(RT.Traits, new[] { guard, attacker }, new BoardSpec(),
                new Setup { Hands = new[] { new[] { "GP" }, new[] { "Q" } } });
            var a = Both(s, PlayCenter).State;
            var b = Both(a, new Move(0, 1, 0));
            Assert.That(Owners(b.State)[4], Is.EqualTo(0));
            Assert.That(RT.TraitEvents(b.Events), Is.EqualTo(new[] { new TraitEvent(4, TraitId.Guard) }));
        }

        [Test]
        public void SameAndPlusStillCountButOnlyTheGuardedCardStaysPut()
        {
            // P の top 3 = A.bottom 3、bottom 2 = D.top 2、right 7 = 自分の B.left 7 → Same が成立（A は Guard、D は普通）
            var p = MkCard("P", 3, 7, 2, 5);
            var a = MkCard("A", 1, 1, 3, 1, trait: TraitId.Guard);
            var b = MkCard("B", 1, 1, 1, 7);
            var d = MkCard("D", 2, 1, 1, 1);
            var s = RT.S(new RuleSet(same: true, traits: true), new[] { p, a, b, d }, new BoardSpec { { 1, "A", 1 }, { 5, "B", 0 }, { 7, "D", 1 } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(null, 1, null, null, 0, 0, null, 0, null)));
            Assert.That(RuleEvents(r.Events), Is.EqualTo(new[] { new RuleEvent(RuleHeading.Same, new[] { 1, 5, 7 }) }));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(7, FlipCause.Same, 0, 4) }));
            Assert.That(RT.TraitEvents(r.Events), Is.EqualTo(new[] { new TraitEvent(1, TraitId.Guard) }));
            Assert.That(r.State.Board[1].Value.GuardUsed, Is.True);
        }

        [Test]
        public void PlusAlsoRespectsGuard()
        {
            // 置いた P: top 3, right 4, bottom 5, left 1。上 A.bottom 6（和 9）、下 D.top 4（和 9）→ Plus 成立。A は Guard
            var p = MkCard("P", 3, 4, 5, 1);
            var a = MkCard("A", 1, 1, 6, 1, trait: TraitId.Guard);
            var d = MkCard("D", 4, 1, 1, 1);
            var s = RT.S(new RuleSet(plus: true, traits: true), new[] { p, a, d }, new BoardSpec { { 1, "A", 1 }, { 7, "D", 1 } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State)[1], Is.EqualTo(1));
            Assert.That(Owners(r.State)[7], Is.EqualTo(0));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(7, FlipCause.Plus, 0, 4) }));
            Assert.That(RT.TraitEvents(r.Events), Is.EqualTo(new[] { new TraitEvent(1, TraitId.Guard) }));
        }

        [Test]
        public void AGuardedCardOnlyMatchedBySameShowsNoRuleHeading()
        {
            // 成立した相手カードが Guard だけなら、実際には 1 枚も裏返らないので rule イベントは出ない
            var p = MkCard("P", 3, 7, 5, 5);
            var a = MkCard("A", 1, 1, 3, 1, trait: TraitId.Guard);
            var b = MkCard("B", 1, 1, 1, 7);
            var s = RT.S(new RuleSet(same: true, traits: true), new[] { p, a, b }, new BoardSpec { { 1, "A", 1 }, { 5, "B", 0 } });
            var r = Both(s, PlayCenter);
            Assert.That(RuleEvents(r.Events), Is.Empty);
            Assert.That(RT.TraitEvents(r.Events), Is.EqualTo(new[] { new TraitEvent(1, TraitId.Guard) }));
        }

        // Combo の連鎖テスト用: P が A・B を Same で裏返し、A から右隣 T（マス 2）と左隣 X（マス 0）へ連鎖する
        static CardDef[] ComboCards(TraitId? tTrait) => new[]
        {
            MkCard("P", 3, 1, 1, 6),
            MkCard("A", 1, 9, 3, 9),
            MkCard("B", 1, 6, 1, 1),
            MkCard("X", 1, 1, 1, 1),
            MkCard("T", 1, 1, 1, 1, trait: tTrait),
        };

        static GameState ComboState(TraitId? tTrait, RuleSet rules) =>
            RT.S(rules, ComboCards(tTrait), new BoardSpec { { 1, "A", 1 }, { 3, "B", 1 }, { 0, "X", 1 }, { 2, "T", 1 } });

        [Test]
        public void BlocksAComboFlipOnceAndTheChainContinuesElsewhere()
        {
            var rules = new RuleSet(same: true, combo: true, traits: true);
            var r = Both(ComboState(TraitId.Guard, rules), PlayCenter);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(0, 0, 1, 0, 0, null, null, null, null)));
            Assert.That(Flips(r.Events), Is.EqualTo(new[]
            {
                F(1, FlipCause.Same, 0, 4), F(3, FlipCause.Same, 0, 4), F(0, FlipCause.Combo, 1, 1),
            }));
            Assert.That(RT.TraitEvents(r.Events), Is.EqualTo(new[] { new TraitEvent(2, TraitId.Guard) }));
            Assert.That(r.State.Board[2].Value.GuardUsed, Is.True);

            // Traits OFF なら T も裏返る（連鎖の順は 右 → 左）
            var off = Both(ComboState(TraitId.Guard, new RuleSet(same: true, combo: true)), PlayCenter);
            Assert.That(Flips(off.Events).Select(f => f.Cell), Is.EqualTo(new[] { 1, 3, 2, 0 }));
        }
    }

    [TestFixture]
    public class AnchorTests
    {
        [Test]
        public void IsNotFlippedByComboButIsFlippedByBasicSameAndPlus()
        {
            // Combo では裏返らない
            var cards = new[]
            {
                MkCard("P", 3, 1, 1, 6),
                MkCard("A", 1, 9, 3, 9),
                MkCard("B", 1, 6, 1, 1),
                MkCard("X", 1, 1, 1, 1),
                MkCard("T", 1, 1, 1, 1, trait: TraitId.Anchor),
            };
            var board = new BoardSpec { { 1, "A", 1 }, { 3, "B", 1 }, { 0, "X", 1 }, { 2, "T", 1 } };
            var r = Both(RT.S(new RuleSet(same: true, combo: true, traits: true), cards, board), PlayCenter);
            Assert.That(Owners(r.State)[2], Is.EqualTo(1));
            Assert.That(Owners(r.State)[0], Is.EqualTo(0));
            Assert.That(RT.TraitEvents(r.Events), Is.Empty);

            // 通常ルールでは裏返る
            var basic = Both(RT.S(RT.Traits, new[] { MkCard("P", 9, 1, 1, 1), MkCard("T", 1, 1, 1, 1, trait: TraitId.Anchor) },
                new BoardSpec { { 1, "T", 1 } }), PlayCenter);
            Assert.That(Owners(basic.State)[1], Is.EqualTo(0));
            Assert.That(Flips(basic.Events), Is.EqualTo(new[] { F(1, FlipCause.Basic, 0, 4) }));

            // Same では裏返る
            var same = Both(RT.S(new RuleSet(same: true, traits: true),
                new[] { MkCard("P", 3, 7, 5, 5), MkCard("A", 1, 1, 3, 1, trait: TraitId.Anchor), MkCard("B", 1, 1, 1, 7) },
                new BoardSpec { { 1, "A", 1 }, { 5, "B", 0 } }), PlayCenter);
            Assert.That(Owners(same.State)[1], Is.EqualTo(0));
            Assert.That(Flips(same.Events), Is.EqualTo(new[] { F(1, FlipCause.Same, 0, 4) }));

            // Plus では裏返る
            var plus = Both(RT.S(new RuleSet(plus: true, traits: true),
                new[] { MkCard("P", 3, 4, 5, 1), MkCard("A", 1, 1, 6, 1, trait: TraitId.Anchor), MkCard("D", 4, 1, 1, 1) },
                new BoardSpec { { 1, "A", 1 }, { 7, "D", 1 } }), PlayCenter);
            Assert.That(Owners(plus.State)[1], Is.EqualTo(0));
            Assert.That(Owners(plus.State)[7], Is.EqualTo(0));
        }

        [Test]
        public void IsIgnoredWhenTheTraitsRuleIsOff()
        {
            var cards = new[]
            {
                MkCard("P", 3, 1, 1, 6),
                MkCard("A", 1, 9, 3, 9),
                MkCard("B", 1, 6, 1, 1),
                MkCard("X", 1, 1, 1, 1),
                MkCard("T", 1, 1, 1, 1, trait: TraitId.Anchor),
            };
            var board = new BoardSpec { { 1, "A", 1 }, { 3, "B", 1 }, { 0, "X", 1 }, { 2, "T", 1 } };
            var r = Both(RT.S(new RuleSet(same: true, combo: true), cards, board), PlayCenter);
            Assert.That(Owners(r.State)[2], Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class PierceTests
    {
        // 中央に置く P(top 6, right 5, bottom 4, left 3): 右上 = top+right 11、右下 = right+bottom 9、左下 = bottom+left 7、左上 = left+top 9
        static readonly CardDef P = MkCard("P", 6, 5, 4, 3, trait: TraitId.Pierce);

        [Test]
        public void ComparesTheSumOfTwoFacingSidesInAllFourDiagonals()
        {
            var cards = new[]
            {
                P,
                MkCard("UR", 1, 1, 5, 5),  // 右上: 相手 bottom+left = 10 < 11 → 裏返る
                MkCard("DR", 4, 1, 1, 4),  // 右下: 相手 top+left = 8 < 9 → 裏返る
                MkCard("DL", 4, 3, 1, 1),  // 左下: 相手 top+right = 7（同値）→ 裏返らない
                MkCard("UL", 1, 5, 5, 1),  // 左上: 相手 right+bottom = 10 > 9 → 裏返らない
            };
            var s = RT.S(RT.Traits, cards, new BoardSpec { { 2, "UR", 1 }, { 8, "DR", 1 }, { 6, "DL", 1 }, { 0, "UL", 1 } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State), Is.EqualTo(Owners(1, null, 0, null, 0, null, 1, null, 0)));
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(2, FlipCause.Basic, 0, 4), F(8, FlipCause.Basic, 0, 4) }));
        }

        [Test]
        public void DoesNothingWhenTheTraitsRuleIsOff()
        {
            var cards = new[] { P, MkCard("UR", 1, 1, 5, 5) };
            var r = Both(RT.S(RuleSet.None, cards, new BoardSpec { { 2, "UR", 1 } }), PlayCenter);
            Assert.That(Owners(r.State)[2], Is.EqualTo(1));
        }

        [Test]
        public void ResolvesInTheOrderUpperRightLowerRightLowerLeftUpperLeft()
        {
            var strong = MkCard("P", 9, 9, 9, 9, trait: TraitId.Pierce);
            var weak = MkCard("W", 1, 1, 1, 1);
            var cards = new[] { strong, weak };
            var s = RT.S(RT.Traits, cards, new BoardSpec { { 0, "W", 1 }, { 2, "W", 1 }, { 6, "W", 1 }, { 8, "W", 1 } });
            var r = Both(s, PlayCenter);
            Assert.That(Flips(r.Events).Select(f => f.Cell), Is.EqualTo(new[] { 2, 8, 6, 0 }));
        }

        [Test]
        public void SkipsOwnCardsAndTheEdgeOfTheBoard()
        {
            var strong = MkCard("P", 9, 9, 9, 9, trait: TraitId.Pierce);
            var weak = MkCard("W", 1, 1, 1, 1);
            // 角（マス 0）に置くと斜めは 4 だけ。4 が自分のカードなら何も起きない
            var own = RT.S(RT.Traits, new[] { strong, weak }, new BoardSpec { { 4, "W", 0 } });
            var r = Both(own, new Move(0, 0, 0));
            Assert.That(Flips(r.Events), Is.Empty);
            var opp = RT.S(RT.Traits, new[] { strong, weak }, new BoardSpec { { 4, "W", 1 } });
            var r2 = Both(opp, new Move(0, 0, 0));
            Assert.That(Flips(r2.Events), Is.EqualTo(new[] { F(4, FlipCause.Basic, 0, 0) }));
        }

        [Test]
        public void ACardTakenByPierceDoesNotStartACombo()
        {
            // 右上 UR(マス 2) を Pierce で取る。UR の left 9 は隣の U(マス 1) の right 1 より大きいが、Pierce で取ったカードは Combo の起点にならない
            var cards = new[]
            {
                P,
                MkCard("UR", 1, 1, 1, 9),
                MkCard("U", 1, 1, 9, 1),
            };
            var s = RT.S(new RuleSet(combo: true, traits: true), cards, new BoardSpec { { 2, "UR", 1 }, { 1, "U", 1 } });
            var r = Both(s, PlayCenter);
            Assert.That(Owners(r.State)[2], Is.EqualTo(0), "UR is taken by Pierce");
            Assert.That(Owners(r.State)[1], Is.EqualTo(1), "no combo from a Pierce capture");
            Assert.That(Flips(r.Events), Is.EqualTo(new[] { F(2, FlipCause.Basic, 0, 4) }));
        }

        [Test]
        public void IsBlockedByAnUnusedGuardAndConsumesIt()
        {
            var cards = new[] { P, MkCard("UR", 1, 1, 1, 1, trait: TraitId.Guard) };
            var r = Both(RT.S(RT.Traits, cards, new BoardSpec { { 2, "UR", 1 } }), PlayCenter);
            Assert.That(Owners(r.State)[2], Is.EqualTo(1));
            Assert.That(RT.TraitEvents(r.Events), Is.EqualTo(new[] { new TraitEvent(2, TraitId.Guard) }));
            Assert.That(r.State.Board[2].Value.GuardUsed, Is.True);
        }

        [Test]
        public void UsesTheSidesAfterRotation()
        {
            // Pivot で 90° 回すと top = 元の left(3)、right = 元の top(6)、bottom = 元の right(5)、left = 元の bottom(4)。右上 = 3 + 6 = 9
            var cards = new[] { P, MkCard("UR", 1, 1, 4, 4) }; // 相手 bottom+left = 8 < 9
            var s = RT.S(new RuleSet(traits: true, pivot: true), cards, new BoardSpec { { 2, "UR", 1 } });
            var r = Both(s, new Move(0, 4, 1));
            Assert.That(Owners(r.State)[2], Is.EqualTo(0));
            var none = Both(s, new Move(0, 4, 0)); // 回さなければ 11 vs 8 でもちろん裏返る
            Assert.That(Owners(none.State)[2], Is.EqualTo(0));
            var weak = RT.S(new RuleSet(traits: true, pivot: true), new[] { P, MkCard("UR", 1, 1, 5, 5) }, new BoardSpec { { 2, "UR", 1 } });
            Assert.That(Owners(Both(weak, new Move(0, 4, 1)).State)[2], Is.EqualTo(1), "9 < 10");
            Assert.That(Owners(Both(weak, new Move(0, 4, 0)).State)[2], Is.EqualTo(0), "11 > 10");
        }
    }

    [TestFixture]
    public class RallyTests
    {
        // 中央に置く R(3,3,3,3, Rally)。下のマス 7 の相手カード D の top を変えて必要な加算量を調べる
        static GameState Setup(int adjacentOwn, int dTop, bool traits = true)
        {
            var cards = new[] { MkCard("R", 3, 3, 3, 3, trait: TraitId.Rally), MkCard("D", dTop, 1, 1, 1), MkCard("F", 1, 1, 1, 1) };
            var board = new BoardSpec { { 7, "D", 1 } };
            int[] own = { 1, 3, 5 }; // 中央の上・左・右
            for (int i = 0; i < adjacentOwn; i++) board.Add(own[i], "F", 0);
            return RT.S(new RuleSet(traits: traits), cards, board, new Setup { Hands = new[] { new[] { "R" }, new[] { "D" } } });
        }

        [Test]
        public void AddsOnePerAdjacentOwnCardUpToTwo()
        {
            // 下の D.top = 4: R.bottom 3 + 隣接数。1 枚なら 4（同値で取れない）、2 枚以上なら 5 で取れる
            Assert.That(Owners(Both(Setup(0, 4), PlayCenter).State)[7], Is.EqualTo(1));
            Assert.That(Owners(Both(Setup(1, 4), PlayCenter).State)[7], Is.EqualTo(1));
            Assert.That(Owners(Both(Setup(2, 4), PlayCenter).State)[7], Is.EqualTo(0));
            Assert.That(Owners(Both(Setup(3, 4), PlayCenter).State)[7], Is.EqualTo(0));
            // 上限は +2: D.top = 5 は 3 枚隣接しても取れない（+3 なら取れてしまう）
            Assert.That(Owners(Both(Setup(3, 5), PlayCenter).State)[7], Is.EqualTo(1));
            Assert.That(Owners(Both(Setup(2, 5), PlayCenter).State)[7], Is.EqualTo(1));
            Assert.That(Owners(Both(Setup(2, 4), PlayCenter).State)[7], Is.EqualTo(0));
        }

        [Test]
        public void OnlyCountsOwnCardsAndOnlyWhenTheRuleIsOn()
        {
            var cards = new[] { MkCard("R", 3, 3, 3, 3, trait: TraitId.Rally), MkCard("D", 4, 1, 1, 1), MkCard("F", 1, 1, 1, 1) };
            // 隣接が相手のカードなら加算されない
            var oppAdj = RT.S(RT.Traits, cards, new BoardSpec { { 7, "D", 1 }, { 1, "F", 1 }, { 3, "F", 1 } },
                new Setup { Hands = new[] { new[] { "R" }, new[] { "D" } } });
            Assert.That(Owners(Both(oppAdj, PlayCenter).State)[7], Is.EqualTo(1));
            Assert.That(Owners(Setup(2, 4, traits: false) is var s ? Both(s, PlayCenter).State : null)[7], Is.EqualTo(1), "Traits OFF");
        }

        [Test]
        public void TheBonusLastsOnlyForThatMove()
        {
            // R(3,3,3,3) を隣接 2 枚の自分のカードの間に置く。その手のあいだは各辺 +2 = 5。次の手で P1 の Q(top 4 → 盤上の R と比べる辺 4)が R の raw 3 に勝てば +2 は残っていない
            var cards = new[]
            {
                MkCard("R", 3, 3, 3, 3, trait: TraitId.Rally),
                MkCard("F", 1, 1, 1, 1),
                MkCard("Q", 1, 1, 1, 1),
                MkCard("Z", 4, 4, 4, 4),
            };
            var s = RT.S(RT.Traits, cards, new BoardSpec { { 1, "F", 0 }, { 3, "F", 0 } },
                new Setup { Hands = new[] { new[] { "R" }, new[] { "Z" } } });
            var a = Both(s, PlayCenter).State;
            // P1 が右（マス 5）に Z を置く: Z.left 4 > R.right 3 なら取れる（R が +2 のままなら 5 で取れない）
            var b = Both(a, new Move(0, 5, 0));
            Assert.That(Owners(b.State)[4], Is.EqualTo(1));
        }
    }

    [TestFixture]
    public class ResonanceTests
    {
        static CardDef Beast(string id, int t, int r, int b, int l, Element? e = null) => MkCard(id, t, r, b, l, e, Tribe.Beast);
        static CardDef Dragon(string id, int t, int r, int b, int l, Element? e = null) => MkCard(id, t, r, b, l, e, Tribe.Dragon);

        [Test]
        public void PlacedCardGetsPlusOneNextToAnOwnCardOfTheSameTribe()
        {
            // P.top 4 vs 相手 D.bottom 4。左隣に自分の Beast がいれば P.top = 5 で取れる
            var cards = new[] { Beast("P", 4, 1, 1, 1), Dragon("D", 1, 1, 4, 1), Beast("F", 1, 1, 1, 1) };
            var withFriend = RT.S(RT.Res, cards, new BoardSpec { { 1, "D", 1 }, { 3, "F", 0 } });
            Assert.That(Owners(Both(withFriend, PlayCenter).State)[1], Is.EqualTo(0));
            var off = RT.S(RuleSet.None, cards, new BoardSpec { { 1, "D", 1 }, { 3, "F", 0 } });
            Assert.That(Owners(Both(off, PlayCenter).State)[1], Is.EqualTo(1), "Resonance OFF");
        }

        [Test]
        public void NeedsTheSameOwnerTheSameTribeAndOrthogonalAdjacency()
        {
            var cards = new[] { Beast("P", 4, 1, 1, 1), Dragon("D", 1, 1, 4, 1), Beast("F", 1, 1, 1, 1), Dragon("Y", 1, 1, 1, 1) };
            // 相手の Beast（別の所有者）、自分の Dragon（別の系統）、斜めの自分の Beast は数えない
            var board = new BoardSpec { { 1, "D", 1 }, { 3, "F", 1 }, { 5, "Y", 0 }, { 0, "F", 0 } };
            Assert.That(Owners(Both(RT.S(RT.Res, cards, board), PlayCenter).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void AddsOnlyOneNoMatterHowManyNeighbours()
        {
            // 同系統が 3 枚隣接しても +1 まで: P.top 4 → 5。D.bottom 4 には勝てる、D.bottom 5 には勝てない（+3 なら勝ててしまう）
            var cards = new[] { Beast("P", 4, 1, 1, 1), Dragon("D4", 1, 1, 4, 1), Dragon("D5", 1, 1, 5, 1), Beast("F", 1, 1, 1, 1) };
            var win = new BoardSpec { { 3, "F", 0 }, { 5, "F", 0 }, { 7, "F", 0 }, { 1, "D4", 1 } };
            Assert.That(Owners(Both(RT.S(RT.Res, cards, win), PlayCenter).State)[1], Is.EqualTo(0));
            var lose = new BoardSpec { { 3, "F", 0 }, { 5, "F", 0 }, { 7, "F", 0 }, { 1, "D5", 1 } };
            Assert.That(Owners(Both(RT.S(RT.Res, cards, lose), PlayCenter).State)[1], Is.EqualTo(1));
        }

        [Test]
        public void ProtectsTheDefenderToo()
        {
            // 相手の D(bottom 4) の隣（マス 0）に相手の Dragon がいる → D の bottom は 5。P.top 5 では取れず、無ければ取れる
            var cards = new[] { Beast("P", 5, 1, 1, 1), Dragon("D", 1, 1, 4, 1), Dragon("E", 1, 1, 1, 1) };
            var guarded = RT.S(RT.Res, cards, new BoardSpec { { 1, "D", 1 }, { 0, "E", 1 } });
            Assert.That(Owners(Both(guarded, PlayCenter).State)[1], Is.EqualTo(1));
            var lonely = RT.S(RT.Res, cards, new BoardSpec { { 1, "D", 1 } });
            Assert.That(Owners(Both(lonely, PlayCenter).State)[1], Is.EqualTo(0));
        }

        [Test]
        public void IsDecidedOnTheBoardAtTheMomentOfEachComparison()
        {
            // P(Beast): top 5, right 5。上の U(Dragon, bottom 3) は P.top 5 > 3 で先に裏返る。
            // 裏返った U は自分のカードだが系統が違うので P は共鳴しない。右の V(Dragon, left 5) とは 5 vs 5 で取れない。
            // 一方、上のカードが Beast だったら裏返った後に P が共鳴して +1 になり、右の V.left 5 に 6 > 5 で勝てる
            var dragonUp = new[] { Beast("P", 5, 5, 1, 1), Dragon("U", 1, 1, 3, 1), Dragon("V", 1, 1, 1, 5) };
            var s1 = RT.S(RT.Res, dragonUp, new BoardSpec { { 1, "U", 1 }, { 5, "V", 1 } });
            var r1 = Both(s1, PlayCenter);
            Assert.That(Owners(r1.State)[1], Is.EqualTo(0));
            Assert.That(Owners(r1.State)[5], Is.EqualTo(1));

            var beastUp = new[] { Beast("P", 5, 5, 1, 1), Beast("U", 1, 1, 3, 1), Dragon("V", 1, 1, 1, 5) };
            var s2 = RT.S(RT.Res, beastUp, new BoardSpec { { 1, "U", 1 }, { 5, "V", 1 } });
            var r2 = Both(s2, PlayCenter);
            Assert.That(Flips(r2.Events), Is.EqualTo(new[] { F(1, FlipCause.Basic, 0, 4), F(5, FlipCause.Basic, 0, 4) }));
        }

        [Test]
        public void StacksWithElementalAndIsNotClamped()
        {
            // マス 4 が Fire。P は Fire の Beast: Elemental +1、共鳴 +1 で top 8 + 2 = 10 → 相手 D.bottom 9 に勝つ
            var cards = new[] { MkCard("P", 8, 1, 1, 1, Element.Fire, Tribe.Beast), MkCard("D", 1, 1, 9, 1), MkCard("F", 1, 1, 1, 1, null, Tribe.Beast) };
            var rules = new RuleSet(resonance: true, elemental: true);
            var setup = new Setup { CellElements = { { 4, Element.Fire } } };
            var s = RT.S(rules, cards, new BoardSpec { { 1, "D", 1 }, { 3, "F", 0 } }, setup);
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(0));
            // 無属性カードの共鳴は −1 + 1 = 0
            var cards2 = new[] { MkCard("P", 9, 1, 1, 1, null, Tribe.Beast), MkCard("D", 1, 1, 9, 1), MkCard("F", 1, 1, 1, 1, null, Tribe.Beast) };
            var s2 = RT.S(rules, cards2, new BoardSpec { { 1, "D", 1 }, { 3, "F", 0 } }, new Setup { CellElements = { { 4, Element.Fire } } });
            Assert.That(Owners(Both(s2, PlayCenter).State)[1], Is.EqualTo(1), "9 - 1 + 1 = 9 does not beat 9");
        }

        [Test]
        public void CountsInSameAndTheSameWallComparison()
        {
            // P.top 4 + 共鳴 1 = 5 = D1.bottom 5、P.right 6 + 1 = 7 = D2.left 7 → Same が成立（素の値では不成立）
            var cards = new[]
            {
                Beast("P", 4, 6, 1, 1), Dragon("D1", 1, 1, 5, 1), Dragon("D2", 1, 1, 1, 7), Beast("F", 1, 1, 1, 1),
            };
            var board = new BoardSpec { { 1, "D1", 1 }, { 5, "D2", 1 }, { 3, "F", 0 } };
            var on = Both(RT.S(new RuleSet(same: true, resonance: true), cards, board), PlayCenter);
            Assert.That(Flips(on.Events).Select(f => (f.Cell, f.Cause)), Is.EqualTo(new[] { (1, FlipCause.Same), (5, FlipCause.Same) }));
            var off = Both(RT.S(new RuleSet(same: true), cards, board), PlayCenter);
            Assert.That(Flips(off.Events), Is.Empty);

            // Same Wall: 角に置いた Beast の top 9 + 共鳴 1 = 10 = 壁(A)。右の D と Same
            var wall = new[] { Beast("P", 9, 4, 1, 1), Dragon("D", 1, 1, 1, 5), Beast("F", 1, 1, 1, 1) };
            var wb = new BoardSpec { { 1, "D", 1 }, { 3, "F", 0 } };
            var won = Both(RT.S(new RuleSet(same: true, sameWall: true, resonance: true), wall, wb), new Move(0, 0, 0));
            Assert.That(Flips(won.Events).Select(f => f.Cause), Is.EqualTo(new[] { FlipCause.Same }));
        }

        [Test]
        public void CountsInComboComparisons()
        {
            // Same で裏返った A(Beast) から右の T(Dragon) へ。A.right 8 + 共鳴 1（隣の自分の Beast=P）= 9 > T.left 8 → 連鎖する。Resonance なしなら 8 = 8 で止まる
            var cards = new[]
            {
                Beast("P", 3, 1, 1, 6),
                Beast("A", 1, 8, 3, 1),
                Beast("B", 1, 6, 1, 1),
                Dragon("T", 1, 1, 1, 8),
            };
            var board = new BoardSpec { { 1, "A", 1 }, { 3, "B", 1 }, { 2, "T", 1 } };
            var on = Both(RT.S(new RuleSet(same: true, combo: true, resonance: true), cards, board), PlayCenter);
            Assert.That(Owners(on.State)[2], Is.EqualTo(0));
            var off = Both(RT.S(new RuleSet(same: true, combo: true), cards, board), PlayCenter);
            Assert.That(Owners(off.State)[2], Is.EqualTo(1));
        }

        [Test]
        public void CardsWithoutATribeNeverResonate()
        {
            var cards = new[] { MkCard("P", 4, 1, 1, 1), Dragon("D", 1, 1, 4, 1), MkCard("F", 1, 1, 1, 1) };
            var s = RT.S(RT.Res, cards, new BoardSpec { { 1, "D", 1 }, { 3, "F", 0 } });
            Assert.That(Owners(Both(s, PlayCenter).State)[1], Is.EqualTo(1));
        }
    }

    [TestFixture]
    public class TraitHashTests
    {
        [Test]
        public void GuardUsedChangesTheHashAndEquality()
        {
            var cards = new[] { MkCard("P", 9, 1, 1, 1), MkCard("G", 1, 1, 1, 1, trait: TraitId.Guard) };
            var fresh = RT.S(RT.Traits, cards, new BoardSpec { { 1, "G", 1 } });
            var used = RT.S(RT.Traits, cards, new BoardSpec { { 1, "G", 1 } }, new Setup { GuardUsedCells = { 1 } });
            Assert.That(used, Is.Not.EqualTo(fresh));
            Assert.That(GameEngine.HashState(used), Is.Not.EqualTo(GameEngine.HashState(fresh)));
            var after = GameEngine.ApplyMove(fresh, PlayCenter).State;
            Assert.That(after.Board[1].Value.GuardUsed, Is.True);
        }

        [Test]
        public void HashIsUnchangedWhenTheRulesAreOffEvenForCardsWithTribesAndTraits()
        {
            var plain = new[] { MkCard("P", 9, 1, 1, 1), MkCard("G", 1, 1, 1, 1) };
            var rich = new[] { MkCard("P", 9, 1, 1, 1, null, Tribe.Beast, TraitId.Pierce), MkCard("G", 1, 1, 1, 1, null, Tribe.Dragon, TraitId.Guard) };
            var a = RT.S(RuleSet.None, plain, new BoardSpec { { 1, "G", 1 } });
            var b = RT.S(RuleSet.None, rich, new BoardSpec { { 1, "G", 1 } });
            Assert.That(StateHash.Serialize(b), Is.EqualTo(StateHash.Serialize(a)));
            // ON のときは系統と Trait も同期チェックの対象になる
            var c = RT.S(new RuleSet(resonance: true, traits: true), rich, new BoardSpec { { 1, "G", 1 } });
            Assert.That(StateHash.Serialize(c), Does.Contain("#beast").And.Contain("@pierce"));
        }

        [Test]
        public void TraitTypeIdsRoundTrip()
        {
            foreach (TraitId t in System.Enum.GetValues(typeof(TraitId)))
            {
                Assert.That(Ids.TryParseTrait(t.ToId(), out var back), Is.True);
                Assert.That(back, Is.EqualTo(t));
            }
            Assert.That(new TraitEvent(3, TraitId.Guard).ToJson(), Is.EqualTo("{\"type\":\"trait\",\"cell\":3,\"trait\":\"guard\"}"));
        }
    }

    [TestFixture]
    public class ResonanceTraitPropertyTests
    {
        /// <summary>辺の値を 3〜6 と A に寄せ、系統と Trait を多めに付けたカード（共鳴・Trait・Same/Plus/Combo が頻発する）。</summary>
        internal static CardDef[] StressCards()
        {
            var r = new TestRandom(0x7a17);
            var values = new[] { 3, 4, 5, 6, 10 };
            var tribes = new Tribe?[] { Tribe.Beast, Tribe.Beast, Tribe.Machine, null };
            var traits = new TraitId?[] { TraitId.Guard, TraitId.Pierce, TraitId.Rally, TraitId.Anchor, null, null };
            return Enumerable.Range(0, 16).Select(i =>
            {
                int V() => values[r.NextInt(values.Length)];
                Element? element = i % 4 == 3 ? (Element?)null : (Element)r.NextInt(8);
                return new CardDef("t" + i, "T" + i, 1, new Sides(V(), V(), V(), V()), element, tribes[r.NextInt(tribes.Length)],
                    traits[r.NextInt(traits.Length)]);
            }).ToArray();
        }

        static GameState NewGame(TestRandom r, IReadOnlyList<CardDef> cards, bool resonance, bool traits, bool? suddenDeath = null) =>
            GameEngine.CreateGame(RandomConfig(r, RandomRules(r, suddenDeath: suddenDeath, resonance: resonance, traits: traits), cards));

        static int RunFastVsReference(int games, uint seedBase, IReadOnlyList<CardDef> cards)
        {
            int positions = 0;
            for (int game = 0; game < games; game++)
            {
                var r = new TestRandom(seedBase + game);
                // Resonance のみ・Traits のみ・両方をまんべんなく
                bool res = (game & 1) == 0 || game % 3 == 0;
                bool traits = (game & 1) == 1 || game % 3 == 0;
                var state = NewGame(r, cards, res, traits);
                while (state.Phase == Phase.Playing)
                {
                    var move = r.Pick(GameEngine.LegalMoves(state));
                    var reference = GameEngine.ApplyMove(state, move).State;
                    var fast = GameEngine.ApplyMoveFast(state, move);
                    if (!fast.Equals(reference))
                        Assert.Fail("mismatch at game " + game + "\n  ref:  " + StateHash.Serialize(reference) + "\n  fast: " + StateHash.Serialize(fast));
                    state = reference;
                    positions++;
                }
            }
            return positions;
        }

        [Test]
        public void ApplyMoveAndFastAgreeWithRealCards()
        {
            int positions = RunFastVsReference(12000, 0x6e5a, CardCatalog.All);
            TestContext.Out.WriteLine("positions: " + positions);
            Assert.That(positions, Is.GreaterThan(100000));
        }

        [Test]
        public void ApplyMoveAndFastAgreeWithStressCardsWhereTraitsAreFrequent()
        {
            int positions = RunFastVsReference(8000, 0x57a1, StressCards());
            TestContext.Out.WriteLine("positions: " + positions);
            Assert.That(positions, Is.GreaterThan(70000));
        }

        [Test]
        public void TraitsAndResonanceActuallyHappenInTheStressGames()
        {
            // 性質テストが空振りしていないことの確認: 共鳴で結果が変わる手・Guard・Pierce が実際に出る
            int guards = 0, pierceFlips = 0, resonanceChanged = 0;
            var cards = StressCards();
            for (int game = 0; game < 400; game++)
            {
                var r = new TestRandom(0x9a9a + game);
                var state = NewGame(r, cards, true, true);
                while (state.Phase == Phase.Playing)
                {
                    var move = r.Pick(GameEngine.LegalMoves(state));
                    var res = GameEngine.ApplyMove(state, move);
                    guards += res.Events.OfType<TraitEvent>().Count();
                    var placed = state.GetHand(state.Turn)[move.HandIndex];
                    if (state.Cards[placed].Trait == TraitId.Pierce) pierceFlips += Flips(res.Events).Count;
                    var plain = GameEngine.ApplyMove(state.WithRules(new RuleSet(state.Rules.Open, state.Rules.Random, state.Rules.Same,
                        state.Rules.SameWall, state.Rules.Plus, state.Rules.Combo, state.Rules.Elemental, state.Rules.SuddenDeath, state.Rules.Pivot)), move);
                    if (!Owners(plain.State).SequenceEqual(Owners(res.State))) resonanceChanged++;
                    state = res.State;
                }
            }
            Assert.That(guards, Is.GreaterThan(30));
            Assert.That(pierceFlips, Is.GreaterThan(100));
            Assert.That(resonanceChanged, Is.GreaterThan(200));
        }

        [Test]
        public void ReplaysAreDeterministicAndEventsMatchTheStateChange()
        {
            for (int game = 0; game < 600; game++)
            {
                List<string> Replay()
                {
                    var r = new TestRandom(0xdede + game);
                    var state = NewGame(r, game % 2 == 0 ? CardCatalog.All : StressCards(), true, true);
                    var log = new List<string> { GameEngine.HashState(state) };
                    while (state.Phase == Phase.Playing)
                    {
                        var res = GameEngine.ApplyMove(state, r.Pick(GameEngine.LegalMoves(state)));
                        // イベント列から盤面を再構成して一致を確かめる: flip の to だけが所有者を変える
                        var owners = Owners(state);
                        foreach (var e in res.Events.OfType<FlipEvent>()) owners[e.Cell] = (int)e.To;
                        foreach (var e in res.Events.OfType<PlaceEvent>()) owners[e.Cell] = (int)e.Player;
                        if (res.State.SuddenDeathRound == state.SuddenDeathRound)
                            Assert.That(Owners(res.State), Is.EqualTo(owners));
                        state = res.State;
                        log.Add(GameEngine.HashState(state));
                    }
                    return log;
                }
                Assert.That(Replay(), Is.EqualTo(Replay()));
            }
        }

        [Test]
        public void SearchBoardMatchesApplyMoveFastWithResonanceAndTraits()
        {
            int positions = 0;
            var stress = StressCards();
            for (int g = 0; g < 1200; g++)
            {
                var r = new TestRandom(0xa7a7 + g);
                var cards = g % 2 == 0 ? (IReadOnlyList<CardDef>)CardCatalog.All : stress;
                var state = NewGame(r, cards, (g % 3) != 1, (g % 3) != 0 || g % 2 == 0);
                while (state.Phase == Phase.Playing)
                {
                    AiTests.SearchBoardTests.CheckAllMoves(state);
                    positions++;
                    state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
                }
            }
            Assert.That(positions, Is.GreaterThan(10000));
        }

        [Test]
        public void SearchBoardPlayedIncrementallyKeepsHashAndOwners()
        {
            for (int g = 0; g < 1500; g++)
            {
                var r = new TestRandom(0xc9c9 + g);
                var state = NewGame(r, g % 2 == 0 ? CardCatalog.All : StressCards(), true, true, suddenDeath: false);
                var b = AiTests.AiH.LoadTrue(state);
                while (state.Phase == Phase.Playing)
                {
                    var m = r.Pick(GameEngine.LegalMoves(state));
                    b.Make(SearchBoard.Code(AiTests.AiH.SlotOf(b, (int)state.Turn, m.HandIndex), m.Cell, m.Rotation));
                    state = GameEngine.ApplyMoveFast(state, m);
                    for (int c = 0; c < 9; c++)
                    {
                        var pc = state.Board[c];
                        Assert.That(b.OwnerAt(c), Is.EqualTo(pc.HasValue ? (int)pc.Value.Owner : -1));
                    }
                    Assert.That(b.ComputeHashSlow(), Is.EqualTo(b.Hash));
                }
                var sc = GameEngine.Score(state);
                Assert.That(b.ScoreDiffP0(), Is.EqualTo(sc.P0 - sc.P1));
            }
        }

        [Test]
        public void TheSearchFindsTheTrueValueWithResonanceAndTraits()
        {
            // 終盤を総当たり（参照実装だけのミニマックス）と比べる: SearchBoard を使う完全読みと一致する
            int checkedPositions = 0;
            for (int i = 0; i < 120; i++)
            {
                var r = new TestRandom(0x5c5c + i);
                var state = NewGame(r, i % 2 == 0 ? CardCatalog.All : StressCards(), true, true, suddenDeath: false);
                // 残り 4〜5 手になるまで進める
                while (state.Phase == Phase.Playing && state.Board.Count(pc => pc.HasValue) < 5)
                    state = GameEngine.ApplyMoveFast(state, r.Pick(GameEngine.LegalMoves(state)));
                if (state.Phase != Phase.Playing) continue;
                int expected = AiTests.AiH.BruteForce(state);
                var evals = SearchSession.AnalyzeMoves(state.WithRules(new RuleSet(true, state.Rules.Random, state.Rules.Same, state.Rules.SameWall,
                    state.Rules.Plus, state.Rules.Combo, state.Rules.Elemental, state.Rules.SuddenDeath, state.Rules.Pivot, state.Rules.Resonance,
                    state.Rules.Traits)), state.Turn, 4000000, null, i);
                Assert.That(evals.Max(e => e.Value), Is.EqualTo(expected), StateHash.Serialize(state));
                checkedPositions++;
            }
            Assert.That(checkedPositions, Is.GreaterThan(60));
        }

        [Test]
        public void AnUnusedGuardIsWorthALittleInTheEvaluationOfItsOwner()
        {
            var cards = new[] { MkCard("P", 5, 5, 5, 5), MkCard("Q", 5, 5, 5, 5), MkCard("G", 5, 5, 5, 5, trait: TraitId.Guard) };
            GameState S(int owner, bool used, bool traits) => RT.S(new RuleSet(traits: traits), cards, new BoardSpec { { 4, "G", owner } },
                new Setup { GuardUsedCells = used ? new HashSet<int> { 4 } : new HashSet<int>() });
            int bonus = EvalWeights.Default.GuardBonus;
            Assert.That(bonus, Is.GreaterThan(0).And.LessThan(100), "a little: less than one card");
            // 手番は P0。P0 の未使用の Guard は P0 から見て +bonus、P1 の Guard は −bonus
            Assert.That(AiTests.AiH.LoadTrue(S(0, false, true)).Evaluate() - AiTests.AiH.LoadTrue(S(0, true, true)).Evaluate(), Is.EqualTo(bonus));
            Assert.That(AiTests.AiH.LoadTrue(S(1, false, true)).Evaluate() - AiTests.AiH.LoadTrue(S(1, true, true)).Evaluate(), Is.EqualTo(-bonus));
            // Traits OFF なら評価に入らない
            Assert.That(AiTests.AiH.LoadTrue(S(0, false, false)).Evaluate(), Is.EqualTo(AiTests.AiH.LoadTrue(S(0, true, false)).Evaluate()));
        }

        [Test]
        public void TheCpuDoesNotPeekWithResonanceAndTraits()
        {
            var profiles = new[] { AiProfile.Easy, AiProfile.Normal.WithMaxNodes(6000), AiProfile.Hard.WithMaxNodes(20000) };
            int cases = 0;
            for (int i = 0; i < 300; i++)
            {
                var r = new TestRandom(0xc4ea8 + i);
                var s = AiTests.AiH.RandomPosition(r, r.NextInt(8), open: false, resonance: true, traits: true);
                if (s == null || s.GetHand(s.Turn.Opponent()).Count == 0) continue;
                var other = AiTests.AiH.WithOtherOpponentHand(s, s.Turn, r);
                var p = profiles[i % 3];
                var a = new SearchSession(s, s.Turn, p, i, AiTests.AiH.Pool);
                var b = new SearchSession(other, s.Turn, p, i, AiTests.AiH.Pool);
                Assert.That(b.Run(), Is.EqualTo(a.Run()), p.Name + " peeked at the hidden hand in " + StateHash.Serialize(s));
                Assert.That(b.NodesSearched, Is.EqualTo(a.NodesSearched));
                cases++;
            }
            Assert.That(cases, Is.GreaterThan(200));
        }

        [Test]
        public void TheCpuSearchIsDeterministicWithResonanceAndTraits()
        {
            for (int i = 0; i < 60; i++)
            {
                var r = new TestRandom(0xde7 + i);
                var s = AiTests.AiH.RandomPosition(r, 1 + r.NextInt(5), resonance: true, traits: true);
                if (s == null) continue;
                var p = AiProfile.Hard.WithMaxNodes(20000);
                var m1 = SearchSession.ChooseMove(s, s.Turn, p, i, AiTests.AiH.Pool);
                var m2 = SearchSession.ChooseMove(s, s.Turn, p, i, AiTests.AiH.Pool);
                Assert.That(m2, Is.EqualTo(m1));
                Assert.That(GameEngine.IsLegalMove(s, m1), Is.True);
            }
        }

        [Test]
        public void ARealGameCanBePlayedToTheEndWithEveryDifficulty()
        {
            foreach (var profile in new[] { AiProfile.Easy, AiProfile.Normal.WithMaxNodes(4000), AiProfile.Hard.WithMaxNodes(8000) })
            {
                var r = new TestRandom(0xfeed);
                var state = GameEngine.CreateGame(RandomConfig(r, new RuleSet(open: true, same: true, plus: true, combo: true, pivot: true,
                    resonance: true, traits: true)));
                int seed = 0;
                while (state.Phase == Phase.Playing)
                {
                    var m = SearchSession.ChooseMove(state, state.Turn, profile, seed++, AiTests.AiH.Pool);
                    state = GameEngine.ApplyMove(state, m).State;
                }
                Assert.That(state.Phase, Is.EqualTo(Phase.Ended));
            }
        }
    }
}
