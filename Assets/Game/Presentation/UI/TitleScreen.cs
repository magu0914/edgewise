using System;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// タイトル画面: ロゴ（仮題）と主ボタン「星図巡り」（続きがあれば下に進み具合「星域 2・収集 18/50」）、
    /// その下に「フリー対戦」「遊び方」「カード図鑑」「設定」「クレジット」。背景の星空は常に動き、ロゴの背後で扇のカードが回る（TitleFan）。
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] RawImage halo;
        [SerializeField] TextMeshProUGUI logo;
        [SerializeField] TextMeshProUGUI subtitle;
        [SerializeField] Button campaignButton;
        [SerializeField] TextMeshProUGUI progress;
        [SerializeField] Button startButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button dexButton;
        [SerializeField] Button creditsButton;
        [SerializeField] Button howToButton;
        [SerializeField] TextMeshProUGUI hint;
        [SerializeField] TextMeshProUGUI homage;

        public event Action CampaignPressed;

        /// <summary>フリー対戦（対戦設定へ）。</summary>
        public event Action StartPressed;
        public event Action SettingsPressed;
        public event Action DexPressed;
        public event Action CreditsPressed;
        public event Action HowToPressed;

        public Button CampaignButton => campaignButton;
        public string ProgressText => progress != null && progress.gameObject.activeSelf ? progress.text : "";

        /// <summary>フリー対戦のボタン（対戦設定へ）。</summary>
        public Button StartButton => startButton;
        public Button SettingsButton => settingsButton;
        public Button DexButton => dexButton;
        public Button CreditsButton => creditsButton;
        public Button HowToButton => howToButton;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            // ロゴと副題の背後の柔らかい暗い光（扇のカードの上に敷いて、文字のコントラストを保つ）。模様のテクスチャは実行時に作る
            halo = UiKit.Rect(root, "Halo").gameObject.AddComponent<RawImage>();
            halo.color = new Color(0.012f, 0.016f, 0.045f, 0.82f);
            halo.raycastTarget = false;
            logo = UiKit.Label(root, "Logo", a, UiText.Logo, 128, Theme.Gold, TextAlignmentOptions.Center, true);
            logo.fontStyle = FontStyles.Bold;
            logo.characterSpacing = 18f;
            logo.textWrappingMode = TextWrappingModes.NoWrap;
            subtitle = UiKit.Label(root, "Subtitle", a, UiText.Subtitle, 30, Theme.Ink);
            campaignButton = UiKit.Button(root, "CampaignButton", a, UiText.TitleCampaign, 36, out _, new Color(0.22f, 0.30f, 0.55f));
            progress = UiKit.Label(root, "Progress", a, "", 22, Theme.Gold);
            progress.textWrappingMode = TextWrappingModes.NoWrap;
            progress.gameObject.SetActive(false);
            startButton = UiKit.Button(root, "StartButton", a, UiText.TitleFree, 26, out _);
            settingsButton = UiKit.Button(root, "TitleSettingsButton", a, UiText.MenuSettings, 26, out _);
            dexButton = UiKit.Button(root, "DexButton", a, UiText.TitleDex, 26, out _);
            creditsButton = UiKit.Button(root, "CreditsButton", a, UiText.TitleCredits, 26, out _);
            howToButton = UiKit.Button(root, "HowToButton", a, UiText.TitleHowTo, 26, out _);
            hint = UiKit.Label(root, "Hint", a, UiText.TitleHint, 18, Theme.InkDim);
            homage = UiKit.Label(root, "Homage", a, UiText.Homage, 15, new Color(0.6f, 0.62f, 0.72f, 0.9f));
            ApplyLayout(false);
        }

        void Awake()
        {
            if (halo != null) halo.texture = SoftEllipse(128, 64);
            campaignButton.onClick.AddListener(() => CampaignPressed?.Invoke());
            startButton.onClick.AddListener(() => StartPressed?.Invoke());
            settingsButton.onClick.AddListener(() => SettingsPressed?.Invoke());
            dexButton.onClick.AddListener(() => DexPressed?.Invoke());
            creditsButton.onClick.AddListener(() => CreditsPressed?.Invoke());
            howToButton.onClick.AddListener(() => HowToPressed?.Invoke());
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            if (halo != null) UiKit.Place(halo.rectTransform, c, new Vector2(0f, portrait ? 170f : 72f), portrait ? new Vector2(760f, 360f) : new Vector2(1240f, 360f));
            UiKit.Place(logo.rectTransform, c, new Vector2(0f, portrait ? 220f : 120f), new Vector2(1200f, 180f));
            logo.fontSize = portrait ? 92 : 128;
            logo.characterSpacing = portrait ? 8f : 18f;
            UiKit.Place(subtitle.rectTransform, c, new Vector2(0f, portrait ? 110f : 20f), new Vector2(680f, 50f));
            UiKit.Place((RectTransform)campaignButton.transform, c, new Vector2(0f, portrait ? -100f : -86f), portrait ? new Vector2(420f, 96f) : new Vector2(340f, 80f));
            campaignButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 42 : 36;
            UiKit.Place(progress.rectTransform, c, new Vector2(0f, portrait ? -170f : -144f), new Vector2(600f, 32f));
            progress.fontSize = portrait ? 28 : 22;
            // 「星図巡り」の下に フリー対戦・遊び方・カード図鑑・設定・クレジット（横は 1 列、縦は 2 列で、余った最後の 1 つは中央）
            var subButtons = new[] { startButton, howToButton, dexButton, settingsButton, creditsButton };
            for (int i = 0; i < subButtons.Length; i++)
            {
                Vector2 pos, size;
                if (portrait)
                {
                    bool lastAlone = i == subButtons.Length - 1 && subButtons.Length % 2 == 1;
                    pos = new Vector2(lastAlone ? 0f : (i % 2 == 0 ? -1f : 1f) * 160f, -250f - (i / 2) * 92f);
                    size = new Vector2(300f, 76f);
                }
                else
                {
                    pos = new Vector2((i - (subButtons.Length - 1) * 0.5f) * 212f, -206f);
                    size = new Vector2(196f, 56f);
                }
                UiKit.Place((RectTransform)subButtons[i].transform, c, pos, size);
                subButtons[i].GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 30 : 24;
            }
            UiKit.Place(hint.rectTransform, c, new Vector2(0f, portrait ? -512f : -262f), new Vector2(680f, 30f));
            hint.fontSize = portrait ? 22 : 18;
            UiKit.Place(homage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(portrait ? 660f : 1100f, 60f));
        }

        public void Show()
        {
            gameObject.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(campaignButton.gameObject);
        }

        /// <summary>星図巡りの進み具合（続きがなければ null で隠す）。</summary>
        public void SetProgress(string text)
        {
            progress.gameObject.SetActive(!string.IsNullOrEmpty(text));
            progress.text = text ?? "";
            if (Edgewise.Game.App.AutomationLog.Enabled) Edgewise.Game.App.AutomationLog.Write("title progress " + (string.IsNullOrEmpty(text) ? "-" : text));
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>中心が濃く、楕円の縁へなめらかに消える白いテクスチャ（色は RawImage.color で付ける）。</summary>
        static Texture2D SoftEllipse(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TitleHalo" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float v = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, r));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(v * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
