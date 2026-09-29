using UnityEngine;

namespace Kishiwada
{
    // コース：大阪臨海線を南南西へ → カンカン場（岸和田港交差点）で左へやりまわし → 北町の路地を東南東へ → 紀州街道で右へ
    // 各点は PLATEAU の道路面と OpenStreetMap の道路線から読み取った道路中心（Unity の xz、原点 北緯34.4655° 東経135.3725°）
    public static class Course
    {
        public static readonly Vector2[] P =
        {
            new Vector2(41.6f, 240.0f),   // 臨海線・港緑町あたり（出発）
            new Vector2(-74.8f, 30.0f),   // カンカン場（岸和田港交差点）
            new Vector2(78.8f, -95.2f),   // 紀州街道との辻
            new Vector2(-4.2f, -196.6f),  // 紀州街道を南西へ
        };

        public struct Seg { public Vector2 a, b, dir, left; public float len, s0, yaw; }
        public sealed class Corner { public float s; public Vector2 pos; public string name, sub; public int dir; } // dir +1 左, -1 右

        public static readonly Seg[] Segs;
        public static readonly Corner[] Corners;
        public static readonly float FinishS;
        public static readonly float[] HalfWidth = { 14.5f, 5.2f, 3.6f }; // 道幅の目安（片側）
        public static readonly Path Smooth;  // 角を丸めた道筋（曳き手・カメラ用）

        static Course()
        {
            Segs = new Seg[P.Length - 1];
            float acc = 0f;
            for (int i = 0; i < Segs.Length; i++)
            {
                Vector2 a = P[i], b = P[i + 1], d = b - a; float len = d.magnitude; d /= len;
                Segs[i] = new Seg { a = a, b = b, dir = d, left = KMath.Left(d), len = len, s0 = acc, yaw = Mathf.Atan2(d.x, d.y) };
                acc += len;
            }
            Corners = new[]
            {
                new Corner { s = Segs[1].s0, pos = P[1], name = "カンカン場", sub = "岸和田港交差点から路地へ", dir = 1 },
                new Corner { s = Segs[2].s0, pos = P[2], name = "紀州街道", sub = "路地から紀州街道へ", dir = -1 },
            };
            FinishS = Segs[2].s0 + 72f;
            Smooth = Path.Fillet(P, new[] { 0f, 13f, 8f, 0f }, 0.5f, 60f);
        }

        public struct Proj { public float s, d, t; public int seg; public Vector2 point; }
        public static Proj Project(Vector2 p)
        {
            var best = new Proj { d = float.MaxValue };
            for (int i = 0; i < Segs.Length; i++)
            {
                var s = Segs[i];
                float t = Mathf.Clamp(Vector2.Dot(p - s.a, s.dir), 0f, s.len);
                Vector2 q = s.a + s.dir * t; float d = (p - q).magnitude;
                if (d < best.d - 1e-6f) best = new Proj { s = s.s0 + t, d = d, t = t, seg = i, point = q };
            }
            return best;
        }
        public static Vector2 PointAt(float s, out int seg)
        {
            for (int i = 0; i < Segs.Length; i++)
                if (s <= Segs[i].s0 + Segs[i].len || i == Segs.Length - 1) { seg = i; return Segs[i].a + Segs[i].dir * (s - Segs[i].s0); }
            seg = Segs.Length - 1; return P[P.Length - 1];
        }
        public static Vector2 PointAt(float s) => PointAt(s, out _);
        public static float Length => Segs[Segs.Length - 1].s0 + Segs[Segs.Length - 1].len;
    }

    // 折れ線を一定間隔の点列にしたもの。近い点の探索は前回の位置から近くだけを見る
    public sealed class Path
    {
        public Vector2[] pts; public float[] cum; public float step;
        public float Length => cum[cum.Length - 1];

        // 角を半径 r[i] の円弧で丸め、始点の手前 pre m・終点の先 post m も伸ばした点列を作る
        public static Path Fillet(Vector2[] P, float[] r, float step, float post)
        {
            var dense = new System.Collections.Generic.List<Vector2>();
            dense.Add(P[0]);
            for (int i = 1; i < P.Length - 1; i++)
            {
                Vector2 a = P[i - 1], b = P[i], c = P[i + 1];
                Vector2 d0 = (b - a).normalized, d1 = (c - b).normalized;
                float ang = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d0, d1), -1f, 1f));
                float tl = r[i] * Mathf.Tan(ang * 0.5f);
                Vector2 p0 = b - d0 * tl, p1 = b + d1 * tl;
                dense.Add(p0);
                // 円弧を二次ベジェで近似（十分細かく）
                for (int k = 1; k < 16; k++) { float t = k / 16f; dense.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * b + t * t * p1); }
                dense.Add(p1);
            }
            Vector2 last = P[P.Length - 1], ld = (last - P[P.Length - 2]).normalized;
            dense.Add(last); dense.Add(last + ld * post);
            return Resample(dense, step);
        }
        static Path Resample(System.Collections.Generic.List<Vector2> src, float step)
        {
            float total = 0f; for (int i = 1; i < src.Count; i++) total += (src[i] - src[i - 1]).magnitude;
            int n = Mathf.CeilToInt(total / step) + 1;
            var path = new Path { pts = new Vector2[n], cum = new float[n], step = step };
            int j = 1; float acc = 0f, segStart = 0f;
            for (int k = 0; k < n; k++)
            {
                float s = Mathf.Min(k * step, total);
                while (j < src.Count - 1 && segStart + (src[j] - src[j - 1]).magnitude < s) { segStart += (src[j] - src[j - 1]).magnitude; j++; }
                float sl = (src[j] - src[j - 1]).magnitude, u = sl > 1e-6f ? (s - segStart) / sl : 0f;
                path.pts[k] = Vector2.Lerp(src[j - 1], src[j], Mathf.Clamp01(u)); path.cum[k] = s;
                acc = s;
            }
            return path;
        }

        public Vector2 PointAt(float s)
        {
            s = Mathf.Clamp(s, 0f, Length);
            int i = Mathf.Min(pts.Length - 2, (int)(s / step));
            float u = (s - cum[i]) / Mathf.Max(1e-6f, cum[i + 1] - cum[i]);
            return Vector2.Lerp(pts[i], pts[i + 1], Mathf.Clamp01(u));
        }
        public Vector2 TangentAt(float s)
        {
            int i = Mathf.Clamp((int)(s / step), 0, pts.Length - 2);
            return (pts[i + 1] - pts[i]).normalized;
        }
        // hint：前回の添字。-1 なら全体を探す
        public float Project(Vector2 p, ref int hint)
        {
            int lo = 0, hi = pts.Length - 2;
            if (hint >= 0) { lo = Mathf.Max(0, hint - 12); hi = Mathf.Min(pts.Length - 2, hint + 12); }
            float bestD = float.MaxValue, bestS = 0f; int bestI = lo;
            for (int i = lo; i <= hi; i++)
            {
                Vector2 q = KMath.ClosestOnSeg(p, pts[i], pts[i + 1], out float t);
                float d = (p - q).sqrMagnitude;
                if (d < bestD) { bestD = d; bestS = cum[i] + t * (cum[i + 1] - cum[i]); bestI = i; }
            }
            hint = bestI;
            return bestS;
        }
    }
}
