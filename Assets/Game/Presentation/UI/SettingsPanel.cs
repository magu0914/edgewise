using System;
using System.Globalization;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 設定（DESIGN §8-1 設定・§8-2 音のオプション）: 演出の速度（×1 / ×2 / 即時）、
    /// 音量 3 本（全体・BGM・効果音。uGUI の Slider、0〜100% の 5% 刻み）、全体ミュート、タブが裏に回ったら消音（初期値 ON）、画面の揺れ OFF。
    /// PlayerPrefs に保存（UserPrefs）。タイトル画面と対戦中のメニューから開く。縦画面（最小 360×640）では 1 行を「見出し → 操作」の 2 段にして大きく出す。
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour, IResponsiveUi
    {
        public const int VolumeSteps = 100 / UserPrefs.VolumeStep; // 20 段（5% 刻み）

        public enum Volume
        {
            Master = 0,
            Bgm = 1,
            Sfx = 2,
        }

        [SerializeField] Image panel;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI speedLabel;
        [SerializeField] Button[] speedButtons = new Button[3];
        [SerializeField] TextMeshProUGUI[] volumeLabels = new TextMeshProUGUI[3];
        [SerializeField] Slider[] volumeSliders = new Slider[3];
        [SerializeField] TextMeshProUGUI[] volumeValues = new TextMeshProUGUI[3];
        [SerializeField] TextMeshProUGUI muteLabel;
        [SerializeField] TextMeshProUGUI muteNote;
        [SerializeField] Button muteButton;
        [SerializeField] TextMeshProUGUI backgroundLabel;
        [SerializeField] TextMeshProUGUI backgroundNote;
        [SerializeField] Button backgroundButton;
        [SerializeField] TextMeshProUGUI shakeLabel;
        [SerializeField] TextMeshProUGUI shakeNote;
        [SerializeField] Button shakeButton;
        [SerializeField] Button closeButton;

        public event Action Closed;

        public bool IsOpen => gameObject.activeSelf;
        public Button ShakeButton => shakeButton;
        public Button MuteButton => muteButton;
        public Button BackgroundMuteButton => backgroundButton;
        public Button CloseButton => closeButton;
        public Slider VolumeSlider(Volume v) => volumeSliders[(int)v];
        public string VolumeText(Volume v) => volumeValues[(int)v].text;

        static readonly string[] VolumeNames = { "MasterSlider", "BgmSlider", "SfxSlider" };

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", Theme.Dim);
            panel = UiKit.Panel(root, "Panel", a, Theme.Panel);
            var p = panel.transform;
            title = UiKit.Label(p, "Title", a, UiText.SettingsTitle, 32, Theme.Gold);
            speedLabel = RowLabel(p, "SpeedLabel", a, UiText.SettingsSpeed);
            string[] speeds = { UiText.SpeedNormal, UiText.SpeedDouble, UiText.SpeedInstant };
            speedButtons = new Button[3];
            for (int i = 0; i < 3; i++) speedButtons[i] = UiKit.Button(p, "Speed" + i, a, speeds[i], 24, out _);

            string[] volNames = { UiText.SettingsMaster, UiText.SettingsBgm, UiText.SettingsSfx };
            Color[] fills = { new Color(0.85f, 0.70f, 0.32f), new Color(0.38f, 0.62f, 0.85f), new Color(0.40f, 0.78f, 0.66f) };
            volumeLabels = new TextMeshProUGUI[3];
            volumeSliders = new Slider[3];
            volumeValues = new TextMeshProUGUI[3];
            for (int i = 0; i < 3; i++)
            {
                volumeLabels[i] = RowLabel(p, VolumeNames[i].Replace("Slider", "Label"), a, volNames[i]);
                volumeSliders[i] = UiKit.Slider(p, VolumeNames[i], a, VolumeSteps, fills[i]);
                volumeValues[i] = UiKit.Label(p, VolumeNames[i].Replace("Slider", "Value"), a, "100" + UiText.Percent, 24, Theme.Ink,
                    TextAlignmentOptions.Right);
                volumeValues[i].textWrappingMode = TextWrappingModes.NoWrap;
            }

            muteLabel = RowLabel(p, "MuteLabel", a, UiText.SettingsMuteAll);
            muteNote = Note(p, "MuteNote", a, UiText.SettingsMuteAllNote);
            muteButton = UiKit.Button(p, "MuteButton", a, UiText.Off, 24, out _);
            backgroundLabel = RowLabel(p, "BackgroundMuteLabel", a, UiText.SettingsBackgroundMute);
            backgroundNote = Note(p, "BackgroundMuteNote", a, UiText.SettingsBackgroundMuteNote);
            backgroundButton = UiKit.Button(p, "BackgroundMuteButton", a, UiText.On, 24, out _);
            shakeLabel = RowLabel(p, "ShakeLabel", a, UiText.SettingsShake);
            shakeNote = Note(p, "ShakeNote", a, UiText.SettingsShakeNote);
            shakeButton = UiKit.Button(p, "ShakeButton", a, UiText.On, 24, out _);
            closeButton = UiKit.Button(p, "CloseButton", a, UiText.MenuClose, 26, out _, new Color(0.25f, 0.42f, 0.40f));
            ApplyLayout(false);
        }

        static TextMeshProUGUI RowLabel(Transform p, string name, GameAssets a, string text)
        {
            var t = UiKit.Label(p, name, a, text, 22, Theme.Ink, TextAlignmentOptions.Left);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        static TextMeshProUGUI Note(Transform p, string name, GameAssets a, string text)
        {
            var t = UiKit.Label(p, name, a, text, 15, Theme.InkDim, TextAlignmentOptions.Left);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.enableAutoSizing = true;
            t.fontSizeMin = 11;
            t.fontSizeMax = 15;
            return t;
        }

        void Awake()
        {
            for (int i = 0; i < 3; i++)
            {
                var s = (PlaybackSpeed)i;
                speedButtons[i].onClick.AddListener(() =>
                {
                    UserPrefs.Speed = s;
                    Refresh();
                });
            }
            for (int i = 0; i < 3; i++)
            {
                var v = (Volume)i;
                volumeSliders[i].onValueChanged.AddListener(x => OnVolume(v, x));
            }
            muteButton.onClick.AddListener(() =>
            {
                UserPrefs.Mute = !UserPrefs.Mute;
                Refresh();
                LogState();
            });
            backgroundButton.onClick.AddListener(() =>
            {
                UserPrefs.MuteInBackground = !UserPrefs.MuteInBackground;
                Refresh();
                LogState();
            });
            shakeButton.onClick.AddListener(() =>
            {
                UserPrefs.ScreenShake = !UserPrefs.ScreenShake;
                Refresh();
            });
            closeButton.onClick.AddListener(Close);
        }

        /// <summary>スライダーの値（0..20 段）を音量（%）にして保存する。</summary>
        public static int StepsToPercent(float steps) => UserPrefs.Snap(Mathf.Round(steps) * UserPrefs.VolumeStep);

        public static int GetPercent(Volume v) =>
            v == Volume.Master ? UserPrefs.MasterPercent : v == Volume.Bgm ? UserPrefs.BgmPercent : UserPrefs.SfxPercent;

        public static void SetPercent(Volume v, int percent)
        {
            switch (v)
            {
                case Volume.Master: UserPrefs.MasterPercent = percent; break;
                case Volume.Bgm: UserPrefs.BgmPercent = percent; break;
                default: UserPrefs.SfxPercent = percent; break;
            }
        }

        void OnVolume(Volume v, float steps)
        {
            int before = GetPercent(v);
            SetPercent(v, StepsToPercent(steps));
            volumeValues[(int)v].text = GetPercent(v) + UiText.Percent;
            if (GetPercent(v) == before) return;
            // 効果音・全体を動かしたら、その音量で短い音を鳴らして確かめられるようにする（カーソル音。続けて鳴りすぎないよう間引かれる）
            if (v != Volume.Bgm && SfxBank.Instance != null) SfxBank.Instance.Play(SfxKind.Cursor);
            LogState();
        }

        static void LogState()
        {
            if (!AutomationLog.Enabled) return;
            AutomationLog.Write("settings master=" + UserPrefs.MasterPercent.ToString(CultureInfo.InvariantCulture)
                + " bgm=" + UserPrefs.BgmPercent.ToString(CultureInfo.InvariantCulture)
                + " sfx=" + UserPrefs.SfxPercent.ToString(CultureInfo.InvariantCulture)
                + " mute=" + (UserPrefs.Mute ? 1 : 0) + " bgmute=" + (UserPrefs.MuteInBackground ? 1 : 0));
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            var top = new Vector2(0.5f, 1f);
            var tl = new Vector2(0f, 1f);
            if (!portrait)
            {
                // 横（基準 1280×720）: 「見出し | 操作」の 1 行ずつ
                const float w = 660f, pad = 30f, rowH = 54f, pitch = 66f, labelW = 170f;
                UiKit.Place((RectTransform)panel.transform, c, Vector2.zero, new Vector2(w, 690f));
                UiKit.Place(title.rectTransform, top, new Vector2(0f, -14f), new Vector2(w - 2 * pad, 46f));
                title.fontSize = 32;
                float x = pad, ctrlX = pad + labelW, ctrlW = w - pad - ctrlX;
                float y = -70f;
                Row(speedLabel, tl, x, y, labelW, rowH, 22);
                for (int i = 0; i < 3; i++)
                    UiKit.Place((RectTransform)speedButtons[i].transform, tl, tl, new Vector2(ctrlX + i * ctrlW / 3f, y), new Vector2(ctrlW / 3f - 10f, rowH));
                y -= pitch;
                for (int i = 0; i < 3; i++)
                {
                    Row(volumeLabels[i], tl, x, y, labelW, rowH, 22);
                    UiKit.Place((RectTransform)volumeSliders[i].transform, tl, tl, new Vector2(ctrlX, y), new Vector2(ctrlW - 96f, rowH));
                    UiKit.SetHandleWidth(volumeSliders[i], 30f);
                    UiKit.Place(volumeValues[i].rectTransform, tl, tl, new Vector2(w - pad - 86f, y), new Vector2(86f, rowH));
                    volumeValues[i].fontSize = 24;
                    y -= pitch;
                }
                ToggleRow(muteLabel, muteNote, muteButton, tl, x, y, w, pad, rowH, 22, 15);
                y -= pitch;
                ToggleRow(backgroundLabel, backgroundNote, backgroundButton, tl, x, y, w, pad, rowH, 22, 15);
                y -= pitch;
                ToggleRow(shakeLabel, shakeNote, shakeButton, tl, x, y, w, pad, rowH, 22, 15);
                UiKit.Place((RectTransform)closeButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(260f, 62f));
            }
            else
            {
                // 縦（基準 720×1280、360×640 では 0.5 倍）: 文字とつまみを大きく
                const float w = 680f, pad = 28f, rowH = 64f, labelH = 38f;
                UiKit.Place((RectTransform)panel.transform, c, new Vector2(0f, 10f), new Vector2(w, 1180f));
                UiKit.Place(title.rectTransform, top, new Vector2(0f, -18f), new Vector2(w - 2 * pad, 56f));
                title.fontSize = 40;
                float x = pad, cw = w - 2 * pad;
                float y = -84f;
                Row(speedLabel, tl, x, y, cw, labelH, 30);
                y -= labelH + 4f;
                for (int i = 0; i < 3; i++)
                    UiKit.Place((RectTransform)speedButtons[i].transform, tl, tl, new Vector2(x + i * cw / 3f, y), new Vector2(cw / 3f - 12f, rowH + 6f));
                y -= rowH + 26f;
                for (int i = 0; i < 3; i++)
                {
                    Row(volumeLabels[i], tl, x, y, cw - 130f, labelH, 30);
                    UiKit.Place(volumeValues[i].rectTransform, tl, tl, new Vector2(w - pad - 130f, y), new Vector2(130f, labelH));
                    volumeValues[i].fontSize = 30;
                    y -= labelH + 2f;
                    UiKit.Place((RectTransform)volumeSliders[i].transform, tl, tl, new Vector2(x, y), new Vector2(cw, rowH + 8f));
                    UiKit.SetHandleWidth(volumeSliders[i], 52f);
                    y -= rowH + 30f;
                }
                const float toggleH = 108f;
                ToggleRow(muteLabel, muteNote, muteButton, tl, x, y, w, pad, rowH + 6f, 30, 22, true);
                y -= toggleH;
                ToggleRow(backgroundLabel, backgroundNote, backgroundButton, tl, x, y, w, pad, rowH + 6f, 30, 22, true);
                y -= toggleH;
                ToggleRow(shakeLabel, shakeNote, shakeButton, tl, x, y, w, pad, rowH + 6f, 30, 22, true);
                UiKit.Place((RectTransform)closeButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(340f, 84f));
            }
            foreach (var b in speedButtons) b.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 32 : 24;
            foreach (var b in new[] { muteButton, backgroundButton, shakeButton })
                b.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 30 : 24;
            closeButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 32 : 26;
        }

        static void Row(TextMeshProUGUI label, Vector2 tl, float x, float y, float width, float h, float size)
        {
            UiKit.Place(label.rectTransform, tl, tl, new Vector2(x, y), new Vector2(width, h));
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Left;
        }

        static void ToggleRow(TextMeshProUGUI label, TextMeshProUGUI note, Button button, Vector2 tl, float x, float y, float w, float pad,
            float h, float size, float noteSize, bool portrait = false)
        {
            float bw = portrait ? 170f : 130f;
            float textW = w - 2 * pad - bw - 16f;
            // 見出し（上）と説明（下）を左に、ON/OFF のボタンを右に
            UiKit.Place(label.rectTransform, tl, tl, new Vector2(x, y - 2f), new Vector2(textW, h * 0.58f));
            label.fontSize = size;
            UiKit.Place(note.rectTransform, tl, tl, new Vector2(x, y - h * 0.58f), new Vector2(textW, h * 0.42f + 4f));
            note.fontSizeMax = noteSize;
            note.fontSizeMin = Mathf.Min(11f, noteSize);
            UiKit.Place((RectTransform)button.transform, tl, tl, new Vector2(w - pad - bw, y), new Vector2(bw, h));
        }

        public void Open()
        {
            gameObject.SetActive(true);
            Refresh();
            EventSystem.current?.SetSelectedGameObject(closeButton.gameObject);
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        public void Refresh()
        {
            var sp = UserPrefs.Speed;
            for (int i = 0; i < 3; i++) Mark(speedButtons[i], (int)sp == i);
            for (int i = 0; i < 3; i++)
            {
                int pct = GetPercent((Volume)i);
                volumeSliders[i].SetValueWithoutNotify(pct / (float)UserPrefs.VolumeStep);
                volumeValues[i].text = pct + UiText.Percent;
            }
            // ミュートは「ON = 音を止める」。ON のときは音量のスライダーを薄くする
            SetOnOff(muteButton, UserPrefs.Mute);
            for (int i = 0; i < 3; i++) volumeValues[i].alpha = UserPrefs.Mute ? 0.45f : 1f;
            SetOnOff(backgroundButton, UserPrefs.MuteInBackground);
            SetOnOff(shakeButton, UserPrefs.ScreenShake);
        }

        static void SetOnOff(Button b, bool on)
        {
            Mark(b, on);
            b.GetComponentInChildren<TextMeshProUGUI>().text = on ? UiText.On : UiText.Off;
        }

        static void Mark(Button b, bool on)
        {
            ((Image)b.targetGraphic).color = on ? Theme.ButtonOn : Theme.Button;
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = on ? new Color(0.08f, 0.06f, 0.02f) : Theme.ButtonText;
        }
    }
}
