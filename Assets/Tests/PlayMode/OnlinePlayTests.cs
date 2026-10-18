using System.Collections;
using System.Linq;
using Edgewise.Core;
using Edgewise.Game.App;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using Edgewise.Game.Online;
using Edgewise.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Edgewise.Tests.PlayMode
{
    /// <summary>
    /// オンライン対戦（Phase 7）: LoopbackTransport で、ホスト（画面つき。OnlineController 経由）とゲスト（もう 1 つの OnlineMatchSession を
    /// コードで動かす）が 1 局を最後まで通す。再戦ではホストが席 1 になり、写した表示（自分が常に手前）でも盤と局面が一致する。
    /// </summary>
    public class OnlinePlayTests
    {
        const float TimeoutSeconds = 120f;

        const string SettingsKey = "edgewise.matchSettings.v1";

        AppFlow _app;
        LoopbackNetwork _net;
        string _savedSettings;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            _savedSettings = PlayerPrefs.HasKey(SettingsKey) ? PlayerPrefs.GetString(SettingsKey) : null;
            UserPrefs.SpeedOverride = PlaybackSpeed.Instant;
            UserPrefs.DebugSlowFactor = 1f;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = Object.FindAnyObjectByType<AppFlow>();
            Assert.That(_app, Is.Not.Null);
            _net = new LoopbackNetwork();
            _app.Online.TransportFactory = () => new LoopbackTransport(_net);
        }

        [TearDown]
        public void Reset()
        {
            // 「オンライン対戦」を押すと設定が保存される。ほかのテストに持ち越さない
            if (_savedSettings != null) PlayerPrefs.SetString(SettingsKey, _savedSettings);
            else PlayerPrefs.DeleteKey(SettingsKey);
            UserPrefs.SpeedOverride = null;
        }

        static double Now() => Time.realtimeSinceStartupAsDouble;

        /// <summary>ホスト側: 自分の番なら先頭の手を指す（画面の操作と同じ PlayerController 経由）。</summary>
        void HostPlays()
        {
            var pc = _app.Controls;
            if (!pc.IsAwaitingMove) return;
            var moves = GameEngine.LegalMoves(pc.State);
            var m = moves[(_app.Match.MovesPlayed * 5) % moves.Count];
            if (m.Rotation != 0) m = new Move(m.HandIndex, m.Cell, 0);
            Assert.That(pc.Pick(m.HandIndex), Is.True);
            pc.Hover(m.Cell);
            Assert.That(pc.Place(m.Cell), Is.True);
        }

        static void GuestPlays(OnlineMatchSession guest)
        {
            if (!guest.IsMyTurn) return;
            var moves = GameEngine.LegalMoves(guest.State);
            guest.SubmitMove(moves[(guest.MovesApplied * 3) % moves.Count]);
        }

        IEnumerator WaitFor(System.Func<bool> cond, OnlineMatchSession guest, string what, System.Action each = null)
        {
            float t = 0f;
            while (!cond() && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                guest?.Tick();
                each?.Invoke();
                yield return null;
            }
            Assert.That(cond(), Is.True, what);
        }

        [UnityTest]
        public IEnumerator HostAndGuestSessionsPlayTwoGamesAndTheViewFollowsMySeat()
        {
            // 対戦設定（Open OFF・Random なし）→ 「オンライン対戦」→ 「部屋を作る」
            var online = _app.Online;
            _app.ShowSetup();
            _app.Setup.Current.open = false;
            _app.Setup.Current.random = false;
            _app.Setup.Current.band = CardBand.Beginner;
            _app.Setup.OnlineButton.onClick.Invoke();
            Assert.That(online.ScreenOpen, Is.True);
            Assert.That(online.Screen.NetworkNoteText, Does.Contain("つながらない"), "the one-line caveat is shown");
            online.Screen.CreateButton.onClick.Invoke();
            string code = online.CurrentCode;
            Assert.That(RoomCode.IsValid(code), Is.True, code);
            Assert.That(online.Screen.CurrentMode, Is.EqualTo(OnlineScreen.Mode.Room));
            Assert.That(online.Screen.RoomCodeText, Is.EqualTo(code));
            Assert.That(online.Screen.InviteUrl, Does.EndWith("?room=" + code));
            Assert.That(online.Screen.CopyCodeButton.gameObject.activeInHierarchy, Is.True);
            Assert.That(online.Screen.CopyUrlButton.gameObject.activeInHierarchy, Is.True);

            // ゲスト（もう 1 つの端末）が入る
            var guestT = new LoopbackTransport(_net);
            var guest = new OnlineMatchSession(guestT, OnlineRole.Guest, Now);
            guestT.Join(code);
            yield return WaitFor(() => _app.Match.IsOnline && _app.Match.State != null, guest, "the match starts when the guest joins");
            var host = online.Session;
            Assert.That(host.LocalSeat, Is.EqualTo(Player.P0));
            Assert.That(guest.LocalSeat, Is.EqualTo(Player.P1));
            Assert.That(guest.Rules.Open, Is.False, "the guest plays with the host's rules");
            Assert.That(_app.Hud.HintButton.gameObject.activeInHierarchy, Is.False, "no hint online");

            // Open OFF: 相手（席 1）の手札は伏せて表示する
            yield return null;
            Assert.That(_app.Match.Hand(Player.P1).Cards.All(c => c.FaceDown), Is.True, "the opponent's hand is face down");
            Assert.That(_app.Match.Hand(Player.P0).Cards.All(c => !c.FaceDown), Is.True);

            // 1 局目: ホストは画面の操作、ゲストはコード
            yield return WaitFor(() => online.ResultReady, guest, "game 1 reaches the result screen", () =>
            {
                GuestPlays(guest);
                HostPlays();
            });
            yield return WaitFor(() => guest.Phase == SessionPhase.Finished, guest, "the guest has received the last move");
            Assert.That(host.Ending, Is.EqualTo(EndReason.Completed));
            Assert.That(guest.Ending, Is.EqualTo(EndReason.Completed));
            Assert.That(GameEngine.HashState(host.State), Is.EqualTo(GameEngine.HashState(guest.State)));
            AssertViewMatches(host);
            var outcome = online.LastOutcome;
            Assert.That(outcome.Moves, Is.EqualTo(9));
            Assert.That(_app.Result.ResultLabel, Is.EqualTo(outcome.Result == GameResult.P0 ? "WIN" : outcome.Result == GameResult.P1 ? "LOSE" : "DRAW"));

            // 再戦: 両者が「もう一度」を押したら、新しい seed で始まる（ホストは席 1 になり、表示は写しで自分が手前）
            long seed0 = host.Seed;
            _app.Result.AgainButton.onClick.Invoke();
            Assert.That(host.RematchRequested, Is.True);
            Assert.That(_app.Result.AgainButton.interactable, Is.False, "waiting for the opponent");
            guest.RequestRematch();
            yield return WaitFor(() => host.Round == 1 && !online.ResultReady && _app.Match.State != null && _app.Match.MovesPlayed == 0, guest, "the rematch starts");
            Assert.That(host.Seed, Is.Not.EqualTo(seed0));
            Assert.That(guest.Seed, Is.EqualTo(host.Seed));
            Assert.That(host.LocalSeat, Is.EqualTo(Player.P1));
            yield return WaitFor(() => online.ResultReady, guest, "game 2 reaches the result screen", () =>
            {
                GuestPlays(guest);
                HostPlays();
            });
            yield return WaitFor(() => guest.Phase == SessionPhase.Finished, guest, "the guest has received the last move of game 2");
            Assert.That(GameEngine.HashState(host.State), Is.EqualTo(GameEngine.HashState(guest.State)));
            AssertViewMatches(host);
            // 表示は自分（席 1）が手前: 画面の P0 の手札・スコアが自分
            var real = GameEngine.Score(host.State);
            Assert.That(_app.Hud.Score(Player.P0), Is.EqualTo(real.P1));
            Assert.That(_app.Hud.Score(Player.P1), Is.EqualTo(real.P0));

            // 部屋を出る → 設定画面に戻り、ゲストには退出が伝わる
            _app.Result.SettingsButton.onClick.Invoke();
            Assert.That(online.IsActive, Is.False);
            Assert.That(_app.Setup.gameObject.activeSelf, Is.True);
            guest.Tick();
            Assert.That(guest.PeerGone, Is.True);
        }

        /// <summary>表示（盤・手札・スコア）が、写した局面（自分が席 0）と一致する。</summary>
        void AssertViewMatches(OnlineMatchSession host)
        {
            var view = host.LocalSeat == Player.P1 ? StateMirror.Mirror(host.State) : host.State;
            var shown = _app.Match.State;
            Assert.That(shown.Equals(view), Is.True, "the displayed state is the mirrored real state");
            for (int c = 0; c < 9; c++)
            {
                var card = _app.Match.Board.Placed[c];
                Assert.That(card, Is.Not.Null, "cell " + c);
                Assert.That(card.Owner, Is.EqualTo(view.Board[c].Value.Owner), "owner at cell " + c);
                Assert.That(card.CardId, Is.EqualTo(view.Board[c].Value.CardId));
            }
            var s = GameEngine.Score(view);
            Assert.That(_app.Hud.Score(Player.P0), Is.EqualTo(s.P0));
            Assert.That(_app.Hud.Score(Player.P1), Is.EqualTo(s.P1));
        }

        [UnityTest]
        public IEnumerator ADroppedLineShowsTheReconnectNoticeAndComesBack()
        {
            var online = _app.Online;
            _app.ShowSetup();
            _app.Setup.OnlineButton.onClick.Invoke();
            online.Screen.CreateButton.onClick.Invoke();
            var guestT = new LoopbackTransport(_net);
            var guest = new OnlineMatchSession(guestT, OnlineRole.Guest, Now);
            guestT.Join(online.CurrentCode);
            yield return WaitFor(() => _app.Match.IsOnline && _app.Match.State != null, guest, "match starts");
            yield return null;

            guestT.Drop();
            yield return WaitFor(() => online.Notice.IsShown, guest, "the reconnect notice is shown");
            Assert.That(online.Notice.Text, Does.Contain("再接続を待っています"));
            Assert.That(online.Notice.Text, Does.Contain("30").Or.Contain("29"));

            guestT.Join(online.CurrentCode);
            yield return WaitFor(() => !online.Notice.IsShown, guest, "the notice goes away after the guest returns");
            Assert.That(online.Session.Phase, Is.EqualTo(SessionPhase.Playing));

            // 「部屋を出る」はこの帯からもできる
            guestT.Drop();
            yield return WaitFor(() => online.Notice.IsShown, guest, "notice again");
            online.Notice.LeaveButton.onClick.Invoke();
            Assert.That(online.IsActive, Is.False);
            Assert.That(_app.Setup.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator AGuestWhoCannotFindTheRoomSeesAnError()
        {
            var online = _app.Online;
            _app.ShowSetup();
            _app.Setup.OnlineButton.onClick.Invoke();
            online.Screen.SetTyped("ZZZZ99");
            online.Screen.JoinButton.onClick.Invoke();
            yield return WaitFor(() => online.Screen.CurrentMode == OnlineScreen.Mode.Error, null, "an error is shown", () => { });
            Assert.That(online.LastError, Does.Contain("見つかりません"));
            online.Screen.BackButton.onClick.Invoke();
            Assert.That(_app.Setup.gameObject.activeSelf, Is.True);
        }
    }
}
