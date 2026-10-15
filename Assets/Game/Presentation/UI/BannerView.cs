using System.Threading;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;

namespace Edgewise.Game.UI
{
    /// <summary>画面中央の大きな文字（SAME! / PLUS! / COMBO ×n / 先攻表示 / SUDDEN DEATH）。弾むように出て消える。</summary>
    public sealed class BannerView : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] TextMeshProUGUI text;
        [SerializeField] TextMeshProUGUI sub;

        int _serial;

        public string CurrentText => text != null && text.gameObject.activeSelf ? text.text : "";

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            text = UiKit.Label(root, "Text", a, "", 120, Theme.Gold, TextAlignmentOptions.Center, true);
            text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            sub = UiKit.Label(root, "Sub", a, "", 34, Theme.Ink, TextAlignmentOptions.Center, true);
            ApplyLayout(false);
            text.gameObject.SetActive(false);
            sub.gameObject.SetActive(false);
        }

        public void ApplyLayout(bool portrait)
        {
            UiKit.Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, portrait ? 40f : 10f), new Vector2(1400f, 200f));
            UiKit.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, portrait ? -50f : -80f), new Vector2(1200f, 60f));
            text.fontSize = portrait ? 96 : 120;
        }

        /// <summary>文字を出して、消えるまで待つ。</summary>
        public Awaitable Show(string message, Color color, float duration, CancellationToken ct, string subText = null, float scale = 1f) =>
            Tweens.Await(Play(message, color, duration, subText, scale), ct);

        /// <summary>文字を出す（待たない）。duration 秒で弾んで出て、消える（duration が 0 なら出さない）。</summary>
        public Tween Play(string message, Color color, float duration, string subText = null, float scale = 1f)
        {
            int id = ++_serial;
            if (duration <= 0f)
            {
                Hide();
                return null;
            }
            text.gameObject.SetActive(true);
            text.text = message;
            text.color = color;
            bool hasSub = !string.IsNullOrEmpty(subText);
            sub.gameObject.SetActive(hasSub);
            if (hasSub) sub.text = subText;
            var rt = text.rectTransform;
            float inT = 0.28f;
            return Tweens.Play(duration, k =>
            {
                if (id != _serial) return;
                float pop = k < inT ? Easing.Evaluate(Ease.OutBack, k / inT) : 1f;
                float fade = k > 0.75f ? 1f - (k - 0.75f) / 0.25f : 1f;
                rt.localScale = Vector3.one * (0.4f + 0.6f * pop) * scale;
                text.alpha = Mathf.Clamp01(fade * Mathf.Min(1f, k / 0.08f));
                if (hasSub) sub.alpha = text.alpha;
                if (k >= 1f) Hide();
            }, Ease.Linear, 0f, this, "banner");
        }

        public void Hide()
        {
            if (text == null) return;
            text.gameObject.SetActive(false);
            sub.gameObject.SetActive(false);
        }
    }
}
