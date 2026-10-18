// オンライン対戦の通信（DESIGN §9-1）。PeerJS（Plugins/WebGL/peerjs.min.jspre が window.peerjs を作る）を C# から呼ぶ薄い橋渡し。
// C# → JS は関数呼び出し、JS → C# は文字列のキュー（EdgewisePeer_Poll で 1 件ずつ取り出す）。SendMessage は使わない。
// キューの中身（1 行 1 件）:
//   state:waiting        ホスト: 部屋が登録できた
//   state:connected      データ接続が開いた（ホストでは、ゲストが入った）
//   state:disconnected   データ接続が閉じた
//   error:<種別>         PeerJS のエラー種別（unavailable-id / peer-unavailable / network / server-error / socket-error ...）
//   data:<本文>          相手から届いたメッセージ
// デバッグ用に window.__edgewisePeer に状態を置く（E2E が回線切断を起こすのに使う）。
mergeInto(LibraryManager.library, {
  EdgewisePeer_Available: function () {
    return (typeof window !== 'undefined' && window.peerjs && window.peerjs.Peer) ? 1 : 0;
  },

  EdgewisePeer_Host: function (idPtr) {
    var id = UTF8ToString(idPtr);
    var st = window.__edgewisePeer = { role: 'host', queue: [], peer: null, conn: null, hostId: id, destroyed: false };
    function push(s) { st.queue.push(s); }
    function attach(c) {
      c.on('open', function () { st.conn = c; push('state:connected'); });
      c.on('data', function (d) { push('data:' + (typeof d === 'string' ? d : JSON.stringify(d))); });
      c.on('close', function () { if (st.conn === c) { st.conn = null; push('state:disconnected'); } });
      c.on('error', function (e) { push('error:' + ((e && e.type) || 'connection')); });
    }
    try {
      st.peer = new window.peerjs.Peer(id, { debug: 1 });
    } catch (e) {
      push('error:network');
      return;
    }
    st.peer.on('open', function () { push('state:waiting'); });
    st.peer.on('connection', function (c) {
      // 同じゲストの繋ぎ直しなら古い接続を置き換える。別の人は断る（満室）
      if (st.conn && st.conn.open && st.conn.peer !== c.peer) { c.on('open', function () { try { c.close(); } catch (e) { } }); return; }
      if (st.conn && st.conn.peer === c.peer) { var old = st.conn; st.conn = null; try { old.close(); } catch (e) { } }
      attach(c);
    });
    st.peer.on('disconnected', function () { if (!st.destroyed) { try { st.peer.reconnect(); } catch (e) { } } });
    st.peer.on('error', function (err) { push('error:' + ((err && err.type) || 'network')); });
  },

  EdgewisePeer_Join: function (hostIdPtr) {
    var hostId = UTF8ToString(hostIdPtr);
    var st = window.__edgewisePeer;
    if (!st || st.role !== 'guest' || st.destroyed || !st.peer || st.peer.destroyed) {
      st = window.__edgewisePeer = { role: 'guest', queue: [], peer: null, conn: null, hostId: hostId, destroyed: false, ready: false };
    }
    st.hostId = hostId;
    function push(s) { st.queue.push(s); }
    function attach(c) {
      c.on('open', function () { st.conn = c; push('state:connected'); });
      c.on('data', function (d) { push('data:' + (typeof d === 'string' ? d : JSON.stringify(d))); });
      c.on('close', function () { if (st.conn === c) { st.conn = null; push('state:disconnected'); } });
      c.on('error', function (e) { push('error:' + ((e && e.type) || 'connection')); });
    }
    function connectNow() {
      if (st.destroyed || (st.conn && st.conn.open)) return;
      var now = Date.now();
      if (st.lastAttempt && now - st.lastAttempt < 2500) return; // 繋ぎ直しの連打を避ける
      st.lastAttempt = now;
      try {
        attach(st.peer.connect(st.hostId, { reliable: true, serialization: 'json' }));
      } catch (e) {
        push('error:network');
      }
    }
    if (st.peer && !st.peer.destroyed) {
      if (st.peer.disconnected) { try { st.peer.reconnect(); } catch (e) { } }
      if (st.ready) connectNow();
      return;
    }
    try {
      st.peer = new window.peerjs.Peer({ debug: 1 });
    } catch (e) {
      push('error:network');
      return;
    }
    st.peer.on('open', function () { st.ready = true; connectNow(); });
    st.peer.on('disconnected', function () { if (!st.destroyed) { try { st.peer.reconnect(); } catch (e) { } } });
    st.peer.on('error', function (err) { push('error:' + ((err && err.type) || 'network')); });
  },

  EdgewisePeer_Send: function (msgPtr) {
    var st = window.__edgewisePeer;
    if (!st || !st.conn || !st.conn.open) return 0;
    try { st.conn.send(UTF8ToString(msgPtr)); return 1; } catch (e) { return 0; }
  },

  // 次の 1 件（なければ null）。返した文字列は C# のマーシャラが解放する
  EdgewisePeer_Poll: function () {
    var st = window.__edgewisePeer;
    if (!st || !st.queue.length) return 0;
    var s = st.queue.shift();
    var size = lengthBytesUTF8(s) + 1;
    var buf = _malloc(size);
    stringToUTF8(s, buf, size);
    return buf;
  },

  EdgewisePeer_Close: function () {
    var st = window.__edgewisePeer;
    if (!st) return;
    st.destroyed = true;
    try { if (st.conn) st.conn.close(); } catch (e) { }
    try { if (st.peer) st.peer.destroy(); } catch (e) { }
    st.conn = null;
    st.queue.length = 0;
  },

  // 招待 URL・部屋コードのコピー。ユーザーの操作の直後に呼ぶ（Clipboard API が使えなければ execCommand）。成功は 1
  EdgewiseClipboard_Copy: function (textPtr) {
    var text = UTF8ToString(textPtr);
    var ok = 0;
    try {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      ok = document.execCommand('copy') ? 1 : 0;
      document.body.removeChild(ta);
    } catch (e) { ok = 0; }
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).catch(function () { });
    } catch (e) { }
    window.__edgewiseCopied = text;
    return ok;
  },

  // スマホにはキーボード入力の手段がないため、ブラウザの入力ダイアログで文字列を受け取る。取り消しは空文字
  EdgewisePrompt_Ask: function (messagePtr, defaultPtr) {
    var result = '';
    try {
      var r = window.prompt(UTF8ToString(messagePtr), UTF8ToString(defaultPtr));
      if (r !== null && r !== undefined) result = String(r);
    } catch (e) { result = ''; }
    var size = lengthBytesUTF8(result) + 1;
    var buf = _malloc(size);
    stringToUTF8(result, buf, size);
    return buf;
  },
});
