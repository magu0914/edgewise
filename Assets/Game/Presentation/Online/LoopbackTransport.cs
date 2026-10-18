using System;
using System.Collections.Generic;

namespace Edgewise.Game.Online
{
    /// <summary>
    /// 同じプロセス内の偽の通信（エディタとテスト用。DESIGN §9-1）。1 つの LoopbackNetwork に ホスト 1 つ・ゲスト 1 つがつながる。
    /// 配送は Pump() のときに行うので、テストから「届かない」「切れる」を作れる。
    /// </summary>
    public sealed class LoopbackNetwork
    {
        /// <summary>エディタで使う共有のネットワーク（テストは new LoopbackNetwork() を使う）。</summary>
        public static readonly LoopbackNetwork Shared = new LoopbackNetwork();

        readonly Dictionary<string, LoopbackTransport> _hosts = new Dictionary<string, LoopbackTransport>();

        internal bool TryGetHost(string code, out LoopbackTransport host) => _hosts.TryGetValue(code, out host);

        internal bool Register(string code, LoopbackTransport host)
        {
            if (_hosts.ContainsKey(code)) return false;
            _hosts[code] = host;
            return true;
        }

        internal void Unregister(string code, LoopbackTransport host)
        {
            if (_hosts.TryGetValue(code, out var h) && ReferenceEquals(h, host)) _hosts.Remove(code);
        }
    }

    public sealed class LoopbackTransport : ITransport
    {
        readonly LoopbackNetwork _net;
        readonly Queue<Action> _inbox = new Queue<Action>();
        string _code;
        LoopbackTransport _peer;
        TransportState _state = TransportState.Idle;

        public LoopbackTransport(LoopbackNetwork network = null)
        {
            _net = network ?? LoopbackNetwork.Shared;
        }

        public TransportState State => _state;
        public string Error { get; private set; } = "";
        public bool IsHost { get; private set; }

        public event Action<TransportState> StateChanged;
        public event Action<string> MessageReceived;

        /// <summary>送ったメッセージの数（テスト用）。</summary>
        public int SentCount { get; private set; }

        /// <summary>true の間、Send は成功扱いで捨てる（届かない通信路を作る。テスト用）。</summary>
        public bool DropOutgoing { get; set; }

        public void Host(string roomCode)
        {
            IsHost = true;
            _code = roomCode;
            if (!_net.Register(roomCode, this))
            {
                Fail(TransportErrors.IdTaken);
                return;
            }
            Set(TransportState.Waiting);
        }

        public void Join(string roomCode)
        {
            IsHost = false;
            _code = roomCode;
            if (_state == TransportState.Connected) return;
            if (!_net.TryGetHost(roomCode, out var host) || host._state == TransportState.Closed)
            {
                if (_state == TransportState.Disconnected) return; // 繋ぎ直し中: まだ戻っていないだけ
                Fail(TransportErrors.RoomNotFound);
                return;
            }
            if (host._peer != null && host._state == TransportState.Connected && !ReferenceEquals(host._peer, this))
            {
                Fail(TransportErrors.RoomNotFound); // 満室
                return;
            }
            _peer = host;
            host._peer = this;
            // ホスト側の「つながった」を先に積む（ゲストがつながってすぐ送る hello より前に届くように）
            host.Enqueue(() => host.Set(TransportState.Connected));
            Set(TransportState.Connected);
        }

        public bool Send(string message)
        {
            if (_state != TransportState.Connected || _peer == null) return false;
            SentCount++;
            if (DropOutgoing) return true;
            var p = _peer;
            p.Enqueue(() => p.MessageReceived?.Invoke(message));
            return true;
        }

        /// <summary>回線が切れたことにする（両側が Disconnected になる。テスト用）。</summary>
        public void Drop()
        {
            var p = _peer;
            _peer = null;
            if (_state == TransportState.Connected) Set(TransportState.Disconnected);
            if (p != null)
            {
                p._peer = null;
                p.Enqueue(() =>
                {
                    if (p._state == TransportState.Connected) p.Set(TransportState.Disconnected);
                });
            }
        }

        public void Pump()
        {
            while (_inbox.Count > 0) _inbox.Dequeue()();
        }

        public void Close()
        {
            if (_state == TransportState.Closed) return;
            if (_code != null && IsHost) _net.Unregister(_code, this);
            var p = _peer;
            _peer = null;
            if (p != null)
            {
                p._peer = null;
                p.Enqueue(() =>
                {
                    if (p._state == TransportState.Connected) p.Set(TransportState.Disconnected);
                });
            }
            Set(TransportState.Closed);
        }

        public void Dispose() => Close();

        void Enqueue(Action a) => _inbox.Enqueue(a);

        void Fail(string error)
        {
            Error = error;
            Set(TransportState.Failed);
        }

        void Set(TransportState s)
        {
            if (_state == s) return;
            _state = s;
            StateChanged?.Invoke(s);
        }
    }
}
