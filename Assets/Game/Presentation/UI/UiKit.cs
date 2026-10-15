using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// uGUI の部品をコードで組み立てる小さな道具箱。Editor の ProjectSetup がシーンを生成するときに使う
    /// （GUI のエディタを使えないため、画面の構成はすべてコードで決める）。
    /// </summary>
    public static class UiKit
    {
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>anchor と pivot を同じ点にして、そこからの位置と大きさで置く。</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        /// <summary>anchor の点を基準に、pivot を別に指定して置く。</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static Image Panel(Transform parent, string name, GameAssets a, Color color, bool raycast = true)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = a.roundRect;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Image Fill(Transform parent, string name, Color color, bool raycast = true)
        {
            var rt = Rect(parent, name);
            Stretch(rt);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, GameAssets a, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool outline = false)
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = a.font;
            if (outline && a.fontOutline != null) t.fontSharedMaterial = a.fontOutline;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string name, GameAssets a, string label, float fontSize, out TextMeshProUGUI text,
            Color? color = null)
        {
            var img = Panel(parent, name, a, color ?? Theme.Button);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.3f, 1f);
            colors.selectedColor = new Color(1.3f, 1.25f, 1.05f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
            colors.colorMultiplier = 1.4f;
            colors.fadeDuration = 0.06f;
            b.colors = colors;
            text = Label(img.transform, "Label", a, label, fontSize, Theme.ButtonText);
            Stretch(text.rectTransform, 8, 2, 8, 2);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return b;
        }
    }

    /// <summary>画面の向きが変わったときに配置をやり直す UI。</summary>
    public interface IResponsiveUi
    {
        void ApplyLayout(bool portrait);
    }
}
