using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    public sealed class Spectator { public Vector3 pos; public float yaw, scale, phase; public bool sitting; public int variant; }

    public sealed class Town
    {
        public GameObject root;
        public readonly List<Spectator> spectators = new List<Spectator>();
        public readonly WallGrid walls = new WallGrid();
        public readonly List<Vector3> spots = new List<Vector3>(); // 桟敷カメラの位置
    }

    // 町並み：PLATEAU の建物の平面形と高さ、道路面から組み立てる。外観（壁・屋根・店先）は用途と高さから当てはめた作り物
    public static class TownBuilder
    {
        static readonly Color W = Color.white;
        static readonly Color[] HOUSE = { Hex(0xefe9dc), Hex(0xe0d8c8), Hex(0xcfc8ba), Hex(0xe9e0d2), Hex(0xc3bcae), Hex(0xd9ccb4), Hex(0xb3ada3), Hex(0xe4ddd5), Hex(0xd6cfc4), Hex(0xa9a49a) };
        static readonly Color[] OFFICE = { Hex(0xe2dfd8), Hex(0xcfccc5), Hex(0xbcb8b0), Hex(0xebe7df), Hex(0xd7d0c4), Hex(0xc9c2b6) };
        static readonly Color[] ROOF = { Hex(0x8e8b86), Hex(0x9a968f), Hex(0x7f7c78), Hex(0xa39f97) };
        static readonly Color[] AWNING = { Hex(0xb33a2a), Hex(0x2f5d8a), Hex(0x3f7a4a), Hex(0xd9b44a), Hex(0x8a3f6a), Hex(0x6b6b6b) };
        static Color Hex(uint c) => KMath.Hex(c);

        sealed class Obb { public Vector2 c, u, v; public float hu, hv; }

        public static Town Build(PlateauData data, MaterialLibrary mats, Transform parent, bool mobile)
        {
            var town = new Town();
            var r = new Rng(1989);
            var ms = new MeshSet();
            var flat = new MeshSet();
            var col = new Dictionary<string, MeshBuilder>(); // 当たり判定用（区画ごと）
            var root = new GameObject("Town");
            root.transform.SetParent(parent, false);
            town.root = root;

            void SetChunk(Vector2 p) => ms.chunk = Mathf.FloorToInt(p.x / 90f) + "," + Mathf.FloorToInt(p.y / 90f);
            MeshBuilder Col(Vector2 p)
            {
                string k = Mathf.FloorToInt(p.x / 90f) + "," + Mathf.FloorToInt(p.y / 90f);
                if (!col.TryGetValue(k, out var b)) col[k] = b = new MeshBuilder();
                return b;
            }

            // ---------- 建物 ----------
            foreach (var bd in data.buildings)
            {
                var p = bd.poly; if (p.Length < 3) continue;
                float A = Triangulator.Area(p); if (Mathf.Abs(A) < 5f) continue;
                int sgn = A > 0 ? 1 : -1;
                Vector2 cen = Vector2.zero; foreach (var q in p) cen += q; cen /= p.Length;
                SetChunk(cen);
                var pr = Course.Project(cen);
                int segI = pr.d < 45f ? pr.seg : -1;
                int u = bd.usage; float h = Mathf.Max(3f, bd.height);
                bool tall = h > 11.5f || u == 412 || u == 401 || u == 431 || u == 441 || (u == 402 && h > 8f);
                var o = ObbOf(p);
                float rect = o != null ? Mathf.Abs(A) / (4f * o.hu * o.hv) : 0f;
                bool machiya = !tall && h < 10f && (segI == 2 || (segI == 1 && r.Next() < 0.3f));
                bool canPitch = o != null && !tall && h < 11.5f && (u == 0 || u == 411 || u == 413 || u == 415 || u == 461 || u == 454 || u == 402) && rect > 0.72f && o.hv > 1.6f && o.hu < 18f;
                float rise = canPitch ? Mathf.Min(2.6f, o.hv * 0.42f) : 0f;
                float wallH = canPitch ? Mathf.Max(2.8f, h - rise) : h;
                int floors = bd.storeys > 0 ? bd.storeys : Mathf.Max(1, Mathf.RoundToInt(wallH / (tall ? 3.2f : 2.9f)));
                float floorH = wallH / floors;

                string wallKey = machiya ? "plaster" : tall ? "office" : "house";
                Color wallCol = machiya ? W : tall ? r.Pick(OFFICE) : r.Pick(HOUSE);
                if (machiya) Walls(ms[wallKey], p, sgn, 0f, wallH, wallCol, 4f, 2.6f, 2.9f);
                else if (tall) Walls(ms[wallKey], p, sgn, 0f, wallH, wallCol, 3.6f, floorH, 0f, r.Next() * 3.6f);
                else Walls(ms[wallKey], p, sgn, 0f, wallH, wallCol, 8f, floorH, 0f, r.Next() < 0.5f ? 0f : 4f);

                if (canPitch) Pitched(ms, o, wallH, rise, !machiya && r.Next() < 0.45f, wallKey, wallCol, floorH, machiya);
                else
                {
                    FlatRoof(ms["concrete"], p, wallH, r.Pick(ROOF));
                    if (tall)
                    {
                        Walls(ms["concrete"], p, sgn, wallH, wallH + 0.6f, Hex(0xd9d6cf), 1f, 1f, 0f);
                        InsetTop(ms["concrete"], p, sgn, wallH + 0.6f, Hex(0xd9d6cf));
                        RoofClutter(ms, o, wallH, r);
                        if (u == 412 && floors >= 3 && o != null) Balconies(ms, p, sgn, o, floors, floorH, r);
                    }
                }
                // 室外機・雨樋（住宅の側面）
                if (!tall && !machiya && o != null && r.Next() < 0.55f) AcUnit(ms, p, sgn, r, Mathf.Min(floorH + 0.3f, wallH - 0.8f));

                // 道に面した壁：店先・格子・看板・庇
                if (segI >= 0)
                {
                    float hw = Course.HalfWidth[segI];
                    for (int i = 0; i < p.Length; i++)
                    {
                        Vector2 a = p[i], b = p[(i + 1) % p.Length], e = b - a; float L = e.magnitude;
                        if (L < 2.2f) continue;
                        Vector2 nOut = Out(e / L, sgn), mid = (a + b) * 0.5f;
                        var q = Course.Project(mid); if (q.d > hw + 9f) continue;
                        Vector2 toC = Course.PointAt(q.s) - mid;
                        if (Vector2.Dot(nOut, toC) / Mathf.Max(0.01f, toC.magnitude) < 0.55f) continue;
                        Vector3 n3 = new Vector3(nOut.x, 0, nOut.y), m3 = new Vector3(mid.x, 0, mid.y);
                        Quaternion rot = Quaternion.LookRotation(n3);
                        if (machiya)
                        {
                            WallQuad(ms["lattice"], m3 + n3 * 0.06f + Vector3.up * 1.5f, n3, L, 3.0f, new Vector2(0, 0), new Vector2(L / 2f, 1f), W);
                            ms["tile"].Box(m3 + n3 * 0.55f + Vector3.up * 3.18f, new Vector3(L + 0.1f, 0.1f, 1.1f), rot * Quaternion.Euler(17f, 0, 0), W, 1f);
                            ms["wood"].Box(m3 + n3 * 0.1f + Vector3.up * 3.0f, new Vector3(L, 0.14f, 0.2f), rot, Hex(0x5a4030), 1f);
                            if (r.Next() < 0.6f) LanternPair(ms, m3 + n3 * 0.95f + Vector3.up * 2.6f, n3);
                        }
                        else if (wallH > 4.5f)
                        {
                            int cell = r.Int(4);
                            Vector2 uv0 = new Vector2((cell % 2) * 0.5f, (cell / 2) * 0.5f);
                            WallQuad(ms["shop"], m3 + n3 * 0.06f + Vector3.up * 1.5f, n3, L - 0.2f, 3.0f, uv0 + new Vector2(0.003f, 0.003f), new Vector2(0.494f, 0.494f), W);
                            ms["plain"].Box(m3 + n3 * 0.6f + Vector3.up * 3.1f, new Vector3(L, 0.07f, 1.2f), rot * Quaternion.Euler(12.6f, 0, 0), r.Pick(AWNING), 1f);
                            if (r.Next() < 0.75f)
                            {
                                int sg = r.Int(TextureFactory.SignWords.Length), sc = sg % TextureFactory.SignCols, sr = sg / TextureFactory.SignCols;
                                float sw = Mathf.Min(L - 0.6f, 3.6f);
                                ms["plain"].Box(m3 + n3 * 0.1f + Vector3.up * 3.8f, new Vector3(sw + 0.2f, 1.0f, 0.12f), rot, Hex(0x2a2a2a), 1f);
                                float cu = 1f / TextureFactory.SignCols, cv = 1f / TextureFactory.SignRows;
                                WallQuad(ms["signs"], m3 + n3 * 0.17f + Vector3.up * 3.8f, n3, sw, 0.9f,
                                    new Vector2(sc * cu, 1f - (sr + 1) * cv), new Vector2(cu, cv), W);
                            }
                            if (r.Next() < 0.3f) LanternPair(ms, m3 + n3 * 1.3f + Vector3.up * 2.7f, n3);
                        }
                    }
                }
                // 当たり判定（コース近くの建物だけ）
                if (pr.d < 40f)
                {
                    for (int i = 0; i < p.Length; i++) town.walls.Add(p[i], p[(i + 1) % p.Length], WallGrid.House);
                    Prism(Col(cen), p, sgn, h + (canPitch ? 0f : 0f));
                }
            }

            // ---------- 地面・道路 ----------
            {
                var g = flat["concrete"];
                float S = 1500f;
                int a0 = g.V(new Vector3(-S, -0.04f, -S), Vector3.up, new Vector2(-S, -S), Hex(0xb8b2a6));
                int a1 = g.V(new Vector3(-S, -0.04f, S), Vector3.up, new Vector2(-S, S), Hex(0xb8b2a6));
                int a2 = g.V(new Vector3(S, -0.04f, S), Vector3.up, new Vector2(S, S), Hex(0xb8b2a6));
                int a3 = g.V(new Vector3(S, -0.04f, -S), Vector3.up, new Vector2(S, -S), Hex(0xb8b2a6));
                g.TriFacing(a0, a1, a2, Vector3.up); g.TriFacing(a0, a2, a3, Vector3.up);
            }
            foreach (var rd in data.roads)
            {
                if (rd.poly.Length < 3) continue;
                if (rd.function == "1") // 阪神高速湾岸線（高架）
                {
                    Vector2 c = Vector2.zero; foreach (var q in rd.poly) c += q; c /= rd.poly.Length;
                    SetChunk(c);
                    FlatRoof(ms["concrete"], rd.poly, 11f, Hex(0x9d9d98));
                    FlatRoof(ms["concrete"], rd.poly, 10.4f, Hex(0x8a8a86), true);
                    ms["concrete"].Box(new Vector3(c.x, 5.2f, c.y), new Vector3(2.2f, 10.4f, 2.2f), Quaternion.identity, Hex(0xa8a8a2), 1f);
                    continue;
                }
                FlatRoof(flat["asphalt"], rd.poly, 0f, W);
            }
            // 臨海線の区画線と横断歩道
            {
                var s0 = Course.Segs[0]; var mk = flat["plain"];
                Quaternion q0 = Quaternion.Euler(0, s0.yaw * Mathf.Rad2Deg, 0);
                Vector2 rt = -s0.left;
                for (float t = 0; t < s0.len - 34f; t += 1f)
                {
                    Vector2 c = s0.a + s0.dir * t;
                    mk.Box(KMath.X0Z(c + rt * 0.2f, 0.01f), new Vector3(0.15f, 0.02f, 0.9f), q0, Hex(0xe8c64a), 1f, default, false);
                    mk.Box(KMath.X0Z(c - rt * 0.2f, 0.01f), new Vector3(0.15f, 0.02f, 0.9f), q0, Hex(0xe8c64a), 1f, default, false);
                    if (t % 8f < 5f) foreach (float k in new[] { -6.5f, 6.5f }) mk.Box(KMath.X0Z(c + rt * k, 0.01f), new Vector3(0.15f, 0.02f, 1f), q0, Hex(0xeeeeee), 1f, default, false);
                }
                void Zebra(Vector2 c, float yaw, float len)
                {
                    Vector2 nx = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw)); // 進行方向に直交
                    for (float k = -len / 2; k < len / 2; k += 1f)
                        mk.Box(KMath.X0Z(c + nx * k, 0.012f), new Vector3(0.5f, 0.02f, 3.4f), Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0), Hex(0xeeeeee), 1f, default, false);
                }
                var J = Course.Segs[1].a; var s1 = Course.Segs[1];
                Zebra(J - s0.dir * 26f, s0.yaw, 26f);
                Zebra(J + s1.dir * 19f, s1.yaw, 9f);
            }

            // ---------- 観覧席・柵・見物人 ----------
            var S0 = Course.Segs[0]; var S1 = Course.Segs[1]; var S2 = Course.Segs[2];
            Vector2 Jc = S1.a, Kc = S2.a;
            Vector2 L0 = S0.left, L1 = S1.left, L2 = S2.left;
            Vector2 AddP(Vector2 a, Course.Seg s, float t, Vector2 n, float uu) => a + s.dir * t + n * uu;
            var crowdCols = new List<(Vector2 a, Vector2 b, float h)>();

            // 柵（ロープと赤い杭）＋ 後ろに見物人。faceSign=+1 なら点の並びの右側が道
            void Fence(Vector2[] pts, int faceSign, int rows = 2)
            {
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector2 a = pts[i], c = pts[i + 1]; float L = (c - a).magnitude; if (L < 0.01f) continue;
                    Vector2 d = (c - a) / L, n = new Vector2(d.y, -d.x) * faceSign; // 道の側
                    SetChunk(a);
                    Quaternion q = Quaternion.Euler(0, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 0);
                    ms["plain"].Box(KMath.X0Z((a + c) * 0.5f, 1.0f), new Vector3(0.05f, 0.05f, L), q, Hex(0xf2efe6), 1f);
                    for (float t = 0; t < L; t += 2.4f) ms["plain"].Box(KMath.X0Z(a + d * t, 0.55f), new Vector3(0.09f, 1.1f, 0.09f), q, Hex(0xc8261c), 1f);
                    for (int row = 0; row < rows; row++)
                        for (float t = r.Next() * 0.6f; t < L; t += 0.62f + r.Next() * 0.5f)
                        {
                            float back = 0.7f + row * 0.72f + r.Next() * 0.2f;
                            Vector2 sp = a + d * t - n * back;
                            town.spectators.Add(new Spectator { pos = KMath.X0Z(sp), yaw = Mathf.Atan2(n.x, n.y) + (r.Next() - 0.5f) * 0.5f });
                        }
                    town.walls.Add(a, c, WallGrid.Crowd);
                    crowdCols.Add((a, c, 1.2f));
                }
            }
            // 桟敷（ひな壇）。a→c の右手へ段が上がる
            void Stand(Vector2 a, Vector2 c, int tiers)
            {
                float L = (c - a).magnitude; Vector2 d = (c - a) / L, bk = new Vector2(d.y, -d.x);
                Quaternion q = Quaternion.Euler(0, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 0);
                SetChunk(a);
                Vector2 mid = (a + c) * 0.5f;
                for (int k = 0; k < tiers; k++)
                {
                    float off = 1f + k * 2f, top = 0.55f * (k + 1);
                    ms["plain"].Box(KMath.X0Z(mid + bk * off, top / 2f), new Vector3(2f, top, L), q, k % 2 == 1 ? Hex(0x707c89) : Hex(0x5e6977), 1f);
                    for (float t = 0.5f; t < L - 0.3f; t += 0.6f + r.Next() * 0.2f)
                        if (r.Next() < 0.9f)
                        {
                            Vector2 sp = a + d * t + bk * (off + 0.35f);
                            town.spectators.Add(new Spectator { pos = KMath.X0Z(sp, top), yaw = Mathf.Atan2(-bk.x, -bk.y), sitting = true });
                        }
                }
                float back = 2f * tiers + 0.2f;
                ms["plain"].Box(KMath.X0Z(mid + bk * back, 2f), new Vector3(0.3f, 4f, L), q, Hex(0x46525f), 1f);
                WallQuad(ms["kohaku"], KMath.X0Z(mid - bk * 0.04f, 0.5f), KMath.X0Z(-bk), L, 1.0f, Vector2.zero, new Vector2(L, 1f), W);
                town.walls.Add(a, c, WallGrid.Stand);
                crowdCols.Add((a, c, 1.1f));
            }
            // 臨海線：両側の柵
            float hw0 = Course.HalfWidth[0];
            Fence(new[] { AddP(S0.a, S0, -10, L0, hw0), AddP(S0.a, S0, S0.len - 52, L0, hw0) }, 1);
            Fence(new[] { AddP(S0.a, S0, S0.len + 22, L0, -hw0), AddP(S0.a, S0, -10, L0, -hw0) }, 1);
            // 山側の桟敷（曲がる内側）と海側の桟敷（外側・正面）
            Stand(AddP(S0.a, S0, S0.len - 16, L0, hw0 + 0.5f), AddP(S0.a, S0, S0.len - 52, L0, hw0 + 0.5f), 3);
            Stand(AddP(Jc, S0, 22, L0, -hw0), AddP(Jc, S0, 22, L0, 9), 5);
            // 角の内側・外側をつなぐ人垣
            float hw1 = Course.HalfWidth[1];
            Fence(new[] { AddP(S0.a, S0, S0.len - 16, L0, hw0 + 0.5f), AddP(Jc, S1, 20, L1, hw1) }, 1);
            Fence(new[] { AddP(Jc, S0, 22, L0, 9), AddP(Jc, S1, 14, L1, -hw1 - 3.5f), AddP(Jc, S1, 28, L1, -hw1) }, -1, 3);
            // 路地・紀州街道：建物の切れ目は人垣でふさぐ
            void Corridor(Course.Seg seg, Vector2 n, float hw, float fromL, float fromR, float to)
            {
                foreach (int sd in new[] { 1, -1 })
                {
                    float from = sd > 0 ? fromL : fromR;
                    var run = new List<Vector2>();
                    void Flush() { if (run.Count > 1) Fence(run.ToArray(), sd > 0 ? 1 : -1, 1); run.Clear(); }
                    for (float t = from; t <= to; t += 1f)
                    {
                        Vector2 c = seg.a + seg.dir * t;
                        float d = town.walls.Cast(c, n * sd, hw + 2.5f, WallGrid.House);
                        if (d > hw) run.Add(c + n * sd * hw);
                        else
                        {
                            Flush();
                            if (r.Next() < 0.85f)
                            {
                                float back = d - 0.45f - r.Next() * 0.35f;
                                town.spectators.Add(new Spectator { pos = KMath.X0Z(c + n * sd * back), yaw = Mathf.Atan2(-n.x * sd, -n.y * sd) + (r.Next() - 0.5f) * 0.6f });
                            }
                        }
                    }
                    Flush();
                }
            }
            Corridor(S1, L1, hw1, 20, 28, S1.len - 7);
            Corridor(S2, L2, Course.HalfWidth[2], 6, 6, S2.len);
            // 紀州街道の辻：まっすぐ・左の道は人垣でふさぐ
            float hw2 = Course.HalfWidth[2];
            Fence(new[] { AddP(S1.a, S1, S1.len + 6, L1, -hw1 - 1), AddP(S1.a, S1, S1.len + 6, L1, hw1 + 1) }, -1, 3);
            Fence(new[] { AddP(Kc, S2, -6, L2, -hw2 - 3), AddP(Kc, S2, -6, L2, hw2 + 3) }, 1, 3);
            Vector2 end = Course.PointAt(Course.FinishS + 18f);
            Fence(new[] { end - L2 * (hw2 + 1), end + L2 * (hw2 + 1) }, -1, 3);

            // ---------- 信号・電柱・電線・提灯 ----------
            void Wire(Vector3 a, Vector3 c, float sag)
            {
                Vector3 prev = a;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f;
                    Vector3 q = Vector3.Lerp(a, c, t) - Vector3.up * Mathf.Sin(t * Mathf.PI) * sag;
                    ms["black"].Cylinder(prev, q, 0.012f, 0.012f, 4, W, false, false);
                    prev = q;
                }
            }
            void Signal(Vector2 at, float yaw)
            {
                SetChunk(at); Vector2 ax = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
                ms["metal"].Cylinder(KMath.X0Z(at), KMath.X0Z(at, 6.2f), 0.12f, 0.1f, 10, Hex(0x8d9096));
                ms["metal"].Cylinder(KMath.X0Z(at, 5.9f), KMath.X0Z(at + ax * 6f, 6.0f), 0.07f, 0.06f, 8, Hex(0x8d9096));
                Vector2 s = at + ax * 5.2f;
                Quaternion q = Quaternion.Euler(0, yaw * Mathf.Rad2Deg + 90f, 0);
                ms["plain"].Box(KMath.X0Z(s, 6.3f), new Vector3(1.25f, 0.42f, 0.3f), q, Hex(0x3f4a44), 1f);
                Vector2 side = new Vector2(ax.y, -ax.x);
                for (int i = 0; i < 3; i++)
                    ms[i == 0 ? "lantern" : "black"].Ellipsoid(KMath.X0Z(s + ax * (i - 1) * 0.38f - side * 0.16f, 6.3f), Vector3.one * 0.13f, 10, 6, i == 0 ? Hex(0x2bbf6a) : Hex(0x222222));
            }
            { var a = AddP(S0.a, S0, S0.len - 30, L0, -hw0 + 0.5f); Signal(a, Mathf.Atan2(L0.x, L0.y)); }
            { var a = AddP(S0.a, S0, S0.len - 54, L0, hw0 - 0.5f); Signal(a, Mathf.Atan2(-L0.x, -L0.y)); }
            void Poles(Course.Seg seg, Vector2 n, float off, float from, float to)
            {
                Vector2? prev = null;
                for (float t = from; t <= to; t += 26f + r.Next() * 6f)
                {
                    Vector2 at = seg.a + seg.dir * t + n * off; SetChunk(at);
                    ms["concrete"].Cylinder(KMath.X0Z(at), KMath.X0Z(at, 10.5f), 0.17f, 0.12f, 10, Hex(0xa9a59c));
                    Quaternion q = Quaternion.Euler(0, seg.yaw * Mathf.Rad2Deg + 90f, 0);
                    ms["metal"].Box(KMath.X0Z(at, 9.6f), new Vector3(0.12f, 0.12f, 1.8f), q, Hex(0x6d6a64), 1f);
                    ms["metal"].Box(KMath.X0Z(at, 8.9f), new Vector3(0.12f, 0.12f, 1.4f), q, Hex(0x6d6a64), 1f);
                    if (r.Next() < 0.4f) ms["metal"].Cylinder(KMath.X0Z(at + n * 0.45f, 7.2f), KMath.X0Z(at + n * 0.45f, 8.0f), 0.28f, 0.28f, 10, Hex(0x8a8f94));
                    if (prev.HasValue)
                        foreach (var (k, y) in new[] { (-0.8f, 9.66f), (0.8f, 9.66f), (0f, 8.96f) })
                        {
                            Vector2 sx = new Vector2(seg.dir.y, -seg.dir.x) * k;
                            Wire(KMath.X0Z(prev.Value + sx, y), KMath.X0Z(at + sx, y), 0.35f);
                        }
                    prev = at;
                }
            }
            Poles(S1, L1, hw1 - 0.3f, 22, S1.len - 10);
            Poles(S2, L2, -(hw2 - 0.2f), 8, S2.len - 4);
            void LanternLine(Course.Seg seg, Vector2 n, float t, float hw)
            {
                Vector2 a = seg.a + seg.dir * t + n * hw, c = a - n * hw * 2f;
                SetChunk(a);
                Wire(KMath.X0Z(a, 8.4f), KMath.X0Z(c, 8.4f), 0.5f);
                for (int i = 1; i < 7; i++)
                {
                    float uu = i / 7f;
                    Lantern(ms, KMath.X0Z(Vector2.Lerp(a, c, uu), 8.1f - Mathf.Sin(uu * Mathf.PI) * 0.5f), 1f, i % 2 == 1);
                }
            }
            for (float t = 26; t < S1.len - 10; t += 22) LanternLine(S1, L1, t, hw1 + 0.3f);
            for (float t = 12; t < S2.len; t += 20) LanternLine(S2, L2, t, hw2 + 0.5f);

            // ---------- 遠景：岸和田城（位置は目安）・和泉山脈 ----------
            {
                Vector2 cs = new Vector2(-95f, -660f); var b = flat["plain"];
                void Fr(float rb, float rt, float hh, float y, Color c)
                {
                    b.Push(KMath.X0Z(cs, y), Quaternion.Euler(0, 45, 0));
                    b.Cylinder(Vector3.zero, Vector3.up * hh, rb, rt, 4, c);
                    b.Pop();
                }
                Fr(30, 23, 10, 0, Hex(0x8f8a7e));
                float y = 10;
                float[,] tiers = { { 26, 10, 18 }, { 19, 8.5f, 13.5f }, { 13, 7.5f, 9 } };
                for (int i = 0; i < 3; i++)
                {
                    float s = tiers[i, 0], hh = tiers[i, 1], rs = tiers[i, 2];
                    b.Box(KMath.X0Z(cs, y + hh * 0.31f), new Vector3(s * 0.62f, hh * 0.62f, s * 0.5f), Hex(0xf2f0ea));
                    y += hh * 0.62f; Fr(rs * 0.8f, rs * 0.42f, 2.6f, y - 0.4f, Hex(0x3b4150)); y += 1.6f;
                    if (i == 2)
                    {
                        b.Box(KMath.X0Z(cs, y + 0.6f), new Vector3(3.2f, 1.6f, 1.2f), Hex(0x3b4150));
                        foreach (int sx in new[] { -1, 1 }) b.Box(KMath.X0Z(cs + new Vector2(sx * 1.6f, 0), y + 1.8f), new Vector3(0.4f, 0.9f, 0.4f), Hex(0xd4a646));
                    }
                }
            }
            {
                var b = flat["hills"]; int N = 90; var hr = new Rng(7);
                int s = b.Count;
                for (int i = 0; i <= N; i++)
                {
                    float a = -1.6f + i / (float)N * 3.2f, R = 1450f;
                    float hh = 70f + Mathf.Sin(i * 0.7f) * 25f + Mathf.Sin(i * 0.23f) * 70f + hr.Next() * 15f;
                    // 山は南東（内陸）側：+z が北なので南は -z
                    Vector3 dir = new Vector3(Mathf.Cos(a) * 0.9f + 0.1f, 0, -Mathf.Sin(a) * 0.9f - 0.35f).normalized;
                    Vector3 nrm = -dir;
                    // 日に照らされても白く飛ばないよう暗めに（霧で青くかすむ）
                    b.V(dir * R + Vector3.down * 5f, nrm, new Vector2(i, 0), Hex(0x2a3440));
                    b.V(dir * R + Vector3.up * hh, nrm, new Vector2(i, 1), Hex(0x3a4a5a));
                }
                for (int i = 0; i < N; i++) { int k = s + i * 2; b.TriFacing(k, k + 1, k + 3, b.nor[k]); b.TriFacing(k, k + 3, k + 2, b.nor[k]); }
            }

            // ---------- まとめ ----------
            ms.Build("Buildings", root.transform, k => mats[k], ShadowCastingMode.On, true, 8);
            flat.Build("Ground", root.transform, k => mats[k], ShadowCastingMode.Off, true, 8);
            foreach (Transform t in root.transform.Find("Ground")) if (t.name.StartsWith("plain")) t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;

            // 当たり判定：建物（区画ごとのメッシュ）、地面、柵・桟敷（箱）
            var colRoot = new GameObject("Colliders"); colRoot.transform.SetParent(root.transform, false); colRoot.layer = 8;
            var wood = new PhysicsMaterial("house") { dynamicFriction = 0.45f, staticFriction = 0.55f, bounciness = 0.08f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Average };
            foreach (var kv in col)
            {
                if (kv.Value.Count == 0) continue;
                var go = new GameObject("walls " + kv.Key) { layer = 8 };
                go.transform.SetParent(colRoot.transform, false);
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = kv.Value.ToMesh("col", false); mc.sharedMaterial = wood;
            }
            {
                var go = new GameObject("ground") { layer = 8 }; go.transform.SetParent(colRoot.transform, false);
                var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, -0.5f, 0); bc.size = new Vector3(3000, 1, 3000);
                bc.sharedMaterial = new PhysicsMaterial("ground") { dynamicFriction = 0.6f, staticFriction = 0.7f };
            }
            var crowdMat = new PhysicsMaterial("crowd") { dynamicFriction = 0.5f, staticFriction = 0.6f, bounciness = 0.05f };
            foreach (var (a, b, hh) in crowdCols)
            {
                Vector2 d = b - a; float L = d.magnitude; if (L < 0.05f) continue;
                var go = new GameObject("crowd") { layer = 10 }; go.transform.SetParent(colRoot.transform, false);
                go.transform.SetPositionAndRotation(KMath.X0Z((a + b) * 0.5f, hh * 0.5f), Quaternion.Euler(0, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 0));
                var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(0.6f, hh, L); bc.sharedMaterial = crowdMat;
            }

            // 見物人の個性
            foreach (var s in town.spectators) { s.scale = 0.9f + r.Next() * 0.16f; s.phase = r.Next() * 6.28f; s.variant = r.Int(8); }
            // 桟敷カメラの位置
            town.spots.Add(KMath.X0Z(Jc + S0.dir * 30f - L0 * 20f, 7.5f));
            town.spots.Add(KMath.X0Z(Kc + S1.dir * 8f + L1 * 1f, 8.5f));
            return town;
        }

        // ---------- 部品 ----------
        static Vector2 Out(Vector2 d, int sgn) => sgn > 0 ? new Vector2(d.y, -d.x) : new Vector2(-d.y, d.x);

        // 壁。uScale：テクスチャ 1 枚の幅(m)、vFloor：テクスチャ 1 枚の高さ(m)、vOff：v=0 の高さ
        static void Walls(MeshBuilder b, Vector2[] p, int sgn, float y0, float y1, Color c, float uScale, float vFloor, float vOff, float uOff = 0f)
        {
            float per = uOff;
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 a = p[i], e2 = p[(i + 1) % p.Length], e = e2 - a; float L = e.magnitude;
                if (L < 0.05f) continue;
                Vector2 n2 = Out(e / L, sgn); Vector3 n = new Vector3(n2.x, 0, n2.y);
                // 外から見て右へ u が増えるように
                bool right = sgn > 0;
                float u0 = per / uScale, u1 = (per + L) / uScale; per += L;
                float v0 = (y0 - vOff) / vFloor, v1 = (y1 - vOff) / vFloor;
                Vector3 A0 = new Vector3(a.x, y0, a.y), A1 = new Vector3(a.x, y1, a.y), B0 = new Vector3(e2.x, y0, e2.y), B1 = new Vector3(e2.x, y1, e2.y);
                float ua = right ? u0 : u1, ub = right ? u1 : u0;
                int i0 = b.VW(A0, n, new Vector2(ua, v0), c), i1 = b.VW(A1, n, new Vector2(ua, v1), c);
                int i2 = b.VW(B1, n, new Vector2(ub, v1), c), i3 = b.VW(B0, n, new Vector2(ub, v0), c);
                b.TriFacing(i0, i1, i2, n); b.TriFacing(i0, i2, i3, n);
            }
        }
        // 平らな屋根・道路面（UV はメートル）。down=true なら下向き（高架の裏）
        static void FlatRoof(MeshBuilder b, Vector2[] p, float y, Color c, bool down = false)
        {
            var tris = Triangulator.Triangulate(p);
            int s = b.Count; Vector3 n = down ? Vector3.down : Vector3.up;
            foreach (var q in p) b.VW(new Vector3(q.x, y, q.y), n, q, c);
            for (int t = 0; t < tris.Count; t += 3) b.TriFacing(s + tris[t], s + tris[t + 1], s + tris[t + 2], n);
        }
        // パラペットの天端
        static void InsetTop(MeshBuilder b, Vector2[] p, int sgn, float y, Color c)
        {
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 a = p[i], e2 = p[(i + 1) % p.Length], e = e2 - a; float L = e.magnitude; if (L < 0.05f) continue;
                Vector2 n = Out(e / L, sgn) * -0.2f;
                int i0 = b.VW(KMath.X0Z(a, y), Vector3.up, a, c), i1 = b.VW(KMath.X0Z(a + n, y), Vector3.up, a + n, c);
                int i2 = b.VW(KMath.X0Z(e2 + n, y), Vector3.up, e2 + n, c), i3 = b.VW(KMath.X0Z(e2, y), Vector3.up, e2, c);
                b.TriFacing(i0, i1, i2, Vector3.up); b.TriFacing(i0, i2, i3, Vector3.up);
            }
        }
        // 当たり判定用の柱体（壁と天井）
        static void Prism(MeshBuilder b, Vector2[] p, int sgn, float h)
        {
            Walls(b, p, sgn, -0.5f, h, W, 1f, 1f, 0f);
            FlatRoof(b, p, h, W);
        }

        static Obb ObbOf(Vector2[] p)
        {
            int n = p.Length; Obb best = null; float bestA = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vector2 e = p[(i + 1) % n] - p[i]; float L = e.magnitude; if (L < 0.5f) continue;
                Vector2 u = e / L, v = new Vector2(-u.y, u.x);
                float a0 = 1e9f, a1 = -1e9f, c0 = 1e9f, c1 = -1e9f;
                foreach (var q in p) { float pu = Vector2.Dot(q, u), pv = Vector2.Dot(q, v); a0 = Mathf.Min(a0, pu); a1 = Mathf.Max(a1, pu); c0 = Mathf.Min(c0, pv); c1 = Mathf.Max(c1, pv); }
                float A = (a1 - a0) * (c1 - c0);
                if (A < bestA) { bestA = A; best = new Obb { u = u, v = v, c = u * ((a0 + a1) / 2) + v * ((c0 + c1) / 2), hu = (a1 - a0) / 2, hv = (c1 - c0) / 2 }; }
            }
            if (best != null && best.hu < best.hv) { var t = best.u; best.u = best.v; best.v = -t; float th = best.hu; best.hu = best.hv; best.hv = th; }
            return best;
        }

        // 切妻・寄棟の瓦屋根
        static void Pitched(MeshSet ms, Obb o, float wallH, float rise, bool hip, string wallKey, Color wallCol, float floorH, bool machiya)
        {
            const float ov = 0.5f;
            float L = o.hu + ov, S = o.hv + ov, slope = rise / o.hv, eaveY = wallH - ov * slope, top = wallH + rise;
            Vector3 P(float a, float c, float y) { Vector2 q = o.c + o.u * a + o.v * c; return new Vector3(q.x, y, q.y); }
            float rl = hip ? Mathf.Max(0.2f, o.hu - o.hv) : L;
            var t = ms["tile"];
            float sl = Mathf.Sqrt(S * S + (rise + ov * slope) * (rise + ov * slope));
            foreach (int s in new[] { 1, -1 })
            {
                Vector3 E1 = P(L, s * S, eaveY), E2 = P(-L, s * S, eaveY), R2 = P(-rl, 0, top), R1 = P(rl, 0, top);
                Vector3 n = Vector3.Cross(E2 - E1, R1 - E1).normalized; if (n.y < 0) n = -n;
                int a = t.VW(E1, n, new Vector2(L, 0), W), b = t.VW(E2, n, new Vector2(-L, 0), W);
                int c = t.VW(R2, n, new Vector2(-rl, sl), W), d = t.VW(R1, n, new Vector2(rl, sl), W);
                t.TriFacing(a, b, c, n); t.TriFacing(a, c, d, n);
                if (hip)
                {
                    Vector3 A1 = P(s * L, S, eaveY), A2 = P(s * L, -S, eaveY), R = P(s * rl, 0, top);
                    Vector3 hn = Vector3.Cross(A2 - A1, R - A1).normalized; if (hn.y < 0) hn = -hn;
                    int i0 = t.VW(A1, hn, new Vector2(S, 0), W), i1 = t.VW(A2, hn, new Vector2(-S, 0), W), i2 = t.VW(R, hn, new Vector2(0, sl), W);
                    t.TriFacing(i0, i1, i2, hn);
                }
            }
            // 棟
            float yaw = Mathf.Atan2(o.u.x, o.u.y) * Mathf.Rad2Deg;
            t.Box(P(0, 0, top + 0.08f), new Vector3(0.32f, 0.24f, rl * 2f + 0.3f), Quaternion.Euler(0, yaw, 0), Hex(0xb8b8b8), 1f);
            if (!hip)
            {
                var wb = ms[wallKey];
                foreach (int s in new[] { 1, -1 })
                {
                    Vector3 a = P(s * o.hu, o.hv, wallH), c = P(s * o.hu, -o.hv, wallH), tp = P(s * o.hu, 0, top);
                    Vector3 n = new Vector3(o.u.x, 0, o.u.y) * s;
                    float vb = machiya ? (wallH - 2.9f) / 2.6f : wallH / floorH, vt = machiya ? (top - 2.9f) / 2.6f : top / floorH;
                    int i0 = wb.VW(a, n, new Vector2(0.1f, vb), wallCol), i1 = wb.VW(c, n, new Vector2(0.4f, vb), wallCol), i2 = wb.VW(tp, n, new Vector2(0.25f, vt), wallCol);
                    wb.TriFacing(i0, i1, i2, n);
                }
            }
        }

        // 屋上の室外機・給水塔・手すり
        static void RoofClutter(MeshSet ms, Obb o, float y, Rng r)
        {
            if (o == null) return;
            int n = Mathf.Min(6, (int)(o.hu * o.hv / 20f));
            var b = ms["metal"];
            for (int i = 0; i < n; i++)
            {
                if (r.Next() < 0.35f) continue;
                Vector2 q = o.c + o.u * r.Range(-o.hu + 1f, o.hu - 1f) + o.v * r.Range(-o.hv + 1f, o.hv - 1f);
                float yaw = Mathf.Atan2(o.u.x, o.u.y) * Mathf.Rad2Deg;
                b.Box(KMath.X0Z(q, y + 0.4f), new Vector3(0.9f, 0.8f, 0.4f), Quaternion.Euler(0, yaw, 0), Hex(0xc9c9c4), 1f);
            }
            if (o.hu * o.hv > 60f && r.Next() < 0.4f)
            {
                Vector2 q = o.c + o.u * (o.hu * 0.5f);
                b.Cylinder(KMath.X0Z(q, y), KMath.X0Z(q, y + 2.2f), 0.9f, 0.9f, 14, Hex(0xb9c3c9));
            }
        }

        // 集合住宅のバルコニー（南に近い長辺へ）
        static void Balconies(MeshSet ms, Vector2[] p, int sgn, Obb o, int floors, float floorH, Rng r)
        {
            Vector2 side = Vector2.Dot(o.v, Vector2.down) > 0 ? o.v : -o.v; // 南寄り（-z）の長辺
            Vector2 c = o.c + side * o.hv;
            float len = o.hu * 2f - 0.6f; if (len < 4f) return;
            float yaw = Mathf.Atan2(o.u.x, o.u.y) * Mathf.Rad2Deg;
            Quaternion q = Quaternion.Euler(0, yaw, 0);
            var slab = ms["concrete"]; var rail = ms["office"];
            for (int f = 1; f < floors; f++)
            {
                float y = f * floorH;
                slab.Box(KMath.X0Z(c + side * 0.55f, y - 0.08f), new Vector3(1.1f, 0.16f, len), q, Hex(0xd8d4cc), 1f);
                // 手すり壁（正面）
                slab.Box(KMath.X0Z(c + side * 1.08f, y + 0.5f), new Vector3(0.06f, 1.0f, len), q, Hex(0xe4e0d8), 1f);
                // 住戸の仕切り
                for (float t = -len / 2; t <= len / 2 + 0.01f; t += Mathf.Max(5f, len / Mathf.Max(1, Mathf.Round(len / 6f))))
                    slab.Box(KMath.X0Z(c + side * 0.55f + o.u * t, y + 0.9f), new Vector3(1.05f, 1.8f, 0.05f), q, Hex(0xc8c4bc), 1f);
            }
        }

        // 住宅の外壁の室外機
        static void AcUnit(MeshSet ms, Vector2[] p, int sgn, Rng r, float y)
        {
            int i = r.Int(p.Length);
            Vector2 a = p[i], b = p[(i + 1) % p.Length], e = b - a; float L = e.magnitude; if (L < 2.5f) return;
            Vector2 n = Out(e / L, sgn), at = a + e * r.Range(0.25f, 0.75f) + n * 0.2f;
            float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;
            bool ground = r.Next() < 0.5f;
            ms["metal"].Box(KMath.X0Z(at, ground ? 0.3f : y), new Vector3(0.78f, 0.56f, 0.28f), Quaternion.Euler(0, yaw, 0), Hex(0xd7d6d0), 1f);
        }

        // 外向き法線 n の壁に貼る矩形（中心 c、幅 w、高さ h）。u は外から見て右
        static void WallQuad(MeshBuilder b, Vector3 c, Vector3 n, float w, float h, Vector2 uv0, Vector2 uvSize, Color color)
        {
            Vector3 u = Vector3.Cross(n, Vector3.up).normalized;
            b.Face(c, n, u, w, h, color, uv0, uvSize);
        }

        public static void Lantern(MeshSet ms, Vector3 at, float s, bool red)
        {
            ms[red ? "lantern" : "paper"].Ellipsoid(at, new Vector3(0.19f, 0.26f, 0.19f) * s, 12, 8, red ? Hex(0xe0402c) : Hex(0xf2ead8));
            ms["black"].Cylinder(at + Vector3.up * 0.22f * s, at + Vector3.up * 0.3f * s, 0.1f * s, 0.1f * s, 10, W);
            ms["black"].Cylinder(at - Vector3.up * 0.3f * s, at - Vector3.up * 0.22f * s, 0.1f * s, 0.1f * s, 10, W);
        }
        static void LanternPair(MeshSet ms, Vector3 at, Vector3 n)
        {
            Vector3 side = Vector3.Cross(n, Vector3.up).normalized;
            foreach (int k in new[] { -1, 1 }) Lantern(ms, at + side * k * 1.1f, 1f, true);
        }
    }
}
