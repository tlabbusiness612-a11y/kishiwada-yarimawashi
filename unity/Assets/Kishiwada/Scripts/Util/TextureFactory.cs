using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Kishiwada
{
    // 手続き的に作る質感（色＋なめらかさ(α)、法線）。どれも繰り返し貼れるように作る。
    // 1 枚が何 m 分に当たるかはコメントに書く。写真素材に差し替えるときも同じ寸法で作ると UV がそのまま使える。
    public sealed class TexPair { public Texture2D albedo, normal; }

    public static class TextureFactory
    {
        struct Px { public float r, g, b, s, h; }
        delegate void PixelFn(float u, float v, int x, int y, ref Px p);

        public static int Scale = 1; // モバイルでは 1/2 にする

        static float Fbm(float u, float v, int per, int oct, int seed) => Noise.Fbm(u * per, v * per, per, oct, seed);
        static float N(float u, float v, int px, int py, int seed) => Noise.Value(u * px, v * py, Math.Max(px, py), seed);
        static float Frac(float x) => x - Mathf.Floor(x);
        static float SStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static float Box(float u, float v, float u0, float v0, float u1, float v1) => u >= u0 && u <= u1 && v >= v0 && v <= v1 ? 1f : 0f;
        static float Hash(int a, int b, int s) => Noise.Hash01(a, b, s);

        static TexPair Gen(string name, int w, int h, float normalStrength, PixelFn fn, bool compress = true, bool readable = false)
        {
            w = Mathf.Max(32, w / Scale); h = Mathf.Max(32, h / Scale);
            var col = new Color32[w * h]; var hgt = new float[w * h];
            Parallel.For(0, h, y =>
            {
                var p = new Px();
                for (int x = 0; x < w; x++)
                {
                    p.r = p.g = p.b = 0.5f; p.s = 0.3f; p.h = 0.5f;
                    fn((x + 0.5f) / w, (y + 0.5f) / h, x, y, ref p);
                    col[y * w + x] = new Color32(B(p.r), B(p.g), B(p.b), B(p.s));
                    hgt[y * w + x] = p.h;
                }
            });
            var nrm = new Color32[w * h];
            float k = normalStrength / Scale;
            Parallel.For(0, h, y =>
            {
                int yu = (y + 1) % h, yd = (y + h - 1) % h;
                for (int x = 0; x < w; x++)
                {
                    int xr = (x + 1) % w, xl = (x + w - 1) % w;
                    float dx = (hgt[y * w + xr] - hgt[y * w + xl]) * k, dy = (hgt[yu * w + x] - hgt[yd * w + x]) * k;
                    float l = Mathf.Sqrt(dx * dx + dy * dy + 1f);
                    nrm[y * w + x] = new Color32(B(-dx / l * 0.5f + 0.5f), B(-dy / l * 0.5f + 0.5f), B(1f / l * 0.5f + 0.5f), 255);
                }
            });
            var a = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            a.SetPixels32(col); a.Apply(true, false);
            if (compress) { a.Compress(true); a.Apply(false, !readable); }
            else if (!readable) a.Apply(false, true);
            var n = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = name + "_N", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            n.SetPixels32(nrm); n.Apply(true, true);
            return new TexPair { albedo = a, normal = n };
        }
        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        static void Set(ref Px p, Color c, float s, float h) { p.r = c.r; p.g = c.g; p.b = c.b; p.s = s; p.h = h; }
        static Color Lin(float r, float g, float b) => new Color(r, g, b);

        public static Dictionary<string, TexPair> MakeAll(TextPainter gothic, TextPainter mincho)
        {
            var t = new Dictionary<string, TexPair>();
            t["wood"] = Wood();
            t["tile"] = Kawara();
            t["plaster"] = Plaster();
            t["lattice"] = Lattice();
            t["house"] = Siding();
            t["office"] = Office();
            t["asphalt"] = Asphalt();
            t["walk"] = Paving();
            t["concrete"] = Concrete();
            t["kohaku"] = Kohaku();
            t["rope"] = Rope();
            t["cloth"] = Cloth();
            t["brass"] = Brass();
            t["shop"] = Shops(mincho);
            t["signs"] = Signs(gothic);
            return t;
        }

        // 欅（けやき）の白木：1 枚 ≒ 1.2m。木目は u 方向
        static TexPair Wood() => Gen("wood", 512, 512, 3.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float warp = Fbm(u, v, 4, 4, 11) * 2.2f + Fbm(u * 0.5f, v, 2, 3, 12);
            float ring = Frac(v * 22f + warp * 2.4f + Mathf.Sin(u * Mathf.PI * 2f * 2f + v * 9f) * 0.35f);
            float late = SStep(0.62f, 0.95f, ring) * (1f - SStep(0.95f, 1f, ring));
            float pore = N(u, v, 10, 256, 13); pore = SStep(0.72f, 0.95f, pore);
            float fleck = N(u, v, 48, 96, 14);
            float tone = 0.9f + 0.2f * Fbm(u, v, 3, 3, 15);
            var c = Lin(0.50f, 0.32f, 0.19f) * tone;
            c = Color.Lerp(c, Lin(0.30f, 0.17f, 0.09f), late * 0.55f);
            c = Color.Lerp(c, Lin(0.62f, 0.44f, 0.29f), SStep(0.15f, 0.4f, ring) * (1 - SStep(0.4f, 0.6f, ring)) * 0.25f);
            c *= 1f - pore * 0.28f;
            c *= 0.96f + fleck * 0.08f;
            Set(ref p, c, 0.34f + late * 0.12f - pore * 0.1f, 0.5f + late * 0.25f - pore * 0.5f + fleck * 0.05f);
        });

        // 燻し瓦（さん瓦）：1 枚 = 2m × 2m、8 列 × 8 段。v が棟の方向
        static TexPair Kawara() => Gen("tile", 512, 512, 7.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float fu = u * 8f, fv = v * 8f; int col = Mathf.FloorToInt(fu), row = Mathf.FloorToInt(fv);
            float cu = fu - col, cv = fv - row;
            float prof = cu < 0.58f ? Mathf.Sin(Mathf.PI * cu / 0.58f) : -0.35f * Mathf.Sin(Mathf.PI * (cu - 0.58f) / 0.42f);
            float hh = prof * 0.55f + (1f - cv) * 0.35f;
            float gap = 1f - SStep(0.0f, 0.05f, cv);
            float tileVar = Hash(col, row, 21);
            float stain = Fbm(u, v, 4, 4, 22);
            var c = Lin(0.30f, 0.32f, 0.35f) * (0.85f + tileVar * 0.3f);
            c = Color.Lerp(c, Lin(0.26f, 0.24f, 0.2f), SStep(0.55f, 0.8f, stain) * 0.5f);
            c *= 1f - gap * 0.55f;
            c *= 0.9f + 0.18f * Mathf.Clamp01(prof);
            Set(ref p, c, 0.52f - gap * 0.3f + tileVar * 0.1f, hh);
        });

        // 町家二階の漆喰と虫籠窓：1 枚 = 幅 4m × 高さ 2.6m
        static TexPair Plaster() => Gen("plaster", 512, 336, 4.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float st = Fbm(u, v, 6, 5, 31), streak = N(u, v, 60, 3, 32);
            var c = Lin(0.86f, 0.83f, 0.76f) * (0.95f + st * 0.08f);
            c = Color.Lerp(c, Lin(0.62f, 0.57f, 0.48f), SStep(0.55f, 0.9f, streak) * 0.25f * (1 - v));
            float h = 0.5f + st * 0.05f, s = 0.2f;
            // 虫籠窓（角の丸い長方形に縦の格子）
            float wu = Mathf.Abs(u - 0.5f) / 0.23f, wv = Mathf.Abs(v - 0.5f) / 0.17f;
            float rr = Mathf.Pow(Mathf.Pow(wu, 6) + Mathf.Pow(wv, 6), 1f / 6f);
            if (rr < 1f)
            {
                float bar = Frac(u * 21f);
                if (bar < 0.5f) { c = Lin(0.8f, 0.77f, 0.7f); h = 0.62f; }
                else { c = Lin(0.1f, 0.085f, 0.07f); h = 0.1f; s = 0.1f; }
            }
            else if (rr < 1.06f) { c *= 0.8f; h = 0.3f; }
            if (v < 0.07f) { c = Lin(0.22f, 0.14f, 0.08f); h = 0.7f; s = 0.3f; }
            Set(ref p, c, s, h);
        });

        // 町家一階の格子：1 枚 = 幅 2m × 高さ 3m
        static TexPair Lattice() => Gen("lattice", 256, 384, 6.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float grain = N(u, v, 20, 60, 41);
            if (v > 0.885f) { Set(ref p, Lin(0.2f, 0.13f, 0.08f) * (0.9f + grain * 0.2f), 0.3f, 0.9f); return; }
            if (v < 0.04f) { Set(ref p, Lin(0.18f, 0.12f, 0.07f), 0.3f, 0.8f); return; }
            float cu = Frac(u * 16f);
            if (cu < 0.6f)
            {
                float round = Mathf.Sin(cu / 0.6f * Mathf.PI);
                Set(ref p, Lin(0.34f, 0.22f, 0.13f) * (0.85f + grain * 0.25f + round * 0.1f), 0.35f, 0.4f + round * 0.5f);
            }
            else Set(ref p, Lin(0.035f, 0.03f, 0.025f), 0.1f, 0f);
        });

        // 住宅の外壁（窯業系サイディング＋窓）：1 枚 = 幅 8m × 1 階 3m。左 4m と右 4m で窓の形が違う
        static TexPair Siding() => Gen("house", 1024, 384, 3.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float bv = Frac(v * 20f), dirt = Fbm(u, v, 8, 4, 51);
            float groove = 1f - SStep(0.0f, 0.08f, bv);
            var c = Lin(0.9f, 0.89f, 0.86f) * (0.95f + dirt * 0.06f) * (1f - groove * 0.35f);
            float h = 0.5f - groove * 0.4f + bv * 0.05f, s = 0.25f;
            // 窓 A：掃き出しに近い引き違い窓
            Window(u, v, 0.08f, 0.3f, 0.42f, 0.83f, 2, ref c, ref h, ref s, 52);
            // 窓 B：小窓
            Window(u, v, 0.62f, 0.5f, 0.78f, 0.83f, 1, ref c, ref h, ref s, 53);
            // 換気口
            if (Box(u, v, 0.88f, 0.72f, 0.905f, 0.78f) > 0) { c = Lin(0.7f, 0.7f, 0.68f); h = 0.7f; s = 0.4f; }
            Set(ref p, c, s, h);
        });
        static void Window(float u, float v, float u0, float v0, float u1, float v1, int panes, ref Color c, ref float h, ref float s, int seed)
        {
            if (u < u0 - 0.006f || u > u1 + 0.006f || v < v0 - 0.02f || v > v1 + 0.02f) return;
            float fw = 0.006f, fh = 0.02f;
            bool frame = u < u0 + fw || u > u1 - fw || v < v0 + fh || v > v1 - fh;
            float pu = (u - u0) / (u1 - u0) * panes; int pi = Mathf.Min(panes - 1, Mathf.FloorToInt(pu));
            bool mull = panes > 1 && Mathf.Abs(pu - Mathf.Round(pu)) * (u1 - u0) / panes < fw * 0.8f && pu > 0.1f && pu < panes - 0.1f;
            if (v < v0) { c = Lin(0.75f, 0.75f, 0.74f); h = 0.75f; s = 0.5f; return; } // 水切り
            if (u < u0 || u > u1 || v > v1) { c = Lin(0.55f, 0.56f, 0.57f); h = 0.6f; s = 0.6f; return; }
            if (frame || mull) { c = Lin(0.62f, 0.63f, 0.64f); h = 0.55f; s = 0.7f; return; }
            float vv = (v - v0) / (v1 - v0);
            bool curtain = Hash(seed, pi, 7) < 0.5f && Frac(u * 90f) < 0.8f;
            var glass = Color.Lerp(Lin(0.07f, 0.09f, 0.11f), Lin(0.28f, 0.33f, 0.38f), vv * 0.8f);
            if (curtain) glass = Color.Lerp(glass, Lin(0.55f, 0.5f, 0.42f), 0.45f);
            c = glass; h = 0.25f; s = 0.95f;
        }

        // ビル・マンションの外壁：1 枚 = 幅 3.6m × 1 階 3.2m
        static TexPair Office() => Gen("office", 512, 512, 3.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float dirt = Fbm(u, v, 5, 4, 61), streak = N(u, v, 40, 4, 62);
            var c = Lin(0.78f, 0.77f, 0.74f) * (0.93f + dirt * 0.1f);
            c *= 1f - SStep(0.6f, 0.95f, streak) * 0.12f;
            float h = 0.5f, s = 0.2f;
            if (v < 0.035f) { c *= 0.72f; h = 0.35f; }
            if (Box(u, v, 0.05f, 0.27f, 0.95f, 0.8f) > 0)
            {
                float lu = (u - 0.05f) / 0.9f, lv = (v - 0.27f) / 0.53f;
                bool frame = lu < 0.012f || lu > 0.988f || lv < 0.03f || lv > 0.97f || Mathf.Abs(lu - 0.5f) < 0.008f;
                if (frame) { c = Lin(0.34f, 0.36f, 0.38f); h = 0.45f; s = 0.6f; }
                else
                {
                    int pane = lu < 0.5f ? 0 : 1;
                    bool blind = Hash(pane, 3, 63) < 0.35f;
                    c = Color.Lerp(Lin(0.08f, 0.1f, 0.13f), Lin(0.35f, 0.42f, 0.5f), lv);
                    if (blind) c = Color.Lerp(c, Lin(0.7f, 0.7f, 0.66f) * (Frac(lv * 30f) < 0.8f ? 1f : 0.7f), 0.55f);
                    h = 0.2f; s = 0.95f;
                }
            }
            if (Box(u, v, 0.05f, 0.24f, 0.95f, 0.27f) > 0) { c = Lin(0.6f, 0.6f, 0.58f); h = 0.7f; s = 0.35f; }
            Set(ref p, c, s, h);
        });

        // アスファルト：1 枚 = 8m
        static TexPair Asphalt() => Gen("asphalt", 1024, 1024, 5.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float agg = Noise.Value(u * 512, v * 512, 512, 71), agg2 = Noise.Value(u * 256, v * 256, 256, 72);
            float patch = Fbm(u, v, 3, 4, 73), wear = Fbm(u, v, 6, 3, 74);
            float crack = Mathf.Abs(Fbm(u, v, 5, 4, 75) - 0.5f);
            var c = Lin(0.25f, 0.25f, 0.26f) * (0.85f + agg * 0.3f + agg2 * 0.12f);
            c *= 0.88f + patch * 0.22f;
            c = Color.Lerp(c, Lin(0.36f, 0.35f, 0.33f), SStep(0.6f, 0.85f, wear) * 0.4f);
            float ck = 1f - SStep(0.004f, 0.012f, crack);
            c *= 1f - ck * 0.45f;
            Set(ref p, c, 0.16f + patch * 0.1f - ck * 0.1f, agg * 0.5f + agg2 * 0.3f - ck * 0.6f);
        });

        // 歩道のインターロッキング：1 枚 = 2m
        static TexPair Paving() => Gen("walk", 512, 512, 5.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float fv = v * 20f; int row = Mathf.FloorToInt(fv);
            float fu = u * 10f + (row % 2) * 0.5f; int col = Mathf.FloorToInt(fu);
            float cu = fu - col, cv = fv - row;
            float joint = Mathf.Max(1f - SStep(0f, 0.05f, cu), 1f - SStep(0f, 0.1f, cv));
            float hv = Hash(col % 10, row, 81);
            var c = hv < 0.6f ? Lin(0.52f, 0.5f, 0.47f) : hv < 0.85f ? Lin(0.58f, 0.53f, 0.46f) : Lin(0.46f, 0.36f, 0.32f);
            c *= 0.9f + Noise.Value(u * 256, v * 256, 256, 82) * 0.15f + Fbm(u, v, 4, 3, 83) * 0.08f;
            c *= 1f - joint * 0.45f;
            Set(ref p, c, 0.18f, 0.6f - joint * 0.6f + hv * 0.05f);
        });

        // 打ちっぱなしの面・陸屋根・地面：1 枚 = 4m
        static TexPair Concrete() => Gen("concrete", 512, 512, 2.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float st = Fbm(u, v, 4, 5, 91), sp = Noise.Value(u * 256, v * 256, 256, 92);
            var c = Lin(0.58f, 0.57f, 0.54f) * (0.86f + st * 0.2f + sp * 0.06f);
            Set(ref p, c, 0.2f + st * 0.08f, st * 0.3f + sp * 0.2f);
        });

        // 紅白幕：1 枚 = 1.8m（縞 4 本）、縦のひだ
        static TexPair Kohaku() => Gen("kohaku", 256, 64, 4.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            int stripe = Mathf.FloorToInt(u * 4f);
            float fold = Mathf.Sin(u * Mathf.PI * 2f * 12f);
            var c = stripe % 2 == 0 ? Lin(0.68f, 0.07f, 0.06f) : Lin(0.9f, 0.88f, 0.84f);
            c *= 0.9f + fold * 0.08f;
            Set(ref p, c, 0.25f, fold * 0.5f + 0.5f);
        });

        // 綱：u が長さ（1 枚 = 0.3m）、v が周。三つ撚り
        static TexPair Rope() => Gen("rope", 128, 128, 5.0f, (float u, float v, int x, int y, ref Px p) =>
        {
            float strand = Mathf.Cos((v * 3f - u * 5f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            float fib = N(u, v, 64, 16, 101);
            var c = Lin(0.78f, 0.7f, 0.52f) * (0.72f + strand * 0.35f) * (0.9f + fib * 0.15f);
            Set(ref p, c, 0.2f, strand * 0.8f + fib * 0.2f);
        });

        // 布：白い平織り（頂点色で色を付ける）
        static TexPair Cloth() => Gen("cloth", 256, 256, 1.5f, (float u, float v, int x, int y, ref Px p) =>
        {
            float wv = Mathf.Sin(u * Mathf.PI * 2f * 96f) * Mathf.Sin(v * Mathf.PI * 2f * 96f);
            float fold = Fbm(u, v, 4, 3, 111);
            var c = Lin(0.9f, 0.9f, 0.9f) * (0.9f + wv * 0.05f + fold * 0.1f);
            Set(ref p, c, 0.12f, wv * 0.3f + fold * 0.7f);
        });

        // 真鍮の飾り金具：ヘアライン
        static TexPair Brass() => Gen("brass", 256, 256, 1.2f, (float u, float v, int x, int y, ref Px p) =>
        {
            float line = N(u, v, 4, 200, 121), sp = Fbm(u, v, 4, 3, 122);
            var c = Lin(0.95f, 0.72f, 0.34f) * (0.85f + line * 0.2f) * (0.9f + sp * 0.15f);
            Set(ref p, c, 0.55f + line * 0.2f, line);
        });

        // 店先 2×2（1 コマ = 幅 4m × 高さ 3m）。コマ番号 c：u0 = (c%2)/2, v0 = (c/2)/2
        static TexPair Shops(TextPainter mincho)
        {
            var tp = Gen("shop", 1024, 768, 3.0f, (float u, float v, int x, int y, ref Px p) =>
            {
                int cell = (u < 0.5f ? 0 : 1) + (v < 0.5f ? 0 : 2);
                float cu = Frac(u * 2f), cv = Frac(v * 2f);
                float n = Noise.Value(u * 128, v * 128, 128, 131);
                switch (cell)
                {
                    case 0: // ガラスの店先と商品
                        if (cv > 0.9f || cu < 0.02f || cu > 0.98f || cv < 0.05f) { Set(ref p, Lin(0.25f, 0.24f, 0.23f), 0.4f, 0.7f); return; }
                        if (Mathf.Abs(cu - 0.5f) < 0.01f) { Set(ref p, Lin(0.7f, 0.7f, 0.7f), 0.6f, 0.6f); return; }
                        {
                            float gx = Frac(cu * 7f), gy = Frac(cv * 5f);
                            int ix = Mathf.FloorToInt(cu * 7f), iy = Mathf.FloorToInt(cv * 5f);
                            float hv = Hash(ix, iy, 132);
                            var goods = Color.HSVToRGB(hv, 0.45f, 0.35f + Hash(iy, ix, 133) * 0.4f);
                            bool has = cv < 0.55f && gx > 0.1f && gx < 0.9f && gy > 0.15f && gy < 0.85f && hv > 0.3f;
                            var glass = Color.Lerp(Lin(0.1f, 0.12f, 0.14f), Lin(0.3f, 0.35f, 0.4f), cv);
                            Set(ref p, has ? Color.Lerp(goods, glass, 0.35f) : glass, 0.92f, 0.2f);
                        }
                        return;
                    case 1: // 閉じたシャッター
                        {
                            float rib = Frac(cv * 60f);
                            if (cv > 0.88f) { Set(ref p, Lin(0.42f, 0.43f, 0.44f), 0.5f, 0.8f); return; }
                            var c = Lin(0.6f, 0.62f, 0.63f) * (0.85f + Mathf.Sin(rib * Mathf.PI) * 0.15f) * (0.95f + n * 0.08f);
                            Set(ref p, c, 0.55f, Mathf.Sin(rib * Mathf.PI));
                        }
                        return;
                    case 2: // 暖簾のかかった食堂（文字はあとで描く）
                        if (cv > 0.62f && cv < 0.95f && cu > 0.14f && cu < 0.86f)
                        {
                            float slit = Frac((cu - 0.14f) / 0.72f * 4f);
                            if (slit < 0.02f || slit > 0.98f) { Set(ref p, Lin(0.1f, 0.08f, 0.06f), 0.1f, 0.2f); return; }
                            Set(ref p, Lin(0.1f, 0.14f, 0.3f) * (0.9f + n * 0.1f), 0.15f, 0.6f);
                            return;
                        }
                        if (cv < 0.6f && cu > 0.14f && cu < 0.86f)
                        {
                            bool fr = Frac(cu * 6f) < 0.06f || cv < 0.03f;
                            Set(ref p, fr ? Lin(0.28f, 0.18f, 0.1f) : Color.Lerp(Lin(0.3f, 0.26f, 0.2f), Lin(0.55f, 0.48f, 0.38f), cv), fr ? 0.3f : 0.85f, fr ? 0.7f : 0.3f);
                            return;
                        }
                        Set(ref p, Lin(0.26f, 0.17f, 0.1f) * (0.85f + n * 0.2f), 0.3f, 0.6f);
                        return;
                    default: // タイル壁と戸口
                        {
                            float tu = Frac(cu * 20f), tv = Frac(cv * 30f);
                            bool joint = tu < 0.06f || tv < 0.08f;
                            var c = joint ? Lin(0.6f, 0.58f, 0.55f) : Lin(0.66f, 0.6f, 0.52f) * (0.9f + Hash(Mathf.FloorToInt(cu * 20), Mathf.FloorToInt(cv * 30), 134) * 0.15f);
                            float h = joint ? 0.3f : 0.6f, s = joint ? 0.2f : 0.45f;
                            if (Box(cu, cv, 0.58f, 0.03f, 0.86f, 0.74f) > 0) { c = Lin(0.3f, 0.22f, 0.16f); s = 0.4f; h = 0.4f; if (Box(cu, cv, 0.62f, 0.4f, 0.82f, 0.7f) > 0) { c = Lin(0.12f, 0.14f, 0.16f); s = 0.9f; } }
                            if (Box(cu, cv, 0.1f, 0.35f, 0.45f, 0.75f) > 0) { c = Lin(0.1f, 0.12f, 0.15f); s = 0.92f; h = 0.2f; }
                            Set(ref p, c, s, h);
                        }
                        return;
                }
            }, false, true);
            // 暖簾の文字（白抜き）。まだ圧縮も読み取り禁止もしていないので書き足せる
            var a = tp.albedo; int w = a.width, h = a.height;
            if (mincho != null)
            {
                var px = a.GetPixels32();
                mincho.DrawText(px, w, h, "御食事処", w * 0.25f, h * 0.5f + h * 0.5f * 0.785f, h * 0.5f * 0.2f, new Color(0.95f, 0.93f, 0.88f), w * 0.5f * 0.6f);
                a.SetPixels32(px); a.Apply(true, false);
            }
            a.wrapMode = TextureWrapMode.Clamp; tp.normal.wrapMode = TextureWrapMode.Clamp;
            a.Compress(true); a.Apply(false, true);
            return tp;
        }

        public static readonly string[] SignWords = { "たこ焼", "喫茶", "鮮魚", "呉服", "酒店", "薬局", "理容", "食堂", "青果", "お好み焼", "精肉", "寝具", "時計眼鏡", "和菓子", "印刷", "文具", "履物", "洋品", "仏壇", "写真館", "かしわ", "豆腐", "銭湯", "居酒屋", "中華そば", "金物", "畳", "クリーニング", "書店", "整骨院", "不動産", "祭用品" };
        public const int SignCols = 4, SignRows = 8;

        // 看板（4 列 × 8 段、1 枚 256×64）
        static TexPair Signs(TextPainter gothic)
        {
            int w = 1024 / Scale, h = 512 / Scale, cw = w / SignCols, ch = h / SignRows;
            var px = new Color32[w * h];
            Color[][] pal = {
                new[] { Lin(0.95f, 0.92f, 0.85f), Lin(0.1f, 0.13f, 0.22f) }, new[] { Lin(0.71f, 0.17f, 0.12f), Color.white },
                new[] { Lin(0.11f, 0.23f, 0.43f), Color.white }, new[] { Lin(0.91f, 0.76f, 0.29f), Lin(0.16f, 0.1f, 0.04f) },
                new[] { Lin(0.18f, 0.42f, 0.24f), Color.white }, new[] { Color.white, Lin(0.71f, 0.17f, 0.12f) } };
            for (int i = 0; i < SignWords.Length; i++)
            {
                int cx = i % SignCols, cy = SignRows - 1 - i / SignCols; var pc = pal[i % pal.Length];
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        bool border = x < 3 || y < 3 || x >= cw - 3 || y >= ch - 3;
                        bool inner = !border && (x < 6 || y < 6 || x >= cw - 6 || y >= ch - 6);
                        Color c = border ? Lin(0.15f, 0.15f, 0.15f) : inner ? pc[0] * 0.8f : pc[0];
                        c.a = 0.35f;
                        px[(cy * ch + y) * w + cx * cw + x] = c;
                    }
                gothic?.DrawText(px, w, h, SignWords[i], cx * cw + cw * 0.5f, cy * ch + ch * 0.5f, ch * 0.62f, pc[1], cw * 0.86f);
            }
            var a = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = "signs", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            a.SetPixels32(px); a.Apply(true, false); a.Compress(true); a.Apply(false, true);
            return new TexPair { albedo = a, normal = null };
        }

        // 町名の札（横書き）
        public static Texture2D TownPlate(TextPainter mincho, string name)
        {
            int w = 512, h = 176; var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    bool frame = x < 10 || y < 10 || x >= w - 10 || y >= h - 10;
                    px[y * w + x] = frame ? new Color(0.1f, 0.08f, 0.06f, 0.3f) : new Color(0.93f, 0.9f, 0.82f, 0.3f);
                }
            mincho?.DrawText(px, w, h, name, w * 0.5f, h * 0.5f, h * 0.62f, new Color(0.08f, 0.07f, 0.06f), w * 0.86f);
            return Finish(px, w, h, "plate");
        }

        // 後ろ旗：法被の色に金の縁取り、町名を縦書き
        public static Texture2D Flag(TextPainter mincho, string name, Color happi)
        {
            int w = 256, h = 384; var px = new Color32[w * h];
            var gold = new Color(0.85f, 0.67f, 0.3f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int yy = h - 1 - y; // 上からの距離
                    Color c = happi;
                    bool outer = x < 16 || x >= w - 16 || yy < 16 || (yy >= h - 40 && yy < h - 26);
                    bool inner = (x >= 26 && x < 30) || (x >= w - 30 && x < w - 26) || (yy >= 26 && yy < 30) || (yy >= h - 56 && yy < h - 52);
                    if (outer || inner) c = gold;
                    c.a = 1f;
                    if (yy >= h - 26) c = (x % 9) < 4 ? gold : new Color(0, 0, 0, 0); // 房（すき間は切り抜き）
                    px[y * w + x] = c;
                }
            // 紋（丸に町名の頭文字）
            float mcx = w * 0.5f, mcy = h - 80;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Sqrt((x - mcx) * (x - mcx) + (y - mcy) * (y - mcy));
                    if (d < 36) px[y * w + x] = new Color(gold.r, gold.g, gold.b, 1f);
                }
            string s = string.IsNullOrEmpty(name) ? "町" : name;
            mincho?.DrawChar(px, w, h, s[0], mcx, mcy, 44, happi);
            int n = Mathf.Min(5, s.Length); float size = Mathf.Min(58, 220f / Mathf.Max(1, n));
            mincho?.DrawVertical(px, w, h, s.Substring(0, n), mcx, h - 150 - size * 0.4f, size, new Color(0.95f, 0.82f, 0.48f));
            return Finish(px, w, h, "flag");
        }

        static Texture2D Finish(Color32[] px, int w, int h, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }
    }
}
