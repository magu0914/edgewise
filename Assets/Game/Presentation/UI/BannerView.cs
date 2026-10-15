using System.Threading;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 弾むように出て消える文字。先攻表示・SUDDEN DEATH は画面中央に大きく、SAME! / PLUS! / COMBO は
    /// 置いたカードの少し上に小さめに出す（盤と光の線を隠さないように）。
    /// </summary>
    public sealed class BannerView : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] TextMeshProUGUI text;
        [SerializeField] TextMeshProUGUI sub;

        int _serial;
        bool _portrait;

        /// <summary>カードの上に出す文字の大きさ（中央の文字に対する倍率）。</summary>
        public float LocalScale = 0.6f;

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
            _portrait = portrait;
            UiKit.Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, portrait ? 40f : 10f), new Vector2(1400f, 200f));
            UiKit.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, portrait ? -50f : -80f), new Vector2(1200f, 60f));
            text.fontSize = portrait ? 96 : 120;
        }

        /// <summary>文字を出して、消えるまで待つ。</summary>
        public Awaitable Show(string message, Color color, float duration, CancellationToken ct, string subText = null, float scale = 1f,
            Vector2? screenPos = null) =>
            Tweens.Await(Play(message, color, duration, subText, scale, screenPos), ct);

        /// <summary>文字を出す（待たない）。duration 秒で弾んで出て、消える（duration が 0 なら出さない）。</summary>
        public Tween Play(string message, Color color, float duration, string subText = null, float scale = 1f, Vector2? screenPos = null)
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
            if (screenPos.HasValue)
            {
                // 画面上の点（左下原点のピクセル）の位置に出す。画面の外・上部の表示にかからないように寄せる
                var root = (RectTransform)transform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos.Value, null, out var local);
                var half = root.rect.size * 0.5f;
                float w = Mathf.Min(text.GetPreferredValues(message).x * scale * LocalScale, root.rect.width) * 0.5f;
                local.x = Mathf.Clamp(local.x, -half.x + w + 10f, half.x - w - 10f);
                local.y = Mathf.Clamp(local.y, -half.y + 80f, half.y - (_portrait ? 150f : 130f));
                rt.anchoredPosition = local;
                scale *= LocalScale;
            }
            else
            {
                rt.anchoredPosition = new Vector2(0f, _portrait ? 40f : 10f);
            }
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
