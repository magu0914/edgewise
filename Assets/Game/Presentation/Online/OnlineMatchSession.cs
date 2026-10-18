using System;
using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Game.Match;

namespace Edgewise.Game.Online
{
    public enum OnlineRole
    {
        Host = 0,
        Guest = 1,
    }

    public enum SessionPhase
    {
        /// <summary>対戦の前（ホストはゲストを待っている／ゲストは開始を待っている）。</summary>
        Lobby = 0,
        Playing = 1,

        /// <summary>決着した（再戦を待てる）。</summary>
        Finished = 2,

        /// <summary>続けられなくなった（Ending に原因）。</summary>
        Aborted = 3,

        Closed = 4,
    }

    public enum EndReason
    {
        None = 0,
        Completed = 1,

        /// <summary>自分が降参した。</summary>
        Resigned = 2,
        OpponentResigned = 3,

        /// <summary>相手が「部屋を出る」を押した。</summary>
        OpponentLeft = 4,

        /// <summary>相手が 30 秒戻らなかった（不戦勝）。</summary>
        OpponentForfeit = 5,
        HashMismatch = 6,
        IllegalMove = 7,
        OutOfOrder = 8,
        VersionMismatch = 9,
        ConnectionFailed = 10,
        Desync = 11,
    }

    /// <summary>届いたメッセージを受け付けなかった理由（テスト・ログ用）。</summary>
    public enum RejectKind
    {
        None = 0,
        Duplicate = 1,
        OutOfOrder = 2,
        Hash = 3,
        Protocol = 4,
        Illegal = 5,
        Malformed = 6,
    }

    /// <summary>1 手分（手を指す前の局面・手・結果）。表示側が再生する。</summary>
    public sealed class PendingMove
    {
        public int Seq;
        public Player Player;
        public GameState Before;
        public Move Move;
        public MoveResult Result;
    }

    /// <summary>
    /// オンライン対戦の進行（DESIGN §9-1）。通信（ITransport）から切り離した純粋なロジックで、EditMode テストで検証する。
    /// - ホスト権威: ホストが rules / band / seed / hostIsP0 を決めて start で送る。両者が同じ設定から CreateGame する。
    /// - 手は move {seq, hand, cell, rot, hash} だけを送る。受けた側は ApplyMove して自分の HashState と照らす。
    ///   seq の重複は捨て（Duplicate）、飛んだら順番違いで中断、hash が違えば中断して相手にも知らせる。
    /// - 最初のメッセージに protocol を入れ、合わなければ「バージョンが違います」で断る。
    /// - 切れたら 30 秒待つ（時計は注入できる）。戻らなければ不戦勝。戻ったら hello / sync で足りない手を送り直す。
    /// </summary>
    public sealed class OnlineMatchSession
    {
        public const int ProtocolVersion = 1;
        public const double ReconnectSeconds = 30.0;

        readonly ITransport _t;
        readonly Func<double> _clock;
        readonly Func<long> _seedSource;
        readonly Queue<PendingMove> _incoming = new Queue<PendingMove>();
        readonly Dictionary<int, string> _sent = new Dictionary<int, string>();

        double? _disconnectedAt;
        bool _hostIsP0 = true;
        bool _iWantRematch;

        public OnlineMatchSession(ITransport transport, OnlineRole role, Func<double> clock, Func<long> seedSource = null)
        {
            _t = transport ?? throw new ArgumentNullException(nameof(transport));
            Role = role;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _seedSource = seedSource ?? DefaultSeed;
            _t.StateChanged += OnTransportState;
            _t.MessageReceived += OnRaw;
        }

        public OnlineRole Role { get; }
        public ITransport Transport => _t;
        public SessionPhase Phase { get; private set; } = SessionPhase.Lobby;
        public EndReason Ending { get; private set; }

        /// <summary>相手が戻らない・出ていった（再戦はできない）。</summary>
        public bool PeerGone { get; private set; }

