// タブが裏に回ったら音を止める（DESIGN §8-2「タブが裏に回ったら消音」）。
// 裏に回ったタブでは requestAnimationFrame が止まり Unity の Update が呼ばれないので、C# 側では音量を下げられない。
// そこでブラウザの visibilitychange を直接受け、Unity の WebAudio の AudioContext を suspend / resume する。
// 有効・無効は C# の BackgroundAudio が設定（UserPrefs.MuteInBackground）に合わせて呼ぶ。
mergeInto(LibraryManager.library, {
  EdgewiseAudio_SetMuteInBackground: function (enabled, log) {
    var g = (typeof window !== 'undefined') ? window : {};
    var st = g.__edgewiseAudio || (g.__edgewiseAudio = { enabled: false, log: false, suspendedByUs: false, installed: false });
    st.enabled = !!enabled;
    st.log = !!log;

    function ctx() {
      try {
        if (typeof WEBAudio !== 'undefined' && WEBAudio && WEBAudio.audioContext) return WEBAudio.audioContext;
      } catch (e) { }
      return null;
    }
    function say(msg) { if (st.log) console.log('[EW] ' + msg); }
    function apply() {
      var c = ctx();
      var hidden = typeof document !== 'undefined' && (document.hidden || document.visibilityState === 'hidden');
      if (hidden && st.enabled) {
        if (c && c.state === 'running') {
          st.suspendedByUs = true;
          c.suspend().then(function () { say('audio suspended (tab hidden) state=' + c.state); });
        } else {
          say('audio tab hidden (context ' + (c ? c.state : 'none') + ')');
        }
      } else if (!hidden && st.suspendedByUs) {
        st.suspendedByUs = false;
        if (c) c.resume().then(function () { say('audio resumed (tab visible) state=' + c.state); });
      } else if (hidden) {
        say('audio tab hidden (mute in background OFF)');
      }
    }
    st.apply = apply;
    if (!st.installed && typeof document !== 'undefined') {
      st.installed = true;
      document.addEventListener('visibilitychange', function () { st.apply(); });
    }
    // 設定を OFF にした瞬間に裏に回っていることはないが、念のため今の状態に合わせる
    if (!st.enabled && st.suspendedByUs) {
      st.suspendedByUs = false;
      var c = ctx();
      if (c) c.resume();
    }
  },
});
