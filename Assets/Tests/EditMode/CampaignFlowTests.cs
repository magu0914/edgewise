using System.Collections.Generic;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.Game.Campaign;
using Edgewise.Game.UI;
using NUnit.Framework;
using CampaignOutcome = Edgewise.Core.Campaign.MatchOutcome;

namespace Edgewise.Tests.EditMode
{
    /// <summary>
    /// 5b-1: 星図巡りの画面の流れ（CampaignFlow）・デッキ編集（DeckEditModel）・報酬（RewardModel）・原作モードの表示・
    /// セーブの読み書きの順序（CampaignSession）。表示に依存しない純粋なロジック。
    /// </summary>
    public class CampaignFlowTests
    {
        static Region Harbor => CampaignData.Regions[0];
        static Region Library => CampaignData.Regions[1];
        static Opponent Mira => Harbor.Opponents[0];
        static Opponent Marlowe => Harbor.Boss;

        static (CampaignFlow flow, MemoryCampaignStore store) NewFlow(string json = null, long seed = 7)
        {
            var store = new MemoryCampaignStore(json);
            var session = new CampaignSession(store, () => seed);
            session.Load();
            return (new CampaignFlow(session), store);
        }

        static string SavedJson(CampaignState s) => CampaignSave.ToJson(s);

