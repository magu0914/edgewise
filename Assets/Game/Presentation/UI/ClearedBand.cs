using System.Threading;
using Edgewise.Core.Campaign;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Campaign;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 星域を巡り終えたとき（主に初めて勝ったとき。DESIGN §12-11）の帯:「〈星域名〉 を巡り終えた」が画面を横切る帯に出て、光が走る。
    /// タップか時間で消え、星図に戻って次の星域の鍵が外れる（StarMapScreen.PlayUnlockAsync）。
    /// </summary>
    public sealed class ClearedBand : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] Button tapArea;
        [SerializeField] Image band;
        [SerializeField] Image lineTop;
        [SerializeField] Image lineBottom;
        [SerializeField] RawImage shine;
        [SerializeField] TextMeshProUGUI text;
        [SerializeField] TextMeshProUGUI sub;

        bool _tapped;

        public bool IsOpen => gameObject.activeSelf;
        public string Text => text.text;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            var dim = UiKit.Fill(root, "ClearedTap", new Color(0f, 0f, 0.02f, 0.45f));
            tapArea = dim.gameObject.AddComponent<Button>();
            tapArea.transition = Selectable.Transition.None;
            band = UiKit.Fill(root, "Band", new Color(0.05f, 0.05f, 0.10f, 0.94f), false);
            lineTop = UiKit.Fill(root, "LineTop", Theme.Gold, false);
            lineBottom = UiKit.Fill(root, "LineBottom", Theme.Gold, false);
            shine = UiKit.Rect(root, "Shine").gameObject.AddComponent<RawImage>();
            shine.raycastTarget = false;
            text = UiKit.Label(root, "Text", a, "", 64, Theme.Gold, TextAlignmentOptions.Center, true);
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 24;
            text.fontSizeMax = 64;
            sub = UiKit.Label(root, "Sub", a, "", 26, Theme.Ink, TextAlignmentOptions.Center, true);
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        void Awake()
        {
            if (shine != null) shine.texture = UiGlyphs.Glow;
            tapArea.onClick.AddListener(() => _tapped = true);
        }

        public void ApplyLayout(bool portrait)
        {
            float w = portrait ? 720f : 1280f;
            float h = portrait ? 240f : 190f;
            var c = new Vector2(0.5f, 0.5f);
            UiKit.Place(band.rectTransform, c, new Vector2(0f, 20f), new Vector2(w * 2f, h));
            UiKit.Place(lineTop.rectTransform, c, new Vector2(0f, 20f + h * 0.5f), new Vector2(w * 2f, 3f));
            UiKit.Place(lineBottom.rectTransform, c, new Vector2(0f, 20f - h * 0.5f), new Vector2(w * 2f, 3f));
            UiKit.Place(shine.rectTransform, c, new Vector2(0f, 20f), new Vector2(h * 1.3f, h * 0.95f));
            UiKit.Place(text.rectTransform, c, new Vector2(0f, portrait ? 46f : 40f), new Vector2(w - 60f, 90f));
            text.fontSizeMax = portrait ? 56 : 64;
            UiKit.Place(sub.rectTransform, c, new Vector2(0f, portrait ? -40f : -30f), new Vector2(w - 60f, 40f));
            sub.fontSize = portrait ? 30 : 26;
        }

        public void Skip() => _tapped = true;

        /// <summary>帯を出し、タップか時間で消えるまで待つ。</summary>
        public async Awaitable PlayAsync(Region region, Region unlocked, float tf, CancellationToken ct)
        {
            gameObject.SetActive(true);
            _tapped = false;
            text.text = CampaignTexts.Cleared(region);
            sub.text = unlocked != null ? UiText.MapUnlocked : UiText.ClearedAll;
            var color = CampaignTheme.RegionColor(region);
            lineTop.color = lineBottom.color = color;
            if (AutomationLog.Enabled) AutomationLog.Write("cleared " + region.Id);
            SfxBank.Instance?.Play(SfxKind.Unlock, 3f);
            SfxBank.Instance?.Play(SfxKind.RewardNew);
            var brt = band.rectTransform;
            var trt = text.rectTransform;
            var srt = shine.rectTransform;
            try
            {
                await Tweens.Run(0.35f * tf, k =>
                {
                    brt.localScale = new Vector3(1f, k, 1f);
                    lineTop.rectTransform.localScale = new Vector3(k, 1f, 1f);
                    lineBottom.rectTransform.localScale = new Vector3(k, 1f, 1f);
                    text.alpha = k;
                    sub.alpha = k;
                    trt.localScale = Vector3.one * Mathf.LerpUnclamped(1.4f, 1f, k);
                }, ct, Ease.OutCubic);
                if (AutomationLog.Enabled) AutomationLog.Write("cleared shown");
                // 光が帯を左から右へ走る
                float t = 0f;
                float hold = Mathf.Max(0.01f, 2.4f * tf);
                float w = ((RectTransform)transform).rect.width;
                while (t < hold && !_tapped && !KeyPressed())
                {
                    float k = (t % 1.6f) / 1.6f;
                    srt.anchoredPosition = new Vector2(Mathf.Lerp(-w * 0.6f, w * 0.6f, k), 20f);
                    shine.color = new Color(color.r, color.g, color.b, 0.3f * Mathf.Sin(Mathf.PI * k));
                    t += Time.unscaledDeltaTime;
                    await Awaitable.NextFrameAsync(ct);
                }
                await Tweens.Run(0.25f * tf, k =>
                {
                    brt.localScale = new Vector3(1f, 1f - k, 1f);
                    text.alpha = 1f - k;
                    sub.alpha = 1f - k;
                    shine.color = Color.clear;
                }, ct, Ease.InCubic);
            }
            finally
            {
                if (this != null) gameObject.SetActive(false);
            }
        }

        static bool KeyPressed()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame);
        }
    }
}
