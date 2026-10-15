using Edgewise.Game.Juice;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Edgewise.Game.Audio
{
    /// <summary>
    /// 「タブが裏に回ったら消音」（UserPrefs.MuteInBackground、初期値 ON）。
    /// - WebGL: 裏のタブでは Update が止まるので、jslib（Assets/Plugins/WebGL/EdgewiseAudio.jslib）が visibilitychange を受けて
    ///   AudioContext を suspend / resume する。ここでは設定の ON/OFF を伝えるだけ。
    /// - それ以外（エディタ・デスクトップ）: ウィンドウのフォーカスを失ったら AudioListener.pause で止める。
    /// </summary>
    public sealed class BackgroundAudio : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void EdgewiseAudio_SetMuteInBackground(int enabled, int log);
#endif

        bool _enabled;

        /// <summary>フォーカスを失って止めている間 true（WebGL 以外）。</summary>
        public bool PausedInBackground { get; private set; }

        void Start()
        {
            UserPrefs.Changed += Sync;
            Sync();
        }

        void OnDestroy()
        {
            UserPrefs.Changed -= Sync;
            if (PausedInBackground) AudioListener.pause = false;
        }

        void Sync()
        {
            _enabled = UserPrefs.MuteInBackground;
#if UNITY_WEBGL && !UNITY_EDITOR
            EdgewiseAudio_SetMuteInBackground(_enabled ? 1 : 0, Edgewise.Game.App.AutomationLog.Enabled ? 1 : 0);
#endif
            if (!_enabled && PausedInBackground) SetPaused(false);
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        void OnApplicationFocus(bool focus)
        {
            // バッチモード（テスト）ではフォーカスの概念がないので何もしない
            if (Application.isBatchMode) return;
            SetPaused(_enabled && !focus);
        }
#endif

        void SetPaused(bool paused)
        {
            if (PausedInBackground == paused) return;
            PausedInBackground = paused;
            AudioListener.pause = paused;
        }
    }
}
