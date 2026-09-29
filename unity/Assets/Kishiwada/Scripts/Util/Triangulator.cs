using System.Collections.Generic;
using UnityEngine;

namespace Kishiwada
{
    // 穴のない多角形を耳切りで三角形に分ける（建物の平面形・屋根・道路面）
    public static class Triangulator
    {
        public static float Area(IList<Vector2> p)
        {
            float a = 0f;
            for (int i = 0, n = p.Count; i < n; i++) { Vector2 u = p[i], v = p[(i + 1) % n]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        // 返す添字は入力と同じ回り方の三角形
        public static List<int> Triangulate(IList<Vector2> p)
        {
            var result = new List<int>();
            int n = p.Count;
            if (n < 3) return result;
            var v = new List<int>(n);
            bool ccw = Area(p) > 0f;
            for (int i = 0; i < n; i++) v.Add(i);
            int guard = 0;
            while (v.Count > 3 && guard++ < n * n)
            {
                bool cut = false;
                for (int i = 0; i < v.Count; i++)
                {
                    int i0 = v[(i + v.Count - 1) % v.Count], i1 = v[i], i2 = v[(i + 1) % v.Count];
                    Vector2 a = p[i0], b = p[i1], c = p[i2];
                    float cr = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (ccw ? cr <= 1e-9f : cr >= -1e-9f) continue; // 凹んだ角
                    bool inside = false;
                    for (int k = 0; k < v.Count && !inside; k++)
                    {
                        int j = v[k]; if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(p[j], a, b, c)) inside = true;
                    }
                    if (inside) continue;
                    result.Add(i0); result.Add(i1); result.Add(i2);
                    v.RemoveAt(i); cut = true; break;
                }
                if (!cut) { // 自己交差などで耳が見つからない：扇形で逃げる
                    for (int i = 1; i < v.Count - 1; i++) { result.Add(v[0]); result.Add(v[i]); result.Add(v[i + 1]); }
                    return result;
                }
            }
            if (v.Count == 3) { result.Add(v[0]); result.Add(v[1]); result.Add(v[2]); }
            return result;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