        /// <summary>自分の席（エンジン上の席）。対戦が始まってから有効。</summary>
        public Player LocalSeat { get; private set; } = Player.P0;

        /// <summary>エンジンの正の局面（席は入れ替えていない）。</summary>
        public GameState State { get; private set; }

        /// <summary>この対戦（ラウンド）の開始局面。表示側はここから、届いた手を順に再生する。</summary>
        public GameState StartState { get; private set; }

        public RuleSet Rules { get; private set; }
        public CardBand Band { get; private set; }
        public long Seed { get; private set; }
        public int Round { get; private set; }

        /// <summary>これまでに適用した手の数 = 次に使う seq。</summary>
        public int MovesApplied { get; private set; }

        /// <summary>ホスト: ゲストが入室して protocol も合っている。</summary>
        public bool GuestPresent { get; private set; }

        public bool PeerConnected => _t.State == TransportState.Connected;
        public bool OpponentWantsRematch { get; private set; }
        public bool RematchRequested => _iWantRematch;
        public int RejectedCount { get; private set; }
        public RejectKind LastRejected { get; private set; }

        public bool IsOver => Phase == SessionPhase.Finished || Phase == SessionPhase.Aborted || Phase == SessionPhase.Closed;

        /// <summary>続行できない終わり方（結果画面は出さない）。</summary>
        public bool IsAborted => Phase == SessionPhase.Aborted;

        public Player OpponentSeat => StateMirror.Swap(LocalSeat);
        public bool IsMyTurn => Phase == SessionPhase.Playing && State != null && State.Phase == Edgewise.Core.Phase.Playing && State.Turn == LocalSeat;

        /// <summary>相手が切れている間、残りの秒数（切れていなければ null）。</summary>
        public double? ReconnectSecondsLeft
        {
            get
            {
                if (_disconnectedAt == null) return null;
                return Math.Max(0.0, ReconnectSeconds - (_clock() - _disconnectedAt.Value));
            }
        }

        public bool WaitingForReconnect => _disconnectedAt != null && !IsOver;

        /// <summary>ホスト: ゲストが入室した。</summary>
        public event Action GuestJoined;

        /// <summary>対戦（または再戦）が始まった。State / LocalSeat / Seed が新しくなっている。</summary>
        public event Action MatchStarted;

        /// <summary>対戦が終わった・中断した。Ending に原因。</summary>
        public event Action<EndReason> Ended;

        /// <summary>相手の接続の出入り（true = つながった）。</summary>
        public event Action<bool> ConnectionChanged;

        /// <summary>再戦の希望の状態が変わった。</summary>
        public event Action RematchChanged;

        /// <summary>メッセージを受け付けなかった。</summary>
        public event Action<RejectKind> Rejected;

        static long DefaultSeed() => (DateTime.UtcNow.Ticks & 0x7fffffffL) | 1L;

        // ------------------------------------------------------------------
        // 進行
        // ------------------------------------------------------------------

        /// <summary>毎フレーム呼ぶ: 通信を取り込み、切断の待ち時間を見る。</summary>
        public void Tick()
        {
            _t.Pump();
            if (_disconnectedAt != null && _clock() - _disconnectedAt.Value >= ReconnectSeconds)
            {
                _disconnectedAt = null;
                PeerGone = true;
                if (Phase == SessionPhase.Playing) End(EndReason.OpponentForfeit, SessionPhase.Finished);
                else RematchChanged?.Invoke();
            }
        }

        /// <summary>ホスト: 対戦を始める（ゲストが入室してから）。ルールと強さ帯はホストが決める。</summary>
        public void StartMatch(RuleSet rules, CardBand band)
        {
            if (Role != OnlineRole.Host) throw new InvalidOperationException("only the host starts a match");
            if (!GuestPresent) throw new InvalidOperationException("no guest yet");
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Band = band;
            _hostIsP0 = true;
            HostBeginRound(0);
        }

