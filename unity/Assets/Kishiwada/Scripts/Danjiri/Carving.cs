using System.Collections.Generic;
using UnityEngine;

namespace Kishiwada
{
    // 彫り物：高さの関数で細かい格子をふくらませた本物の凹凸。人物（武者）・馬・雲・波を手続き的に並べる。
    // くぼみ（まわりより低い所）は頂点色の α で暗くする（Kishiwada/Lit が陰として読む）
    public sealed class Carving
    {
        struct Blob { public Vector2 c; public Vector2 r; public float h, rot; }
        readonly List<Blob> blobs = new List<Blob>();
        readonly float aspect; // 幅/高さ
        readonly int seed;
        readonly bool waves;
        const float Frame = 0.07f;

        // style 0: 武者と馬（土呂幕・見送り）、1: 雲と龍の胴（枡合）、2: 雲だけ（懸魚・小さな面）
        public Carving(float w, float h, int seed, int style)
        {
            aspect = w / h; this.seed = seed;
            var r = new Rng((uint)(seed * 7919 + 17));
            waves = style == 0 && r.Chance(0.6f);
            if (style == 0)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(aspect / 0.9f));
                for (int i = 0; i < n; i++)
                {
                    float cx = (i + 0.5f + r.Range(-0.15f, 0.15f)) / n * aspect;
                    bool horse = r.Chance(0.45f);
                    float s = r.Range(0.85f, 1.05f);
                    if (horse)
                    {
                        blobs.Add(new Blob { c = new Vector2(cx, 0.38f), r = new Vector2(0.3f, 0.13f) * s, h = 0.75f, rot = r.Range(-0.2f, 0.2f) });
                        blobs.Add(new Blob { c = new Vector2(cx + 0.27f * s, 0.52f), r = new Vector2(0.08f, 0.14f) * s, h = 0.8f, rot = -0.6f });
                        blobs.Add(new Blob { c = new Vector2(cx + 0.33f * s, 0.62f), r = new Vector2(0.07f, 0.05f) * s, h = 0.85f });
                        for (int k = 0; k < 4; k++) blobs.Add(new Blob { c = new Vector2(cx + (k - 1.5f) * 0.14f * s, 0.18f), r = new Vector2(0.025f, 0.12f) * s, h = 0.6f, rot = r.Range(-0.4f, 0.4f) });
                        Figure(r, cx - 0.02f, 0.55f, 0.75f * s);
                    }
                    else Figure(r, cx, 0.12f, s);
                }
                int clouds = Mathf.RoundToInt(aspect * 2.5f);
                for (int i = 0; i < clouds; i++)
                    blobs.Add(new Blob { c = new Vector2(r.Range(0, aspect), r.Range(0.65f, 0.95f)), r = Vector2.one * r.Range(0.06f, 0.12f), h = 0.45f });
            }
            else
            {
                int clouds = Mathf.RoundToInt(aspect * (style == 1 ? 5f : 3f)) + 2;
                for (int i = 0; i < clouds; i++)
                    blobs.Add(new Blob { c = new Vector2(r.Range(0, aspect), r.Range(0.15f, 0.85f)), r = Vector2.one * r.Range(0.1f, 0.22f), h = r.Range(0.4f, 0.65f) });
                if (style == 1) // 龍の胴：うねる帯
                {
                    float ph = r.Range(0, 6.28f);
                    for (float x = 0.05f; x < aspect - 0.05f; x += 0.045f)
                        blobs.Add(new Blob { c = new Vector2(x, 0.5f + Mathf.Sin(x * 5f + ph) * 0.22f), r = new Vector2(0.06f, 0.09f), h = 0.9f });
                }
            }
        }

        void Figure(Rng r, float cx, float foot, float s)
        {
            float lean = r.Range(-0.25f, 0.25f);
            blobs.Add(new Blob { c = new Vector2(cx, foot + 0.34f * s), r = new Vector2(0.075f, 0.2f) * s, h = 0.9f, rot = lean }); // 胴
            blobs.Add(new Blob { c = new Vector2(cx + lean * 0.12f, foot + 0.6f * s), r = new Vector2(0.05f, 0.055f) * s, h = 1f }); // 頭
            blobs.Add(new Blob { c = new Vector2(cx + lean * 0.14f, foot + 0.67f * s), r = new Vector2(0.07f, 0.03f) * s, h = 0.95f, rot = lean }); // 兜
            blobs.Add(new Blob { c = new Vector2(cx - 0.04f * s, foot + 0.12f * s), r = new Vector2(0.03f, 0.12f) * s, h = 0.7f, rot = 0.25f }); // 脚
            blobs.Add(new Blob { c = new Vector2(cx + 0.04f * s, foot + 0.12f * s), r = new Vector2(0.03f, 0.12f) * s, h = 0.7f, rot = -0.3f });
            float arm = r.Range(0.6f, 1.4f);
            blobs.Add(new Blob { c = new Vector2(cx + 0.11f * s, foot + 0.46f * s), r = new Vector2(0.025f, 0.12f) * s, h = 0.8f, rot = -arm });
            blobs.Add(new Blob { c = new Vector2(cx - 0.1f * s, foot + 0.42f * s), r = new Vector2(0.025f, 0.11f) * s, h = 0.8f, rot = arm * 0.7f });
            // 刀・槍
            blobs.Add(new Blob { c = new Vector2(cx + 0.2f * s, foot + 0.6f * s), r = new Vector2(0.012f, 0.18f) * s, h = 0.85f, rot = -arm * 0.8f });
        }

        // s, t ∈ [0,1]（t は上向き）→ 高さ 0..1
        public float Height(float s, float t)
        {
            float x = s * aspect, y = t;
            // 枠
            float fs = Mathf.Min(Mathf.Min(s * aspect, (1 - s) * aspect), Mathf.Min(t, 1 - t));
            if (fs < Frame) return 0.8f + 0.2f * Mathf.Clamp01(fs / Frame * 2f) - (fs > Frame * 0.6f ? 0.25f * (fs - Frame * 0.6f) / (Frame * 0.4f) : 0);
            float bg = 0.12f + Noise.Value(x * 40f, y * 40f, 4096, seed) * 0.05f;
            float hmax = bg;
            if (waves && y < 0.3f)
            {
                float wv = 0.35f + 0.2f * Mathf.Sin(x * 22f + Mathf.Sin(y * 30f) * 1.5f) * (0.3f - y) / 0.3f;
                hmax = Mathf.Max(hmax, wv);
            }
            foreach (var b in blobs)
            {
                Vector2 d = new Vector2(x, y) - b.c;
                float cs = Mathf.Cos(b.rot), sn = Mathf.Sin(b.rot);
                Vector2 q = new Vector2(cs * d.x + sn * d.y, -sn * d.x + cs * d.y);
                float rr = (q.x * q.x) / (b.r.x * b.r.x) + (q.y * q.y) / (b.r.y * b.r.y);
                if (rr >= 1f) continue;
                float dome = Mathf.Sqrt(1f - rr) * b.h;
                // 雲の渦・衣の襞
                dome *= 0.88f + 0.12f * Mathf.Sin(Mathf.Atan2(q.y, q.x) * 3f + Mathf.Sqrt(rr) * 14f);
                hmax = Mathf.Max(hmax, bg + dome * (0.88f - bg));
            }
            return hmax;
        }

        // 面を作る：中心 c、外向き法線 n、横方向 u、幅 w・高さ h、深さ depth
        // topCurve があれば、各列の上端をその高さ（面内のローカル y、中心基準）にする（破風の懸魚など）
        public void Build(MeshBuilder b, Vector3 c, Vector3 n, Vector3 u, float w, float h, float depth, Color tint, float density, System.Func<float, float> topCurve = null)
        {
            Vector3 v = Vector3.Cross(u, n);
            int nu = Mathf.Clamp(Mathf.RoundToInt(w * density), 8, 220), nv = Mathf.Clamp(Mathf.RoundToInt(h * density), 4, 120);
            var H = new float[(nu + 1) * (nv + 1)];
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++) H[j * (nu + 1) + i] = Height(i / (float)nu, j / (float)nv);
            // くぼみ：周りの平均より低い所
            var blur = new float[H.Length]; int R = 2;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float sum = 0; int cnt = 0;
                    for (int dj = -R; dj <= R; dj++) for (int di = -R; di <= R; di++)
                        {
                            int ii = Mathf.Clamp(i + di, 0, nu), jj = Mathf.Clamp(j + dj, 0, nv);
                            sum += H[jj * (nu + 1) + ii]; cnt++;
                        }
                    blur[j * (nu + 1) + i] = sum / cnt;
                }
            int s0 = b.Count;
            float du = w / nu;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float s = i / (float)nu, t = j / (float)nv;
                    float x = (s - 0.5f) * w, yb = -h * 0.5f, yt = topCurve != null ? topCurve(x) : h * 0.5f;
                    float y = Mathf.Lerp(yb, yt, t);
                    int k = j * (nu + 1) + i;
                    float hh = H[k];
                    // 法線（格子の差分）
                    float hl = H[j * (nu + 1) + Mathf.Max(0, i - 1)], hr = H[j * (nu + 1) + Mathf.Min(nu, i + 1)];
                    float hd = H[Mathf.Max(0, j - 1) * (nu + 1) + i], hu = H[Mathf.Min(nv, j + 1) * (nu + 1) + i];
                    float dv = (yt - yb) / nv;
                    float gx = (hr - hl) * depth / (2f * du), gy = (hu - hd) * depth / (2f * dv);
                    Vector3 nn = (n - u * gx - v * gy).normalized;
                    float cav = Mathf.Clamp01(1f - (blur[k] - hh) * 5f) * Mathf.Lerp(0.55f, 1f, hh);
                    var col = tint; col.a = cav;
                    b.V(c + u * x + v * y + n * (hh * depth), nn, new Vector2(x, y), col);
                }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = s0 + j * (nu + 1) + i, bb = a + 1, cc = a + nu + 1, d = cc + 1;
                    b.Quad(a, cc, d, bb);
                }
        }
    }
}
