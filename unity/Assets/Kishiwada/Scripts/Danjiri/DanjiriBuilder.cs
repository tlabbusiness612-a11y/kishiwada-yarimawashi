using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    // 組み上がった地車の見た目と、物理・人物が使う取り付け位置（root 基準、メートル）
    public sealed class DanjiriModel
    {
        public GameObject root;
        public Transform body;                 // 揺れ（ロール・ピッチ）を見た目だけ足す枠
        public Transform[] wheels = new Transform[4]; // 前左・前右・後左・後右
        public Transform[] maePivot = new Transform[2]; // 前梃子（左・右）
        public Transform rearPole;             // 後梃子
        public Transform flagPivot; public Mesh flagMesh; public Vector3[] flagBase;
        public Material plateMat, flagMat;
        public Vector3[] ropeAnchors = new Vector3[2];
        public Vector3[] wheelLocal = new Vector3[4];
        public float wheelRadius;
        public Vector3 daikuFront, daikuRear;
        public List<(Vector3 pos, float yaw, int role)> crewSlots = new List<(Vector3, float, int)>();
        public Vector3[] maeGrip = new Vector3[2];   // 前梃子の握り（pivot 基準）
    }

    public static class DanjiriBuilder
    {
        public const float Scale = 0.92f;
        static readonly Color W = Color.white, DARK = KMath.Hex(0x9a8878), AGED = KMath.Hex(0xd2c0ac);

        public enum Role { Drum = 0, Kane = 1, Front = 2, Rear = 3, Mae = 4 }

        public static DanjiriModel Build(MaterialLibrary mats, Transform parent, bool mobile)
        {
            var dm = new DanjiriModel();
            var root = new GameObject("Danjiri"); root.transform.SetParent(parent, false);
            dm.root = root;
            var bodyT = new GameObject("Body").transform; bodyT.SetParent(root.transform, false);
            dm.body = bodyT;
            float density = mobile ? 34f : 70f;

            var ms = new MeshSet();
            var b = ms["wood"]; var carve = ms["wood"]; var gold = ms["gold"]; var plain = ms["plain"]; var roof = ms["roofWood"];
            var sc = Matrix4x4.Scale(Vector3.one * Scale);
            foreach (var mb in new[] { b, gold, plain, roof }) mb.Push(sc);
            int seed = 1;

            // ---------- 台・地覆・土呂幕 ----------
            b.Box(new Vector3(0, 0.54f, 0), new Vector3(1.95f, 0.36f, 3.9f), Quaternion.identity, DARK, 1f);
            b.Box(new Vector3(0, 0.56f, 1.99f), new Vector3(2.14f, 0.26f, 0.34f), Quaternion.identity, DARK, 1f);
            b.Box(new Vector3(0, 0.56f, -1.99f), new Vector3(2.14f, 0.26f, 0.34f), Quaternion.identity, DARK, 1f);
            foreach (int sx in new[] { -1, 1 }) b.Box(new Vector3(sx * 1.02f, 0.7f, 0), new Vector3(0.14f, 0.12f, 3.5f), Quaternion.identity, DARK, 1f);
            // 土呂幕：左右・前後の彫り物（本当の凹凸）
            Relief(b, new Vector3(-1.03f, 1.02f, 0), Vector3.left, Vector3.back, 3.4f, 0.58f, 0.07f, seed++, 0, density);
            Relief(b, new Vector3(1.03f, 1.02f, 0), Vector3.right, Vector3.forward, 3.4f, 0.58f, 0.07f, seed++, 0, density);
            Relief(b, new Vector3(0, 1.02f, 1.91f), Vector3.forward, Vector3.left, 1.95f, 0.58f, 0.07f, seed++, 0, density);
            Relief(b, new Vector3(0, 1.02f, -1.91f), Vector3.back, Vector3.right, 1.95f, 0.58f, 0.07f, seed++, 0, density);
            b.Box(new Vector3(0, 1.02f, 0), new Vector3(1.96f, 0.6f, 3.72f), Quaternion.identity, DARK * 0.7f, 1f); // 彫り物の裏板
            // 縁と高欄（擬宝珠つき）
            b.Box(new Vector3(0, 1.35f, 0), new Vector3(2.46f, 0.1f, 4.12f), Quaternion.identity, AGED, 1f);
            foreach (int sx in new[] { -1, 1 })
            {
                b.Box(new Vector3(sx * 1.17f, 1.71f, 0), new Vector3(0.08f, 0.07f, 3.98f), Quaternion.identity, AGED, 1f);
                b.Box(new Vector3(sx * 1.17f, 1.52f, 0), new Vector3(0.05f, 0.04f, 3.98f), Quaternion.identity, AGED, 1f);
                for (float z = -1.95f; z <= 1.96f; z += 0.39f) b.Box(new Vector3(sx * 1.17f, 1.54f, z), new Vector3(0.07f, 0.36f, 0.07f), Quaternion.identity, AGED, 1f);
                foreach (int zs in new[] { -1, 1 })
                {
                    gold.Lathe(new Vector3(sx * 1.17f, 1.74f, zs * 1.98f), new[] { new Vector2(0.045f, 0f), new Vector2(0.06f, 0.03f), new Vector2(0.068f, 0.07f), new Vector2(0.055f, 0.11f), new Vector2(0.02f, 0.15f), new Vector2(0.004f, 0.19f) }, 12, W, 4f);
                }
            }
            b.Box(new Vector3(0, 1.71f, 2.03f), new Vector3(2.36f, 0.07f, 0.08f), Quaternion.identity, AGED, 1f);
            b.Box(new Vector3(0, 1.71f, -2.03f), new Vector3(2.36f, 0.07f, 0.08f), Quaternion.identity, AGED, 1f);

            // ---------- 上屋：180°回して「大屋根が前・小屋根が後ろ」にする ----------
            foreach (var mb in new[] { b, gold, roof }) mb.Push(Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)));
            // 小屋根の下の間（回したあと後ろ）：柱・虹梁・枡合
            foreach (int sx in new[] { -1, 1 }) foreach (float z in new[] { 0.62f, 1.82f }) b.Box(new Vector3(sx * 0.82f, 2.0f, z), new Vector3(0.15f, 1.3f, 0.15f), Quaternion.identity, W, 1f);
            b.Box(new Vector3(0, 2.3f, 1.86f), new Vector3(1.8f, 0.14f, 0.16f), Quaternion.identity, AGED, 1f);
            Relief(b, new Vector3(0, 2.52f, 1.91f), Vector3.forward, Vector3.left, 1.78f, 0.3f, 0.045f, seed++, 1, density);
            Relief(b, new Vector3(-0.91f, 2.52f, 1.22f), Vector3.left, Vector3.back, 1.3f, 0.3f, 0.045f, seed++, 1, density);
            Relief(b, new Vector3(0.91f, 2.52f, 1.22f), Vector3.right, Vector3.forward, 1.3f, 0.3f, 0.045f, seed++, 1, density);
            b.Box(new Vector3(0, 2.52f, 1.22f), new Vector3(1.78f, 0.32f, 1.28f), Quaternion.identity, DARK * 0.6f, 1f);
            // 大屋根の下の間（回したあと前）：柱・枡合・組物
            foreach (int sx in new[] { -1, 1 }) foreach (float z in new[] { -1.86f, 0.56f }) b.Box(new Vector3(sx * 0.9f, 2.24f, z), new Vector3(0.17f, 1.78f, 0.17f), Quaternion.identity, W, 1f);
            Relief(b, new Vector3(-1.0f, 2.95f, -0.65f), Vector3.left, Vector3.back, 2.5f, 0.34f, 0.05f, seed++, 1, density);
            Relief(b, new Vector3(1.0f, 2.95f, -0.65f), Vector3.right, Vector3.forward, 2.5f, 0.34f, 0.05f, seed++, 1, density);
            Relief(b, new Vector3(0, 2.95f, 0.65f), Vector3.forward, Vector3.left, 1.9f, 0.34f, 0.05f, seed++, 1, density);
            Relief(b, new Vector3(0, 2.95f, -1.95f), Vector3.back, Vector3.right, 1.9f, 0.34f, 0.05f, seed++, 1, density);
            b.Box(new Vector3(0, 2.95f, -0.65f), new Vector3(1.9f, 0.36f, 2.5f), Quaternion.identity, DARK * 0.6f, 1f);
            b.Box(new Vector3(0, 2.72f, 0.6f), new Vector3(1.9f, 0.12f, 0.14f), Quaternion.identity, AGED, 1f);
            // 組物（斗と肘木を二手先に重ねる）
            Brackets(b, new Vector3(-1.02f, 3.12f, -0.5f), Vector3.left, Vector3.forward, 2.6f, 2);
            Brackets(b, new Vector3(1.02f, 3.12f, -0.5f), Vector3.right, Vector3.forward, 2.6f, 2);
            Brackets(b, new Vector3(0, 3.12f, 0.72f), Vector3.forward, Vector3.right, 1.9f, 2);
            Brackets(b, new Vector3(0, 3.12f, -1.98f), Vector3.back, Vector3.right, 1.9f, 2);
            Brackets(b, new Vector3(-0.93f, 2.67f, 1.32f), Vector3.left, Vector3.forward, 1.3f, 1);
            Brackets(b, new Vector3(0.93f, 2.67f, 1.32f), Vector3.right, Vector3.forward, 1.3f, 1);
            Brackets(b, new Vector3(0, 2.67f, 1.95f), Vector3.forward, Vector3.right, 1.7f, 1);

            // 屋根（上面・裏面・破風・軒先・垂木）
            var big = new RoofShape(2.95f, 3.15f, 3.98f, 3.1f, 0.36f); float bz = -0.52f;
            var small = new RoofShape(2.35f, 1.75f, 3.1f, 2.62f, 0.24f); float sz = 1.36f;
            foreach (var (R, zc, drop) in new[] { (big, bz, 0.26f), (small, sz, 0.2f) })
            {
                R.Surface(roof, zc, 0f, AGED, true);
                R.Surface(roof, zc, -0.11f, KMath.Hex(0xa89684), false);
                R.Ribbon(roof, zc, true, 0f, drop, DARK); R.Ribbon(roof, zc, true, 1f, drop, DARK);
                R.Ribbon(roof, zc, false, -1f, 0.14f, DARK); R.Ribbon(roof, zc, false, 1f, 0.14f, DARK);
                foreach (int uu in new[] { -1, 1 })
                    for (float z = -R.l / 2 + 0.18f; z < R.l / 2 - 0.1f; z += 0.17f)
                    {
                        float t = (z + R.l / 2) / R.l, y = R.H(uu * 0.86f, t) - 0.13f;
                        b.Box(new Vector3(uu * (R.w / 2 - 0.3f), y, zc + z), new Vector3(0.42f, 0.05f, 0.05f), Quaternion.Euler(0, 0, uu * 20f), AGED, 1f);
                    }
            }
            // 破風の懸魚（彫り物）
            Gable(b, big, bz + 3.15f / 2 - 0.34f, Vector3.forward, seed++, density);
            Gable(b, big, bz - 3.15f / 2 + 0.34f, Vector3.back, seed++, density);
            Gable(b, small, sz + 1.75f / 2 - 0.26f, Vector3.forward, seed++, density);
            b.Box(new Vector3(0, 4.09f, bz), new Vector3(0.22f, 0.26f, 3.1f), Quaternion.identity, DARK, 1f);
            b.Box(new Vector3(0, 4.23f, bz), new Vector3(0.3f, 0.06f, 3.1f), Quaternion.identity, DARK, 1f);
            b.Box(new Vector3(0, 3.19f, sz), new Vector3(0.17f, 0.2f, 1.7f), Quaternion.identity, DARK, 1f);
            // 鬼板・懸魚・飾り金具
            var oni = OniShape();
            foreach (int s in new[] { -1, 1 })
            {
                b.Push(new Vector3(0, 4.0f, bz + s * 1.52f - 0.06f), Quaternion.identity);
                b.Extrude(oni, 0f, 0.12f, W, 1f);
                b.Pop();
                Relief(b, new Vector3(0, 3.55f, bz + s * 1.6f), new Vector3(0, 0, s), new Vector3(-s, 0, 0), 0.3f, 0.38f, 0.04f, seed++, 2, density);
                gold.Box(new Vector3(0, 4.74f, bz + s * 1.52f), new Vector3(0.22f, 0.12f, 0.22f), Quaternion.identity, W, 4f);
                foreach (int sx in new[] { -1, 1 }) gold.Box(new Vector3(sx * 1.46f, 3.5f, bz + s * 1.56f), new Vector3(0.14f, 0.1f, 0.2f), Quaternion.identity, W, 4f);
            }
            b.Push(Matrix4x4.TRS(new Vector3(0, 3.12f, sz + 0.8f), Quaternion.identity, Vector3.one * 0.7f));
            b.Extrude(oni, 0f, 0.12f, W, 1f);
            b.Pop();
            gold.Box(new Vector3(0, 3.62f, sz + 0.86f), new Vector3(0.14f, 0.1f, 0.14f), Quaternion.identity, W, 4f);
            foreach (var mb in new[] { b, gold, roof }) mb.Pop();

            // ---------- 見送り（後ろの彫り物）と内部 ----------
            Relief(b, new Vector3(0, 1.88f, -1.98f), Vector3.back, Vector3.right, 1.6f, 0.9f, 0.08f, seed++, 0, density);
            b.Box(new Vector3(0, 1.88f, -1.93f), new Vector3(1.62f, 0.92f, 0.1f), Quaternion.identity, DARK * 0.7f, 1f);
            plain.Box(new Vector3(0, 1.92f, -0.1f), new Vector3(1.3f, 1.0f, 1.4f), Quaternion.identity, KMath.Hex(0x2b1d13), 1f);
            // 太鼓（前寄り）
            plain.Cylinder(new Vector3(0, 1.78f, 0.98f), new Vector3(0, 1.78f, 1.42f), 0.3f, 0.3f, 28, KMath.Hex(0x7d2a17));
            plain.Cylinder(new Vector3(0, 1.78f, 1.415f), new Vector3(0, 1.78f, 1.43f), 0.29f, 0.29f, 28, KMath.Hex(0xe8dcc0), false, true);
            plain.Cylinder(new Vector3(0, 1.78f, 0.97f), new Vector3(0, 1.78f, 0.985f), 0.29f, 0.29f, 28, KMath.Hex(0xe8dcc0), true, false);
            for (int k = 0; k < 14; k++) { float a = k / 14f * Mathf.PI * 2f; gold.Ellipsoid(new Vector3(Mathf.Cos(a) * 0.3f, 1.78f + Mathf.Sin(a) * 0.3f, 1.43f), Vector3.one * 0.018f, 6, 4, W); }
            // 大屋根の前の両角に房
            foreach (int sx in new[] { -1, 1 })
            {
                plain.Cylinder(new Vector3(sx * 1.4f, 3.05f, 2.0f), new Vector3(sx * 1.4f, 3.42f, 2.0f), 0.012f, 0.012f, 4, KMath.Hex(0xd8ac4c));
                gold.Ellipsoid(new Vector3(sx * 1.4f, 3.05f, 2.0f), Vector3.one * 0.06f, 10, 8, W);
                plain.Cylinder(new Vector3(sx * 1.4f, 2.58f, 2.0f), new Vector3(sx * 1.4f, 3.02f, 2.0f), 0.12f, 0.05f, 14, KMath.Hex(0x7a1f3d));
            }
            // 後ろ旗の竿と横木
            b.Box(new Vector3(0, 0.75f, -2.25f), new Vector3(0.12f, 0.12f, 0.22f), Quaternion.identity, DARK, 1f);
            b.Cylinder(new Vector3(0, 0.7f, -2.34f), new Vector3(0, 5.35f, -2.34f), 0.045f, 0.04f, 10, DARK);
            gold.Cylinder(new Vector3(-0.64f, 5.22f, -2.34f), new Vector3(0.64f, 5.22f, -2.34f), 0.025f, 0.025f, 8, W);
            gold.Ellipsoid(new Vector3(0, 5.42f, -2.34f), Vector3.one * 0.08f, 10, 8, W);
            // 綱の結び目
            foreach (int sx in new[] { -1, 1 }) ms["rope"].Torus(new Vector3(sx * 0.52f, 0.56f, 2.18f) * Scale, Vector3.forward, 0.1f * Scale, 0.035f * Scale, 14, 8, W);

            ms.Build("Mesh", bodyT, k => mats[k], ShadowCastingMode.On, true, 9);

            // ---------- 車輪（鉄の輪つき） ----------
            dm.wheelRadius = 0.36f * Scale;
            int wi = 0;
            foreach (float z in new[] { 1.3f, -1.3f })
                foreach (int sx in new[] { -1, 1 })
                {
                    var wset = new MeshSet();
                    var wb = wset["wood"];
                    wb.Cylinder(new Vector3(-0.13f, 0, 0) * Scale, new Vector3(0.13f, 0, 0) * Scale, 0.33f * Scale, 0.33f * Scale, 28, DARK);
                    wset["metal"].Cylinder(new Vector3(-0.12f, 0, 0) * Scale, new Vector3(0.12f, 0, 0) * Scale, 0.36f * Scale, 0.36f * Scale, 28, KMath.Hex(0x3a3634), false, false);
                    wset["gold"].Cylinder(new Vector3(-0.15f, 0, 0) * Scale, new Vector3(0.15f, 0, 0) * Scale, 0.1f * Scale, 0.1f * Scale, 12, W);
                    for (int k = 0; k < 8; k++) { float a = k / 8f * Mathf.PI * 2f; wset["gold"].Ellipsoid(new Vector3(0.132f * sx, Mathf.Cos(a) * 0.2f, Mathf.Sin(a) * 0.2f) * Scale, Vector3.one * 0.018f, 6, 4, W); }
                    var wgo = wset.Build("Wheel", bodyT, k => mats[k], ShadowCastingMode.On, true, 9);
                    Vector3 lp = new Vector3(sx * 0.86f, 0.36f, z) * Scale;
                    // 前輪は z>0。並びは 前左, 前右, 後左, 後右
                    int idx = (z > 0 ? 0 : 2) + (sx < 0 ? 0 : 1);
                    wgo.transform.localPosition = lp;
                    dm.wheels[idx] = wgo.transform; dm.wheelLocal[idx] = lp;
                    wi++;
                }

            // ---------- 前梃子（左右）と後梃子 ----------
            for (int k = 0; k < 2; k++)
            {
                int sx = k == 0 ? -1 : 1;
                var pivot = new GameObject(k == 0 ? "MaeTekoL" : "MaeTekoR").transform;
                pivot.SetParent(bodyT, false);
                pivot.localPosition = new Vector3(sx * 1.55f, 1.05f, 2.95f) * Scale;
                pivot.localRotation = Quaternion.Euler(6f, sx * 23f, 0);
                var pset = new MeshSet();
                pset["wood"].Cylinder(new Vector3(0, 0, 0.1f), new Vector3(0, 0, -2.2f) * Scale, 0.065f, 0.07f, 10, KMath.Hex(0xb8a08a));
                pset.Build("Pole", pivot, k2 => mats[k2], ShadowCastingMode.On, true, 9);
                dm.maePivot[k] = pivot;
                dm.maeGrip[k] = new Vector3(0, 0, 0.0f);
            }
            {
                var pole = new GameObject("UshiroTeko").transform; pole.SetParent(bodyT, false);
                pole.localPosition = new Vector3(0, 0.85f, -1.95f) * Scale;
                var pset = new MeshSet();
                pset["wood"].Cylinder(Vector3.zero, new Vector3(0, 0.25f, -3.4f), 0.075f, 0.065f, 10, DARK);
                foreach (int sx in new[] { -1, 1 }) pset["wood"].Cylinder(new Vector3(sx * 0.5f, -0.05f, 0), new Vector3(sx * 0.12f, 0.2f, -2.4f), 0.05f, 0.05f, 8, DARK);
                pset.Build("Pole", pole, k2 => mats[k2], ShadowCastingMode.On, true, 9);
                dm.rearPole = pole;
            }

            // ---------- 町名の札と後ろ旗 ----------
            {
                var plate = new GameObject("Plate") { layer = 9 };
                plate.transform.SetParent(bodyT, false);
                var pb = new MeshBuilder();
                pb.Face(new Vector3(0, 0.58f, 2.185f) * Scale, Vector3.forward, Vector3.left, 0.7f * Scale, 0.24f * Scale, W, Vector2.zero, Vector2.one);
                plate.AddComponent<MeshFilter>().sharedMesh = pb.ToMesh("plate");
                dm.plateMat = plate.AddComponent<MeshRenderer>().sharedMaterial = new Material(mats["plate"]);
            }
            {
                var fp = new GameObject("FlagPivot").transform; fp.SetParent(bodyT, false);
                fp.localPosition = new Vector3(0, 5.2f, -2.36f) * Scale;
                var fgo = new GameObject("Flag") { layer = 9 }; fgo.transform.SetParent(fp, false);
                var fb = new MeshBuilder();
                int nx = 8, ny = 12; float fw = 1.16f * Scale, fh = 1.7f * Scale;
                for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                        fb.V(new Vector3((i / (float)nx - 0.5f) * fw, -j / (float)ny * fh, 0), Vector3.back, new Vector2(i / (float)nx, 1f - j / (float)ny), W);
                for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) { int a = j * (nx + 1) + i; fb.Quad(a + nx + 1, a, a + 1, a + nx + 2); }
                var fm = fb.ToMesh("flag"); fm.MarkDynamic();
                fgo.AddComponent<MeshFilter>().sharedMesh = fm;
                var mr = fgo.AddComponent<MeshRenderer>();
                dm.flagMat = mr.sharedMaterial = new Material(mats["flag"]);
                dm.flagPivot = fp; dm.flagMesh = fm; dm.flagBase = fm.vertices;
            }

            // ---------- 取り付け位置 ----------
            dm.ropeAnchors[0] = new Vector3(-0.52f, 0.56f, 2.2f) * Scale;
            dm.ropeAnchors[1] = new Vector3(0.52f, 0.56f, 2.2f) * Scale;
            dm.daikuFront = new Vector3(0, 4.27f, 1.55f) * Scale;
            dm.daikuRear = new Vector3(0, 3.3f, -1.5f) * Scale;
            dm.crewSlots.Add((new Vector3(0, 1.4f, 0.62f) * Scale, 0f, (int)Role.Drum));
            dm.crewSlots.Add((new Vector3(-0.95f, 1.4f, 0.2f) * Scale, 90f, (int)Role.Kane));
            dm.crewSlots.Add((new Vector3(0.95f, 1.4f, -0.4f) * Scale, -90f, (int)Role.Kane));
            foreach (float x in new[] { -0.55f, 0f, 0.55f }) dm.crewSlots.Add((new Vector3(x, 1.4f, 1.9f) * Scale, 0f, (int)Role.Front));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int k = 0; k < 3; k++) dm.crewSlots.Add((new Vector3(sx * (0.45f + 0.12f * k), 0f, -3.0f - k * 0.9f) * Scale, sx * -8f, (int)Role.Rear));
            foreach (int sx in new[] { -1, 1 })
            {
                dm.crewSlots.Add((new Vector3(sx * 1.85f, 0f, 2.75f) * Scale, -sx * 20f, (int)Role.Mae));
                dm.crewSlots.Add((new Vector3(sx * 1.35f, 0f, 3.35f) * Scale, -sx * 10f, (int)Role.Mae));
            }
            return dm;
        }

        static void Relief(MeshBuilder b, Vector3 c, Vector3 n, Vector3 u, float w, float h, float depth, int seed, int style, float density)
        {
            new Carving(w, h, seed, style).Build(b, c, n, u, w, h, depth, W, density);
        }

        // 組物：外向き n の面に沿って u 方向へ並べる。tiers：手先の数
        static void Brackets(MeshBuilder b, Vector3 c, Vector3 n, Vector3 u, float len, int tiers)
        {
            int count = Mathf.Max(2, Mathf.RoundToInt(len / 0.3f));
            Quaternion q = Quaternion.LookRotation(n);
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count - 0.5f;
                Vector3 p = c + u * (t * len);
                for (int k = 0; k < tiers; k++)
                {
                    float y = k * 0.1f, o = k * 0.07f;
                    b.Box(p + n * o + Vector3.up * y, new Vector3(0.11f, 0.06f, 0.11f), q, AGED, 1f);                // 斗
                    b.Box(p + n * (o + 0.02f) + Vector3.up * (y + 0.055f), new Vector3(0.3f, 0.05f, 0.07f), q, W, 1f); // 肘木
                    foreach (int s in new[] { -1, 1 }) b.Box(p + n * (o + 0.02f) + Vector3.Cross(Vector3.up, n) * (s * 0.13f) + Vector3.up * (y + 0.1f), new Vector3(0.07f, 0.045f, 0.07f), q, AGED, 1f); // 巻斗
                }
                b.Box(p + n * (tiers * 0.07f + 0.02f) + Vector3.up * (tiers * 0.1f + 0.02f), new Vector3(0.05f, 0.05f, 0.2f), q, DARK, 1f); // 尾垂木の先
            }
        }

        static void Gable(MeshBuilder b, RoofShape R, float z, Vector3 n, int seed, float density)
        {
            float span = 0.82f, w = span * R.w, yb = R.eaveY - 0.3f;
            float yTopMid = R.H(0, 0.5f) - 0.18f, cy = (yb + yTopMid) * 0.5f, h = yTopMid - yb;
            Vector3 u = Vector3.Cross(n, Vector3.up);
            new Carving(w, h, seed, 2).Build(b, new Vector3(0, cy, z), n, u, w, h, 0.035f, W, density,
                x => (R.H(Mathf.Clamp(x / (R.w * 0.5f), -1f, 1f), 0.5f) - 0.18f) - cy);
        }

        static List<Vector2> OniShape()
        {
            var pts = new List<Vector2> { new Vector2(-0.34f, 0), new Vector2(0.34f, 0) };
            void Q(Vector2 p0, Vector2 c, Vector2 p1) { for (int i = 1; i <= 8; i++) { float t = i / 8f; pts.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * c + t * t * p1); } }
            Q(new Vector2(0.34f, 0), new Vector2(0.44f, 0.5f), new Vector2(0.3f, 0.7f));
            Q(new Vector2(0.3f, 0.7f), new Vector2(0, 0.56f), new Vector2(-0.3f, 0.7f));
            Q(new Vector2(-0.3f, 0.7f), new Vector2(-0.44f, 0.5f), new Vector2(-0.34f, 0.02f));
            return pts;
        }

        // 反りのある切妻屋根。u: -1..1（幅方向）、t: 0..1（前後方向）
        sealed class RoofShape
        {
            public readonly float w, l, ridgeY, eaveY, flare;
            public RoofShape(float w, float l, float ridgeY, float eaveY, float flare) { this.w = w; this.l = l; this.ridgeY = ridgeY; this.eaveY = eaveY; this.flare = flare; }
            public float H(float u, float t)
            {
                float a = Mathf.Abs(u), e = Mathf.Abs(2 * t - 1);
                return eaveY + (ridgeY - eaveY) * Mathf.Pow(1 - a, 1.7f) + flare * Mathf.Pow(e, 4) * (0.25f + 0.75f * a * a);
            }
            public void Surface(MeshBuilder b, float zc, float off, Color c, bool up)
            {
                const int nx = 24, nz = 14; int s = b.Count;
                for (int j = 0; j <= nz; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        float t = j / (float)nz, u = i / (float)nx * 2 - 1, z = -l / 2 + t * l;
                        float du = 0.01f, dt = 0.01f;
                        float dhdx = (H(Mathf.Clamp(u + du, -1, 1), t) - H(Mathf.Clamp(u - du, -1, 1), t)) / (2 * du * w / 2);
                        float dhdz = (H(u, Mathf.Clamp01(t + dt)) - H(u, Mathf.Clamp01(t - dt))) / (2 * dt * l);
                        Vector3 n = new Vector3(-dhdx, 1, -dhdz).normalized; if (!up) n = -n;
                        b.V(new Vector3(u * w / 2, H(u, t) + off, zc + z), n, new Vector2(z, u * w * 0.5f), c);
                    }
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int a = s + j * (nx + 1) + i, bb = a + 1, cc = a + nx + 1, d = cc + 1;
                        Vector3 nn = b.nor[a] + b.nor[d];
                        b.TriFacing(a, bb, d, nn); b.TriFacing(a, d, cc, nn);
                    }
            }
            // 縁の板（fixT=true：t 固定の破風、false：u 固定の軒先）
            public void Ribbon(MeshBuilder b, float zc, bool fixT, float val, float drop, Color c)
            {
                int n = fixT ? 24 : 14, s = b.Count;
                Vector3 outward = fixT ? new Vector3(0, 0, val > 0.5f ? 1 : -1) : new Vector3(val, 0, 0);
                for (int i = 0; i <= n; i++)
                {
                    float x, y, z;
                    if (fixT) { float u = i / (float)n * 2 - 1; x = u * w / 2; z = -l / 2 + val * l; y = H(u, val); }
                    else { float t = i / (float)n; x = val * w / 2; z = -l / 2 + t * l; y = H(val, t); }
                    b.V(new Vector3(x, y + 0.05f, zc + z), outward, new Vector2(i / (float)n * (fixT ? w : l), 0.1f), c);
                    b.V(new Vector3(x, y - drop, zc + z), outward, new Vector2(i / (float)n * (fixT ? w : l), 0f), c);
                }
                for (int i = 0; i < n; i++) { int a = s + i * 2; b.TriFacing(a, a + 1, a + 3, b.nor[a]); b.TriFacing(a, a + 3, a + 2, b.nor[a]); }
            }
        }
    }
}