        void HostBeginRound(int round)
        {
            long seed = _seedSource();
            if (seed == Seed && round > 0) seed = (seed ^ 0x5a5a5a5aL) | 1L;
            Round = round;
            BeginRound(_hostIsP0, seed);
            Send(new OnlineMessage
            {
                type = OnlineMessage.Start,
                protocol = ProtocolVersion,
                rules = Rules.ToBitString(),
                band = (int)Band,
                seed = seed,
                hostIsP0 = _hostIsP0,
                round = round,
            });
        }

        void BeginRound(bool hostIsP0, long seed)
        {
            Seed = seed;
            LocalSeat = (Role == OnlineRole.Host) == hostIsP0 ? Player.P0 : Player.P1;
            State = OnlineSetup.CreateState(Rules, Band, seed);
            StartState = State;
            MovesApplied = 0;
            _sent.Clear();
            _incoming.Clear();
            _iWantRematch = false;
            OpponentWantsRematch = false;
            Ending = EndReason.None;
            Phase = SessionPhase.Playing;
            MatchStarted?.Invoke();
        }

        /// <summary>自分の手を指す。エンジンで検証して適用し、相手へ送る。不正な手は IllegalMoveException。</summary>
        public PendingMove SubmitMove(Move move)
        {
            if (Phase != SessionPhase.Playing) throw new InvalidOperationException("no match in progress");
            if (State.Turn != LocalSeat) throw new InvalidOperationException("not your turn");
            GameEngine.AssertLegalMove(State, move);
            var before = State;
            var result = GameEngine.ApplyMove(before, move);
            State = result.State;
            int seq = MovesApplied++;
            var msg = new OnlineMessage
            {
                type = OnlineMessage.Move,
                seq = seq,
                hand = move.HandIndex,
                cell = move.Cell,
                rot = move.Rotation,
                hash = GameEngine.HashState(State),
            };
            _sent[seq] = msg.ToJson();
            _t.Send(_sent[seq]); // 切れていれば届かない。つなぎ直したときの sync で送り直す
            if (State.Phase == Edgewise.Core.Phase.Ended) Ending = EndReason.Completed;
            if (State.Phase == Edgewise.Core.Phase.Ended) Phase = SessionPhase.Finished;
            return new PendingMove { Seq = seq, Player = LocalSeat, Before = before, Move = move, Result = result };
        }

        /// <summary>相手の手が届いていれば 1 手取り出す（検証と適用は届いた時点で済んでいる）。</summary>
        public bool TryTakeOpponentMove(out PendingMove pending)
        {
            if (_incoming.Count > 0)
            {
                pending = _incoming.Dequeue();
                return true;
            }
            pending = null;
            return false;
        }

        public bool HasPendingOpponentMove => _incoming.Count > 0;

        /// <summary>降参する（対戦中だけ）。</summary>
        public void Resign()
        {
            if (Phase != SessionPhase.Playing) return;
            Send(new OnlineMessage { type = OnlineMessage.Resign });
            End(EndReason.Resigned, SessionPhase.Finished);
        }

        /// <summary>部屋を出る。相手には bye が届く（相手は不戦勝）。通信路も閉じる。</summary>
        public void Leave()
        {
            if (Phase == SessionPhase.Closed) return;
            Send(new OnlineMessage { type = OnlineMessage.Bye });
            Phase = SessionPhase.Closed;
            _t.Close();
        }

        /// <summary>再戦を希望する。両者が押したらホストが新しい seed で始める。</summary>
        public void RequestRematch()
        {
            if (Phase != SessionPhase.Finished || PeerGone || _iWantRematch) return;
            if (Ending != EndReason.Completed && Ending != EndReason.Resigned && Ending != EndReason.OpponentResigned) return;
            _iWantRematch = true;
            Send(new OnlineMessage { type = OnlineMessage.Rematch });
            RematchChanged?.Invoke();
            TryHostRematch();
        }

