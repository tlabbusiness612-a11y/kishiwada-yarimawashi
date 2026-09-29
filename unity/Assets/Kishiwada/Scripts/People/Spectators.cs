using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    // 見物人：姿勢ごとに焼いたメッシュを GPU インスタンスでまとめて描く。地車が近いと跳ねて手を振る
    public sealed class Spectators
    {
        const int Poses = 6; // 0 立つ, 1 両手上げ, 2 片手, 3 手拍子, 4 座る, 5 座って手を上げる
        readonly Mesh[,] meshes;
        readonly int variants;
        readonly Material mat;
        readonly List<Spectator> list;
        readonly List<Matrix4x4>[,] batch;
        readonly Matrix4x4[] tmp = new Matrix4x4[1023];
        RenderParams rp;

        public Spectators(List<Spectator> list, Material mat, bool mobile, Transform tempParent)
        {
            this.list = list; this.mat = mat;
            variants = mobile ? 5 : 10;
            meshes = new Mesh[Poses, variants];
            batch = new List<Matrix4x4>[Poses, variants];
            var r = new Rng(55);
            Color[] shirts = { KMath.Hex(0x2b3a55), KMath.Hex(0xe9e4d8), KMath.Hex(0x8c2f2f), KMath.Hex(0x3f5f3f), KMath.Hex(0x2a2a2a), KMath.Hex(0xc9a86a), KMath.Hex(0x5b6f8f), KMath.Hex(0xf2f2f2), KMath.Hex(0x7a4a6a), KMath.Hex(0x1f4f6f), KMath.Hex(0xd46a2a), KMath.Hex(0xb8321f) };
            Color[] pants = { KMath.Hex(0x2d3444), KMath.Hex(0x3a4152), KMath.Hex(0x5a5448), KMath.Hex(0x1e1e22), KMath.Hex(0x6b6f78) };
            Color[] skins = { KMath.Hex(0xd9a67f), KMath.Hex(0xc9936b), KMath.Hex(0xe2b690) };
            for (int v = 0; v < variants; v++)
            {
                Outfit o;
                if (v % 4 == 3) o = Outfit.Happi(r.Pick(new[] { KMath.Hex(0xb8321f), KMath.Hex(0x5d7a2a), KMath.Hex(0x1f2e5a), KMath.Hex(0x5b3a6e) }), r.Chance(0.5f)); // よその町の法被
                else o = new Outfit { top = r.Pick(shirts), sleeve = r.Pick(shirts), pants = r.Pick(pants), feet = KMath.Hex(0x2a2a2a), skin = r.Pick(skins), hair = KMath.Hex(r.Chance(0.2f) ? 0x5a4a3au : 0x1c1714u), band = r.Pick(shirts), cap = r.Chance(0.3f) };
                o.skin = r.Pick(skins);
                if (!o.happi) o.sleeve = o.top;
                var mesh = HumanBuilder.BuildMesh(o, true);
                var rig = HumanBuilder.Spawn(mesh, mat, tempParent, "bake", 0);
                for (int p = 0; p < Poses; p++)
                {
                    if (p < 4) HumanPose.Spectate(rig, p, v);
                    else { HumanPose.Sit(rig, 5f); if (p == 5) { HumanPose.Arm(rig, true, -20f, 150f, 20f); HumanPose.Arm(rig, false, -20f, 150f, 20f); } else { HumanPose.Arm(rig, true, -40f, 12f, 60f); HumanPose.Arm(rig, false, -40f, 12f, 60f); } }
                    meshes[p, v] = HumanBuilder.Bake(rig);
                    batch[p, v] = new List<Matrix4x4>(256);
                }
                Object.Destroy(rig.root.gameObject);
            }
            rp = new RenderParams(mat)
            {
                shadowCastingMode = mobile ? ShadowCastingMode.Off : ShadowCastingMode.On,
                receiveShadows = true,
                worldBounds = new Bounds(Vector3.zero, new Vector3(3000, 200, 3000)),
                layer = 10,
            };
        }

        public void Update(Vector3 focus, float t, bool excited, float excite)
        {
            for (int p = 0; p < Poses; p++) for (int v = 0; v < variants; v++) batch[p, v].Clear();
            float nearR2 = excited ? 38f * 38f : -1f;
            foreach (var s in list)
            {
                float dx = s.pos.x - focus.x, dz = s.pos.z - focus.z;
                bool near = dx * dx + dz * dz < nearR2;
                int pose; float y = 0f;
                if (near)
                {
                    int cyc = (int)((t * 1.3f + s.phase) % 3f);
                    pose = s.sitting ? 5 : 1 + cyc;
                    y = s.sitting ? 0f : Mathf.Abs(Mathf.Sin(t * 8f + s.phase)) * 0.18f * excite;
                }
                else pose = s.sitting ? 4 : ((int)(s.phase * 10f) % 5 == 0 ? 3 : 0);
                var m = Matrix4x4.TRS(s.pos + new Vector3(0, y, 0), Quaternion.Euler(0, s.yaw * Mathf.Rad2Deg, 0), Vector3.one * s.scale);
                batch[pose, s.variant % variants].Add(m);
            }
            for (int p = 0; p < Poses; p++)
                for (int v = 0; v < variants; v++)
                {
                    var l = batch[p, v]; if (l.Count == 0) continue;
                    for (int start = 0; start < l.Count; start += 1023)
                    {
                        int n = Mathf.Min(1023, l.Count - start);
                        l.CopyTo(start, tmp, 0, n);
                        Graphics.RenderMeshInstanced(rp, meshes[p, v], 0, tmp, n);
                    }
                }
        }
    }
}
