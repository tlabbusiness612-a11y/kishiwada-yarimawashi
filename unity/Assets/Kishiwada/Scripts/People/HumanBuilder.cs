using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    // 服装の色
    public struct Outfit
    {
        public Color top, sleeve, pants, feet, skin, hair, band, belly;
        public bool happi, longSleeve, cap;
        public static Outfit Happi(Color c, bool whitePants = false) => new Outfit
        {
            top = c, sleeve = c, pants = whitePants ? KMath.Hex(0xe9e5dc) : KMath.Hex(0x1f2638), feet = KMath.Hex(0x1b1b1b),
            skin = KMath.Hex(0xd9a67f), hair = KMath.Hex(0x1c1714), band = KMath.Hex(0xf4f1ea), belly = KMath.Hex(0x1d2438), happi = true
        };
    }

    // 骨組みの番号
    public static class Bone
    {
        public const int Root = 0, Hips = 1, Spine = 2, Chest = 3, Neck = 4, Head = 5,
            UArmL = 6, LArmL = 7, HandL = 8, UArmR = 9, LArmR = 10, HandR = 11,
            ULegL = 12, LLegL = 13, FootL = 14, ULegR = 15, LLegR = 16, FootR = 17, Count = 18;
        public static readonly int[] Parent = { -1, 0, 1, 2, 3, 4, 3, 6, 7, 3, 9, 10, 1, 12, 13, 1, 15, 16 };
        // 休みの姿勢の関節位置（足元原点、身長 1.68m 前後）
        public static readonly Vector3[] RestPos =
        {
            new Vector3(0, 0, 0), new Vector3(0, 0.93f, 0), new Vector3(0, 1.08f, 0), new Vector3(0, 1.28f, 0), new Vector3(0, 1.47f, 0), new Vector3(0, 1.56f, 0),
            new Vector3(-0.19f, 1.41f, 0), new Vector3(-0.2f, 1.13f, 0), new Vector3(-0.2f, 0.88f, 0),
            new Vector3(0.19f, 1.41f, 0), new Vector3(0.2f, 1.13f, 0), new Vector3(0.2f, 0.88f, 0),
            new Vector3(-0.09f, 0.9f, 0), new Vector3(-0.09f, 0.49f, 0), new Vector3(-0.09f, 0.085f, 0),
            new Vector3(0.09f, 0.9f, 0), new Vector3(0.09f, 0.49f, 0), new Vector3(0.09f, 0.085f, 0),
        };
        public const float UpperArm = 0.28f, LowerArm = 0.25f, UpperLeg = 0.41f, LowerLeg = 0.405f;
    }

    // 一人分の骨（Transform）と見た目
    public sealed class HumanRig
    {
        public Transform root;
        public readonly Transform[] b = new Transform[Bone.Count];
        public SkinnedMeshRenderer smr;
        public Transform prop; // 団扇などの持ち物
    }

    public static class HumanBuilder
    {
        struct VW { public int b0, b1; public float w1; }

        // 骨組みつきのメッシュ（全員で共有）を作る
        public static Mesh BuildMesh(Outfit o, bool mobile)
        {
            var mb = new MeshBuilder();
            var weights = new List<VW>();
            int seg = mobile ? 8 : 12;
            void Mark(int from, int bone, int bone2 = -1, float w2 = 0f) { while (weights.Count < mb.Count) weights.Add(new VW { b0 = bone, b1 = bone2 < 0 ? bone : bone2, w1 = w2 }); }

            // 輪切りを積み上げた筒。rings：高さ・半径x・半径z・骨・次の骨への重み
            void Loft(Vector3 axisPos, (float y, float rx, float rz, int bone, int bone2, float w2, float zoff)[] rings, Color c, System.Func<float, float, Color> colorAt = null, bool capTop = false, bool capBottom = false)
            {
                int start = mb.Count;
                for (int j = 0; j < rings.Length; j++)
                {
                    var rg = rings[j];
                    for (int i = 0; i <= seg; i++)
                    {
                        float th = i / (float)seg * Mathf.PI * 2f;
                        Vector3 dir = new Vector3(Mathf.Sin(th), 0, Mathf.Cos(th));
                        Vector3 pnt = axisPos + new Vector3(dir.x * rg.rx, rg.y, dir.z * rg.rz + rg.zoff);
                        Vector3 n = new Vector3(dir.x / rg.rx, 0, dir.z / rg.rz).normalized;
                        Color col = colorAt != null ? colorAt(th, rg.y) : c;
                        mb.V(pnt, n, new Vector2(i / (float)seg, rg.y), col);
                        weights.Add(new VW { b0 = rg.bone, b1 = rg.bone2 < 0 ? rg.bone : rg.bone2, w1 = rg.w2 });
                    }
                }
                for (int j = 0; j < rings.Length - 1; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = start + j * (seg + 1) + i, bb = a + 1, cc = a + seg + 1, d = cc + 1;
                        Vector3 nn = mb.nor[a] + mb.nor[d];
                        mb.TriFacing(a, bb, d, nn); mb.TriFacing(a, d, cc, nn);
                    }
            }
            void Blob(Vector3 c, Vector3 r, Color col, int bone, float vFrom = 0f, float vTo = 1f)
            {
                mb.Ellipsoid(c, r, seg, Mathf.Max(4, seg / 2), col, vFrom, vTo);
                Mark(0, bone);
            }

            float bellyOn = o.happi ? 1f : 0f;
            // 胴（法被・腹掛け）
            Color TorsoCol(float th, float y)
            {
                bool front = Mathf.Cos(th) > 0.55f;
                if (o.happi && front && y > 0.95f && y < 1.42f) return Mathf.Cos(th) > 0.8f ? o.belly : o.top * 0.85f; // 前の合わせと腹掛け
                return o.top;
            }
            Loft(Vector3.zero, new[] {
                (0.86f, 0.165f, 0.12f, Bone.Hips, -1, 0f, 0f), (0.98f, 0.155f, 0.112f, Bone.Hips, Bone.Spine, 0.3f, 0f),
                (1.1f, 0.15f, 0.108f, Bone.Spine, -1, 0f, 0f), (1.22f, 0.165f, 0.115f, Bone.Spine, Bone.Chest, 0.6f, 0f),
                (1.33f, 0.18f, 0.118f, Bone.Chest, -1, 0f, 0.005f), (1.42f, 0.2f, 0.11f, Bone.Chest, -1, 0f, 0f),
                (1.47f, 0.13f, 0.09f, Bone.Chest, Bone.Neck, 0.3f, 0f) }, o.top, TorsoCol, true);
            // 法被の裾（腰から太ももへ広がる）
            if (o.happi)
                Loft(Vector3.zero, new[] {
                    (0.95f, 0.17f, 0.125f, Bone.Hips, -1, 0f, 0f), (0.82f, 0.2f, 0.145f, Bone.Hips, -1, 0f, 0f),
                    (0.7f, 0.215f, 0.155f, Bone.Hips, -1, 0f, 0f) }, o.top * 0.95f);
            // 首・頭・髪・鉢巻
            Loft(Vector3.zero, new[] { (1.46f, 0.05f, 0.05f, Bone.Neck, -1, 0f, 0f), (1.56f, 0.048f, 0.05f, Bone.Neck, Bone.Head, 0.6f, 0f) }, o.skin);
            Blob(new Vector3(0, 1.645f, 0.012f), new Vector3(0.088f, 0.112f, 0.1f), o.skin, Bone.Head);
            Blob(new Vector3(0, 1.662f, -0.004f), new Vector3(0.094f, 0.106f, 0.104f), o.hair, Bone.Head, 0f, 0.47f);
            Blob(new Vector3(0, 1.62f, 0.092f), new Vector3(0.018f, 0.025f, 0.02f), o.skin, Bone.Head); // 鼻
            if (o.cap) Blob(new Vector3(0, 1.71f, 0.01f), new Vector3(0.1f, 0.06f, 0.11f), o.band, Bone.Head, 0f, 0.5f);
            else if (o.happi) Loft(Vector3.zero, new[] { (1.675f, 0.097f, 0.107f, Bone.Head, -1, 0f, -0.003f), (1.71f, 0.095f, 0.105f, Bone.Head, -1, 0f, -0.003f) }, o.band);
            // 腕（両側）
            foreach (int s in new[] { -1, 1 })
            {
                int ua = s < 0 ? Bone.UArmL : Bone.UArmR, la = s < 0 ? Bone.LArmL : Bone.LArmR, hd = s < 0 ? Bone.HandL : Bone.HandR;
                Vector3 sh = Bone.RestPos[ua];
                // 上腕（袖）
                Loft(new Vector3(sh.x, 0, 0), new[] {
                    (1.45f, 0.07f, 0.068f, ua, Bone.Chest, 0.4f, 0f), (1.36f, 0.068f, 0.066f, ua, -1, 0f, 0f),
                    (1.2f, o.happi ? 0.085f : 0.06f, o.happi ? 0.08f : 0.058f, ua, -1, 0f, 0f), (1.13f, o.happi ? 0.09f : 0.055f, o.happi ? 0.085f : 0.054f, ua, la, 0.3f, 0f) }, o.sleeve);
                // 前腕
                Color fore = o.longSleeve ? o.sleeve : o.skin;
                Loft(new Vector3(sh.x, 0, 0), new[] {
                    (1.16f, 0.045f, 0.045f, la, ua, 0.4f, 0f), (1.02f, 0.04f, 0.042f, la, -1, 0f, 0f), (0.9f, 0.031f, 0.034f, la, hd, 0.3f, 0f) }, fore);
                // 手
                Blob(new Vector3(sh.x, 0.83f, 0.01f), new Vector3(0.034f, 0.075f, 0.048f), o.skin, hd);
            }
            // 脚（股引）と足（地下足袋）
            foreach (int s in new[] { -1, 1 })
            {
                int ul = s < 0 ? Bone.ULegL : Bone.ULegR, ll = s < 0 ? Bone.LLegL : Bone.LLegR, ft = s < 0 ? Bone.FootL : Bone.FootR;
                float x = Bone.RestPos[ul].x;
                Loft(new Vector3(x, 0, 0), new[] {
                    (0.92f, 0.085f, 0.09f, ul, Bone.Hips, 0.4f, 0f), (0.72f, 0.075f, 0.08f, ul, -1, 0f, 0f), (0.52f, 0.055f, 0.06f, ul, ll, 0.5f, 0f),
                    (0.36f, 0.058f, 0.062f, ll, -1, 0f, -0.005f), (0.14f, 0.04f, 0.045f, ll, -1, 0f, 0f), (0.09f, 0.042f, 0.046f, ll, ft, 0.5f, 0f) }, o.pants);
                Blob(new Vector3(x, 0.045f, 0.045f), new Vector3(0.045f, 0.045f, 0.12f), o.feet, ft);
            }

            // 骨の重み・バインドポーズ
            var mesh = mb.ToMesh("human", false);
            var bw = new BoneWeight[mb.Count];
            for (int i = 0; i < mb.Count; i++)
            {
                var q = weights[i];
                bw[i] = q.b0 == q.b1 || q.w1 <= 0f ? new BoneWeight { boneIndex0 = q.b0, weight0 = 1f } : new BoneWeight { boneIndex0 = q.b0, weight0 = 1f - q.w1, boneIndex1 = q.b1, weight1 = q.w1 };
            }
            mesh.boneWeights = bw;
            var bind = new Matrix4x4[Bone.Count];
            for (int i = 0; i < Bone.Count; i++) bind[i] = Matrix4x4.Translate(-Bone.RestPos[i]);
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            return mesh;
        }

        // 骨の Transform を作り、共有メッシュで描く
        public static HumanRig Spawn(Mesh mesh, Material mat, Transform parent, string name, int layer = 0)
        {
            var rig = new HumanRig();
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            rig.root = go.transform;
            rig.b[0] = go.transform;
            for (int i = 1; i < Bone.Count; i++)
            {
                var t = new GameObject(((BoneName)i).ToString()).transform;
                t.SetParent(rig.b[Bone.Parent[i]], false);
                t.localPosition = Bone.RestPos[i] - Bone.RestPos[Bone.Parent[i]];
                rig.b[i] = t;
            }
            var skin = new GameObject("Skin") { layer = layer };
            skin.transform.SetParent(go.transform, false);
            var smr = skin.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.bones = rig.b; smr.rootBone = rig.b[Bone.Hips];
            smr.sharedMaterial = mat;
            smr.localBounds = new Bounds(new Vector3(0, 0f, 0), new Vector3(2.4f, 3.6f, 2.4f));
            smr.updateWhenOffscreen = false;
            smr.shadowCastingMode = ShadowCastingMode.On;
            smr.quality = SkinQuality.Bone2;
            rig.smr = smr;
            return rig;
        }

        enum BoneName { Root, Hips, Spine, Chest, Neck, Head, UpperArmL, LowerArmL, HandL, UpperArmR, LowerArmR, HandR, UpperLegL, LowerLegL, FootL, UpperLegR, LowerLegR, FootR }

        // 姿勢を止めた形でメッシュに焼く（見物人のまとめ描き用）
        public static Mesh Bake(HumanRig rig)
        {
            var m = new Mesh();
            rig.smr.BakeMesh(m, true);
            // 焼いたメッシュは Skin の座標系。root 基準に直す
            var toRoot = rig.root.worldToLocalMatrix * rig.smr.transform.localToWorldMatrix;
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = toRoot.MultiplyPoint3x4(v[i]); n[i] = toRoot.MultiplyVector(n[i]).normalized; }
            m.vertices = v; m.normals = n; m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }
    }
}
