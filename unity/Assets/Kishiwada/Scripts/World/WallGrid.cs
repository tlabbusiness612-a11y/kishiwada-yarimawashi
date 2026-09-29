using System.Collections.Generic;
using UnityEngine;

namespace Kishiwada
{
    // 家の壁・柵・桟敷を平面の線分で持つ。曳き手やカメラを壁の外へ押し出すのに使う（地車の当たりは PhysX）
    public sealed class WallGrid
    {
        public struct Seg { public Vector2 a, b; public byte kind; }
        public const byte House = 0, Crowd = 1, Stand = 2;
        public readonly List<Seg> segs = new List<Seg>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        const float Cell = 8f;
        readonly HashSet<int> seen = new HashSet<int>();

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(Vector2 a, Vector2 b, byte kind)
        {
            int i = segs.Count;
            segs.Add(new Seg { a = a, b = b, kind = kind });
            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - 2f) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + 2f) / Cell);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - 2f) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + 2f) / Cell);
            for (int gx = x0; gx <= x1; gx++)
                for (int gz = z0; gz <= z1; gz++)
                {
                    long k = Key(gx, gz);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                    l.Add(i);
                }
        }

        // 半径 r の円を線分の外へ押し出す。押し出した量を返す
        public Vector2 Push(ref Vector2 p, float r)
        {
            Vector2 total = Vector2.zero;
            seen.Clear();
            int gx0 = Mathf.FloorToInt((p.x - r) / Cell), gx1 = Mathf.FloorToInt((p.x + r) / Cell);
            int gz0 = Mathf.FloorToInt((p.y - r) / Cell), gz1 = Mathf.FloorToInt((p.y + r) / Cell);
            for (int gx = gx0; gx <= gx1; gx++)
                for (int gz = gz0; gz <= gz1; gz++)
                {
                    if (!grid.TryGetValue(Key(gx, gz), out var l)) continue;
                    foreach (int i in l)
                    {
                        if (!seen.Add(i)) continue;
                        var s = segs[i];
                        Vector2 q = KMath.ClosestOnSeg(p, s.a, s.b, out _);
                        Vector2 d = p - q; float d2 = d.sqrMagnitude;
                        if (d2 >= r * r || d2 < 1e-12f) continue;
                        float dl = Mathf.Sqrt(d2); Vector2 push = d / dl * (r - dl);
                        p += push; total += push;
                    }
                }
            return total;
        }

        // o から dir 方向に lim まで進んで最初に当たる線分までの距離（当たらなければ lim+1）
        public float Cast(Vector2 o, Vector2 dir, float lim, byte? only = null)
        {
            float best = lim + 1f;
            int steps = Mathf.CeilToInt(lim / Cell) + 1;
            seen.Clear();
            for (int k = 0; k <= steps; k++)
            {
                Vector2 c = o + dir * (k * Cell);
                int gx = Mathf.FloorToInt(c.x / Cell), gz = Mathf.FloorToInt(c.y / Cell);
                for (int ox = -1; ox <= 1; ox++)
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        if (!grid.TryGetValue(Key(gx + ox, gz + oz), out var l)) continue;
                        foreach (int i in l)
                        {
                            if (!seen.Add(i)) continue;
                            var s = segs[i]; if (only.HasValue && s.kind != only.Value) continue;
                            Vector2 e = s.b - s.a; float den = dir.x * e.y - dir.y * e.x;
                            if (Mathf.Abs(den) < 1e-9f) continue;
                            Vector2 w = s.a - o;
                            float t = (w.x * e.y - w.y * e.x) / den, u = (w.x * dir.y - w.y * dir.x) / den;
                            if (t > 0 && t < best && u >= 0 && u <= 1) best = t;
                        }
                    }
            }
            return best;
        }
    }
}
