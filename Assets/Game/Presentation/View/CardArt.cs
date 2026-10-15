using System.Collections.Generic;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// カード ID から手続き生成する星座エンブレム（DESIGN §8-1）。星の点（位置・大きさ）と、それを結ぶ線。
    /// 同じ ID なら常に同じ形になる（ID の FNV-1a ハッシュを種にした xorshift）。描画は Card シェーダーが行う。
    /// </summary>
    public sealed class Constellation
    {
        public const int MaxStars = 7;

        /// <summary>xy = エンブレム内の位置（-1..1）、z = 星の大きさ、w = 明るさ。</summary>
        public readonly Vector4[] Stars = new Vector4[MaxStars];
        public int Count;

        /// <summary>星どうしを結ぶ線（i → i+1 の折れ線に加えて 1 本の枝）。x = 枝の始点、y = 枝の終点（なければ -1）。</summary>
        public Vector2 Branch = new Vector2(-1, -1);

        static readonly Dictionary<string, Constellation> Cache = new Dictionary<string, Constellation>();

        public static Constellation For(string id, int level)
        {
            string key = id + "#" + level;
            if (Cache.TryGetValue(key, out var c)) return c;
            c = Generate(id, level);
            Cache[key] = c;
            return c;
        }

        static Constellation Generate(string id, int level)
        {
            uint h = 2166136261u;
            foreach (char ch in id)
            {
                h ^= ch;
                h *= 16777619u;
            }
            uint s = h == 0 ? 0x9e3779b9u : h;
            float Next()
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                return (s & 0xffffff) / (float)0x1000000;
            }

            var c = new Constellation();
            int n = Mathf.Clamp(4 + level / 3, 4, MaxStars);
            var pts = new List<Vector2>();
            int guard = 0;
            while (pts.Count < n && guard++ < 400)
            {
                float a = Next() * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Next()) * 0.82f;
                var p = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                bool ok = true;
                foreach (var q in pts)
                {
                    if ((q - p).sqrMagnitude < 0.33f * 0.33f)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) pts.Add(p);
            }

            // 最寄りの星をたどって折れ線にする（線が交差しにくい）
            var order = new List<Vector2> { pts[0] };
            pts.RemoveAt(0);
            while (pts.Count > 0)
            {
                var last = order[order.Count - 1];
                int best = 0;
                for (int i = 1; i < pts.Count; i++)
                {
                    if ((pts[i] - last).sqrMagnitude < (pts[best] - last).sqrMagnitude) best = i;
                }
                order.Add(pts[best]);
                pts.RemoveAt(best);
            }

            c.Count = order.Count;
            int brightest = (int)(Next() * c.Count) % c.Count;
            for (int i = 0; i < c.Count; i++)
            {
                float size = 0.05f + Next() * 0.05f;
                if (i == brightest) size = 0.12f;
                c.Stars[i] = new Vector4(order[i].x, order[i].y, size, 0.7f + Next() * 0.3f);
            }
            if (c.Count >= 5)
            {
                int a = (int)(Next() * (c.Count - 3));
                c.Branch = new Vector2(a, a + 2 + (int)(Next() * (c.Count - a - 2)));
            }
            return c;
        }
    }
}