        /// <summary>星図から対戦の直前まで進める（デッキはおまかせのまま）。</summary>
        static void ToMatch(CampaignFlow flow, Region region, Opponent opponent)
        {
            Assert.That(flow.OpenRegion(region), Is.True, "open region");
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Region));
            Assert.That(flow.ChooseOpponent(opponent), Is.True, "choose opponent");
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Deck));
            Assert.That(flow.Deck.CanBegin, Is.True, "the first deck is ready");
            Assert.That(flow.ConfirmDeck(), Is.True, "confirm deck");
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.TalkBefore));
            Assert.That(flow.StartMatch(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Match));
        }

        // ------------------------------------------------------------------
        // 画面の流れ
        // ------------------------------------------------------------------

        [Test]
        public void NewPlayerGoesThroughTheIntroThenTheWholeLoopBackToTheMap()
        {
            var (flow, store) = NewFlow();
            var steps = new List<CampaignStep>();
            flow.StepChanged += (_, next) => steps.Add(next);
            Assert.That(flow.Session.HasSave, Is.False);
            Assert.That(flow.Enter(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Intro), "no save → intro");
            Assert.That(flow.FinishIntro(), Is.True);
            Assert.That(flow.State.TotalCards, Is.EqualTo(7));
            Assert.That(flow.OpenRegion(Library), Is.False, "region 2 is locked");

            ToMatch(flow, Harbor, Mira);
            Assert.That(flow.Lines.Select(l => l.Text), Is.EqualTo(Mira.Lines.Intro), "the intro lines before the match");
            Assert.That(flow.MatchConfig().Rules, Is.EqualTo(Harbor.Rules));
            Assert.That(flow.FinishMatch(new CampaignOutcome(MatchResultKind.Win, false)), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Result));
            Assert.That(flow.FinishResult(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.TalkAfter));
            Assert.That(flow.Lines.Select(l => l.Text), Is.EqualTo(Mira.Lines.PlayerWins.Concat(Mira.Lines.GiveCard)));
            Assert.That(flow.FinishTalkAfter(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Reward));
            Assert.That(flow.Reward.Count, Is.EqualTo(1));
            var pick = flow.Pick(0);
            Assert.That(pick.HasValue, Is.True);
            Assert.That(flow.FinishReward(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map));
            Assert.That(flow.State.Count(pick.Value.CardId), Is.EqualTo(pick.Value.CopiesAfter));
            Assert.That(flow.State.Record(Mira.Id).Wins, Is.EqualTo(1));

            Assert.That(steps, Is.EqualTo(new[]
            {
                CampaignStep.Intro, CampaignStep.Map, CampaignStep.Region, CampaignStep.Deck, CampaignStep.TalkBefore, CampaignStep.Match,
                CampaignStep.Result, CampaignStep.TalkAfter, CampaignStep.Reward, CampaignStep.Map,
            }));
            Assert.That(store.Writes, Is.EqualTo(4), "intro, deck, match, reward");
        }

        [Test]
        public void BackGoesDeckToRegionToMapToTitle()
        {
            var (flow, _) = NewFlow(SavedJson(CampaignRules.NewGame(3)));
            Assert.That(flow.Enter(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map), "a saved game skips the intro");
            flow.OpenRegion(Harbor);
            flow.ChooseOpponent(Mira);
            Assert.That(flow.Back(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Region));
            Assert.That(flow.Back(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map));
            Assert.That(flow.Back(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Closed));
            Assert.That(flow.StartMatch(), Is.False, "operations of other steps are ignored");
        }

        [Test]
        public void ADrawGoesStraightBackToTheMapWithoutReward()
        {
            var (flow, _) = NewFlow(SavedJson(CampaignRules.NewGame(3)));
            flow.Enter();
            ToMatch(flow, Harbor, Mira);
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Draw, false));
            flow.FinishResult();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map));
            Assert.That(flow.State.Record(Mira.Id).Draws, Is.EqualTo(1));
            Assert.That(flow.State.TotalCards, Is.EqualTo(7));
        }

        [Test]
        public void BeatingTheBossForTheFirstTimeClearsTheRegionAndUnlocksTheNext()
        {
            var (flow, store) = NewFlow(SavedJson(CampaignRules.NewGame(3)));
            flow.Enter();
            ToMatch(flow, Harbor, Marlowe);
            Assert.That(flow.MatchRules.SuddenDeath, Is.True, "the boss match adds Sudden Death");
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Win, false));
            Assert.That(flow.State.IsUnlocked(Library.Id), Is.True, "unlocked (and saved) right after the match");
            Assert.That(CampaignSave.Load(store.Json).State.IsUnlocked(Library.Id), Is.True);
            flow.FinishResult();
            flow.FinishTalkAfter();
            flow.Pick(2);
            Assert.That(flow.FinishReward(), Is.True);
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.TalkClear));
            Assert.That(flow.Lines.Select(l => l.Text), Is.EqualTo(Marlowe.Lines.RegionClear));
            flow.FinishClearTalk();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Cleared));
            Assert.That(flow.ClearedRegion, Is.SameAs(Harbor));
            Assert.That(CampaignTexts.Cleared(Harbor), Is.EqualTo("灯の港 を巡り終えた"));
            flow.FinishCleared();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map));
            Assert.That(flow.ConsumePendingUnlock(), Is.SameAs(Library), "the map plays the unlock once");
            Assert.That(flow.ConsumePendingUnlock(), Is.Null);
            Assert.That(flow.OpenRegion(Library), Is.True);

            // 2 回目の勝利では帯も解放も出ない
            flow.Back();
            ToMatch(flow, Harbor, Marlowe);
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Win, false));
            flow.FinishResult();
            flow.FinishTalkAfter();
            flow.Pick(0);
            flow.FinishReward();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map));
            Assert.That(flow.ConsumePendingUnlock(), Is.Null);
        }

        [Test]
        public void ResigningCountsAsALoss()
        {
            var (flow, _) = NewFlow(SavedJson(CampaignRules.NewGame(3)));
            flow.Enter();
            ToMatch(flow, Harbor, Mira);
            Assert.That(flow.FinishMatch(null, true), Is.True);
            Assert.That(flow.Outcome.Kind, Is.EqualTo(MatchResultKind.Loss));
            flow.FinishResult();
            Assert.That(flow.Lines.Select(l => l.Text), Is.EqualTo(Mira.Lines.PlayerLoses));
            flow.FinishTalkAfter();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map), "no classic mode → nothing is taken");
            Assert.That(flow.State.Record(Mira.Id).Losses, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // デッキ編集
        // ------------------------------------------------------------------

        static Dictionary<string, int> Collection(params string[] ids)
        {
            var d = new Dictionary<string, int>();
            foreach (var id in ids) d[id] = (d.TryGetValue(id, out int n) ? n : 0) + 1;
            return d;
        }

        static IEnumerable<string> IdsOfLevel(int min, int max) => CardCatalog.All.Where(c => c.Level >= min && c.Level <= max).Select(c => c.Id);

        [Test]
        public void DeckGoesRedOverTheCapAndCannotBegin()
        {
            var high = IdsOfLevel(4, 6).Take(6).ToArray();
            var model = new DeckEditModel(Collection(high), 15, Harbor.Rules, new string[0]);
            // おまかせで組めないほど強い手持ちしかない（Lv4 × 5 = 20 > 15）→ 最初は空
            Assert.That(model.Selected.Count, Is.EqualTo(0));
            for (int i = 0; i < 4; i++) Assert.That(model.Toggle(high[i]), Is.EqualTo(DeckToggle.Added));
            Assert.That(model.OverCap, Is.True, "over the cap (red)");
            Assert.That(model.CanBegin, Is.False, "cannot begin");
            Assert.That(model.Issues.Any(i => i.Problem == DeckProblem.OverLevelCap), Is.True);
            Assert.That(CampaignTexts.DeckStatus(model), Is.EqualTo(UiText.DeckOver));
            Assert.That(CampaignTexts.DeckTotal(model.TotalLevel, 15), Does.StartWith("合計 Lv ").And.EndWith(" / 15"));
            Assert.That(model.Fits(high[5]), Is.False, "cards that do not fit the remaining budget are dimmed");
            Assert.That(model.Fits(high[0]), Is.True, "cards in the deck are never dimmed");
            Assert.That(model.Toggle(high[4]), Is.EqualTo(DeckToggle.Added));
            Assert.That(model.Toggle(high[5]), Is.EqualTo(DeckToggle.Full), "the 6th card does not go in");
            Assert.That(model.Selected.Count, Is.EqualTo(5));
            Assert.That(model.Toggle(high[0]), Is.EqualTo(DeckToggle.Removed));
            Assert.That(model.Toggle("no-such-card"), Is.EqualTo(DeckToggle.NotOwned));
        }

        [Test]
        public void AutoBuildsTheBestDeckWithinTheCap()
        {
            var ids = IdsOfLevel(1, 4).ToArray();
            var col = Collection(ids);
            var model = new DeckEditModel(col, 15, Harbor.Rules, new[] { ids[ids.Length - 1], ids[ids.Length - 2], ids[ids.Length - 3], ids[ids.Length - 4], ids[ids.Length - 5] });
            Assert.That(model.OverCap, Is.True, "five Lv4 cards (20) are over 15");
            Assert.That(model.Auto(), Is.True);
            Assert.That(model.Selected, Is.EqualTo(CampaignRules.AutoDeck(col, Harbor.Rules, 15)));
            Assert.That(model.TotalLevel, Is.LessThanOrEqualTo(15));
            Assert.That(model.CanBegin, Is.True);
            Assert.That(CampaignTexts.DeckStatus(model), Is.EqualTo(UiText.DeckReady));
        }

        [Test]
        public void DeckStartsFromTheLastDeckOfTheRegionAndListsByLevelWithFilters()
        {
            var state = CampaignRules.NewGame(1);
            var last = CampaignData.StarterCards.Skip(2).Take(5).ToArray();
            state = state.WithLastDeck(Harbor.Id, last);
            var model = DeckEditModel.For(state, Harbor);
            Assert.That(model.Selected, Is.EqualTo(last));
            var list = model.Listing();
            Assert.That(list.Count, Is.EqualTo(7));
            for (int i = 1; i < list.Count; i++) Assert.That(list[i].Level, Is.GreaterThanOrEqualTo(list[i - 1].Level), "sorted by level");
            var tribe = list.First(c => c.Tribe.HasValue).Tribe;
            model.TribeFilter = tribe;
            Assert.That(model.Listing().All(c => c.Tribe == tribe), Is.True);
            model.TribeFilter = null;
            var element = list.First(c => c.Element.HasValue).Element;
            model.ElementFilter = element;
            Assert.That(model.Listing().All(c => c.Element == element), Is.True);
            // 今の手持ちにないカードは最後のデッキから外す
            var lost = new DeckEditModel(Collection(last.Skip(1).ToArray()), 15, Harbor.Rules, last);
            Assert.That(lost.Selected, Is.EqualTo(last.Skip(1)));
        }

        // ------------------------------------------------------------------
        // 報酬
        // ------------------------------------------------------------------

        [Test]
        public void APerfectWinLetsYouPickTwoCards()
        {
            var (flow, _) = NewFlow(SavedJson(CampaignRules.NewGame(3)));
            flow.Enter();
            ToMatch(flow, Harbor, Mira);
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Win, true));
            Assert.That(flow.Outcome.RewardCount, Is.EqualTo(2));
            flow.FinishResult();
            flow.FinishTalkAfter();
            var reward = flow.Reward;
            Assert.That(reward.Count, Is.EqualTo(2), "perfect → 2 picks");
            Assert.That(reward.Cards, Is.EqualTo(flow.OpponentDeck));
            var a = flow.Pick(1);
            Assert.That(a.HasValue, Is.True);
            Assert.That(flow.Pick(1).HasValue, Is.False, "the same card twice");
            Assert.That(flow.FinishReward(), Is.False, "one more to pick");
            var b = flow.Pick(3);
            Assert.That(b.HasValue, Is.True);
            Assert.That(flow.Pick(4).HasValue, Is.False, "no more than two");
            Assert.That(flow.FinishReward(), Is.True);
            Assert.That(flow.State.TotalCards, Is.EqualTo(9));
            Assert.That(flow.State.Count(a.Value.CardId), Is.GreaterThanOrEqualTo(1));
            Assert.That(flow.State.Count(b.Value.CardId), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void RevealSaysNewOnlyTheFirstTime()
        {
            var deck = new[] { "glimmouse", "bolthare", "frostnewt", "rivetcrab", "lynxlight" };
            var model = new RewardModel(deck, 2, Collection("glimmouse", "glimmouse"));
            var dup = model.Pick(0).Value;
            Assert.That(dup.IsNew, Is.False);
            Assert.That(dup.CopiesAfter, Is.EqualTo(3), "×3");
            var fresh = model.Pick(1).Value;
            Assert.That(fresh.IsNew, Is.True, "NEW!");
            Assert.That(fresh.CopiesAfter, Is.EqualTo(1));
            Assert.That(model.IsDone, Is.True);
        }

        // ------------------------------------------------------------------
        // 原作モード
        // ------------------------------------------------------------------

        static CampaignState RichClassicState()
        {
            var col = Collection(CampaignData.StarterCards.Concat(new[] { "lynxlight", "velahound" }).ToArray());
            return CampaignRules.NewGame(5, true).WithCollection(col);
        }

        [Test]
        public void ClassicModeLossShowsTheTakenCardAndMarksItOnTheOpponent()
        {
            var (flow, _) = NewFlow(SavedJson(RichClassicState()));
            flow.Enter();
            ToMatch(flow, Harbor, Mira);
            var deckBefore = flow.PlayerDeck.ToArray();
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Loss, false));
            var app = flow.Application;
            Assert.That(app.TakenCard, Is.Not.Null, "classic mode takes a card");
            Assert.That(deckBefore, Does.Contain(app.TakenCard));
            Assert.That(CampaignTexts.ClassicTakeMessage(app), Is.EqualTo(CampaignTexts.CardName(app.TakenCard) + " を取られた"));
            flow.FinishResult();
            Assert.That(flow.Lines.Select(l => l.Text), Is.EqualTo(Mira.Lines.PlayerLoses.Concat(Mira.Lines.TakeCard)), "the take line follows");
            flow.FinishTalkAfter();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.ClassicTake));
            flow.FinishClassicTake();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Map));
            // 相手の欄では「取」の印（勝てば取り返せる）
            var slots = CampaignTexts.OpponentCards(flow.State, Mira);
            var taken = slots.Single(s => s.CardId == app.TakenCard && s.Captured);
            Assert.That(taken.Seen, Is.True);
        }

        [Test]
        public void ClassicModeProtectsTheLastFiveKinds()
        {
            var (flow, _) = NewFlow(SavedJson(CampaignRules.NewGame(5, true).WithCollection(Collection(CampaignData.StarterCards.Take(5).ToArray()))));
            flow.Enter();
            ToMatch(flow, Harbor, Mira);
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Loss, false));
            var app = flow.Application;
            Assert.That(app.TakenCard, Is.Null);
            Assert.That(app.ProtectedCard, Is.Not.Null);
            Assert.That(CampaignTexts.ClassicTakeMessage(app), Is.EqualTo("手持ちが少ないので、" + CampaignTexts.CardName(app.ProtectedCard) + " は取られなかった"));
            flow.FinishResult();
            Assert.That(flow.Lines.Select(l => l.Text), Is.EqualTo(Mira.Lines.PlayerLoses), "no take line when protected");
            flow.FinishTalkAfter();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.ClassicTake), "the protected card is shown too");
        }

        [Test]
        public void ClassicModeOffNeverShowsTheTakeMessage()
        {
            var (flow, _) = NewFlow(SavedJson(CampaignRules.NewGame(5, false).WithCollection(Collection(CampaignData.StarterCards.Concat(new[] { "lynxlight" }).ToArray()))));
            flow.Enter();
            ToMatch(flow, Harbor, Mira);
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Loss, false));
            Assert.That(CampaignTexts.ClassicTakeMessage(flow.Application), Is.Null);
        }

        // ------------------------------------------------------------------
        // セーブの読み書きの順序
        // ------------------------------------------------------------------

        [Test]
        public void SaveIsReadOnceThenWrittenAfterIntroDeckMatchAndReward()
        {
            var (flow, store) = NewFlow();
            Assert.That(store.Calls, Is.EqualTo(new[] { "read" }));
            flow.Enter();
            Assert.That(store.Calls.Count, Is.EqualTo(1), "entering does not write");
            flow.FinishIntro();
            Assert.That(store.Calls, Is.EqualTo(new[] { "read", "write" }), "after the intro");
            flow.OpenRegion(Harbor);
            flow.ChooseOpponent(Mira);
            Assert.That(store.Writes, Is.EqualTo(1), "opening screens does not write");
            flow.ConfirmDeck();
            Assert.That(store.Writes, Is.EqualTo(2), "after deck edit");
            Assert.That(CampaignSave.Load(store.Json).State.LastDecks[Harbor.Id], Is.EqualTo(flow.PlayerDeck));
            flow.StartMatch();
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Win, false));
            Assert.That(store.Writes, Is.EqualTo(3), "after the match (before the reward)");
            var afterMatch = CampaignSave.Load(store.Json).State;
            Assert.That(afterMatch.Record(Mira.Id).Wins, Is.EqualTo(1));
            Assert.That(afterMatch.TotalCards, Is.EqualTo(7), "the reward is not in yet");
            flow.FinishResult();
            flow.FinishTalkAfter();
            var pick = flow.Pick(4).Value;
            Assert.That(store.Writes, Is.EqualTo(3), "picking alone does not write");
            flow.FinishReward();
            Assert.That(store.Calls, Is.EqualTo(new[] { "read", "write", "write", "write", "write" }));
            var final = CampaignSave.Load(store.Json).State;
            Assert.That(final.Count(pick.CardId), Is.EqualTo(pick.CopiesAfter), "after the reward");
            Assert.That(final.Record(Mira.Id).Wins, Is.EqualTo(1), "the win is counted once");
        }

        [Test]
        public void CorruptSaveIsBackedUpBeforeItIsOverwritten()
        {
            var (flow, store) = NewFlow("{not json");
            Assert.That(flow.Session.LoadStatus, Is.EqualTo(SaveLoadStatus.Corrupt));
            Assert.That(store.Calls, Is.EqualTo(new[] { "read", "backup" }));
            Assert.That(store.Backup, Is.EqualTo("{not json"));
            Assert.That(flow.Session.HasSave, Is.False);
            flow.Enter();
            Assert.That(flow.Step, Is.EqualTo(CampaignStep.Intro));
            flow.FinishIntro();
            Assert.That(store.Calls, Is.EqualTo(new[] { "read", "backup", "write" }));
        }

        [Test]
        public void TooNewSaveIsNeverOverwritten()
        {
            const string future = "{\"version\":99,\"collection\":{}}";
            var (flow, store) = NewFlow(future);
            Assert.That(flow.Session.LoadStatus, Is.EqualTo(SaveLoadStatus.TooNew));
            Assert.That(flow.Session.ReadOnly, Is.True);
            flow.Enter();
            flow.FinishIntro();
            ToMatch(flow, Harbor, Mira);
            flow.FinishMatch(new CampaignOutcome(MatchResultKind.Win, false));
            Assert.That(store.Writes, Is.EqualTo(0), "the newer save is kept");
            Assert.That(store.Json, Is.EqualTo(future));
            Assert.That(flow.State.Record(Mira.Id).Wins, Is.EqualTo(1), "the game still goes on in memory");
        }

        [Test]
        public void EraseDeletesTheSaveAndClassicModeIsKeptUntilTheNewGame()
        {
            var (flow, store) = NewFlow(SavedJson(CampaignRules.NewGame(3)));
            var session = flow.Session;
            Assert.That(session.HasSave, Is.True);
            session.SetClassicMode(true);
            Assert.That(CampaignSave.Load(store.Json).State.ClassicMode, Is.True, "toggling saves when there is a save");
            session.Erase();
            Assert.That(store.Json, Is.Null);
            Assert.That(session.HasSave, Is.False);
            session.SetClassicMode(true);
            Assert.That(store.Writes, Is.EqualTo(1), "no save before starting");
            flow.Enter();
            flow.FinishIntro();
            Assert.That(CampaignSave.Load(store.Json).State.ClassicMode, Is.True, "the new game keeps the setting");
        }

        // ------------------------------------------------------------------
        // 表示用の文字
        // ------------------------------------------------------------------

        [Test]
        public void DisplayTextsFollowTheDesign()
        {
            var s = CampaignRules.NewGame(1);
            Assert.That(CampaignTexts.TitleProgress(s), Is.EqualTo("星域 1・収集 7/50"));
            s = s.WithUnlocked(new[] { Harbor.Id, Library.Id });
            Assert.That(CampaignTexts.TitleProgress(s), Does.StartWith("星域 2・"));
            Assert.That(CampaignTexts.RegionProgress(s, Harbor), Is.EqualTo("○○○"));
            s = s.WithRecord(Mira.Id, new OpponentRecord(1, 0, 0, null, null));
            Assert.That(CampaignTexts.RegionProgress(s, Harbor), Is.EqualTo("●○○"));
            Assert.That(CampaignTexts.Record(s.Record(Mira.Id)), Is.EqualTo("1勝 0敗 0分"));
            // 強さは ★1〜5、順に強くなる
            var stars = CampaignData.Opponents.Select(CampaignTexts.Stars).ToArray();
            Assert.That(stars.All(n => n >= 1 && n <= 5), Is.True);
            Assert.That(CampaignTexts.Stars(Mira), Is.EqualTo(1));
            Assert.That(CampaignTexts.Stars(CampaignData.Regions[3].Boss), Is.EqualTo(5));
            Assert.That(CampaignTexts.StarText(2), Is.EqualTo("★★☆☆☆"));
            // ルール: 星域 1 は Open と基本、星域 4 は Open OFF から 1 行ずつ
            Assert.That(CampaignTexts.RuleLines(Harbor.Rules).Select(kv => kv.Key), Is.EqualTo(new[] { "Open", "基本" }));
            var gate = CampaignTexts.RuleLines(CampaignData.Regions[3].Rules).Select(kv => kv.Key).ToArray();
            Assert.That(gate[0], Is.EqualTo("Open OFF"));
            Assert.That(gate, Does.Contain("Pivot").And.Contain("Sudden Death"));
            Assert.That(CampaignTexts.RuleChips(Library.Rules), Is.EqualTo(new[] { "Open", "Same", "Wall", "Plus" }));
            Assert.That(CampaignTexts.Initial(Mira), Is.EqualTo("ミ"));
            // 星域の画面の持ちカード: 未見は「？」（null）、見たカードと手持ちの印
            var slots = CampaignTexts.OpponentCards(s, Mira);
            Assert.That(slots.Count, Is.EqualTo(Mira.Cards.Count));
            Assert.That(slots.All(x => !x.Seen), Is.True);
            s = s.WithRecord(Mira.Id, new OpponentRecord(1, 0, 0, null, new[] { "rivetcrab", "bolthare" }));
            slots = CampaignTexts.OpponentCards(s, Mira);
            Assert.That(slots.Single(x => x.CardId == "rivetcrab").Owned, Is.True, "a starter card");
            Assert.That(slots.Single(x => x.CardId == "bolthare").Owned, Is.False);
            Assert.That(slots.Count(x => x.Seen), Is.EqualTo(2));
        }

        [Test]
        public void HintPoolUsesTheOpponentsCardsOnlyWhenAllAreSeen()
        {
            var s = CampaignRules.NewGame(1);
            Assert.That(CampaignTexts.HintPool(s, Mira), Is.SameAs(CardCatalog.All));
            s = s.WithRecord(Mira.Id, new OpponentRecord(1, 0, 0, null, Mira.Cards));
            var pool = CampaignTexts.HintPool(s, Mira);
            Assert.That(pool.Select(c => c.Id), Is.EquivalentTo(Mira.Cards));
        }

        [Test]
        public void TypewriterShowsOneCharacterAtATimeAndCompletesOnTap()
        {
            var t = new Typewriter { CharsPerSecond = 10f };
            t.Reset("はじめまして");
            Assert.That(t.Visible, Is.EqualTo(0));
            t.Advance(0.25f);
            Assert.That(t.Visible, Is.EqualTo(2));
            Assert.That(t.IsComplete, Is.False);
            t.Complete();
            Assert.That(t.IsComplete, Is.True);
            t.Reset("abc");
            t.Advance(0.01f, 0f);
            Assert.That(t.IsComplete, Is.True, "instant speed shows the whole line");
        }

        [Test]
        public void StarMapPathRunsLeftToRightInLandscapeAndTopToBottomInPortrait()
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.That(StarMapScreen.NodePosition(i + 1, false).x, Is.GreaterThan(StarMapScreen.NodePosition(i, false).x));
                Assert.That(StarMapScreen.NodePosition(i + 1, true).y, Is.LessThan(StarMapScreen.NodePosition(i, true).y));
            }
        }
    }
}
