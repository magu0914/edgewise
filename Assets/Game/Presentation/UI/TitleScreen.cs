using System;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>タイトル画面: ロゴ（仮題）と「はじめる」「設定」「カード図鑑」「クレジット」。背景の星空は常に動き、ロゴの背後で扇のカードが回る（TitleFan）。</summary>
    public sealed class TitleScreen : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] RawImage halo;
        [SerializeField] TextMeshProUGUI logo;
        [SerializeField] TextMeshProUGUI subtitle;
        [SerializeField] Button startButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button dexButton;
        [SerializeField] Button creditsButton;
        [SerializeField] TextMeshProUGUI hint;
        [SerializeField] TextMeshProUGUI homage;

        public event Action StartPressed;
        public event Action SettingsPressed;
        public event Action DexPressed;
        public event Action CreditsPressed;

        public Button StartButton => startButton;
        public Button SettingsButton => settingsButton;
        public Button DexButton => dexButton;
        public Button CreditsButton => creditsButton;

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
            startButton = UiKit.Button(root, "StartButton", a, UiText.Start, 34, out _, new Color(0.22f, 0.30f, 0.55f));
            settingsButton = UiKit.Button(root, "TitleSettingsButton", a, UiText.MenuSettings, 26, out _);
            dexButton = UiKit.Button(root, "DexButton", a, UiText.TitleDex, 26, out _);
            creditsButton = UiKit.Button(root, "CreditsButton", a, UiText.TitleCredits, 26, out _);
            hint = UiKit.Label(root, "Hint", a, UiText.TitleHint, 18, Theme.InkDim);
            homage = UiKit.Label(root, "Homage", a, UiText.Homage, 15, new Color(0.6f, 0.62f, 0.72f, 0.9f));
            ApplyLayout(false);
        }

        void Awake()
        {
            if (halo != null) halo.texture = SoftEllipse(128, 64);
            startButton.onClick.AddListener(() => StartPressed?.Invoke());
            settingsButton.onClick.AddListener(() => SettingsPressed?.Invoke());
            dexButton.onClick.AddListener(() => DexPressed?.Invoke());
            creditsButton.onClick.AddListener(() => CreditsPressed?.Invoke());
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            if (halo != null) UiKit.Place(halo.rectTransform, c, new Vector2(0f, portrait ? 170f : 72f), portrait ? new Vector2(760f, 360f) : new Vector2(1240f, 360f));
            UiKit.Place(logo.rectTransform, c, new Vector2(0f, portrait ? 220f : 120f), new Vector2(1200f, 180f));
            logo.fontSize = portrait ? 92 : 128;
            logo.characterSpacing = portrait ? 8f : 18f;
            UiKit.Place(subtitle.rectTransform, c, new Vector2(0f, portrait ? 110f : 20f), new Vector2(680f, 50f));
            UiKit.Place((RectTransform)startButton.transform, c, new Vector2(0f, portrait ? -120f : -110f), new Vector2(300f, 78f));
            // 「はじめる」の下に 設定・カード図鑑・クレジット を 1 列に並べる
            var subButtons = new[] { settingsButton, dexButton, creditsButton };
            float bw = portrait ? 210f : 200f, gap = portrait ? 16f : 20f;
            for (int i = 0; i < subButtons.Length; i++)
            {
                float x = (i - 1) * (bw + gap);
                UiKit.Place((RectTransform)subButtons[i].transform, c, new Vector2(x, portrait ? -222f : -196f), new Vector2(bw, portrait ? 72f : 56f));
                subButtons[i].GetComponentInChildren<TextMeshProUGUI>().fontSize = portrait ? 30 : 24;
            }
            UiKit.Place(hint.rectTransform, c, new Vector2(0f, portrait ? -300f : -258f), new Vector2(680f, 30f));
            UiKit.Place(homage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(portrait ? 660f : 1100f, 60f));
        }

        public void Show()
        {
            gameObject.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(startButton.gameObject);
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
