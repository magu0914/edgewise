using System;
using System.Threading;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using Edgewise.Game.UI;
using UnityEngine;

namespace Edgewise.Game.Online
{
    /// <summary>
    /// オンライン対戦の画面の流れ（DESIGN §9-1）。部屋の画面（OnlineScreen）→ 対戦（MatchController.RunOnlineAsync）→ 結果（再戦／部屋を出る）。
    /// 通信は ITransport（WebGL は PeerJS、エディタは LoopbackTransport。TransportFactory で差し替えられる）、
    /// 対戦の進行は OnlineMatchSession に任せ、ここは画面と遷移だけを受け持つ。
    /// - ホスト: フリー対戦の設定画面の設定（ルール・強さ帯）で部屋を作る。ゲストが入ったら自動で始める。
    /// - ゲスト: コードを入力するか、招待 URL（?room=コード）で入る。設定はホストのものを使う。
    /// - 切れたら画面上部に「再接続を待っています（残り N 秒）」と「部屋を出る」を出す。
    /// </summary>
    public sealed class OnlineController : MonoBehaviour
    {
        const int MaxCodeTries = 4;

        [SerializeField] AppFlow app;
        [SerializeField] OnlineScreen screen;
        [SerializeField] OnlineNotice notice;

        readonly System.Random _rng = new System.Random(unchecked((int)DateTime.UtcNow.Ticks));
        OnlineMatchSession _session;
        ITransport _transport;
        MatchSettings _hostSettings;
        CancellationTokenSource _cts;
        string _code = "";
        int _codeTries;
        bool _matchRunning;

        /// <summary>通信路の作り方。既定は WebGL = PeerJS、それ以外 = LoopbackTransport。テストが差し替える。</summary>
        public Func<ITransport> TransportFactory { get; set; }

        /// <summary>時計（秒）。既定は実時間。テストが差し替える。</summary>
        public Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;

        public OnlineMatchSession Session => _session;
        public OnlineScreen Screen => screen;
        public OnlineNotice Notice => notice;
        public string CurrentCode => _code;
        public bool ScreenOpen => screen != null && screen.IsOpen;

        /// <summary>オンラインの途中（部屋の画面・対戦・結果のどれか）。</summary>
        public bool IsActive => _session != null;

        public bool ResultReady { get; private set; }
        public MatchOutcome LastOutcome { get; private set; }

        /// <summary>直近のエラー文（テスト用）。</summary>
        public string LastError { get; private set; } = "";

        public void Wire(AppFlow flow, OnlineScreen onlineScreen, OnlineNotice onlineNotice)
        {
            app = flow;
            screen = onlineScreen;
            notice = onlineNotice;
        }

        void Awake()
        {
            screen.CreatePressed += CreateRoom;
            screen.JoinPressed += JoinRoom;
            screen.BackPressed += Back;
            notice.LeavePressed += Leave;
        }

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _transport?.Dispose();
        }

        public void HideScreens()
        {
            if (screen != null) screen.Hide();
            if (notice != null) notice.Hide();
        }

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>フリー対戦の設定画面から: いまの設定を持って、部屋の画面（メニュー）を開く。</summary>
        public void Enter(MatchSettings setup)
        {
            Teardown();
            _hostSettings = setup.Clone();
            app.HideAll();
            app.PlayBgm(BgmCue.Title);
            screen.ShowMenu();
        }

        /// <summary>招待 URL（?room=コード）で開いたとき: 設定画面を通らずに、その部屋へ入る。</summary>
        public void EnterWithRoom(string code)
        {
            Teardown();
            _hostSettings = MatchSettings.Load();
            app.HideAll();
            app.PlayBgm(BgmCue.Title);
            screen.ShowMenu();
            JoinRoom(code);
        }

        public void Back()
        {
            if (screen.CurrentMode == OnlineScreen.Mode.Menu)
            {
                Teardown();
                app.ShowSetup();
            }
            else Leave();
        }

        /// <summary>部屋を出る（相手には退出が伝わる）。設定画面へ戻る。</summary>
        public void Leave()
        {
            Teardown();
            app.ShowSetup();
        }

        public void CreateRoom()
        {
            _codeTries = 0;
            StartSession(OnlineRole.Host);
            HostWithNewCode();
        }

