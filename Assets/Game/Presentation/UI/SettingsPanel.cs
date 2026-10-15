using System;
using Edgewise.Game.Juice;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 設定（DESIGN §8-1 設定）: 演出の速度（×1 / ×2 / 即時）、効果音の音量とミュート、画面の揺れ OFF。PlayerPrefs に保存（UserPrefs）。
    /// メニューから開く。
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] Image panel;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI speedLabel;
        [SerializeField] Button[] speedButtons = new Button[3];
        [SerializeField] TextMeshProUGUI volumeLabel;
        [SerializeField] Button volumeDown;
        [SerializeField] Button volumeUp;
        [SerializeField] TextMeshProUGUI volumeValue;
        [SerializeField] TextMeshProUGUI muteLabel;
        [SerializeField] Button muteButton;
        [SerializeField] TextMeshProUGUI shakeLabel;
        [SerializeField] TextMeshProUGUI shakeNote;
        [SerializeField] Button shakeButton;
        [SerializeField] Button closeButton;

        public event Action Closed;

        public bool IsOpen => gameObject.activeSelf;
        public Button ShakeButton => shakeButton;
        public Button VolumeUpButton => volumeUp;
        public Button VolumeDownButton => volumeDown;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", Theme.Dim);
            panel = UiKit.Panel(root, "Panel", a, Theme.Panel);
            var p = panel.transform;
            title = UiKit.Label(p, "Title", a, UiText.SettingsTitle, 32, Theme.Gold);
            speedLabel = UiKit.Label(p, "SpeedLabel", a, UiText.SettingsSpeed, 22, Theme.InkDim, TextAlignmentOptions.Left);
            string[] speeds = { UiText.SpeedNormal, UiText.SpeedDouble, UiText.SpeedInstant };
            speedButtons = new Button[3];
            for (int i = 0; i < 3; i++) speedButtons[i] = UiKit.Button(p, "Speed" + i, a, speeds[i], 24, out _);
            volumeLabel = UiKit.Label(p, "VolumeLabel", a, UiText.SettingsVolume, 22, Theme.InkDim, TextAlignmentOptions.Left);
            volumeDown = UiKit.Button(p, "VolumeDown", a, UiText.Minus, 28, out _);
            volumeUp = UiKit.Button(p, "VolumeUp", a, UiText.Plus, 28, out _);
            volumeValue = UiKit.Label(p, "VolumeValue", a, "60%", 28, Theme.Ink);
            muteLabel = UiKit.Label(p, "MuteLabel", a, UiText.SettingsMute, 22, Theme.InkDim, TextAlignmentOptions.Left);
            muteButton = UiKit.Button(p, "MuteButton", a, UiText.On, 24, out _);
            shakeLabel = UiKit.Label(p, "ShakeLabel", a, UiText.SettingsShake, 22, Theme.InkDim, TextAlignmentOptions.Left);
            shakeNote = UiKit.Label(p, "ShakeNote", a, UiText.SettingsShakeNote, 15, Theme.InkDim, TextAlignmentOptions.Left);
            shakeButton = UiKit.Button(p, "ShakeButton", a, UiText.On, 24, out _);
            closeButton = UiKit.Button(p, "CloseButton", a, UiText.MenuClose, 24, out _, new Color(0.25f, 0.42f, 0.40f));
            ApplyLayout(false);
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
            volumeDown.onClick.AddListener(() =>
            {
                StepVolume(-1);
                Refresh();
            });
            volumeUp.onClick.AddListener(() =>
            {
                StepVolume(+1);
                Refresh();
            });
            muteButton.onClick.AddListener(() =>
            {
                UserPrefs.Mute = !UserPrefs.Mute;
                Refresh();
            });
            shakeButton.onClick.AddListener(() =>
            {
                UserPrefs.ScreenShake = !UserPrefs.ScreenShake;
                Refresh();
            });
            closeButton.onClick.AddListener(Close);
        }

        static void StepVolume(int dir)
        {
            int v = Mathf.RoundToInt(UserPrefs.SfxVolume * 10f) + dir;
            UserPrefs.SfxVolume = Mathf.Clamp(v, 0, 10) / 10f;
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            UiKit.Place((RectTransform)panel.transform, c, Vector2.zero, portrait ? new Vector2(660f, 640f) : new Vector2(620f, 560f));
            var top = new Vector2(0.5f, 1f);
            float w = portrait ? 600f : 560f;
            float x0 = -w * 0.5f;
            UiKit.Place(title.rectTransform, top, new Vector2(0f, -18f), new Vector2(w, 50f));
            float y = portrait ? -86f : -80f;
            float rowGap = portrait ? 112f : 100f;
            UiKit.Place(speedLabel.rectTransform, top, new Vector2(0f, y), new Vector2(w, 30f));
            for (int i = 0; i < 3; i++)
                UiKit.Place((RectTransform)speedButtons[i].transform, top, new Vector2(x0 + w / 6f + i * w / 3f, y - 34f), new Vector2(w / 3f - 12f, 54f));
            y -= rowGap;
            UiKit.Place(volumeLabel.rectTransform, top, new Vector2(0f, y), new Vector2(w, 30f));
            UiKit.Place((RectTransform)volumeDown.transform, top, new Vector2(x0 + 50f, y - 34f), new Vector2(100f, 54f));
            UiKit.Place(volumeValue.rectTransform, top, new Vector2(0f, y - 34f), new Vector2(200f, 54f));
            UiKit.Place((RectTransform)volumeUp.transform, top, new Vector2(-x0 - 50f, y - 34f), new Vector2(100f, 54f));
            y -= rowGap;
            UiKit.Place(muteLabel.rectTransform, top, new Vector2(-w * 0.18f, y - 34f), new Vector2(w * 0.64f, 54f));
            UiKit.Place((RectTransform)muteButton.transform, top, new Vector2(-x0 - 80f, y - 34f), new Vector2(160f, 54f));
            y -= rowGap * 0.75f;
            UiKit.Place(shakeLabel.rectTransform, top, new Vector2(-w * 0.18f, y - 26f), new Vector2(w * 0.64f, 34f));
            UiKit.Place(shakeNote.rectTransform, top, new Vector2(-w * 0.18f, y - 60f), new Vector2(w * 0.64f, 26f));
            UiKit.Place((RectTransform)shakeButton.transform, top, new Vector2(-x0 - 80f, y - 34f), new Vector2(160f, 54f));
            UiKit.Place((RectTransform)closeButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(260f, 64f));
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

        void Refresh()
        {
            var sp = UserPrefs.Speed;
            for (int i = 0; i < 3; i++) Mark(speedButtons[i], (int)sp == i);
            volumeValue.text = Mathf.RoundToInt(UserPrefs.SfxVolume * 100f) + "%";
            SetOnOff(muteButton, !UserPrefs.Mute);
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
