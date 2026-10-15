using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// マスク PNG → SDF のセル → 星座の点と線（DESIGN §7-1 パイプライン 2・3）。UnityEngine の型は入出力の器にだけ使い、
    /// 計算はすべて double の決定論的な処理（時計・乱数ライブラリ・並列化なし）。同じマスク・同じカード ID なら同じ結果になる。
    /// </summary>
    public static class CardArtBaker
    {
        /// <summary>セル（1 アイコン）の大きさ（px）。</summary>
        public const int Cell = 128;

        /// <summary>SDF を計算する高解像度の倍率（セル 128px を 512px で計算して平均する）。</summary>
        public const int Super = 4;

        /// <summary>SDF の片側の幅（セルの px）。</summary>
        public const float Spread = 12f;

        /// <summary>図形の外接矩形とセルの端の間の余白（セルの px）。外側の光の余地。</summary>
        public const double Margin = 13.0;

        public sealed class Mask
        {
            public int Width;
            public int Height;

            /// <summary>白い図形の被覆率 0..1（行 0 が下）。</summary>
            public double[] Coverage;
        }

        public sealed class BakedCell
        {
            /// <summary>SDF の値（Cell×Cell、行 0 が下）。0.5 が輪郭、大きいほど内側。</summary>
            public byte[] Bytes;

            /// <summary>符号つき距離（セルの px、+ が外側）。星の選択に使う。</summary>
            public double[] Distance;
        }

        public struct Constellation
        {
            public Vector4[] Stars;
            public Vector4[] Lines;
        }

        /// <summary>マスク PNG の画素（R を使う。Texture2D.GetPixels32 の並び＝行 0 が下）から。</summary>
        public static Mask MaskFromPixels(Color32[] px, int width, int height)
        {
            var cov = new double[width * height];
            for (int i = 0; i < cov.Length; i++) cov[i] = px[i].r / 255.0;
            return new Mask { Width = width, Height = height, Coverage = cov };
        }

        // ------------------------------------------------------------------
        // SDF
        // ------------------------------------------------------------------

        /// <summary>
        /// マスクの図形をセルに収めて（外接矩形の長い辺を Cell−2×Margin に。scale・offset・flipX で個別調整）、
        /// 512px で二値化 → 厳密なユークリッド距離変換 → 4×4 平均で 128px の SDF にする。
        /// </summary>
        public static BakedCell BakeCell(Mask m, double scale = 1.0, double offsetX = 0.0, double offsetY = 0.0, bool flipX = false)
        {
            // 外接矩形（被覆 0.5 以上）
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < m.Height; y++)
            {
                for (int x = 0; x < m.Width; x++)
                {
                    if (m.Coverage[y * m.Width + x] < 0.5) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) throw new ArgumentException("empty mask");
            double cx = (minX + maxX + 1) * 0.5;
            double cy = (minY + maxY + 1) * 0.5;
            double extent = Math.Max(maxX - minX + 1, maxY - minY + 1);

            int n = Cell * Super;
            double target = (Cell - 2.0 * Margin) * Super * scale;
            double s = target / extent; // 高解像度の px / マスクの px
            var inside = new bool[n * n];
            for (int j = 0; j < n; j++)
            {
                double sy = cy + (j + 0.5 - n * 0.5 - offsetY * n) / s;
                for (int i = 0; i < n; i++)
                {
                    double dx = (i + 0.5 - n * 0.5 - offsetX * n) / s;
                    if (flipX) dx = -dx;
                    inside[j * n + i] = Sample(m, cx + dx, sy) >= 0.5;
                }
            }

            double[] toInside = SquaredEdt(inside, n, true);
            double[] toOutside = SquaredEdt(inside, n, false);
            var hi = new double[n * n];
            for (int k = 0; k < hi.Length; k++)
                hi[k] = inside[k] ? -(Math.Sqrt(toOutside[k]) - 0.5) : Math.Sqrt(toInside[k]) - 0.5;

            var dist = new double[Cell * Cell];
            var bytes = new byte[Cell * Cell];
            double inv = 1.0 / (Super * Super);
            for (int y = 0; y < Cell; y++)
            {
                for (int x = 0; x < Cell; x++)
                {
                    double sum = 0;
                    for (int b = 0; b < Super; b++)
                        for (int a = 0; a < Super; a++)
                            sum += hi[(y * Super + b) * n + x * Super + a];
                    double d = sum * inv / Super;
                    dist[y * Cell + x] = d;
                    double v = 0.5 - d / (2.0 * Spread);
                    v = v < 0 ? 0 : v > 1 ? 1 : v;
                    bytes[y * Cell + x] = (byte)Math.Round(v * 255.0, MidpointRounding.AwayFromZero);
                }
            }
            return new BakedCell { Bytes = bytes, Distance = dist };
        }

        /// <summary>被覆率の双線形補間（画素の中心が整数＋0.5）。外は 0。</summary>
        static double Sample(Mask m, double x, double y)
        {
            double fx = x - 0.5, fy = y - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double tx = fx - x0, ty = fy - y0;
            double a = At(m, x0, y0), b = At(m, x0 + 1, y0), c = At(m, x0, y0 + 1), d = At(m, x0 + 1, y0 + 1);
            return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
        }

        static double At(Mask m, int x, int y) =>
            x < 0 || y < 0 || x >= m.Width || y >= m.Height ? 0.0 : m.Coverage[y * m.Width + x];

        const double Inf = 1e20;

        /// <summary>Felzenszwalb の 2 乗ユークリッド距離変換。feature = (inside == toInside) の画素までの距離の 2 乗。</summary>
        static double[] SquaredEdt(bool[] inside, int n, bool toInside)
        {
            var g = new double[n * n];
            for (int k = 0; k < g.Length; k++) g[k] = inside[k] == toInside ? 0.0 : Inf;
            var f = new double[n];
            var d = new double[n];
            var v = new int[n];
            var z = new double[n + 1];
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++) f[y] = g[y * n + x];
                Edt1D(f, n, d, v, z);
                for (int y = 0; y < n; y++) g[y * n + x] = d[y];
            }
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++) f[x] = g[y * n + x];
                Edt1D(f, n, d, v, z);
                for (int x = 0; x < n; x++) g[y * n + x] = d[x];
            }
            return g;
        }

        static void Edt1D(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = -Inf;
            z[1] = Inf;
            for (int q = 1; q < n; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = Inf;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                double dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        // ------------------------------------------------------------------
        // 星座
        // ------------------------------------------------------------------

        /// <summary>レベルごとの星の数（Lv1 = 7 … Lv10 = 14）。</summary>
        public static int StarCount(int level)
        {
            int lv = Math.Max(1, Math.Min(10, level));
            return 7 + ((lv - 1) * 7 + 4) / 9;
        }

        /// <summary>
        /// 輪郭（内側の境界の画素）から星を選ぶ: 先端（周りが外側ばかりの点）を優先した最遠点サンプリング。
        /// 揺らぎはカード ID の FNV-1a ハッシュを種にした xorshift32。線は「外にはみ出す線ほど重い」最小全域木＋ループ 1 本。
        /// </summary>
        public static Constellation PickConstellation(double[] dist, string cardId, int level)
        {
            var rng = new XorShift(Fnv1a(cardId));
            var cand = new List<int>();
            var convex = new List<double>();
            for (int y = 1; y < Cell - 1; y++)
            {
                for (int x = 1; x < Cell - 1; x++)
                {
                    int k = y * Cell + x;
                    if (dist[k] > 0) continue;
                    if (dist[k - 1] <= 0 && dist[k + 1] <= 0 && dist[k - Cell] <= 0 && dist[k + Cell] <= 0) continue;
                    // 周り 9×9 の外側の割合（先端ほど大きい）
                    int outside = 0, total = 0;
                    for (int b = -4; b <= 4; b++)
                    {
                        for (int a = -4; a <= 4; a++)
                        {
                            int xx = x + a, yy = y + b;
                            if (xx < 0 || yy < 0 || xx >= Cell || yy >= Cell) continue;
                            total++;
                            if (dist[yy * Cell + xx] > 0) outside++;
                        }
                    }
                    cand.Add(k);
                    convex.Add((double)outside / total);
                }
            }
            if (cand.Count == 0) return new Constellation { Stars = new Vector4[0], Lines = new Vector4[0] };

            var jitter = new double[cand.Count];
            for (int i = 0; i < jitter.Length; i++) jitter[i] = 0.85 + 0.3 * rng.NextDouble();

            int want = Math.Min(StarCount(level), cand.Count);
            var chosen = new List<int>();
            // 1 つ目: いちばん尖った点
            int first = 0;
            double best = -1;
            for (int i = 0; i < cand.Count; i++)
            {
                double sc = convex[i] * jitter[i];
                if (sc > best) { best = sc; first = i; }
            }
            chosen.Add(first);
            var minD = new double[cand.Count];
            for (int i = 0; i < cand.Count; i++) minD[i] = DistPx(cand[i], cand[first]);
            while (chosen.Count < want)
            {
                int pick = -1;
                best = -1;
                for (int i = 0; i < cand.Count; i++)
                {
                    double sc = minD[i] * (0.55 + 0.9 * convex[i]) * jitter[i];
                    if (sc > best) { best = sc; pick = i; }
                }
                if (pick < 0 || minD[pick] < 6.0) break;
                chosen.Add(pick);
                for (int i = 0; i < cand.Count; i++) minD[i] = Math.Min(minD[i], DistPx(cand[i], cand[pick]));
            }

            // 星（位置はセル内の 0..1）。1 つ目（最も尖った点）を主星として大きくする
            int ns = chosen.Count;
            var pos = new double[ns, 2];
            var stars = new Vector4[ns];
            for (int i = 0; i < ns; i++)
            {
                int k = cand[chosen[i]];
                pos[i, 0] = (k % Cell + 0.5) / Cell;
                pos[i, 1] = (k / Cell + 0.5) / Cell;
                double size = i == 0 ? 1.75 : 0.75 + 0.6 * rng.NextDouble();
                double phase = rng.NextDouble();
                stars[i] = new Vector4((float)pos[i, 0], (float)pos[i, 1], (float)size, (float)phase);
            }

            // 線: Prim の最小全域木（費用 = 長さ ×（1 + 4 × 外にはみ出す割合））
            var cost = new double[ns, ns];
            for (int a = 0; a < ns; a++)
                for (int b = a + 1; b < ns; b++)
                    cost[a, b] = cost[b, a] = Length(pos, a, b) * (1.0 + 4.0 * OutsideFraction(dist, pos, a, b));
            var inTree = new bool[ns];
            var edges = new List<(int a, int b)>();
            inTree[0] = true;
            for (int added = 1; added < ns; added++)
            {
                int ba = -1, bb = -1;
                double bc = double.MaxValue;
                for (int a = 0; a < ns; a++)
                {
                    if (!inTree[a]) continue;
                    for (int b = 0; b < ns; b++)
                    {
                        if (inTree[b] || cost[a, b] >= bc) continue;
                        bc = cost[a, b];
                        ba = a;
                        bb = b;
                    }
                }
                inTree[bb] = true;
                edges.Add((ba, bb));
            }
            // ループを 1 本（星が 9 個以上のとき。短く、ほかの線と交わらないもの）
            if (ns >= 9)
            {
                int la = -1, lb = -1;
                double lc = double.MaxValue;
                for (int a = 0; a < ns; a++)
                {
                    for (int b = a + 1; b < ns; b++)
                    {
                        if (cost[a, b] >= lc || Length(pos, a, b) > 0.42) continue;
                        bool used = false, crosses = false;
                        foreach (var e in edges)
                        {
                            if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) { used = true; break; }
                            if (e.a == a || e.a == b || e.b == a || e.b == b) continue;
                            if (Intersects(pos, a, b, e.a, e.b)) { crosses = true; break; }
                        }
                        if (used || crosses) continue;
                        lc = cost[a, b];
                        la = a;
                        lb = b;
                    }
                }
                if (la >= 0) edges.Add((la, lb));
            }
            var lines = new Vector4[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                lines[i] = new Vector4((float)pos[e.a, 0], (float)pos[e.a, 1], (float)pos[e.b, 0], (float)pos[e.b, 1]);
            }
            return new Constellation { Stars = stars, Lines = lines };
        }

        static double DistPx(int k0, int k1)
        {
            double dx = k0 % Cell - k1 % Cell, dy = k0 / Cell - k1 / Cell;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static double Length(double[,] p, int a, int b)
        {
            double dx = p[a, 0] - p[b, 0], dy = p[a, 1] - p[b, 1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static double OutsideFraction(double[] dist, double[,] p, int a, int b)
        {
            const int steps = 16;
            int outside = 0;
            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;
                int x = (int)Math.Floor((p[a, 0] + (p[b, 0] - p[a, 0]) * t) * Cell);
                int y = (int)Math.Floor((p[a, 1] + (p[b, 1] - p[a, 1]) * t) * Cell);
                x = Math.Max(0, Math.Min(Cell - 1, x));
                y = Math.Max(0, Math.Min(Cell - 1, y));
                if (dist[y * Cell + x] > 1.0) outside++;
            }
            return outside / (double)(steps - 1);
        }

        static bool Intersects(double[,] p, int a, int b, int c, int d)
        {
            double o1 = Orient(p, a, b, c), o2 = Orient(p, a, b, d), o3 = Orient(p, c, d, a), o4 = Orient(p, c, d, b);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }

        static double Orient(double[,] p, int a, int b, int c) =>
            (p[b, 0] - p[a, 0]) * (p[c, 1] - p[a, 1]) - (p[b, 1] - p[a, 1]) * (p[c, 0] - p[a, 0]);

        public static uint Fnv1a(string s)
        {
            uint h = 2166136261u;
            foreach (char ch in s)
            {
                h ^= ch;
                h *= 16777619u;
            }
            return h == 0 ? 0x9e3779b9u : h;
        }

        struct XorShift
        {
            uint _s;

            public XorShift(uint seed) { _s = seed == 0 ? 0x9e3779b9u : seed; }

            public double NextDouble()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return (_s & 0xffffff) / (double)0x1000000;
            }
        }
    }
}