        void HostWithNewCode()
        {
            _code = RoomCode.Generate(_rng);
            _codeTries++;
            screen.ShowRoom(_code, InviteUrl(_code), UiText.OnlineOpening);
            _transport.Host(_code);
            if (AutomationLog.Enabled) AutomationLog.Write("online host " + _code);
        }

        public void JoinRoom(string text)
        {
            var code = RoomCode.Normalize(text);
            if (code == null)
            {
                screen.SetStatus(UiText.OnlineBadCode, true);
                return;
            }
            _code = code;
            StartSession(OnlineRole.Guest);
            screen.ShowProgress(UiText.OnlineJoining);
            _transport.Join(code);
            if (AutomationLog.Enabled) AutomationLog.Write("online join " + code);
        }

        /// <summary>招待 URL: いまのページの URL からクエリとハッシュを外して ?room=コード を付ける。</summary>
        public static string InviteUrl(string code)
        {
            var url = Application.absoluteURL ?? "";
            int cut = url.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) url = url.Substring(0, cut);
            return url + "?room=" + code;
        }

        // ------------------------------------------------------------------
        // 通信・対戦の準備
        // ------------------------------------------------------------------

        void StartSession(OnlineRole role)
        {
            Teardown(false);
            _transport = (TransportFactory ?? DefaultTransport)();
            _session = new OnlineMatchSession(_transport, role, Clock);
            _session.GuestJoined += OnGuestJoined;
            _session.MatchStarted += OnMatchStarted;
            _session.Ended += OnEnded;
            _session.RematchChanged += UpdateResultUi;
            _transport.StateChanged += OnTransportState;
            ResultReady = false;
            LastOutcome = null;
            _matchRunning = false;
        }

        static ITransport DefaultTransport()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new PeerJsTransport();
#else
            return new LoopbackTransport();