        void TryHostRematch()
        {
            if (Role != OnlineRole.Host || !_iWantRematch || !OpponentWantsRematch || Phase != SessionPhase.Finished) return;
            _hostIsP0 = !_hostIsP0;
            HostBeginRound(Round + 1);
        }

        // ------------------------------------------------------------------
        // 通信の受け口
        // ------------------------------------------------------------------

        void OnTransportState(TransportState s)
        {
            switch (s)
            {
                case TransportState.Connected:
                    _disconnectedAt = null;
                    if (Role == OnlineRole.Guest) SendHello();
                    ConnectionChanged?.Invoke(true);
                    break;
                case TransportState.Disconnected:
                    GuestPresent = false;
                    if (Phase == SessionPhase.Playing || (Phase == SessionPhase.Finished && !PeerGone)) _disconnectedAt = _clock();
                            ConnectionChanged?.Invoke(false);
                    break;
                case TransportState.Failed:
                    if (!IsOver) End(EndReason.ConnectionFailed, SessionPhase.Aborted);
                    break;
            }
        }

        void SendHello()
        {
            Send(new OnlineMessage { type = OnlineMessage.Hello, protocol = ProtocolVersion, next = MovesApplied, round = Round });
        }

        void OnRaw(string raw)
        {
            var m = OnlineMessage.Parse(raw);
            if (m == null)
            {
                Reject(RejectKind.Malformed);
                return;
            }
            if (Phase == SessionPhase.Aborted || Phase == SessionPhase.Closed) return;
            switch (m.type)
            {
                case OnlineMessage.Hello: OnHello(m); break;
                case OnlineMessage.Reject: Abort(EndReason.VersionMismatch, false); break;
                case OnlineMessage.Start: OnStart(m); break;
                case OnlineMessage.Move: OnMove(m); break;
                case OnlineMessage.Sync:
                    if (m.round == Round) ResendFrom(m.next);
                    break;
                case OnlineMessage.Resign:
                    if (Phase == SessionPhase.Playing) End(EndReason.OpponentResigned, SessionPhase.Finished);
                    break;
                case OnlineMessage.Bye:
                    PeerGone = true;
                    _disconnectedAt = null;
                    if (Phase == SessionPhase.Playing) End(EndReason.OpponentLeft, SessionPhase.Finished);
                    else RematchChanged?.Invoke();
                    break;
                case OnlineMessage.Rematch:
                    OpponentWantsRematch = true;
                    RematchChanged?.Invoke();
                    TryHostRematch();
                    break;
                case OnlineMessage.Abort:
                    Abort(ReasonFromWire(m.reason), false);
                    break;
                default:
                    Reject(RejectKind.Malformed);
                    break;
            }
        }

        void OnHello(OnlineMessage m)
        {
            if (Role != OnlineRole.Host) return;
            if (m.protocol != ProtocolVersion)
            {
                Send(new OnlineMessage { type = OnlineMessage.Reject, protocol = ProtocolVersion, reason = "version" });
                Reject(RejectKind.Protocol);
                if (Phase == SessionPhase.Lobby) return; // ロビーなら部屋は残して、別のゲストを待てる
                Abort(EndReason.VersionMismatch, false);
                return;
            }
            bool first = !GuestPresent;
            GuestPresent = true;
            _disconnectedAt = null;
            if (Phase == SessionPhase.Playing || Phase == SessionPhase.Finished)
            {
                // 繋ぎ直し: 相手に足りない手を送り直し、こちらの状況を知らせる
                if (m.round != Round)
                {
                    Abort(EndReason.Desync, true);
                    return;
                }
                ResendFrom(m.next);
                Send(new OnlineMessage { type = OnlineMessage.Sync, next = MovesApplied, round = Round });
                if (Phase == SessionPhase.Finished && _iWantRematch) Send(new OnlineMessage { type = OnlineMessage.Rematch });
                RematchChanged?.Invoke();
            }
            else if (first)
            {
                GuestJoined?.Invoke();
            }
        }

