using UnityEngine;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 小さな図形のテクスチャを実行時に作る（円・輪・柔らかい光・冠・錠前・光の筋）。外部の絵素材を使わず、アセットも増やさない。
    /// 一度作ったものは使い回す。白で描き、色は RawImage.color で付ける。
    /// </summary>
    public static class UiGlyphs
    {
        static Texture2D _circle, _ring, _glow, _crown, _lock, _rays, _line;

        public static Texture2D Circle => _circle != null ? _circle : _circle = Make("Circle", 128, (x, y) => Disc(x, y, 0.47f, 0.02f));

        public static Texture2D Ring => _ring != null ? _ring : _ring = Make("Ring", 128, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return Smooth(0.47f - r, 0.015f) * Smooth(r - 0.40f, 0.015f);
        });

        /// <summary>中心が明るく縁へ消える丸い光。</summary>
        public static Texture2D Glow => _glow != null ? _glow : _glow = Make("Glow", 128, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y) * 2f;
            return Mathf.Clamp01(Mathf.Exp(-r * r * 3.2f) - 0.04f) * 1.04f;
        });

        /// <summary>放射状の光の筋（NEW! の後ろ）。</summary>
        public static Texture2D Rays => _rays != null ? _rays : _rays = Make("Rays", 256, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y) * 2f;
            float a = Mathf.Atan2(y, x);
            float ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 6f)), 6f);
            float fade = Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
            return Mathf.Clamp01(ray * fade * 1.2f + Mathf.Exp(-r * r * 9f) * 0.6f);
        });

        /// <summary>横に長い柔らかい線（星図の道筋）。縦方向の中央が濃い。</summary>
        public static Texture2D Line => _line != null ? _line : _line = Make("Line", 32, (x, y) =>
        {
            float d = Mathf.Abs(y) * 2f;
            return Mathf.Clamp01(1f - d * d);
        });

        /// <summary>冠（主の印）。</summary>
        public static Texture2D Crown => _crown != null ? _crown : _crown = Make("Crown", 96, (x, y) =>
        {
            // x, y は -0.5..0.5（y は上が +）。土台の帯と 3 つの山
            float a = 0f;
            if (y > -0.30f && y < -0.12f && Mathf.Abs(x) < 0.40f) a = 1f;
            float top = -0.12f;
            float peaks = 0.22f + 0.18f * Mathf.Pow(Mathf.Abs(Mathf.Cos(x * Mathf.PI * 2.5f)), 1.5f);
            if (Mathf.Abs(x) < 0.40f && y >= top && y < peaks) a = 1f;
            // 山の先の玉
            foreach (float px in new[] { -0.4f, 0f, 0.4f })
            {
                float dx = x - px * 0.95f, dy = y - 0.42f;
                if (dx * dx + dy * dy < 0.06f * 0.06f) a = 1f;
            }
            return a;
        }, true);

        /// <summary>錠前（未解放の印）。</summary>
        public static Texture2D Lock => _lock != null ? _lock : _lock = Make("Lock", 96, (x, y) =>
        {
            float a = 0f;
            // 本体（角丸の四角）
            if (y < 0.02f && y > -0.42f && Mathf.Abs(x) < 0.32f) a = 1f;
            // つる（上半分の輪）
            float cy = 0.08f;
            float r = Mathf.Sqrt(x * x + (y - cy) * (y - cy));
            if (y > cy - 0.02f && r < 0.25f && r > 0.15f) a = 1f;
            if (y > -0.02f && y <= cy && Mathf.Abs(Mathf.Abs(x) - 0.20f) < 0.05f) a = 1f;
            // 鍵穴
            float kx = x, ky = y + 0.17f;
            if (kx * kx + ky * ky < 0.06f * 0.06f || (Mathf.Abs(kx) < 0.025f && ky < 0f && ky > -0.14f)) a = 0f;
            return a;
        }, true);

        static float Smooth(float d, float soft) => Mathf.Clamp01(d / soft * 0.5f + 0.5f);

        static float Disc(float x, float y, float radius, float soft) => Smooth(radius - Mathf.Sqrt(x * x + y * y), soft);

        delegate float Shape(float x, float y);

        static Texture2D Make(string name, int size, Shape shape, bool supersample = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "UiGlyph " + name,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[size * size];
            int ss = supersample ? 4 : 1;
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    float acc = 0f;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float x = (i + (sx + 0.5f) / ss) / size - 0.5f;
                            float y = (j + (sy + 0.5f) / ss) / size - 0.5f;
                            acc += shape(x, y);
                        }
                    }
                    float v = Mathf.Clamp01(acc / (ss * ss));
                    px[j * size + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(v * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
