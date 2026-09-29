using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    // 綱と曳き手の見た目。RopeSim の点に人を並べ、手を綱に合わせる
    public sealed class CrowdView
    {
        sealed class Puller { public HumanRig rig; public int rope; public float f; public int side; public float phase, speed, scale; public Vector3 lastPos; }
        readonly List<Puller> pullers = new List<Puller>();
        readonly RopeSim sim;
        readonly Transform root;
        readonly Mesh[] ropeMesh = new Mesh[2];
        readonly List<Vector3> rv = new List<Vector3>(); readonly List<Vector3> rn = new List<Vector3>(); readonly List<Vector2> ruv = new List<Vector2>(); readonly List<int> ri = new List<int>(); readonly List<Color> rc = new List<Color>();
        readonly List<Vector4> rt = new List<Vector4>();
        readonly Vector3[] line;
        const float RopeR = 0.032f;

        public CrowdView(RopeSim sim, Transform parent, Mesh happiMesh, Mesh happiMesh2, Material peopleMat, MaterialLibrary mats, bool mobile)
        {
            this.sim = sim;
            root = new GameObject("Pullers").transform; root.SetParent(parent, false);
            line = new Vector3[sim.N + 1];
            int per = mobile ? 1 : 3;
            var r = new Rng(3);
            for (int j = 0; j < 2; j++)
            {
                for (int i = sim.spec.firstPuller; i < sim.N; i++)
                    for (int k = 0; k < per; k++)
                    {
                        var p = new Puller { rope = j, f = i - k / (float)per, side = ((i * per + k) % 2 == 0) ? 1 : -1, phase = r.Range(0, 6.28f), scale = r.Range(0.93f, 1.06f) };
                        p.rig = HumanBuilder.Spawn(r.Chance(0.5f) ? happiMesh : happiMesh2, peopleMat, root, "puller", 10);
                        p.rig.root.localScale = Vector3.one * p.scale;
                        pullers.Add(p);
                    }
                // 綱先（先頭の二人は綱の端を持って走る）
                for (int k = 0; k < 2; k++)
                {
                    var p = new Puller { rope = j, f = sim.N - 1 + 0.6f + k * 0.7f, side = k == 0 ? 1 : -1, phase = r.Range(0, 6.28f), scale = r.Range(0.95f, 1.05f) };
                    p.rig = HumanBuilder.Spawn(happiMesh, peopleMat, root, "tsunasaki", 10);
                    p.rig.root.localScale = Vector3.one * p.scale;
                    pullers.Add(p);
                }
                var go = new GameObject("Rope" + j) { layer = 10 };
                go.transform.SetParent(root, false);
                ropeMesh[j] = new Mesh { name = "rope" }; ropeMesh[j].MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = ropeMesh[j];
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats["rope"]; mr.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        // 綱の中心線（結び目＋点）上の、点番号 f の位置と向き（先端向き）
        Vector3 RopeAt(int rope, float f, out Vector3 tangent)
        {
            int n = sim.N;
            // f=-1 が結び目
            float ff = Mathf.Clamp(f, -1f, n - 1);
            int i0 = Mathf.FloorToInt(ff), i1 = Mathf.Min(n - 1, i0 + 1);
            Vector3 a = i0 < 0 ? sim.anchorWorld[rope] : sim.Node(rope, i0), b = sim.Node(rope, Mathf.Max(0, i1));
            tangent = (b - a); if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.forward; tangent.Normalize();
            Vector3 pnt = Vector3.Lerp(a, b, ff - i0);
            if (f > n - 1) pnt += tangent * (f - (n - 1)); // 端より先
            return pnt;
        }

        public void Update(float dt, float t)
        {
            // 綱
            for (int j = 0; j < 2; j++) BuildRope(j);
            // 曳き手
            foreach (var p in pullers)
            {
                Vector3 c = RopeAt(p.rope, p.f, out Vector3 tg);
                Vector3 flat = new Vector3(tg.x, 0, tg.z); if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward; flat.Normalize();
                Vector3 side = new Vector3(flat.z, 0, -flat.x) * p.side; // 綱のどちら側に立つか
                Vector3 pos = new Vector3(c.x, 0, c.z) + side * 0.4f;
                float spd = dt > 0 ? (pos - p.lastPos).magnitude / dt : 0f; if (spd > 15f) spd = 0f;
                p.speed = Mathf.Lerp(p.speed, spd, KMath.Damp(6f, dt));
                p.lastPos = pos;
                p.rig.root.SetPositionAndRotation(pos, Quaternion.LookRotation(flat));
                float stride = Mathf.Clamp01(p.speed / 6.5f);
                float strideLen = 1.1f + 0.3f * p.speed;
                p.phase += dt * p.speed / Mathf.Max(0.5f, strideLen) * Mathf.PI * 2f;
                int node = Mathf.Clamp(Mathf.RoundToInt(p.f), 0, sim.N - 1);
                float T = sim.NodeTension(p.rope, node);
                float lean = 6f + Mathf.Clamp(T / sim.spec.pullerForce, 0f, 1.5f) * 18f + stride * 10f;
                bool tip = p.f > sim.N - 1;
                HumanPose.Run(p.rig, p.phase, stride, lean, tip);
                if (!tip)
                {
                    // 綱側の手を前、反対の手を後ろで握る
                    Vector3 hFront = RopeAt(p.rope, p.f + 0.05f, out _), hBack = RopeAt(p.rope, p.f - 0.3f, out _);
                    bool nearLeft = p.side > 0; // 綱の右に立つ＝綱が体の左
                    HumanPose.ArmIK(p.rig, nearLeft, hFront, -flat * 0.6f - side * 0.3f + Vector3.down * 0.4f);
                    HumanPose.ArmIK(p.rig, !nearLeft, hBack, -flat * 0.7f + side * 0.3f + Vector3.down * 0.4f);
                }
                else
                {
                    Vector3 hEnd = RopeAt(p.rope, sim.N - 1 + 0.3f, out _);
                    HumanPose.ArmIK(p.rig, p.side > 0, hEnd, -flat + Vector3.down * 0.3f);
                }
            }
        }

        void BuildRope(int j)
        {
            int n = sim.N;
            line[0] = sim.anchorWorld[j];
            for (int i = 0; i < n; i++) line[i + 1] = sim.Node(j, i);
            rv.Clear(); rn.Clear(); ruv.Clear(); ri.Clear(); rc.Clear(); rt.Clear();
            const int sides = 6;
            float along = 0f;
            for (int i = 0; i < line.Length; i++)
            {
                Vector3 a = line[Mathf.Max(0, i - 1)], b = line[Mathf.Min(line.Length - 1, i + 1)];
                Vector3 tg = (b - a); if (tg.sqrMagnitude < 1e-8f) tg = Vector3.forward; tg.Normalize();
                Vector3 n1 = Vector3.Cross(tg, Vector3.up); if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.right; n1.Normalize();
                Vector3 n2 = Vector3.Cross(n1, tg);
                if (i > 0) along += (line[i] - line[i - 1]).magnitude;
                for (int k = 0; k <= sides; k++)
                {
                    float th = k / (float)sides * Mathf.PI * 2f;
                    Vector3 d = Mathf.Cos(th) * n1 + Mathf.Sin(th) * n2;
                    rv.Add(line[i] + d * RopeR); rn.Add(d); ruv.Add(new Vector2(along, k / (float)sides)); rc.Add(Color.white);
                    rt.Add(new Vector4(tg.x, tg.y, tg.z, 1f));
                }
            }
            for (int i = 0; i < line.Length - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = i * (sides + 1) + k, b = a + 1, c = a + sides + 1, d = c + 1;
                    Vector3 nn = rn[a] + rn[d];
                    if (Vector3.Dot(Vector3.Cross(rv[b] - rv[a], rv[d] - rv[a]), nn) >= 0) { ri.Add(a); ri.Add(b); ri.Add(d); ri.Add(a); ri.Add(d); ri.Add(c); }
                    else { ri.Add(a); ri.Add(d); ri.Add(b); ri.Add(a); ri.Add(c); ri.Add(d); }
                }
            var m = ropeMesh[j];
            m.Clear();
            m.SetVertices(rv); m.SetNormals(rn); m.SetTangents(rt); m.SetUVs(0, ruv); m.SetColors(rc); m.SetTriangles(ri, 0, true);
        }
    }
}