#endif
        }

        void OnTransportState(TransportState s)
        {
            if (_session == null || _matchRunning) return;
            bool host = _session.Role == OnlineRole.Host;
            if (AutomationLog.Enabled) AutomationLog.Write("online transport " + s + (string.IsNullOrEmpty(_transport.Error) ? "" : " " + _transport.Error));
            switch (s)
            {
                case TransportState.Waiting:
                case TransportState.Disconnected:
                    if (host) screen.SetStatus(UiText.OnlineWaitingGuest);
                    break;
                case TransportState.Connected:
                    screen.SetStatus(host ? UiText.OnlineStarting : UiText.OnlineWaitStart);
                    break;
                case TransportState.Failed:
                    if (host && _transport.Error == TransportErrors.IdTaken && _codeTries < MaxCodeTries)
                    {
                        StartSession(OnlineRole.Host);
                        HostWithNewCode();
                        return;
                    }
                    ShowError(MessageFor(_transport.Error));
                    break;
            }
        }

        void OnGuestJoined()
        {
            if (_hostSettings == null) _hostSettings = MatchSettings.Load();
            _session.StartMatch(_hostSettings.ToRuleSet(), _hostSettings.band);
        }

        void OnMatchStarted()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            _matchRunning = true;
            notice.Hide();
            RunMatch(_cts.Token);
        }

        void OnEnded(EndReason reason)
        {
            // 対戦の途中の終わり方は RunOnlineAsync が見て扱う。ここは対戦の前（部屋の画面）の失敗だけ
            // 通信路の失敗（ConnectionFailed）は OnTransportState が扱う（部屋コードの衝突なら作り直す）
            if (_matchRunning || _session == null || reason == EndReason.ConnectionFailed) return;
            if (_session.IsAborted) ShowError(MessageFor(reason));
        }

        async void RunMatch(CancellationToken ct)
        {
            try
            {
                ResultReady = false;
                LastOutcome = null;
                app.BeginOnlineMatchView();
                // 前の対戦の後始末（キャンセル）が終わるのを 1 フレーム待つ
                await Awaitable.NextFrameAsync(ct);
                var session = _session;
                var outcome = await app.Match.RunOnlineAsync(session, ct);
                if (outcome == null)
                {
                    ShowError(MessageFor(session.Ending));
                    return;
                }
                LastOutcome = outcome;
                app.Hud.ShowRotateButtons(false);
                await app.Match.PlayFinaleAsync(outcome, ct);
                app.Result.SetOnline(true);
                await app.Result.Show(outcome.Score, outcome.Result, outcome.Resigned, app.Assets.juice, UserPrefs.TimeFactor, ct,
                    () => app.OnResultShown(outcome.Result), _ => app.Match.Fx.OnCursorMove(), NoteFor(session.Ending));
                ResultReady = true;
                UpdateResultUi();
                AutomationLog.Buttons("Result", app.Result.transform);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ------------------------------------------------------------------
        // 結果画面・再戦
        // ------------------------------------------------------------------

        /// <summary>結果画面の「もう一度」: 再戦を希望する（両者が押したらホストが新しい seed で始める）。</summary>
        public void RequestRematch()
        {
            if (_session == null) return;
            _session.RequestRematch();
            UpdateResultUi();
        }

        void UpdateResultUi()
        {
            if (_session == null || !ResultReady || !app.Result.IsShown) return;
            var r = app.Result;
            if (_session.PeerGone)
            {
                r.SetAgainWaiting(true);
                r.SetNote(_session.Ending == EndReason.OpponentForfeit ? UiText.OnlineForfeitWin : UiText.OnlineOpponentLeft);
            }
            else if (_session.RematchRequested)
            {
                r.SetAgainWaiting(true);
                r.SetNote(UiText.OnlineWaitRematch);
            }
            else if (_session.OpponentWantsRematch) r.SetNote(UiText.OnlineRematchAsked);
        }

        static string NoteFor(EndReason reason)
        {
            switch (reason)
            {
                case EndReason.Resigned: return UiText.Resigned;
                case EndReason.OpponentResigned: return UiText.OnlineOpponentResigned;
                case EndReason.OpponentLeft: return UiText.OnlineOpponentLeft;
                case EndReason.OpponentForfeit: return UiText.OnlineForfeitWin;
                default: return "";
            }
        }

        // ------------------------------------------------------------------
        // 毎フレーム・エラー・後始末
        // ------------------------------------------------------------------

        void Update()
        {
            if (_session == null) return;
            _session.Tick();
            if (_session == null) return; // Tick の中の通信イベントで部屋を出た
            if (_matchRunning && _session.WaitingForReconnect && _session.Phase == SessionPhase.Playing)
            {
                int left = Mathf.CeilToInt((float)(_session.ReconnectSecondsLeft ?? 0.0));
                notice.Show(UiText.OnlineReconnect + UiText.OnlineSecondsLeftPrefix + left + UiText.OnlineSecondsLeftSuffix);
            }
            else if (notice.IsShown) notice.Hide();
            if (ResultReady && _session.PeerGone) UpdateResultUi();
        }

        static string MessageFor(string transportError)
        {
            switch (transportError)
            {
                case TransportErrors.RoomNotFound: return UiText.OnlineErrNotFound;
                case TransportErrors.IdTaken: return UiText.OnlineErrTaken;
                case TransportErrors.Timeout: return UiText.OnlineErrTimeout;
                case TransportErrors.Unsupported: return UiText.OnlineErrUnsupported;
                default: return UiText.OnlineErrNetwork;
            }
        }

        string MessageFor(EndReason reason)
        {
            switch (reason)
            {
                case EndReason.VersionMismatch: return UiText.OnlineErrVersion;
                case EndReason.HashMismatch: return UiText.OnlineErrHash;
                case EndReason.ConnectionFailed: return MessageFor(_transport != null ? _transport.Error : "");
                case EndReason.OutOfOrder:
                case EndReason.IllegalMove:
                case EndReason.Desync: return UiText.OnlineErrOther;
                default: return UiText.OnlineErrClosed;
            }
        }

        void ShowError(string message)
        {
            LastError = message;
            Teardown();
            app.HideAll();
            screen.ShowError(message);
            if (AutomationLog.Enabled) AutomationLog.Write("online error " + message);
        }

        void Teardown(bool resetScreen = true)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _matchRunning = false;
            ResultReady = false;
            if (_session != null)
            {
                _session.GuestJoined -= OnGuestJoined;
                _session.MatchStarted -= OnMatchStarted;
                _session.Ended -= OnEnded;
                _session.RematchChanged -= UpdateResultUi;
                var s = _session;
                _session = null;
                if (_transport != null) _transport.StateChanged -= OnTransportState;
                s.Leave();
            }
            _transport?.Dispose();
            _transport = null;
            if (resetScreen)
            {
                notice.Hide();
                if (app != null && app.Result != null) app.Result.SetOnline(false);
            }
        }
    }
}
