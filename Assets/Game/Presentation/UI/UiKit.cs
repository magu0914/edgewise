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

        /// <summary>
        /// 横向きの uGUI Slider（背景・塗り・つまみ）。steps 段の整数値（wholeNumbers）にする。
        /// 例: steps = 20 で 0..20（音量の 5% 刻み）。キーボードの ←→ でも 1 段ずつ動く。
        /// </summary>
        public static Slider Slider(Transform parent, string name, GameAssets a, int steps, Color fill)
        {
            var root = Rect(parent, name);
            var slider = root.gameObject.AddComponent<Slider>();

            var bg = Panel(root, "Background", a, new Color(0.06f, 0.07f, 0.14f, 1f));
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = new Vector2(0f, 0.3f);
            bgRt.anchorMax = new Vector2(1f, 0.7f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var fillArea = Rect(root, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.3f);
            fillArea.anchorMax = new Vector2(1f, 0.7f);
            fillArea.offsetMin = new Vector2(4f, 3f);
            fillArea.offsetMax = new Vector2(-4f, -3f);
            var fillImg = Panel(fillArea, "Fill", a, fill, false);
            var fillRt = (RectTransform)fillImg.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            // つまみは指で押しやすいように太め（幅は ApplyLayout 側で SetHandleWidth で変える）
            var handleArea = Rect(root, "Handle Slide Area");
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(16f, 0f);
            handleArea.offsetMax = new Vector2(-16f, 0f);
            var handle = Panel(handleArea, "Handle", a, new Color(0.96f, 0.86f, 0.55f));
            var handleRt = (RectTransform)handle.transform;
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            handleRt.sizeDelta = new Vector2(32f, 0f);

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = steps;
            slider.wholeNumbers = true;
            var colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.selectedColor = new Color(1.25f, 1.2f, 1.0f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.colorMultiplier = 1.3f;
            colors.fadeDuration = 0.06f;
            slider.colors = colors;
            return slider;
        }

        /// <summary>Slider のつまみの幅とつまみが動く範囲の端の余白を合わせる。</summary>
        public static void SetHandleWidth(Slider s, float width)
        {
            var area = (RectTransform)s.handleRect.parent;
            area.offsetMin = new Vector2(width * 0.5f, 0f);
            area.offsetMax = new Vector2(-width * 0.5f, 0f);
            s.handleRect.sizeDelta = new Vector2(width, 0f);
        }
    }

    /// <summary>画面の向きが変わったときに配置をやり直す UI。</summary>
    public interface IResponsiveUi
    {
        void ApplyLayout(bool portrait);
    }
}
