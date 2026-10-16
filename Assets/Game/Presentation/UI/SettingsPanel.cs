using System;
using System.Globalization;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.Tweening;
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
    /// 5b-1: タブ「基本」「星図巡り」。星図巡りのタブに「原作モード（負けるとカードを取られる）」の切り替え（説明つき）と
    /// 「データを消す」（2 段階の確認）。星図巡りの中身は ICampaignSettings（CampaignController）を通して触る。
    /// </summary>
    /// <summary>設定画面から星図巡りの設定に触る窓口（CampaignController が実装する）。</summary>
    public interface ICampaignSettings
    {
        /// <summary>保存済みの進行があるか（「データを消す」を押せるか）。</summary>
        bool HasSave { get; }

        /// <summary>原作モード（負けるとカードを取られる）。</summary>
        bool ClassicMode { get; set; }

        /// <summary>今変えてよいか（星図巡りの対戦の途中などは false）。</summary>
        bool CanEdit { get; }

        void Erase();
    }

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
        [SerializeField] Button tabBasic;
        [SerializeField] Button tabCampaign;
        [SerializeField] RectTransform basicPage;
        [SerializeField] RectTransform campaignPage;
        [SerializeField] TextMeshProUGUI classicLabel;
        [SerializeField] TextMeshProUGUI classicNote;
        [SerializeField] TextMeshProUGUI classicDetail;
        [SerializeField] Button classicButton;
        [SerializeField] TextMeshProUGUI eraseLabel;
        [SerializeField] TextMeshProUGUI eraseNote;
        [SerializeField] Button eraseButton;
        [SerializeField] TextMeshProUGUI campaignStatus;
        [SerializeField] Image confirmDim;
        [SerializeField] Image confirmBox;
        [SerializeField] TextMeshProUGUI confirmText;
        [SerializeField] Button confirmYes;
        [SerializeField] Button confirmNo;

        int _confirmStage;
        bool _campaignTab;
        static bool _open;
        static int _closedFrame = -1;

        public event Action Closed;

        /// <summary>データを消した（タイトルの進み具合を消すため）。</summary>
        public event Action CampaignErased;

        /// <summary>星図巡りの設定の窓口（CampaignController）。null なら星図巡りのタブは何もしない。</summary>
        public ICampaignSettings Campaign { get; set; }

        /// <summary>設定が開いているか（このフレームに閉じた場合も含む。Esc が他の画面の「戻る」と重ならないように）。</summary>
        public static bool AnyOpen => _open || Time.frameCount == _closedFrame;

        public bool IsOpen => gameObject.activeSelf;
        public bool CampaignTab => _campaignTab;
        public int ConfirmStage => _confirmStage;
        public Button TabBasic => tabBasic;
        public Button TabCampaign => tabCampaign;
        public Button ClassicButton => classicButton;
        public Button EraseButton => eraseButton;
        public Button ConfirmYes => confirmYes;
        public Button ConfirmNo => confirmNo;
        public string CampaignStatus => campaignStatus.text;
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
            title = UiKit.Label(panel.transform, "Title", a, UiText.SettingsTitle, 32, Theme.Gold);
            tabBasic = UiKit.Button(panel.transform, "TabBasic", a, UiText.SettingsTabBasic, 22, out _);
            tabCampaign = UiKit.Button(panel.transform, "TabCampaign", a, UiText.SettingsTabCampaign, 22, out _);
            basicPage = UiKit.Rect(panel.transform, "BasicPage");
            UiKit.Stretch(basicPage);
            campaignPage = UiKit.Rect(panel.transform, "CampaignPage");
            UiKit.Stretch(campaignPage);
            var p = basicPage;
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

            // 星図巡りのタブ
            var cp = campaignPage;
            classicLabel = RowLabel(cp, "ClassicLabel", a, UiText.SettingsClassic);
            classicNote = Note(cp, "ClassicNote", a, UiText.SettingsClassicNote);
            classicButton = UiKit.Button(cp, "ClassicButton", a, UiText.Off, 24, out _);
            classicDetail = UiKit.Label(cp, "ClassicDetail", a, UiText.SettingsClassicDetail, 17, Theme.InkDim, TextAlignmentOptions.TopLeft);
            eraseLabel = RowLabel(cp, "EraseLabel", a, UiText.SettingsErase);
            eraseNote = Note(cp, "EraseNote", a, UiText.SettingsEraseNote);
            eraseButton = UiKit.Button(cp, "EraseButton", a, UiText.SettingsEraseButton, 24, out _, new Color(0.45f, 0.18f, 0.18f));
            campaignStatus = UiKit.Label(cp, "CampaignStatus", a, "", 18, Theme.Gold, TextAlignmentOptions.Left);

            closeButton = UiKit.Button(panel.transform, "CloseButton", a, UiText.MenuClose, 26, out _, new Color(0.25f, 0.42f, 0.40f));

            // 消す前の確認（2 段階）
            confirmDim = UiKit.Fill(panel.transform, "ConfirmDim", new Color(0f, 0f, 0.02f, 0.7f));
            confirmBox = UiKit.Panel(confirmDim.transform, "ConfirmBox", a, new Color(0.12f, 0.08f, 0.10f, 0.98f), false);
            confirmText = UiKit.Label(confirmBox.transform, "ConfirmText", a, "", 26, Theme.Ink);
            confirmYes = UiKit.Button(confirmBox.transform, "EraseYes", a, UiText.EraseYes, 26, out _, new Color(0.55f, 0.20f, 0.20f));
            confirmNo = UiKit.Button(confirmBox.transform, "EraseNo", a, UiText.EraseNo, 26, out _);
            confirmDim.gameObject.SetActive(false);
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
            tabBasic.onClick.AddListener(() => ShowTab(false));
            tabCampaign.onClick.AddListener(() => ShowTab(true));
            classicButton.onClick.AddListener(() =>
            {
                if (Campaign == null || !Campaign.CanEdit) return;
                Campaign.ClassicMode = !Campaign.ClassicMode;
                Refresh();
            });
            eraseButton.onClick.AddListener(() =>
            {
                if (Campaign == null || !Campaign.CanEdit || !Campaign.HasSave) return;
                OpenConfirm(1);
            });
            confirmYes.onClick.AddListener(() =>
            {
                if (_confirmStage == 1)
                {
                    OpenConfirm(2);
                    return;
                }
                CloseConfirm();
                if (Campaign == null || !Campaign.CanEdit) return;
                Campaign.Erase();
                campaignStatus.text = UiText.EraseDone;
                Refresh(false);
                CampaignErased?.Invoke();
                if (AutomationLog.Enabled) AutomationLog.Write("campaign erased");
            });
            confirmNo.onClick.AddListener(CloseConfirm);
        }

        void OpenConfirm(int stage)
        {
            _confirmStage = stage;
            confirmDim.gameObject.SetActive(true);
            confirmText.text = stage == 1 ? UiText.EraseConfirm1 : UiText.EraseConfirm2;
            var box = (RectTransform)confirmBox.transform;
            Tweens.Play(0.18f * UserPrefs.TimeFactor, k => box.localScale = Vector3.one * Mathf.LerpUnclamped(0.8f, 1f, k), Ease.OutBack, 0f, box, "pop");
            // 2 段階目は「やめる」を選んでおく（Enter の連打で消えないように）
            EventSystem.current?.SetSelectedGameObject((stage == 1 ? confirmYes : confirmNo).gameObject);
            AutomationLog.Buttons("Confirm" + stage, confirmDim.transform);
        }

        void CloseConfirm()
        {
            _confirmStage = 0;
            confirmDim.gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(eraseButton.gameObject);
        }

        /// <summary>タブを切り替える（星図巡りのタブ = true）。</summary>
        public void ShowTab(bool campaign)
        {
            _campaignTab = campaign;
            basicPage.gameObject.SetActive(!campaign);
            campaignPage.gameObject.SetActive(campaign);
            Mark(tabBasic, !campaign);
            Mark(tabCampaign, campaign);
            if (campaign) campaignStatus.text = "";
            Refresh(false);
            if (gameObject.activeSelf)
            {
                EventSystem.current?.SetSelectedGameObject((campaign ? tabCampaign : tabBasic).gameObject);
                AutomationLog.Buttons("Settings", transform);
            }
        }

        /// <summary>Esc: 確認を開いていれば閉じ、そうでなければ設定を閉じる。</summary>
        public void Back()
        {
            if (_confirmStage > 0) CloseConfirm();
            else Close();
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
                UiKit.Place((RectTransform)tabBasic.transform, top, new Vector2(-105f, -64f), new Vector2(200f, 44f));
                UiKit.Place((RectTransform)tabCampaign.transform, top, new Vector2(105f, -64f), new Vector2(200f, 44f));
                float x = pad, ctrlX = pad + labelW, ctrlW = w - pad - ctrlX;
                float y = -124f;
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
                // 星図巡りのタブ
                float cy = -130f;
                ToggleRow(classicLabel, classicNote, classicButton, tl, x, cy, w, pad, rowH, 22, 15);
                UiKit.Place(classicDetail.rectTransform, tl, tl, new Vector2(x, cy - rowH - 10f), new Vector2(w - 2 * pad, 90f));
                classicDetail.fontSize = 17;
                cy -= rowH + 130f;
                ToggleRow(eraseLabel, eraseNote, eraseButton, tl, x, cy, w, pad, rowH, 22, 15);
                UiKit.Place(campaignStatus.rectTransform, tl, tl, new Vector2(x, cy - rowH - 14f), new Vector2(w - 2 * pad, 32f));
                campaignStatus.fontSize = 18;
                UiKit.Place((RectTransform)confirmBox.transform, c, Vector2.zero, new Vector2(560f, 260f));
                UiKit.Place(confirmText.rectTransform, top, new Vector2(0f, -30f), new Vector2(500f, 110f));
                confirmText.fontSize = 26;
                UiKit.Place((RectTransform)confirmYes.transform, new Vector2(0.5f, 0f), new Vector2(-120f, 28f), new Vector2(200f, 64f));
                UiKit.Place((RectTransform)confirmNo.transform, new Vector2(0.5f, 0f), new Vector2(120f, 28f), new Vector2(200f, 64f));
            }
            else
            {
                // 縦（基準 720×1280、360×640 では 0.5 倍）: 文字とつまみを大きく
                const float w = 680f, pad = 28f, rowH = 64f, labelH = 38f;
                UiKit.Place((RectTransform)panel.transform, c, new Vector2(0f, 10f), new Vector2(w, 1180f));
                UiKit.Place(title.rectTransform, top, new Vector2(0f, -18f), new Vector2(w - 2 * pad, 56f));
                title.fontSize = 40;
                UiKit.Place((RectTransform)tabBasic.transform, top, new Vector2(-150f, -82f), new Vector2(280f, 62f));
                UiKit.Place((RectTransform)tabCampaign.transform, top, new Vector2(150f, -82f), new Vector2(280f, 62f));
                float x = pad, cw = w - 2 * pad;
                float y = -158f;
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
                float cy = -170f;
                ToggleRow(classicLabel, classicNote, classicButton, tl, x, cy, w, pad, rowH + 6f, 30, 22, true);
                UiKit.Place(classicDetail.rectTransform, tl, tl, new Vector2(x, cy - 92f), new Vector2(cw, 150f));
                classicDetail.fontSize = 24;
                cy -= 270f;
                ToggleRow(eraseLabel, eraseNote, eraseButton, tl, x, cy, w, pad, rowH + 6f, 30, 22, true);
                UiKit.Place(campaignStatus.rectTransform, tl, tl, new Vector2(x, cy - 96f), new Vector2(cw, 40f));
                campaignStatus.fontSize = 26;
                UiKit.Place((RectTransform)confirmBox.transform, c, Vector2.zero, new Vector2(640f, 380f));
                UiKit.Place(confirmText.rectTransform, top, new Vector2(0f, -40f), new Vector2(580f, 160f));
                confirmText.fontSize = 32;
                UiKit.Place((RectTransform)confirmYes.transform, new Vector2(0.5f, 0f), new Vector2(-150f, 36f), new Vector2(260f, 90f));
                UiKit.Place((RectTransform)confirmNo.transform, new Vector2(0.5f, 0f), new Vector2(150f, 36f), new Vector2(260f, 90f));
            }
            foreach (var b in speedButtons) b.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 32 : 24;
            foreach (var b in new[] { muteButton, backgroundButton, shakeButton, classicButton, eraseButton })
                b.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 30 : 24;
            foreach (var b in new[] { tabBasic, tabCampaign, confirmYes, confirmNo })
                b.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 30 : 22;
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
            _open = true;
            confirmDim.gameObject.SetActive(false);
            _confirmStage = 0;
            campaignStatus.text = "";
            ShowTab(false);
            EventSystem.current?.SetSelectedGameObject(closeButton.gameObject);
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            _open = false;
            _closedFrame = Time.frameCount;
            _confirmStage = 0;
            Closed?.Invoke();
        }

        void OnDisable()
        {
            if (_open && !gameObject.activeSelf) _closedFrame = Time.frameCount;
            _open = false;
        }

        public void Refresh() => Refresh(true);

        void Refresh(bool _)
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

            // 星図巡り（対戦の途中などで変えられないときは押せない）
            bool can = Campaign != null && Campaign.CanEdit;
            SetOnOff(classicButton, Campaign != null && Campaign.ClassicMode);
            classicButton.interactable = can;
            bool has = Campaign != null && Campaign.HasSave;
            eraseButton.interactable = can && has;
            if (string.IsNullOrEmpty(campaignStatus.text) || campaignStatus.text == UiText.NoSaveYet)
                campaignStatus.text = has ? "" : UiText.NoSaveYet;
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
