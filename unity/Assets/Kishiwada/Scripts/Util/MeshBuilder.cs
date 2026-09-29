using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    // 頂点を積んでメッシュを作る。頂点色は rgb＝色味、a＝くぼみの陰（Kishiwada/Lit が読む）
    // 三角形は「表から見て時計回り」が Unity の表。向きを間違えないよう、法線を渡して自動で揃える関数を使う
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> pos = new List<Vector3>(1024);
        public readonly List<Vector3> nor = new List<Vector3>(1024);
        public readonly List<Vector2> uv = new List<Vector2>(1024);
        public readonly List<Color> col = new List<Color>(1024);
        public readonly List<int> idx = new List<int>(3072);
        Matrix4x4 m = Matrix4x4.identity, nm = Matrix4x4.identity; // nm：法線用（逆転置）
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();

        public int Count => pos.Count;
        public Matrix4x4 Matrix => m;
        public void Push(Matrix4x4 local) { stack.Push(m); SetM(m * local); }
        public void Push(Vector3 p, Quaternion r) => Push(Matrix4x4.TRS(p, r, Vector3.one));
        public void Push(Vector3 p, Quaternion r, Vector3 s) => Push(Matrix4x4.TRS(p, r, s));
        public void Pop() => SetM(stack.Pop());
        void SetM(Matrix4x4 x) { m = x; nm = x.inverse.transpose; }
        Vector3 WN(Vector3 n) => nm.MultiplyVector(n);

        public int V(Vector3 p, Vector3 n, Vector2 t, Color c)
        {
            pos.Add(m.MultiplyPoint3x4(p));
            nor.Add(nm.MultiplyVector(n).normalized);
            uv.Add(t); col.Add(c);
            return pos.Count - 1;
        }
        // 変換済み（ワールド）座標で直接積む。大量の建物向けに行列計算を省く
        public int VW(Vector3 p, Vector3 n, Vector2 t, Color c)
        {
            pos.Add(p); nor.Add(n); uv.Add(t); col.Add(c);
            return pos.Count - 1;
        }
        public void Tri(int a, int b, int c) { idx.Add(a); idx.Add(b); idx.Add(c); }
        // a,b,c,d は表から見て時計回り（左下・左上・右上・右下）
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
        // 法線 n の側が表になるように並べる（積んだ後の座標で判定）
        public void TriFacing(int a, int b, int c, Vector3 n)
        {
            Vector3 pa = pos[a], pb = pos[b], pc = pos[c];
            if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), n) >= 0f) Tri(a, b, c); else Tri(a, c, b);
        }

        // 面：中心 c、法線 n、横方向 u（単位）、横幅 w・高さ h。v = cross(u, n)
        public void Face(Vector3 c, Vector3 n, Vector3 u, float w, float h, Color color, Vector2 uv0, Vector2 uvSize)
        {
            Vector3 v = Vector3.Cross(u, n);
            Vector3 bl = c - u * (w * 0.5f) - v * (h * 0.5f);
            int a = V(bl, n, uv0, color);
            int b = V(bl + v * h, n, uv0 + new Vector2(0, uvSize.y), color);
            int cc = V(bl + v * h + u * w, n, uv0 + uvSize, color);
            int d = V(bl + u * w, n, uv0 + new Vector2(uvSize.x, 0), color);
            Quad(a, b, cc, d);
        }

        // 直方体。木目（u）が一番長い辺に沿うように UV を張る。uvScale は 1m あたりのテクスチャ枚数
        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color color, float uvScale = 1f, Vector2 uvOffset = default, bool bottom = true)
        {
            Push(center, rot);
            Vector3 h = size * 0.5f;
            BoxFace(Vector3.right, h.x, Vector3.forward, size.z, Vector3.up, size.y, color, uvScale, uvOffset);
            BoxFace(Vector3.left, h.x, Vector3.back, size.z, Vector3.up, size.y, color, uvScale, uvOffset);
            BoxFace(Vector3.up, h.y, Vector3.right, size.x, Vector3.forward, size.z, color, uvScale, uvOffset);
            if (bottom) BoxFace(Vector3.down, h.y, Vector3.right, size.x, Vector3.back, size.z, color, uvScale, uvOffset);
            BoxFace(Vector3.forward, h.z, Vector3.left, size.x, Vector3.up, size.y, color, uvScale, uvOffset);
            BoxFace(Vector3.back, h.z, Vector3.right, size.x, Vector3.up, size.y, color, uvScale, uvOffset);
            Pop();
        }
        public void Box(Vector3 center, Vector3 size, Color color, float uvScale = 1f) => Box(center, size, Quaternion.identity, color, uvScale);

        void BoxFace(Vector3 n, float dist, Vector3 a1, float l1, Vector3 a2, float l2, Color color, float uvScale, Vector2 off)
        {
            // 長い方を u に
            Vector3 u; float w, hgt;
            if (l1 >= l2) { u = a1; w = l1; hgt = l2; } else { u = a2; w = l2; hgt = l1; }
            // u と法線から v を決める（v = cross(u,n)）。u の向きは表から見て右
            Vector3 v = Vector3.Cross(u, n);
            if (Vector3.Dot(v, a1) < -0.5f || Vector3.Dot(v, a2) < -0.5f) { u = -u; v = Vector3.Cross(u, n); }
            Face(n * dist, n, u, w, hgt, color, off, new Vector2(w, hgt) * uvScale);
        }

        // 円柱・円錐台（a→b、半径 r0→r1）
        public void Cylinder(Vector3 a, Vector3 b, float r0, float r1, int seg, Color color, bool capA = true, bool capB = true, float uvScale = 1f)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 1e-6f) return; d /= len;
            Vector3 t1 = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(t1, d);
            float slope = (r0 - r1) / len;
            int start = Count;
            for (int i = 0; i <= seg; i++)
            {
                float th = i / (float)seg * Mathf.PI * 2f;
                Vector3 dir = Mathf.Cos(th) * t1 + Mathf.Sin(th) * t2;
                Vector3 n = (dir + d * slope).normalized;
                float u = th * (r0 + r1) * 0.5f * uvScale;
                V(a + dir * r0, n, new Vector2(u, 0), color);
                V(b + dir * r1, n, new Vector2(u, len * uvScale), color);
            }
            for (int i = 0; i < seg; i++)
            {
                int p0 = start + i * 2, p1 = p0 + 2;
                // p0:a側 i, p0+1:b側 i, p1:a側 i+1, p1+1:b側 i+1
                TriFacing(p0, p0 + 1, p1 + 1, nor[p0] + nor[p1]);
                TriFacing(p0, p1 + 1, p1, nor[p0] + nor[p1]);
            }
            if (capA && r0 > 1e-4f) Disc(a, -d, t1, r0, seg, color, uvScale);
            if (capB && r1 > 1e-4f) Disc(b, d, t1, r1, seg, color, uvScale);
        }
        public void Disc(Vector3 c, Vector3 n, Vector3 t1, float r, int seg, Color color, float uvScale = 1f)
        {
            Vector3 t2 = Vector3.Cross(t1, n);
            int ci = V(c, n, new Vector2(0.5f, 0.5f), color), s = Count;
            for (int i = 0; i <= seg; i++)
            {
                float th = i / (float)seg * Mathf.PI * 2f, cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                V(c + (cs * t1 + sn * t2) * r, n, new Vector2(0.5f + cs * r * uvScale, 0.5f + sn * r * uvScale), color);
            }
            Vector3 nw = WN(n);
            for (int i = 0; i < seg; i++) TriFacing(ci, s + i, s + i + 1, nw);
        }

        // 楕円体
        public void Ellipsoid(Vector3 c, Vector3 r, int seg, int rings, Color color, float vFrom = 0f, float vTo = 1f)
        {
            int s = Count;
            for (int j = 0; j <= rings; j++)
            {
                float v = Mathf.Lerp(vFrom, vTo, j / (float)rings), ph = v * Mathf.PI; // 0=上 1=下
                for (int i = 0; i <= seg; i++)
                {
                    float th = i / (float)seg * Mathf.PI * 2f;
                    Vector3 unit = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
                    Vector3 p = Vector3.Scale(unit, r);
                    Vector3 n = new Vector3(unit.x / r.x, unit.y / r.y, unit.z / r.z).normalized;
                    V(c + p, n, new Vector2(i / (float)seg, v), color);
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = s + j * (seg + 1) + i, b = a + 1, cc = a + seg + 1, d = cc + 1;
                    Vector3 n = nor[a] + nor[d];
                    TriFacing(a, b, d, n); TriFacing(a, d, cc, n);
                }
        }

        // 回転体。profile は (半径, 高さ) の並び（下から上）
        public void Lathe(Vector3 c, Vector2[] profile, int seg, Color color, float uvScale = 1f)
        {
            int s = Count, n = profile.Length;
            for (int j = 0; j < n; j++)
            {
                Vector2 p = profile[j];
                Vector2 dp = profile[Mathf.Min(n - 1, j + 1)] - profile[Mathf.Max(0, j - 1)];
                Vector2 pn = new Vector2(dp.y, -dp.x).normalized; // (radial, up)
                for (int i = 0; i <= seg; i++)
                {
                    float th = i / (float)seg * Mathf.PI * 2f, cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                    V(c + new Vector3(cs * p.x, p.y, sn * p.x), new Vector3(cs * pn.x, pn.y, sn * pn.x), new Vector2(th * uvScale, p.y * uvScale), color);
                }
            }
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = s + j * (seg + 1) + i, b = a + 1, cc = a + seg + 1, d = cc + 1;
                    Vector3 nn = nor[a] + nor[d];
                    if (nn.sqrMagnitude < 1e-8f) nn = pos[a] - m.MultiplyPoint3x4(c);
                    TriFacing(a, b, d, nn); TriFacing(a, d, cc, nn);
                }
        }

        public void Torus(Vector3 c, Vector3 axis, float R, float r, int seg, int tube, Color color)
        {
            axis.Normalize();
            Vector3 t1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, t2 = Vector3.Cross(t1, axis);
            int s = Count;
            for (int i = 0; i <= seg; i++)
            {
                float th = i / (float)seg * Mathf.PI * 2f;
                Vector3 dir = Mathf.Cos(th) * t1 + Mathf.Sin(th) * t2;
                for (int j = 0; j <= tube; j++)
                {
                    float ph = j / (float)tube * Mathf.PI * 2f;
                    Vector3 n = Mathf.Cos(ph) * dir + Mathf.Sin(ph) * axis;
                    V(c + dir * R + n * r, n, new Vector2(i / (float)seg, j / (float)tube), color);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < tube; j++)
                {
                    int a = s + i * (tube + 1) + j, b = a + 1, cc = a + tube + 1, d = cc + 1;
                    Vector3 n = nor[a] + nor[d];
                    TriFacing(a, b, d, n); TriFacing(a, d, cc, n);
                }
        }

        // 平面の多角形（xy）を z 方向に押し出す。表裏の蓋つき
        public void Extrude(IList<Vector2> shape, float z0, float z1, Color color, float uvScale = 1f)
        {
            int n = shape.Count;
            var tris = Triangulator.Triangulate(shape);
            foreach (float z in new[] { z0, z1 })
            {
                Vector3 nn = z == z1 ? Vector3.forward : Vector3.back;
                int s = Count;
                for (int i = 0; i < n; i++) V(new Vector3(shape[i].x, shape[i].y, z), nn, shape[i] * uvScale, color);
                Vector3 nw = WN(nn);
                for (int t = 0; t < tris.Count; t += 3) TriFacing(s + tris[t], s + tris[t + 1], s + tris[t + 2], nw);
            }
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = shape[i], b = shape[(i + 1) % n], e = b - a;
                float l = e.magnitude; if (l < 1e-6f) continue;
                Vector2 on = new Vector2(e.y, -e.x) / l;
                if (Triangulator.Area(shape) < 0) on = -on;
                Vector3 nn = new Vector3(on.x, on.y, 0);
                int i0 = V(new Vector3(a.x, a.y, z0), nn, new Vector2(acc, 0) * uvScale, color);
                int i1 = V(new Vector3(a.x, a.y, z1), nn, new Vector2(acc, z1 - z0) * uvScale, color);
                int i2 = V(new Vector3(b.x, b.y, z1), nn, new Vector2(acc + l, z1 - z0) * uvScale, color);
                int i3 = V(new Vector3(b.x, b.y, z0), nn, new Vector2(acc + l, 0) * uvScale, color);
                Vector3 nw = WN(nn);
                TriFacing(i0, i1, i2, nw); TriFacing(i0, i2, i3, nw);
                acc += l;
            }
        }

        // 別の MeshBuilder の中身を今の行列で取り込む
        public void Append(MeshBuilder o, Color tint)
        {
            int s = Count;
            for (int i = 0; i < o.Count; i++) V(o.pos[i], o.nor[i], o.uv[i], o.col[i] * tint);
            for (int i = 0; i < o.idx.Count; i++) idx.Add(o.idx[i] + s);
        }

        public Mesh ToMesh(string name = "mesh", bool tangents = true)
        {
            var mesh = new Mesh { name = name };
            if (Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos); mesh.SetNormals(nor); mesh.SetUVs(0, uv); mesh.SetColors(col);
            mesh.SetTriangles(idx, 0, true);
            if (tangents && idx.Count > 0) mesh.RecalculateTangents();
            return mesh;
        }
        public void Clear() { pos.Clear(); nor.Clear(); uv.Clear(); col.Clear(); idx.Clear(); SetM(Matrix4x4.identity); stack.Clear(); }
    }

    // 材質ごと・区画ごとに MeshBuilder を分けて持ち、まとめて GameObject にする
    public sealed class MeshSet
    {
        readonly Dictionary<string, MeshBuilder> parts = new Dictionary<string, MeshBuilder>();
        public string chunk = "";
        public MeshBuilder this[string mat] => Get(mat + "|" + chunk);
        public MeshBuilder Get(string key)
        {
            if (!parts.TryGetValue(key, out var b)) parts[key] = b = new MeshBuilder();
            return b;
        }
        public IEnumerable<KeyValuePair<string, MeshBuilder>> Parts => parts;
        public GameObject Build(string name, Transform parent, Func<string, Material> mat, ShadowCastingMode cast = ShadowCastingMode.On, bool receive = true, int layer = 0)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.layer = layer;
            foreach (var kv in parts)
            {
                if (kv.Value.Count == 0) continue;
                string mk = kv.Key.Split('|')[0];
                var go = new GameObject(kv.Key);
                go.layer = layer;
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh(kv.Key);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat(mk);
                mr.shadowCastingMode = cast; mr.receiveShadows = receive;
            }
            parts.Clear();
            return root;
        }
    }
}
