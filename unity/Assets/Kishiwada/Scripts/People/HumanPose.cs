using UnityEngine;

namespace Kishiwada
{
    // 手続き的な動き。角度は度。脚・腕は下向き（-y）が休みの向きなので、x の負で前へ振り上げる
    public static class HumanPose
    {
        static Quaternion E(float x, float y, float z) => Quaternion.Euler(x, y, z);

        public static void Rest(HumanRig r)
        {
            for (int i = 1; i < Bone.Count; i++)
            {
                r.b[i].localRotation = Quaternion.identity;
                r.b[i].localPosition = Bone.RestPos[i] - Bone.RestPos[Bone.Parent[i]];
            }
        }

        // 走る・歩く。phase：歩調（2π で左右一歩ずつ）、stride：0..1、lean：前傾（度）
        public static void Run(HumanRig r, float phase, float stride, float lean, bool swingArms)
        {
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            float bob = 0.045f * stride * (Mathf.Abs(s) - 0.55f) - 0.02f * stride;
            r.b[Bone.Hips].localPosition = Bone.RestPos[Bone.Hips] + new Vector3(0, bob, 0);
            r.b[Bone.Hips].localRotation = E(lean * 0.35f, 9f * stride * s, 3f * stride * c);
            r.b[Bone.Spine].localRotation = E(lean * 0.4f, -7f * stride * s, 0);
            r.b[Bone.Chest].localRotation = E(lean * 0.25f, -5f * stride * s, -2f * stride * c);
            r.b[Bone.Neck].localRotation = E(-lean * 0.45f, 0, 0);
            r.b[Bone.Head].localRotation = E(-lean * 0.35f, 4f * s * stride, 0);
            float amp = 22f + 30f * stride;
            Leg(r, true, -amp * s - lean * 0.2f, KneeFlex(phase, stride));
            Leg(r, false, amp * s - lean * 0.2f, KneeFlex(phase + Mathf.PI, stride));
            if (swingArms)
            {
                float aa = 15f + 35f * stride;
                Arm(r, true, aa * s - 5f, -8f, 55f + 35f * stride);
                Arm(r, false, -aa * s - 5f, 8f, 55f + 35f * stride);
            }
        }
        static float KneeFlex(float ph, float stride)
        {
            float c = Mathf.Max(0f, Mathf.Cos(ph));
            return 6f + stride * (18f + 95f * c * c);
        }
        public static void Leg(HumanRig r, bool left, float hip, float knee, float hipSide = 0f)
        {
            int ul = left ? Bone.ULegL : Bone.ULegR, ll = left ? Bone.LLegL : Bone.LLegR, ft = left ? Bone.FootL : Bone.FootR;
            r.b[ul].localRotation = E(hip, 0, left ? -hipSide : hipSide);
            r.b[ll].localRotation = E(knee, 0, 0);
            r.b[ft].localRotation = E(-(hip + knee) * 0.6f, 0, 0);
        }
        // 腕：pitch は負で前へ、side は外へ開く角度、elbow は曲げ（正の値で前腕が前へ）
        public static void Arm(HumanRig r, bool left, float pitch, float side, float elbow)
        {
            int ua = left ? Bone.UArmL : Bone.UArmR, la = left ? Bone.LArmL : Bone.LArmR;
            r.b[ua].localRotation = E(pitch, 0, left ? -side : side);
            r.b[la].localRotation = E(-elbow, 0, 0);
            r.b[left ? Bone.HandL : Bone.HandR].localRotation = Quaternion.identity;
        }

        // 二関節の IK：手を target（ワールド）へ。肘は pole の方へ曲がる
        public static void ArmIK(HumanRig r, bool left, Vector3 target, Vector3 pole)
        {
            int ua = left ? Bone.UArmL : Bone.UArmR, la = left ? Bone.LArmL : Bone.LArmR, hd = left ? Bone.HandL : Bone.HandR;
            Transform U = r.b[ua], Lw = r.b[la], P = U.parent;
            float sc = r.root.lossyScale.y;
            float l1 = Bone.UpperArm * sc, l2 = Bone.LowerArm * sc;
            Vector3 S = U.position;
            Vector3 d = target - S; float dist = d.magnitude;
            if (dist < 1e-4f) return;
            float reach = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.005f);
            Vector3 n = d / dist;
            float cosA = Mathf.Clamp((l1 * l1 + reach * reach - l2 * l2) / (2f * l1 * reach), -1f, 1f);
            Vector3 pp = pole - n * Vector3.Dot(pole, n);
            if (pp.sqrMagnitude < 1e-6f) pp = Vector3.Cross(n, Vector3.up); pp.Normalize();
            Vector3 elbow = S + n * (l1 * cosA) + pp * (l1 * Mathf.Sqrt(1f - cosA * cosA));
            Vector3 hand = S + n * reach;
            Quaternion pr = P.rotation;
            U.rotation = Quaternion.FromToRotation(pr * Vector3.down, elbow - S) * pr;
            Quaternion ur = U.rotation;
            Lw.rotation = Quaternion.FromToRotation(ur * Vector3.down, hand - elbow) * ur;
            r.b[hd].localRotation = Quaternion.identity;
        }

