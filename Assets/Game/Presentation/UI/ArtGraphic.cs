using Edgewise.Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// uGUI でカード絵・立ち絵を描く（5b-2）。カード絵の SDF アトラス（CardArtAtlas）と Edgewise/UIArt シェーダーを使う。
    /// 1 つのメッシュに「本体（星雲＋シルエット）」と、必要なら「星」「線」（星座）の四角形を並べ、頂点の値で描き分ける。
    /// マテリアルとテクスチャを共有するので、小さなカードが何十枚あっても描画はまとまる（デッキ編集で 60FPS を保つため）。
    /// Canvas の additionalShaderChannels に TexCoord1〜3 が必要（SceneBuilder が設定する）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArtGraphic : MaskableGraphic
    {
        public enum Shape
        {
            /// <summary>角丸の四角（小さなカード）。</summary>
            RoundRect = 0,

            /// <summary>円（立ち絵の小さな印）。</summary>
            Circle = 1,

            /// <summary>柔らかい光の中に浮かぶ（会話の大きな立ち絵）。</summary>
            Soft = 2,
        }

        /// <summary>配色: 系統（0..3）、色の明暗（立ち絵）。</summary>
        public const float PaletteNone = -1f;
        public const float PaletteTint = 4f;

        [SerializeField] CardArtAtlas atlas;

        string _artId;
        Shape _shape;
        float _palette = PaletteNone;
        Vector2 _center = new Vector2(0.5f, 0.5f);
        float _artScale = 0.8f;
        bool _constellation;
        float _seed;

        /// <summary>描いているアトラスの ID（カード ID、立ち絵は "@相手の ID"）。描いていなければ null。</summary>
        public string ArtId => _artId;
        public Shape CurrentShape => _shape;
        public bool HasConstellation => _constellation;

        public CardArtAtlas Atlas
        {
            get => atlas;
            set
            {
                atlas = value;
                SetAllDirty();
            }
        }

        public override Texture mainTexture => atlas != null && atlas.atlas != null ? atlas.atlas : s_WhiteTexture;

        /// <summary>parent の下に作る（Editor の Build・実行時のどちらからでも）。</summary>
        public static ArtGraphic Create(Transform parent, string name, GameAssets a)
        {
            var rt = UiKit.Rect(parent, name);
            if (rt.GetComponent<CanvasRenderer>() == null) rt.gameObject.AddComponent<CanvasRenderer>();
            var g = rt.gameObject.AddComponent<ArtGraphic>();
            g.atlas = a.cardArt;
            g.material = a.uiArtMaterial;
            g.raycastTarget = false;
            return g;
        }

        /// <summary>
        /// 絵を設定する。center と artScale は四角形の高さを 1 とした単位（center は 0..1）。constellation なら星座の点と線も描く。
        /// tint は星雲と光の色、palette は系統（0..3）か PaletteTint（tint の明暗）。
        /// </summary>
        public void Set(string artId, Shape shape, Color tint, float palette, Vector2 center, float artScale, bool constellation)
        {
            _artId = artId;
            _shape = shape;
            _palette = palette;
            _center = center;
            _artScale = artScale;
            _constellation = constellation;
            _seed = artId == null ? 0f : (Hash(artId) % 997) / 997f;
            color = tint;
            enabled = true;
            SetVerticesDirty();
        }

        public void Clear()
        {
            _artId = null;
            SetVerticesDirty();
        }

        static uint Hash(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s) h = (h ^ c) * 16777619u;
            return h;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (atlas == null || string.IsNullOrEmpty(_artId)) return;
            var entry = atlas.Find(_artId);
            if (entry == null) return;
            var r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            float aspect = r.width / r.height;
            var c = color;

            // 本体
            var cell = entry.uvRect;
            var uv2 = new Vector4(0f, _palette, (float)_shape, _seed);
            var uv3 = new Vector4(_center.x, _center.y, _artScale, aspect);
            AddQuad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), c, cell, uv2, uv3);

            if (!_constellation) return;
            // 星座: セル内の 0..1 の座標 → 四角形の中の位置
            Vector2 ToRect(Vector2 q) => new Vector2(
                r.xMin + (_center.x + (q.x - 0.5f) * _artScale / aspect) * r.width,
                r.yMin + (_center.y + (q.y - 0.5f) * _artScale) * r.height);
            float unit = r.height * _artScale;
            if (entry.lines != null)
            {
                float w = Mathf.Max(1.5f, unit * 0.012f);
                foreach (var l in entry.lines)
                {
                    var a = ToRect(new Vector2(l.x, l.y));
                    var b = ToRect(new Vector2(l.z, l.w));
                    var d = b - a;
                    if (d.sqrMagnitude < 1e-4f) continue;
                    var n = new Vector2(-d.y, d.x).normalized * w;
                    AddQuad(vh, a - n, b - n, b + n, a + n, c, cell, new Vector4(2f, 0f, 0f, 0f), Vector4.zero);
                }
            }
            if (entry.stars != null)
            {
                foreach (var s in entry.stars)
                {
                    var p = ToRect(new Vector2(s.x, s.y));
                    float half = Mathf.Max(3f, unit * 0.05f * s.z);
                    AddQuad(vh, p + new Vector2(-half, -half), p + new Vector2(half, -half), p + new Vector2(half, half), p + new Vector2(-half, half), c, cell,
                        new Vector4(1f, 0f, 0f, 0f), new Vector4(s.w, s.z, 0f, 0f));
                }
            }
        }

        static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color32 c, Vector4 uv1, Vector4 uv2, Vector4 uv3)
        {
            int i = vh.currentVertCount;
            vh.AddVert(p0, c, new Vector4(0f, 0f, 0f, 0f), uv1, uv2, uv3, Vector3.back, new Vector4(1f, 0f, 0f, -1f));
            vh.AddVert(p1, c, new Vector4(1f, 0f, 0f, 0f), uv1, uv2, uv3, Vector3.back, new Vector4(1f, 0f, 0f, -1f));
            vh.AddVert(p2, c, new Vector4(1f, 1f, 0f, 0f), uv1, uv2, uv3, Vector3.back, new Vector4(1f, 0f, 0f, -1f));
            vh.AddVert(p3, c, new Vector4(0f, 1f, 0f, 0f), uv1, uv2, uv3, Vector3.back, new Vector4(1f, 0f, 0f, -1f));
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