        void OnStart(OnlineMessage m)
        {
            if (Role != OnlineRole.Guest) return;
            if (m.protocol != ProtocolVersion)
            {
                Abort(EndReason.VersionMismatch, true);
                return;
            }
            var rules = OnlineSetup.ParseRules(m.rules);
            if (rules == null || m.band < 0 || m.band > 2)
            {
                Reject(RejectKind.Malformed);
                Abort(EndReason.Desync, true);
                return;
            }
            if (Phase == SessionPhase.Playing && m.round == Round) return; // 同じ開始の重複
            Rules = rules;
            Band = (CardBand)m.band;
            Round = m.round;
            _hostIsP0 = m.hostIsP0;
            BeginRound(m.hostIsP0, m.seed);
        }

        void OnMove(OnlineMessage m)
        {
            if (Phase != SessionPhase.Playing)
            {
                Reject(RejectKind.Illegal);
                return;
            }
            if (m.seq < MovesApplied)
            {
                Reject(RejectKind.Duplicate); // 送り直しで同じ手が届いた: 無視する
                return;
            }
            if (m.seq > MovesApplied)
            {
                Reject(RejectKind.OutOfOrder);
                Abort(EndReason.OutOfOrder, true);
                return;
            }
            var move = new Move(m.hand, m.cell, m.rot);
            if (State.Turn != OpponentSeat || !GameEngine.IsLegalMove(State, move))
            {
                Reject(RejectKind.Illegal);
                Abort(EndReason.IllegalMove, true);
                return;
            }
            var before = State;
            var result = GameEngine.ApplyMove(before, move);
            if (GameEngine.HashState(result.State) != m.hash)
            {
                Reject(RejectKind.Hash);
                Abort(EndReason.HashMismatch, true);
                return;
            }
            State = result.State;
            int seq = MovesApplied++;
            _incoming.Enqueue(new PendingMove { Seq = seq, Player = OpponentSeat, Before = before, Move = move, Result = result });
            if (State.Phase == Edgewise.Core.Phase.Ended)
            {
                Ending = EndReason.Completed;
                Phase = SessionPhase.Finished;
            }
        }

        void ResendFrom(int next)
        {
            for (int seq = Math.Max(0, next); seq < MovesApplied; seq++)
            {
                if (_sent.TryGetValue(seq, out var json)) _t.Send(json);
            }
        }

        // ------------------------------------------------------------------
        // 終わり方
        // ------------------------------------------------------------------

        void End(EndReason reason, SessionPhase phase)
        {
            if (phase != SessionPhase.Aborted && Phase != SessionPhase.Playing) return;
            Ending = reason;
            Phase = phase;
            if (reason == EndReason.OpponentLeft || reason == EndReason.OpponentForfeit) PeerGone = true;
            Ended?.Invoke(reason);
        }

        void Abort(EndReason reason, bool tellPeer)
        {
            if (IsAborted) return;
            if (tellPeer) Send(new OnlineMessage { type = OnlineMessage.Abort, reason = WireReason(reason) });
            Ending = reason;
            Phase = SessionPhase.Aborted;
            Ended?.Invoke(reason);
        }

        static string WireReason(EndReason r)
        {
            switch (r)
            {
                case EndReason.HashMismatch: return "hash";
                case EndReason.IllegalMove: return "illegal";
                case EndReason.OutOfOrder: return "order";
                case EndReason.VersionMismatch: return "version";
                default: return "desync";
            }
        }

        static EndReason ReasonFromWire(string s)
        {
            switch (s)
            {
                case "hash": return EndReason.HashMismatch;
                case "illegal": return EndReason.IllegalMove;
                case "order": return EndReason.OutOfOrder;
                case "version": return EndReason.VersionMismatch;
                default: return EndReason.Desync;
            }
        }

        void Reject(RejectKind kind)
        {
            RejectedCount++;
            LastRejected = kind;
            Rejected?.Invoke(kind);
        }

        void Send(OnlineMessage m) => _t.Send(m.ToJson());
    }
}
