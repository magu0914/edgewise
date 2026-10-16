using Edgewise.Core.Campaign;
using Edgewise.Game.Campaign;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 対戦相手の立ち絵（DESIGN §12-7。5b-2）: game-icons.net の人物のシルエット（SDF。CardArtAtlas の "@相手の ID"）を、
    /// 星域の色の星雲と星座の点・線で描く（ArtGraphic / Edgewise/UIArt）。
    /// - 小さな印（Large = false）: 円の中に立ち絵、星域の色の輪と光、主には冠。星域の画面・デッキ編集・会話の帯・対戦の上部・取られる演出で使う。
    /// - 大きな立ち絵（Large = true）: 柔らかい光の中に浮かぶ立ち絵と星座（会話の画面の上の空間）。
    /// アトラスに絵が無いとき（古いアセットなど）は 5b-1 の仮の表示（名前の頭文字）に戻る。
    /// </summary>
    public sealed class PortraitView : MonoBehaviour
    {
        [SerializeField] RawImage glow;
        [SerializeField] ArtGraphic art;
        [SerializeField] RawImage ring;
        [SerializeField] RawImage crown;
        [SerializeField] TextMeshProUGUI initial;
        [SerializeField] bool large;

        public Opponent Opponent { get; private set; }
        public string InitialText => initial != null && initial.gameObject.activeSelf ? initial.text : "";
        public bool CrownShown => crown != null && crown.gameObject.activeSelf;
        public bool Large => large;

        /// <summary>描いている立ち絵の ID（"@相手の ID"）。絵が無ければ null。</summary>
        public string ArtId => art != null && art.gameObject.activeSelf ? art.ArtId : null;

        public ArtGraphic Art => art;

        /// <summary>parent の下に作る（Editor の Build から呼ぶ）。大きさは呼び出し側が RectTransform で決める。</summary>
        public static PortraitView Create(Transform parent, string name, GameAssets a, bool large = false)
        {
            var root = UiKit.Rect(parent, name);
            var p = root.gameObject.AddComponent<PortraitView>();
            p.large = large;
            if (!large) p.glow = Img(root, "Glow", -0.28f);
            p.art = ArtGraphic.Create(root, "Art", a);
            Stretch(p.art.rectTransform, 0f);
            if (!large) p.ring = Img(root, "Ring", 0f);
            p.initial = UiKit.Label(root, "Initial", a, "", 40, Theme.Ink, TextAlignmentOptions.Center);
            p.initial.fontStyle = FontStyles.Bold;
            p.initial.enableAutoSizing = true;
            p.initial.fontSizeMin = 10;
            p.initial.fontSizeMax = 200;
            p.initial.rectTransform.anchorMin = new Vector2(0.2f, 0.18f);
            p.initial.rectTransform.anchorMax = new Vector2(0.8f, 0.82f);
            p.initial.rectTransform.offsetMin = Vector2.zero;
            p.initial.rectTransform.offsetMax = Vector2.zero;
            p.initial.margin = Vector4.zero;
            p.initial.gameObject.SetActive(false);
            if (!large)
            {
                p.crown = Img(root, "Crown", 0f);
                var crt = p.crown.rectTransform;
                crt.anchorMin = new Vector2(0.28f, 0.80f);
                crt.anchorMax = new Vector2(0.72f, 1.14f);
                crt.offsetMin = Vector2.zero;
                crt.offsetMax = Vector2.zero;
                p.crown.gameObject.SetActive(false);
            }
            return p;
        }

        static void Stretch(RectTransform rt, float outset)
        {
            rt.anchorMin = new Vector2(-outset, -outset);
            rt.anchorMax = new Vector2(1f + outset, 1f + outset);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static RawImage Img(RectTransform parent, string name, float outset)
        {
            var rt = UiKit.Rect(parent, name);
            Stretch(rt, outset);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        void Awake()
        {
            if (glow != null) glow.texture = UiGlyphs.Glow;
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
            gameObject.SetActive(true);
            if (ring != null && ring.texture == null) Awake();
            var color = CampaignTheme.RegionColor(opponent.RegionId);
            string id = CardArtTable.PortraitId(opponent.Id);
            bool hasArt = art != null && art.Atlas != null && art.Atlas.Find(id) != null;
            if (art != null)
            {
                art.gameObject.SetActive(hasArt);
                if (hasArt)
                {
                    if (large) art.Set(id, ArtGraphic.Shape.Soft, color, ArtGraphic.PaletteTint, new Vector2(0.5f, 0.5f), 0.9f, true);
                    else art.Set(id, ArtGraphic.Shape.Circle, color, ArtGraphic.PaletteTint, new Vector2(0.5f, 0.47f), 0.82f, true);
                }
            }
            if (glow != null) glow.color = new Color(color.r, color.g, color.b, 0.35f);
            if (ring != null) ring.color = color;
            initial.gameObject.SetActive(!hasArt);
            initial.text = CampaignTexts.Initial(opponent);
            initial.color = Color.Lerp(color, Color.white, 0.55f);
            if (crown != null)
            {
                crown.gameObject.SetActive(opponent.IsBoss);
                crown.color = Theme.Gold;
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
