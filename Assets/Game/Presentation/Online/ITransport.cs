using System;

namespace Edgewise.Game.Online
{
    /// <summary>通信路の状態。</summary>
    public enum TransportState
    {
        Idle = 0,

        /// <summary>ホスト: シグナリングサーバーに部屋を登録中。</summary>
        Opening = 1,

        /// <summary>ホスト: 部屋ができて、ゲストを待っている。</summary>
        Waiting = 2,

        /// <summary>ゲスト: 部屋へつなぎ中。</summary>
        Connecting = 3,

        Connected = 4,

        /// <summary>いったんつながった後で切れた（ホストは待ち続け、ゲストは自動で繋ぎ直す）。</summary>
        Disconnected = 5,

        /// <summary>つなげなかった（Error に原因）。</summary>
        Failed = 6,

        Closed = 7,
    }

    /// <summary>Failed のときの原因コード（PeerJS のエラー種別に合わせる）。</summary>
    public static class TransportErrors
    {
        /// <summary>部屋コードがすでに使われている（ホスト）。</summary>
        public const string IdTaken = "unavailable-id";

        /// <summary>その部屋が見つからない（ゲスト）。</summary>
        public const string RoomNotFound = "peer-unavailable";

        /// <summary>シグナリングサーバーに届かない・ネットワークが対応していない。</summary>
        public const string Network = "network";

        public const string Timeout = "timeout";

        /// <summary>この環境では使えない（WebGL 以外など）。</summary>
        public const string Unsupported = "unsupported";
    }

    /// <summary>
    /// 通信の抽象（DESIGN §9-1）。文字列のメッセージを順番どおりに送り合うだけ。
    /// WebGL では PeerJS（PeerJsTransport）、エディタとテストでは同じプロセス内の LoopbackTransport。
    /// イベントは Pump() の中で発火する（呼び出し側のスレッド・フレームで受け取れる）。
    /// </summary>
    public interface ITransport : IDisposable
    {
        TransportState State { get; }

        /// <summary>Failed の原因（TransportErrors）。それ以外は空。</summary>
        string Error { get; }

        bool IsHost { get; }

        event Action<TransportState> StateChanged;
        event Action<string> MessageReceived;

        /// <summary>部屋を作る（ホスト）。PeerJS の ID は edgewise-v1-コード。</summary>
        void Host(string roomCode);

        /// <summary>部屋に入る（ゲスト）。切れた後に呼べば繋ぎ直す。</summary>
        void Join(string roomCode);

        /// <summary>送る。つながっていなければ false（送らない）。</summary>
        bool Send(string message);

        /// <summary>届いたものを取り出してイベントを発火する。毎フレーム呼ぶ。</summary>
        void Pump();

        void Close();
    }

    /// <summary>部屋コード（紛らわしい 0/O/1/I/L を除いた英大文字と数字の 6 文字）。</summary>
    public static class RoomCode
    {
        public const int Length = 6;
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const string PeerIdPrefix = "edgewise-v1-";

        public static string Generate(Random rng)
        {
            var c = new char[Length];
            for (int i = 0; i < c.Length; i++) c[i] = Alphabet[rng.Next(Alphabet.Length)];
            return new string(c);
        }

        /// <summary>大文字にして空白を除く。使えない文字が混じっていたら null。</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char ch in text.Trim())
            {
                if (char.IsWhiteSpace(ch) || ch == '-') continue;
                char u = char.ToUpperInvariant(ch);
                if (Alphabet.IndexOf(u) < 0) return null;
                sb.Append(u);
            }
            return sb.Length == Length ? sb.ToString() : null;
        }

        public static bool IsValid(string code) => Normalize(code) == code;

        public static string PeerId(string code) => PeerIdPrefix + code;
    }
}
