using System;
using System.Collections;
using System.Linq;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Game.App;
using Edgewise.Game.Campaign;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CampaignOutcome = Edgewise.Core.Campaign.MatchOutcome;

namespace Edgewise.Tests.PlayMode
{
    /// <summary>
    /// 5b-1: 星図巡りの通し。セーブの置き場はメモリ（MemoryCampaignStore）に差し替える（エディタの PlayerPrefs を汚さない）。
    /// 1. 新しく始める → 導入 → 星域 1 の最初の相手にデッキを組んで挑む → 即時速度で勝つ（入力を注入）→ 報酬 → 星図 → セーブが更新されている
    /// 2. 主に勝つ（勝敗を注入）→ 帯 → 星図で星域 2 が開く
    /// 3. 設定でデータを消す（2 段階の確認）
    /// </summary>
    public class CampaignPlayTests
    {
        const float Timeout = 240f;

        AppFlow _app;
        CampaignController _camp;
        MemoryCampaignStore _store;

        IEnumerator Load(string json)
        {
            _store = new MemoryCampaignStore(json);
            CampaignStores.Override = _store;
            CampaignController.TestSeed = 20261016;
            UserPrefs.SpeedOverride = PlaybackSpeed.Instant;
            UserPrefs.DebugSlowFactor = 1f;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = UnityEngine.Object.FindAnyObjectByType<AppFlow>();
            Assert.That(_app, Is.Not.Null);
            _camp = _app.Campaign;
            Assert.That(_camp, Is.Not.Null, "CampaignController must be in Main.unity");
        }

        [TearDown]
        public void Reset()
        {
            CampaignStores.Override = null;
            CampaignController.TestSeed = 0;
            UserPrefs.SpeedOverride = null;
        }

        IEnumerator WaitUntil(Func<bool> cond, string what, float timeout = 60f)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(cond(), Is.True, "timed out waiting for: " + what + " (step " + _camp.Step + ")");
        }

