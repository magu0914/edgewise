using System;
using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Edgewise.Game.Online
{
    /// <summary>
    /// PeerJS（WebRTC）の通信（WebGL 専用。DESIGN §9-1）。jslib（EdgewisePeer.jslib）がブラウザの window.peerjs を呼び、
    /// 届いたものは文字列のキューで受け取る。WebGL 以外では「使えない」状態になる（エディタでは LoopbackTransport を使う）。
    /// - ホストは PeerJS の ID に edgewise-v1-コードを登録して待つ。ゲストは同じ ID にデータ接続する。
    /// - 切れたら、ゲストは数秒おきに繋ぎ直す（ホストは待ち続ける）。つながる前の失敗は Failed。
    /// </summary>
    public sealed class PeerJsTransport : ITransport
    {
        /// <summary>つながる前の無応答をあきらめるまでの秒数。</summary>
        public const double OpenTimeoutSeconds = 20.0;

        /// <summary>切れた後の繋ぎ直しの間隔（ゲスト）。</summary>
        public const double RetrySeconds = 3.0;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int EdgewisePeer_Available();
        [DllImport("__Internal")] static extern void EdgewisePeer_Host(string id);
        [DllImport("__Internal")] static extern void EdgewisePeer_Join(string hostId);
        [DllImport("__Internal")] static extern int EdgewisePeer_Send(string message);
        [DllImport("__Internal")] static extern string EdgewisePeer_Poll();
        [DllImport("__Internal")] static extern void EdgewisePeer_Close();
#endif

        /// <summary>この環境で PeerJS が使えるか（WebGL かつ peerjs.min.jspre が読み込まれている）。</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                try { return EdgewisePeer_Available() != 0; } catch (Exception) { return false; }
#else
                return false;
#endif
            }
        }

        readonly Func<double> _clock;
        string _code;
        TransportState _state = TransportState.Idle;
        double _stateSince;
        double _lastRetry;
        bool _everConnected;

        public PeerJsTransport(Func<double> clock = null)
        {
            _clock = clock ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        public TransportState State => _state;
        public string Error { get; private set; } = "";
        public bool IsHost { get; private set; }

        public event Action<TransportState> StateChanged;
        public event Action<string> MessageReceived;

        public void Host(string roomCode)
        {
            IsHost = true;
            _code = roomCode;
            if (!IsSupported)
            {
                Fail(TransportErrors.Unsupported);
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            EdgewisePeer_Host(RoomCode.PeerId(roomCode));
#endif
            Set(TransportState.Opening);
        }

        public void Join(string roomCode)
        {
            IsHost = false;
            _code = roomCode;
            if (!IsSupported)
            {
                Fail(TransportErrors.Unsupported);
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            EdgewisePeer_Join(RoomCode.PeerId(roomCode));
#endif
            _lastRetry = _clock();
            if (_state != TransportState.Disconnected) Set(TransportState.Connecting);
        }

        public bool Send(string message)
        {
            if (_state != TransportState.Connected) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            return EdgewisePeer_Send(message) != 0;
#else
            return false;
#endif
        }

        public void Pump()
        {
            if (_state == TransportState.Idle || _state == TransportState.Closed || _state == TransportState.Failed) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            for (int i = 0; i < 64; i++)
            {
                string line = EdgewisePeer_Poll();
                if (line == null) break;
                Handle(line);
                if (_state == TransportState.Closed || _state == TransportState.Failed) return;
            }
#endif
            double now = _clock();
            if ((_state == TransportState.Opening || _state == TransportState.Connecting) && now - _stateSince > OpenTimeoutSeconds)
            {
                Fail(TransportErrors.Timeout);
                return;
            }
            // ゲスト: 切れた後は数秒おきに繋ぎ直す
            if (!IsHost && _state == TransportState.Disconnected && now - _lastRetry >= RetrySeconds && _code != null)
            {
                _lastRetry = now;
#if UNITY_WEBGL && !UNITY_EDITOR
                EdgewisePeer_Join(RoomCode.PeerId(_code));
#endif
            }
        }

        /// <summary>JS から届いた 1 行を処理する（テストから直接呼べる）。</summary>
        public void Handle(string line)
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                MessageReceived?.Invoke(line.Substring(5));
                return;
            }
            if (line == "state:waiting")
            {
                if (_state == TransportState.Opening) Set(TransportState.Waiting);
            }
            else if (line == "state:connected")
            {
                _everConnected = true;
                Set(TransportState.Connected);
            }
            else if (line == "state:disconnected")
            {
                if (_state == TransportState.Connected) Set(TransportState.Disconnected);
            }
            else if (line.StartsWith("error:", StringComparison.Ordinal))
            {
                string type = line.Substring(6);
                // つながった後（切れて繋ぎ直し中）のエラーは無視して繋ぎ直しを続ける。つながる前なら失敗
                if (_everConnected || _state == TransportState.Connected || _state == TransportState.Disconnected) return;
                if (_state == TransportState.Waiting && type != TransportErrors.IdTaken) return; // 待っている間の一時的な失敗
                Fail(type == "server-error" || type == "socket-error" || type == "socket-closed" ? TransportErrors.Network : type);
            }
        }

        public void Close()
        {
            if (_state == TransportState.Closed) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { EdgewisePeer_Close(); } catch (Exception) { }
#endif
            Set(TransportState.Closed);
        }

        public void Dispose() => Close();

        void Fail(string error)
        {
            Error = error;
            Set(TransportState.Failed);
#if UNITY_WEBGL && !UNITY_EDITOR
            try { EdgewisePeer_Close(); } catch (Exception) { }
#endif
        }

        void Set(TransportState s)
        {
            if (_state == s) return;
            _state = s;
            _stateSince = _clock();
            StateChanged?.Invoke(s);
        }
    }

    /// <summary>クリップボードへのコピー（招待 URL・部屋コード）。WebGL では jslib、それ以外は GUIUtility.systemCopyBuffer。</summary>
    public static class ClipboardUtil
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int EdgewiseClipboard_Copy(string text);
#endif

        public static bool Copy(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return EdgewiseClipboard_Copy(text) != 0; } catch (Exception) { return false; }
#else
            try
            {
                GUIUtility.systemCopyBuffer = text;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
#endif
        }
    }

    /// <summary>ブラウザの入力ダイアログ（WebGL のみ。ほかの環境と取り消しは空文字）。</summary>
    public static class PromptUtil
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string EdgewisePrompt_Ask(string message, string defaultText);
#endif

        public static string Ask(string message, string defaultText)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return EdgewisePrompt_Ask(message, defaultText ?? "") ?? ""; } catch (Exception) { return ""; }
#else
            return "";
#endif
        }
    }
}
