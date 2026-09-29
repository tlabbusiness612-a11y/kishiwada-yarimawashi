using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    // 地車の見た目の動き：車輪・揺れ・梃子・旗・乗り手
    public sealed class DanjiriView
    {
        readonly DanjiriModel dm;
        readonly DanjiriBody body;
        public HumanRig daikuFront, daikuRear;
        readonly List<(HumanRig rig, int role, Vector3 slot, float yaw, int idx)> crew = new List<(HumanRig, int, Vector3, float, int)>();
        float roll, rollV, pitch, pitchV, crewPhase;
        public float jumpAt = -9f;
        readonly float[] maeAmt = new float[2];

        public DanjiriView(DanjiriModel dm, DanjiriBody body, Mesh daikuMesh, Mesh crewMesh, Material mat, MaterialLibrary mats)
        {
            this.dm = dm; this.body = body;
            daikuFront = HumanBuilder.Spawn(daikuMesh, mat, dm.body, "DaikuFront", 9);
            daikuRear = HumanBuilder.Spawn(daikuMesh, mat, dm.body, "DaikuRear", 9);
            daikuFront.root.localPosition = dm.daikuFront; daikuRear.root.localPosition = dm.daikuRear;
            foreach (var d in new[] { daikuFront, daikuRear }) d.prop = Uchiwa(d.b[Bone.HandR], mats);
            int i = 0;
            foreach (var s in dm.crewSlots)
            {
                var rig = HumanBuilder.Spawn(crewMesh, mat, dm.body, "Crew" + i, 9);
                rig.root.localPosition = s.pos; rig.root.localRotation = Quaternion.Euler(0, s.yaw, 0);
                crew.Add((rig, s.role, s.pos, s.yaw, i++));
            }
        }

        static Transform Uchiwa(Transform hand, MaterialLibrary mats)
        {
            var go = new GameObject("Uchiwa");
            go.transform.SetParent(hand, false);
            go.transform.localPosition = new Vector3(0, -0.08f, 0.02f);
            var ms = new MeshSet();
            ms["plain"].Cylinder(Vector3.zero, new Vector3(0, -0.22f, 0), 0.012f, 0.012f, 6, KMath.Hex(0x3b2a1a));
            ms["plain"].Disc(new Vector3(0, -0.4f, 0), Vector3.forward, Vector3.right, 0.19f, 20, KMath.Hex(0xf4efe4));
            ms["plain"].Disc(new Vector3(0, -0.4f, 0), Vector3.back, Vector3.right, 0.19f, 20, KMath.Hex(0xf4efe4));
            ms["lantern"].Torus(new Vector3(0, -0.4f, 0), Vector3.forward, 0.175f, 0.012f, 20, 4, KMath.Hex(0xc23a26));
            ms.Build("fan", go.transform, k => mats[k], ShadowCastingMode.On, true, 9);
            return go.transform;
        }

        // beat：鳴物の拍の位相（拍数）
        public void Update(float dt, float t, double beat, float clock)
        {
            var rb = body.rb;
            float speed = rb.linearVelocity.magnitude;
            // 車輪
            for (int i = 0; i < 4; i++) dm.wheels[i].localPosition = dm.wheelLocal[i];
            for (int i = 0; i < 4; i++) dm.wheels[i].localRotation = Quaternion.Euler(body.wheels[i].angle * Mathf.Rad2Deg, 0, 0);
            // 揺れ（横 G で外へ傾き、前後 G で前のめり）
            Vector3 a = body.accel;
            float tr = Mathf.Clamp(-a.x * 0.75f, -5f, 5f), tp = Mathf.Clamp(a.z * 0.35f, -3f, 3f);
            rollV += ((tr - roll) * 60f - rollV * 9f) * dt; roll += rollV * dt;
            pitchV += ((tp - pitch) * 70f - pitchV * 10f) * dt; pitch += pitchV * dt;
            dm.body.localRotation = Quaternion.Euler(pitch, 0, roll);
            // 前梃子
            for (int k = 0; k < 2; k++)
            {
                float want = k == 0 ? body.maeL : body.maeR;
                maeAmt[k] = Mathf.Lerp(maeAmt[k], want, KMath.Damp(14f, dt));
                int sx = k == 0 ? -1 : 1;
                dm.maePivot[k].localRotation = Quaternion.Euler(6f - maeAmt[k] * 26f, sx * 23f, 0);
            }
            // 旗
            {
                float kf = Mathf.Min(1f, speed / 5f);
                dm.flagPivot.localRotation = Quaternion.Euler((0.04f + kf * 0.55f + Mathf.Sin(t * 5.3f) * 0.05f * kf) * Mathf.Rad2Deg, 0, 0);
                var v = dm.flagBase; var outV = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    float x = v[i].x, y = v[i].y;
                    outV[i] = new Vector3(x, y, v[i].z + Mathf.Sin(x * 4f + y * 2.5f + t * 9f) * 0.07f * (0.25f + kf) * (-y / 1.6f));
                }
                dm.flagMesh.vertices = outV; dm.flagMesh.RecalculateNormals();
            }
            // 大工方
            float sway = Mathf.Clamp(-a.x * 3.5f, -28f, 28f);
            float beatPh = (float)(beat % 1.0) * Mathf.PI;
            for (int i = 0; i < 2; i++)
            {
                var d = i == 0 ? daikuFront : daikuRear;
                float jt = (clock - jumpAt - i * 0.12f) / 0.7f;
                HumanPose.Daiku(d, t + i * 1.7f, jt, sway, beatPh);
            }
            // 乗り手・梃子方
            crewPhase += dt * speed / 1.5f * Mathf.PI * 2f;
            float stride = Mathf.Clamp01(speed / 6.5f);
            float rearTurn = body.rearTurn;
            foreach (var c in crew)
            {
                var r = c.rig;
                switch ((DanjiriBuilder.Role)c.role)
                {
                    case DanjiriBuilder.Role.Drum:
                        HumanPose.Drummer(r, Frac((float)beat), Frac((float)beat + 0.5f));
                        break;
                    case DanjiriBuilder.Role.Kane:
                        HumanPose.Kane(r, Frac((float)beat * 2f + c.idx * 0.25f));
                        break;
                    case DanjiriBuilder.Role.Front:
                        {
                            HumanPose.Rest(r);
                            r.b[Bone.Hips].localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 2f + c.idx) * 3f + sway * 0.2f);
                            HumanPose.Leg(r, true, -4f, 10f, 5f); HumanPose.Leg(r, false, 4f, 10f, 5f);
                            Vector3 fwd = dm.body.forward, rt = dm.body.right;
                            Vector3 rail = dm.body.TransformPoint(c.slot + new Vector3(0, 0.3f, 0.2f));
                            HumanPose.ArmIK(r, true, rail - rt * 0.22f, -fwd + Vector3.down);
                            HumanPose.ArmIK(r, false, rail + rt * 0.22f, -fwd + Vector3.down);
                        }
                        break;
                    case DanjiriBuilder.Role.Rear:
                        {
                            HumanPose.Run(r, crewPhase + c.idx * 1.3f, stride, 16f + Mathf.Abs(rearTurn) * 10f, false);
                            r.b[Bone.Hips].localRotation *= Quaternion.Euler(0, 0, -rearTurn * 12f);
                            float tt = 0.35f + (c.idx % 3) * 0.22f;
                            Vector3 grip = dm.rearPole.TransformPoint(new Vector3(0, 0.25f * tt, -3.4f * tt));
                            Vector3 rt = dm.body.right;
                            HumanPose.ArmIK(r, true, grip - rt * 0.08f + Vector3.up * 0.02f, Vector3.down + rt * -0.5f);
                            HumanPose.ArmIK(r, false, grip + rt * 0.08f, Vector3.down + rt * 0.5f);
                        }
                        break;
                    case DanjiriBuilder.Role.Mae:
                        {
                            int side = c.slot.x < 0 ? 0 : 1;
                            float press = maeAmt[side];
                            HumanPose.Run(r, crewPhase + c.idx * 0.9f, stride, 18f + press * 22f, false);
                            var pv = dm.maePivot[side];
                            Vector3 g1 = pv.TransformPoint(new Vector3(0, 0, -0.25f - (c.idx % 2) * 0.55f));
                            Vector3 g2 = pv.TransformPoint(new Vector3(0, 0, -0.55f - (c.idx % 2) * 0.55f));
                            HumanPose.ArmIK(r, true, g1, Vector3.down - dm.body.forward);
                            HumanPose.ArmIK(r, false, g2, Vector3.down - dm.body.forward);
                        }
                        break;
                }
            }
        }
        static float Frac(float x) => x - Mathf.Floor(x);

        public void Kick(float amount) { rollV += (Random.value - 0.5f) * amount * 40f; pitchV -= amount * 25f; }
    }
}
