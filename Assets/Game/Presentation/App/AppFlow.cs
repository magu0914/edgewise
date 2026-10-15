using System;
using System.Globalization;
using System.Threading;
using Edgewise.Game.Controls;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Edgewise.Game.App
{
    /// <summary>
    /// 画面の流れ（DESIGN §8-1）: タイトル → 対戦設定 → 対戦 → 結果（→ 再戦 or 対戦設定へ）。シーンは Main.unity 1 つ。
    /// URL のクエリ（WebGL）: ?log = 自動操作用のログ、?seed=N = 配牌と先攻を固定、?speed=x1|x2|instant、?slow=4 = 撮影用のスロー、
    /// ?bench / ?debug = デバッグシーン（CPU 対 CPU の計測）へ。
    /// </summary>
    public sealed class AppFlow : MonoBehaviour
    {
        public const string DebugSceneName = "Debug";

        [SerializeField] GameAssets assets;
        [SerializeField] GameObject table;
        [SerializeField] TitleScreen title;
        [SerializeField] SetupScreen setup;
        [SerializeField] HudView hud;
        [SerializeField] MenuPanel menu;
        [SerializeField] ResultScreen result;
        [SerializeField] BannerView banner;
        [SerializeField] MatchController match;
        [SerializeField] FpsMeter fps;
        [SerializeField] SettingsPanel settings;

        MatchSettings _settings;
        MatchSettings _last;
        CancellationTokenSource _cts;
        long _seedOverride;

        public MatchController Match => match;
        public PlayerController Controls => match.Controls;
        public ResultScreen Result => result;
        public SetupScreen Setup => setup;
        public TitleScreen Title => title;
        public HudView Hud => hud;
        public MenuPanel Menu => menu;
        public FpsMeter Fps => fps;
        public SettingsPanel Settings => settings;
        public MatchOutcome LastOutcome { get; private set; }

        /// <summary>結果画面のボタンが押せる状態になったか（テスト用）。</summary>
        public bool ResultReady { get; private set; }

        public void Wire(GameAssets a, GameObject tableRoot, TitleScreen t, SetupScreen s, HudView h, MenuPanel m, ResultScreen r, BannerView b,
            MatchController mc, FpsMeter f, SettingsPanel sp)
        {
            settings = sp;
            assets = a;
            table = tableRoot;
            title = t;
            setup = s;
            hud = h;
            menu = m;
            result = r;
            banner = b;
            match = mc;
            fps = f;
        }

        void Awake()
        {
            ReadUrl();
            _settings = MatchSettings.Load();
            title.StartPressed += ShowSetup;
            setup.BeginPressed += s => StartMatch(s);
            setup.BackPressed += ShowTitle;
            result.AgainPressed += () => StartMatch(_last ?? _settings);
            result.SettingsPressed += () =>
            {
                StopMatch();
                ShowSetup();
            };
            hud.MenuPressed += OpenMenu;
            hud.RotatePressed += dir => Controls.Rotate(dir);
            menu.ResignPressed += () => match.Resign();
            menu.Closed += () => Controls.Blocked = false;
            menu.SettingsPressed += () =>
            {
                // メニューを閉じずに（入力は止めたまま）設定を開き、閉じたらメニューに戻る
                menu.gameObject.SetActive(false);
                settings.Open();
                AutomationLog.Buttons("Settings", settings.transform);
            };
            settings.Closed += () =>
            {
                if (match.State != null && match.IsRunning) menu.Open(match.State.Rules);
                else Controls.Blocked = false;
            };
            hud.HintPressed += () => match.RequestHint();
            Controls.MenuRequested += OpenMenu;
            Controls.HoldingChanged += holding => hud.ShowRotateButtons(holding && Controls.CanRotate);
        }

        void Start()
        {
            if (Application.absoluteURL != null && (HasParam("bench") || HasParam("debug")))
            {
                SceneManager.LoadScene(DebugSceneName);
                return;
            }
            ShowTitle();
        }

        void Update()
        {
            // メニューは Esc でも閉じる
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (settings.IsOpen) settings.Close();
                else if (menu.IsOpen) menu.Close();
            }
        }

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        static bool HasParam(string name)
        {
            var url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q < 0) return false;
            foreach (var part in url.Substring(q + 1).Split('&'))
            {
                var kv = part.Split('=');
                if (kv[0] == name) return true;
            }
            return false;
        }

        static string Param(string name)
        {
            var url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q < 0) return null;
            foreach (var part in url.Substring(q + 1).Split('&'))
            {
                var kv = part.Split('=');
                if (kv[0] == name) return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            }
            return null;
        }

        void ReadUrl()
        {
            if (string.IsNullOrEmpty(Application.absoluteURL)) return;
            if (HasParam("log")) AutomationLog.Enabled = true;
            if (long.TryParse(Param("seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed)) _seedOverride = seed;
            if (float.TryParse(Param("slow"), NumberStyles.Float, CultureInfo.InvariantCulture, out float slow) && slow > 0f)
                UserPrefs.DebugSlowFactor = slow;
            switch (Param("speed"))
            {
                case "x1": UserPrefs.SpeedOverride = PlaybackSpeed.Normal; break;
                case "x2": UserPrefs.SpeedOverride = PlaybackSpeed.Double; break;
                case "instant": UserPrefs.SpeedOverride = PlaybackSpeed.Instant; break;
            }
        }

        void HideAll()
        {
            title.Hide();
            setup.Hide();
            result.Hide();
            menu.gameObject.SetActive(false);
            settings.gameObject.SetActive(false);
            banner.Hide();
            hud.SetVisible(false);
            hud.ShowRotateButtons(false);
        }

        public void ShowTitle()
        {
            HideAll();
            table.SetActive(false);
            title.Show();
            AutomationLog.Buttons("Title", title.transform);
        }

        public void ShowSetup()
        {
            HideAll();
            table.SetActive(false);
            setup.Show(_settings);
            AutomationLog.Buttons("Setup", setup.transform);
        }

        /// <summary>対戦を始める（結果画面の「もう一度」・テストからも呼ぶ）。</summary>
        public void StartMatch(MatchSettings settings)
        {
            _last = settings.Clone();
            if (_seedOverride != 0 && _last.seed == 0) _last.seed = _seedOverride;
            HideAll();
            table.SetActive(true);
            hud.SetVisible(true);
            EventSystem.current?.SetSelectedGameObject(null);
            Controls.Blocked = false;
            ResultReady = false;
            LastOutcome = null;
            AutomationLog.Buttons("Hud", hud.transform);
            RunMatch(_last);
        }

        void StopMatch()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            if (fps != null) fps.Measuring = false;
        }

        async void RunMatch(MatchSettings settings)
        {
            StopMatch();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            var ct = _cts.Token;
            try
            {
                // 前の対戦の後始末（キャンセル）が終わるのを 1 フレーム待つ
                await Awaitable.NextFrameAsync(ct);
                if (fps != null) fps.Measuring = true;
                var outcome = await match.RunAsync(settings, ct);
                LastOutcome = outcome;
                if (fps != null) fps.Measuring = false;
                if (AutomationLog.Enabled && fps != null)
                    AutomationLog.Write("fps-match avg " + fps.AverageFps.ToString("0.0", CultureInfo.InvariantCulture) + " worst "
                        + fps.WorstFrameMs.ToString("0", CultureInfo.InvariantCulture) + "ms");
                hud.ShowRotateButtons(false);
                // 決着: 盤のカードが勝者の色に順に光る → スコアのカウントアップ → 勝敗の文字（と勝ち・負け・引き分けの音）
                await match.PlayFinaleAsync(outcome, ct);
                await result.Show(outcome.Score, outcome.Result, outcome.Resigned, assets.juice, UserPrefs.TimeFactor, ct,
                    () => match.Fx.OnEnd(outcome.Result), _ => match.Fx.OnCursorMove());
                ResultReady = true;
                AutomationLog.Buttons("Result", result.transform);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OpenMenu()
        {
            if (match.State == null || !match.IsRunning || menu.IsOpen || settings.IsOpen) return;
            Controls.Cancel();
            Controls.Blocked = true;
            menu.Open(match.State.Rules);
            AutomationLog.Buttons("Menu", menu.transform);
        }
    }
}
