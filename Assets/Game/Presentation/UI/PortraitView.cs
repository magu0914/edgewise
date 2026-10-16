using Edgewise.Core.Campaign;
using Edgewise.Game.Campaign;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 対戦相手の立ち絵（DESIGN §12-7）。5b-1 では仮の表示: 星域の色の円に、名前の頭文字と（主なら）冠。
    /// 5b-2 で本物の立ち絵（人物のシルエット＋星雲＋星座）に差し替えるときは、この部品の中身だけを替える
    /// （呼び出し側は Show(Opponent) / Hide() と大きさ（RectTransform）しか知らない）。
    /// </summary>
    public sealed class PortraitView : MonoBehaviour
    {
        [SerializeField] RawImage glow;
        [SerializeField] RawImage disc;
        [SerializeField] RawImage ring;
        [SerializeField] RawImage crown;
        [SerializeField] TextMeshProUGUI initial;

        public Opponent Opponent { get; private set; }
        public string InitialText => initial != null ? initial.text : "";
        public bool CrownShown => crown != null && crown.gameObject.activeSelf;

        /// <summary>parent の下に作る（Editor の Build から呼ぶ）。大きさは呼び出し側が RectTransform で決める。</summary>
        public static PortraitView Create(Transform parent, string name, GameAssets a)
        {
            var root = UiKit.Rect(parent, name);
            var p = root.gameObject.AddComponent<PortraitView>();
            p.glow = Img(root, "Glow", -0.28f);
            p.disc = Img(root, "Disc", 0f);
            p.ring = Img(root, "Ring", 0f);
            p.initial = UiKit.Label(root, "Initial", a, "", 40, Theme.Ink, TextAlignmentOptions.Center);
            UiKit.Stretch(p.initial.rectTransform);
            p.initial.fontStyle = FontStyles.Bold;
            p.initial.enableAutoSizing = true;
            p.initial.fontSizeMin = 10;
            p.initial.fontSizeMax = 200;
            // 円の内側に収める（縁に触れないように）
            p.initial.rectTransform.anchorMin = new Vector2(0.2f, 0.18f);
            p.initial.rectTransform.anchorMax = new Vector2(0.8f, 0.82f);
            p.initial.margin = new Vector4(0f, 0f, 0f, 0f);
            p.crown = Img(root, "Crown", 0f);
            var crt = p.crown.rectTransform;
            crt.anchorMin = new Vector2(0.28f, 0.80f);
            crt.anchorMax = new Vector2(0.72f, 1.14f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            p.crown.gameObject.SetActive(false);
            return p;
        }

        static RawImage Img(RectTransform parent, string name, float outset)
        {
            var rt = UiKit.Rect(parent, name);
            rt.anchorMin = new Vector2(-outset, -outset);
            rt.anchorMax = new Vector2(1f + outset, 1f + outset);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        void Awake()
        {
            if (glow != null) glow.texture = UiGlyphs.Glow;
            if (disc != null) disc.texture = UiGlyphs.Circle;
            if (ring != null) ring.texture = UiGlyphs.Ring;
            if (crown != null) crown.texture = UiGlyphs.Crown;
        }

        public void Show(Opponent opponent)
        {
            Opponent = opponent;
            if (opponent == null)
            {
                Hide();
                return;
            }
            Show(CampaignTexts.Initial(opponent), CampaignTheme.RegionColor(opponent.RegionId), opponent.IsBoss);
        }

        public void Show(string initialText, Color color, bool boss)
        {
            gameObject.SetActive(true);
            if (glow != null && glow.texture == null) Awake();
            glow.color = new Color(color.r, color.g, color.b, 0.35f);
            disc.color = Color.Lerp(new Color(0.05f, 0.06f, 0.14f), color, 0.38f);
            ring.color = color;
            initial.text = initialText;
            initial.color = Color.Lerp(color, Color.white, 0.55f);
            crown.gameObject.SetActive(boss);
            crown.color = Theme.Gold;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