        // 座る（腰の高さは root を置く位置で決める）
        public static void Sit(HumanRig r, float lean)
        {
            r.b[Bone.Hips].localPosition = Bone.RestPos[Bone.Hips] + new Vector3(0, -0.45f, -0.05f);
            r.b[Bone.Hips].localRotation = E(lean * 0.3f, 0, 0);
            r.b[Bone.Spine].localRotation = E(lean * 0.4f, 0, 0);
            r.b[Bone.Chest].localRotation = E(lean * 0.3f, 0, 0);
            r.b[Bone.Neck].localRotation = E(-lean * 0.5f, 0, 0);
            r.b[Bone.Head].localRotation = Quaternion.identity;
            Leg(r, true, -85f, 88f, 10f);
            Leg(r, false, -85f, 88f, 10f);
        }

        // 見物人：pose 0 立ち、1 両手を上げる、2 片手を振る、3 手をたたく
        public static void Spectate(HumanRig r, int pose, float t)
        {
            Rest(r);
            float sway = Mathf.Sin(t) * 3f;
            r.b[Bone.Hips].localRotation = E(0, sway, 0);
            r.b[Bone.Spine].localRotation = E(-3f, 0, 0);
            Leg(r, true, 0, 3f, 4f); Leg(r, false, 0, 3f, 4f);
            switch (pose)
            {
                case 0: Arm(r, true, 5f, 8f, 15f); Arm(r, false, 5f, 8f, 15f); break;
                case 1: Arm(r, true, -20f, 150f, 20f); Arm(r, false, -20f, 150f, 20f); r.b[Bone.Head].localRotation = E(-12f, 0, 0); break;
                case 2: Arm(r, true, 5f, 8f, 15f); Arm(r, false, -30f, 140f, 40f); break;
                default: Arm(r, true, -55f, 14f, 70f); Arm(r, false, -55f, 14f, 70f); break;
            }
        }

        // 大工方：団扇を振って踊る。jump 0..1 は跳びの進み具合、sway は横 G による傾き（度）
        public static void Daiku(HumanRig r, float t, float jump, float sway, float beat)
        {
            Rest(r);
            float bounce = Mathf.Abs(Mathf.Sin(beat)) * 0.04f;
            float jy = 0f, tuck = 0f;
            if (jump > 0f && jump < 1f) { jy = 3.2f * jump * (1f - jump) * 0.55f; tuck = Mathf.Sin(jump * Mathf.PI); }
            r.b[Bone.Hips].localPosition = Bone.RestPos[Bone.Hips] + new Vector3(0, jy + bounce - 0.06f * (1 - tuck), 0);
            r.b[Bone.Hips].localRotation = E(8f, Mathf.Sin(t * 1.1f) * 25f, sway);
            r.b[Bone.Spine].localRotation = E(4f, Mathf.Sin(t * 1.1f + 0.5f) * 8f, sway * 0.5f);
            r.b[Bone.Chest].localRotation = E(0, 0, sway * 0.3f);
            r.b[Bone.Head].localRotation = E(-6f, -Mathf.Sin(t * 1.1f) * 15f, -sway * 0.8f);
            float knee = 22f + tuck * 70f;
            Leg(r, true, -knee * 0.5f - tuck * 20f, knee, 6f);
            Leg(r, false, -knee * 0.5f - tuck * 20f, knee, 6f);
            // 右手で団扇を振る、左手は腰か、跳ぶときは広げる
            float wave = Mathf.Sin(t * 7f);
            Arm(r, false, -40f + wave * 25f, 110f + wave * 20f, 35f + wave * 30f);
            Arm(r, true, -10f - tuck * 30f, 30f + tuck * 90f, 60f - tuck * 40f);
        }

        // 太鼓打ち（座って両腕を振り下ろす）。hit 0..1（拍で 0 に戻る）
        public static void Drummer(HumanRig r, float hitL, float hitR)
        {
            Sit(r, 12f);
            Arm(r, true, -60f - 50f * (1f - Mathf.Clamp01(hitL * 3f)), 18f, 70f);
            Arm(r, false, -60f - 50f * (1f - Mathf.Clamp01(hitR * 3f)), 18f, 70f);
        }
        // 鉦（座って片手で打つ）
        public static void Kane(HumanRig r, float hit)
        {
            Sit(r, 8f);
            Arm(r, true, -55f, 20f, 85f);
            Arm(r, false, -45f - 30f * (1f - Mathf.Clamp01(hit * 4f)), 25f, 75f);
        }
    }
}