        /// <summary>星図 → 星域 → 相手 → デッキ（おまかせ）→ 挑む → 登場の会話をスキップ → 対戦。</summary>
        IEnumerator ChallengeFromMap(int region, int opponent)
        {
            yield return WaitUntil(() => _camp.Step == CampaignStep.Map && !_camp.Busy, "map");
            _camp.Map.Node(region).onClick.Invoke();
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.Region));
            Assert.That(_camp.Region.IsOpen, Is.True);
            _camp.Region.ChallengeButton(opponent).onClick.Invoke();
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.Deck));
            Assert.That(_camp.Deck.IsOpen, Is.True);
            _camp.Deck.Auto();
            Assert.That(_camp.Deck.Model.CanBegin, Is.True);
            Assert.That(_camp.Deck.BeginButton.interactable, Is.True);
            _camp.Deck.BeginButton.onClick.Invoke();
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.TalkBefore));
            yield return WaitUntil(() => _camp.Dialogue.IsOpen, "intro dialogue");
            Assert.That(_camp.Dialogue.SpeakerName, Does.Contain(_camp.Flow.Opponent.Name));
            Assert.That(_camp.Dialogue.Portrait.ArtId, Is.EqualTo("@" + _camp.Flow.Opponent.Id), "the opponent's portrait");
            Assert.That(_camp.Dialogue.BigPortrait.ArtId, Is.EqualTo("@" + _camp.Flow.Opponent.Id), "the big portrait above the band");
            _camp.Dialogue.Skip();
            yield return WaitUntil(() => _camp.Step == CampaignStep.Match, "match");
        }

        /// <summary>プレイヤーの手番にヒント（Hard の解析）の手を注入して、結果画面まで指す。</summary>
        IEnumerator PlayWithHints()
        {
            var pc = _app.Controls;
            var hint = new HintEngine();
            float t = 0f;
            bool checkedHud = false;
            while (!_camp.ResultWaiting && t < Timeout)
            {
                t += Time.unscaledDeltaTime;
                if (pc.IsAwaitingMove)
                {
                    if (!checkedHud)
                    {
                        // 対戦の上部に相手の名前と小さな立ち絵
                        Assert.That(_app.Hud.OpponentPortraitShown, Is.True);
                        checkedHud = true;
                    }
                    var state = pc.State;
                    hint.Begin(state, Player.P0, 250000, 99, Edgewise.Core.Data.CardCatalog.All, Edgewise.Core.AI.CardPoolModel.Shared);
                    var move = hint.RunToEnd().Move;
                    Assert.That(pc.Pick(move.HandIndex), Is.True);
                    for (int i = 0; i < move.Rotation; i++) pc.Rotate(+1);
                    pc.Hover(move.Cell);
                    Assert.That(pc.Place(move.Cell), Is.True);
                }
                yield return null;
            }
            Assert.That(_camp.ResultWaiting, Is.True, "the result screen with 次へ");
        }

        [UnityTest]
        public IEnumerator NewGameBeatsTheFirstOpponentAndSavesTheReward()
        {
            yield return Load(null);
            Assert.That(_app.Title.ProgressText, Is.EqualTo(""), "no progress before the first game");
            _app.Title.CampaignButton.onClick.Invoke();
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.Intro));

            // 導入: はじめの 7 枚をめくって受け取る
            yield return WaitUntil(() => _camp.Reward.WaitingForPick, "intro cards");
            Assert.That(_camp.Reward.Stage.Count, Is.EqualTo(7));
            _camp.Reward.RequestPick(3);
            yield return WaitUntil(() => !_camp.Reward.Stage.Card(3).FaceDown, "one card flipped");
            _camp.Reward.RevealAll();
            yield return WaitUntil(() => _camp.Reward.WaitingForNext, "intro done");
            Assert.That(_camp.Reward.HandCount, Is.EqualTo(7));
            _camp.Reward.Continue();
            yield return WaitUntil(() => _camp.Step == CampaignStep.Map, "map after intro");
            Assert.That(_store.Writes, Is.EqualTo(1), "saved after the intro");
            Assert.That(_camp.Map.NodeLooksUnlocked(0), Is.True);
            Assert.That(_camp.Map.NodeLooksUnlocked(1), Is.False, "region 2 is locked");

            var mira = CampaignData.Regions[0].Opponents[0];
            int attempts = 0;
            while (true)
            {
                attempts++;
                yield return ChallengeFromMap(0, 0);
                Assert.That(_camp.Flow.Opponent, Is.SameAs(mira));
                Assert.That(_store.Writes, Is.EqualTo(attempts * 2), "saved after the deck edit");
                yield return PlayWithHints();
                Assert.That(_store.Writes, Is.EqualTo(attempts * 2 + 1), "saved after the match");
                var afterMatch = CampaignSave.Load(_store.Json).State;
                Assert.That(afterMatch.Record(mira.Id).Games, Is.EqualTo(attempts));
                if (_camp.Flow.Outcome.Kind == MatchResultKind.Win) break;
                // 引き分け・負け: 星図に戻ってもう一度
                Assert.That(attempts, Is.LessThan(4), "could not beat Mira in 4 games");
                _camp.PressResultNext();
                yield return WaitUntil(() => _camp.Step == CampaignStep.Map || _camp.Dialogue.IsOpen, "after a non-win");
                if (_camp.Dialogue.IsOpen) _camp.Dialogue.Skip();
            }
            Assert.That(CampaignSave.Load(_store.Json).State.TotalCards, Is.EqualTo(7), "the reward is not in before picking");

            // 結果 → 会話 → 報酬
            _camp.PressResultNext();
            yield return WaitUntil(() => _camp.Dialogue.IsOpen, "after-match dialogue");
            _camp.Dialogue.Skip();
            yield return WaitUntil(() => _camp.Reward.WaitingForPick, "reward cards");
            Assert.That(_camp.Reward.Stage.Count, Is.EqualTo(5));
            Assert.That(_camp.Reward.Stage.Card(0).FaceDown, Is.True, "the reward cards start face down");
            int picks = _camp.Flow.Reward.Count;
            for (int i = 0; i < picks; i++)
            {
                yield return WaitUntil(() => _camp.Reward.WaitingForPick, "pick " + i);
                _camp.Reward.RequestPick(i);
                yield return WaitUntil(() => _camp.Reward.LastPick.HasValue && _camp.Reward.LastPick.Value.Index == i, "reveal " + i);
            }
            yield return WaitUntil(() => _camp.Reward.WaitingForNext, "reward done");
            var picked = _camp.Flow.Reward.Picks.ToArray();
            Assert.That(_camp.Reward.HandCount, Is.EqualTo(7 + picks));
            _camp.Reward.Continue();
            yield return WaitUntil(() => _camp.Step == CampaignStep.Map, "back to the map");

            // セーブが更新されている（報酬の後に保存）
            Assert.That(_store.Writes, Is.EqualTo(attempts * 2 + 2), "intro + (deck, match) per game + reward");
            var saved = CampaignSave.Load(_store.Json);
            Assert.That(saved.Status, Is.EqualTo(SaveLoadStatus.Ok));
            Assert.That(saved.State.Record(mira.Id).Wins, Is.EqualTo(1));
            foreach (var id in picked) Assert.That(saved.State.Count(id), Is.GreaterThanOrEqualTo(1), id);
            Assert.That(saved.State.TotalCards, Is.EqualTo(7 + picks));
            Assert.That(saved.State.LastDecks.ContainsKey("harbor"), Is.True);
            Assert.That(_camp.Map.NodeProgress(0), Is.EqualTo("●○○"));

            // タイトルに戻ると進み具合が出る
            _camp.Map.BackButton.onClick.Invoke();
            Assert.That(_app.Title.gameObject.activeSelf, Is.True);
            Assert.That(_app.Title.ProgressText, Does.StartWith("星域 1・収集 "));
        }

        [UnityTest]
        public IEnumerator BeatingTheBossOpensRegionTwo()
        {
            yield return Load(CampaignSave.ToJson(CampaignRules.NewGame(11)));
            _camp.MatchOverride = f => new CampaignOutcome(MatchResultKind.Win, false);
            _app.Title.CampaignButton.onClick.Invoke();
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.Map), "a saved game skips the intro");
            yield return ChallengeFromMap(0, 2);
            Assert.That(_camp.Flow.Opponent.IsBoss, Is.True);
            Assert.That(_camp.Region.NoteText(2), Does.Contain("Sudden Death"));
            yield return WaitUntil(() => _camp.Dialogue.IsOpen, "after-match dialogue");
            Assert.That(CampaignSave.Load(_store.Json).State.IsUnlocked("library"), Is.True, "unlocked and saved right after the match");
            _camp.Dialogue.Skip();
            yield return WaitUntil(() => _camp.Reward.WaitingForPick, "reward");
            _camp.Reward.RequestPick(0);
            yield return WaitUntil(() => _camp.Reward.WaitingForNext, "reward done");
            _camp.Reward.Continue();
            yield return WaitUntil(() => _camp.Step == CampaignStep.TalkClear && _camp.Dialogue.IsOpen, "region clear dialogue");
            _camp.Dialogue.Skip();
            yield return WaitUntil(() => _camp.Step == CampaignStep.Cleared, "cleared band");
            Assert.That(_camp.Cleared.Text, Is.EqualTo("灯の港 を巡り終えた"));
            yield return WaitUntil(() => _camp.Step == CampaignStep.Map && !_camp.Busy && !_camp.Map.IsAnimating, "map after the unlock");
            Assert.That(_camp.Map.NodeLooksUnlocked(1), Is.True, "the lock is gone");
            Assert.That(_camp.Map.NodeLooksUnlocked(2), Is.False);
            var saved = CampaignSave.Load(_store.Json).State;
            Assert.That(saved.IsUnlocked("library"), Is.True);
            Assert.That(saved.Record("harbor-master").Wins, Is.EqualTo(1));
            _camp.Map.Node(1).onClick.Invoke();
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.Region));
            Assert.That(_camp.Region.Region.Id, Is.EqualTo("library"));
            Assert.That(_camp.Region.CapText, Does.Contain("25"));
        }

        /// <summary>原作モードで負ける（勝敗を注入）: 会話 → 取られる演出 → 星図。手持ちから 1 枚減り、相手の欄に「取」の印。</summary>
        [UnityTest]
        public IEnumerator ClassicModeLossTakesACardWithAShortScene()
        {
            var col = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var id in CampaignData.StarterCards.Concat(new[] { "lynxlight", "velahound" })) col[id] = 1;
            var start = CampaignRules.NewGame(3, true).WithCollection(col);
            yield return Load(CampaignSave.ToJson(start));
            _camp.MatchOverride = f => new CampaignOutcome(MatchResultKind.Loss, false);
            _app.Title.CampaignButton.onClick.Invoke();
            yield return ChallengeFromMap(0, 0);
            yield return WaitUntil(() => _camp.Dialogue.IsOpen, "after-match dialogue");
            var taken = _camp.Flow.Application.TakenCard;
            Assert.That(taken, Is.Not.Null);
            _camp.Dialogue.Skip();
            yield return WaitUntil(() => _camp.Step == CampaignStep.ClassicTake && _camp.Reward.WaitingForNext, "take scene");
            Assert.That(_camp.Reward.SubText, Does.Contain(CampaignTexts.CardName(taken) + " を取られた"));
            Assert.That(_camp.Reward.Stage.Card(_camp.Flow.PlayerDeck.ToList().IndexOf(taken)).gameObject.activeSelf, Is.False, "the card left");
            _camp.Reward.Continue();
            yield return WaitUntil(() => _camp.Step == CampaignStep.Map, "map");
            var saved = CampaignSave.Load(_store.Json).State;
            Assert.That(saved.Count(taken), Is.EqualTo(0));
            Assert.That(saved.TotalCards, Is.EqualTo(8));
            Assert.That(saved.Record("harbor-apprentice").Captured.ContainsKey(taken), Is.True);
            _camp.Map.Node(0).onClick.Invoke();
            int marked = 0;
            for (int i = 0; i < 8; i++)
            {
                var mc = _camp.Region.Card(0, i);
                if (mc.gameObject.activeSelf && mc.CardId == taken && mc.MarkText == "取") marked++;
            }
            Assert.That(marked, Is.EqualTo(1), "the taken card is marked on the opponent");
        }

        /// <summary>5b-2: 図鑑の収集表示（タイトル・星図から）と、フリー対戦から開いたときの全カード表示。</summary>
        [UnityTest]
        public IEnumerator DexShowsTheCollectionAndHidesUnknownCards()
        {
            var col = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var id in CampaignData.StarterCards) col[id] = 1;
            col["glimmouse"] = 2;
            yield return Load(CampaignSave.ToJson(CampaignRules.NewGame(3).WithCollection(col)));
            var all = Edgewise.Core.Data.CardCatalog.All;
            int glim = all.ToList().FindIndex(c => c.Id == "glimmouse");
            int leo = all.ToList().FindIndex(c => c.Id == "leomane");

            // タイトルから: 収集状況（持っている枚数・収集率・未入手は暗いシルエット）
            _app.Title.DexButton.onClick.Invoke();
            yield return null;
            var dex = _app.Dex;
            Assert.That(dex.CollectionView, Is.True);
            Assert.That(dex.CountText, Is.EqualTo("収集 7/50"));
            Assert.That(dex.Cards[glim].Count, Is.EqualTo(2), "owned count badge");
            Assert.That(dex.Cards[glim].Unknown, Is.False);
            Assert.That(dex.Cards[leo].Unknown, Is.True, "unknown cards are dark silhouettes");
            dex.OpenZoom(leo);
            yield return null;
            Assert.That(dex.ZoomCard.Unknown, Is.True);
            Assert.That(dex.InfoName, Is.EqualTo("？？？"), "the name and numbers are hidden");
            dex.BackToList();
            // 「すべて見る」で全カード
            dex.ViewButton.onClick.Invoke();
            Assert.That(dex.CollectionView, Is.False);
            Assert.That(dex.Cards.All(c => !c.Unknown), Is.True);
            Assert.That(dex.CountText, Is.EqualTo("全 50 枚"));
            dex.BackButton.onClick.Invoke();
            yield return null;
            Assert.That(_app.Title.gameObject.activeSelf, Is.True);

            // フリー対戦（対戦設定）から: 今までどおり全カード
            _app.ShowSetup();
            _app.Setup.DexButton.onClick.Invoke();
            yield return null;
            Assert.That(dex.CollectionView, Is.False);
            Assert.That(dex.ViewButton.gameObject.activeSelf, Is.False);
            Assert.That(dex.Cards.All(c => !c.Unknown), Is.True);
            dex.BackButton.onClick.Invoke();
            yield return null;
            Assert.That(_app.Setup.gameObject.activeSelf, Is.True, "back to the match setup");

            // 星図から: 収集状況 → 戻ると星図
            _app.ShowTitle();
            _app.Title.CampaignButton.onClick.Invoke();
            yield return WaitUntil(() => _camp.Step == CampaignStep.Map, "map");
            _camp.Map.DexButton.onClick.Invoke();
            yield return null;
            Assert.That(dex.IsOpen && dex.CollectionView, Is.True);
            dex.BackButton.onClick.Invoke();
            yield return null;
            Assert.That(_camp.Map.IsOpen, Is.True, "back to the map");
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.Map));
        }

        [UnityTest]
        public IEnumerator SettingsEraseTheCampaignDataAfterTwoConfirmations()
        {
            var state = CampaignRules.NewGame(5).WithRecord("harbor-apprentice", new OpponentRecord(2, 1, 0, null, new[] { "bolthare" }));
            yield return Load(CampaignSave.ToJson(state));
            Assert.That(_app.Title.ProgressText, Is.EqualTo("星域 1・収集 7/50"));
            _app.Title.SettingsButton.onClick.Invoke();
            var s = _app.Settings;
            Assert.That(s.IsOpen, Is.True);
            s.TabCampaign.onClick.Invoke();
            Assert.That(s.CampaignTab, Is.True);
            // 原作モードの切り替え（保存される）
            s.ClassicButton.onClick.Invoke();
            Assert.That(CampaignSave.Load(_store.Json).State.ClassicMode, Is.True);
            s.ClassicButton.onClick.Invoke();
            Assert.That(CampaignSave.Load(_store.Json).State.ClassicMode, Is.False);
            // データを消す: 確認 → もう一度確認 → 消える
            Assert.That(s.EraseButton.interactable, Is.True);
            s.EraseButton.onClick.Invoke();
            Assert.That(s.ConfirmStage, Is.EqualTo(1));
            s.ConfirmNo.onClick.Invoke();
            Assert.That(s.ConfirmStage, Is.EqualTo(0), "やめる cancels");
            Assert.That(_store.Json, Is.Not.Null);
            s.EraseButton.onClick.Invoke();
            s.ConfirmYes.onClick.Invoke();
            Assert.That(s.ConfirmStage, Is.EqualTo(2), "the second confirmation");
            Assert.That(_store.Json, Is.Not.Null, "not yet");
            s.ConfirmYes.onClick.Invoke();
            Assert.That(_store.Json, Is.Null);
            Assert.That(_store.Calls, Does.Contain("delete"));
            Assert.That(_camp.Session.HasSave, Is.False);
            Assert.That(s.EraseButton.interactable, Is.False, "nothing left to erase");
            Assert.That(_app.Title.ProgressText, Is.EqualTo(""), "the title no longer shows progress");
            s.Close();
            yield return null;
            _app.Title.CampaignButton.onClick.Invoke();
            Assert.That(_camp.Step, Is.EqualTo(CampaignStep.Intro), "starts over from the intro");
        }
    }
}
