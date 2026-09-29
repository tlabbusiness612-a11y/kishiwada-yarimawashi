using System;
using UnityEngine;

namespace Kishiwada
{
    // 地車の重さ・車輪・梃子の数値（インスペクタで調整できる）
    [Serializable]
    public class DanjiriSpec
    {
        [Header("車体")]
        public float mass = 4000f;
        public Vector3 centerOfMass = new Vector3(0f, 1.2f, 0.15f);
        public Vector3 inertia = new Vector3(9000f, 7000f, 5500f);
        [Header("車輪（木の車輪とアスファルト）")]
        public float springK = 400000f;      // 車輪 1 つのばね（N/m）
        public float damperC = 24000f;
        public float travel = 0.06f;
        public float muRoll = 0.62f;         // 転がっている車輪の横の摩擦
        public float muSlide = 0.46f;        // 滑っている車輪の摩擦
        public float slipPeak = 0.06f;       // 横滑りの立ち上がり（横/前の速さの比）
        public float rollResist = 0.022f;    // 転がり抵抗
        [Header("梃子")]
        public float maeBrake = 12000f;      // 前梃子のブレーキ（車輪 1 つあたり最大 N）
        public float maePush = 2600f;        // 前梃子の衆が前を横へ押す力
        public float maePushZ = 2.6f;
        public float rearPush = 4800f;       // 後梃子で尻を振る力
        public float rearArm = -3.3f;        // 後梃子の力点（z）
        public float rearBrake = 2500f;      // 後梃子の衆が後ろへ踏ん張る力（止める時）
    }

    public struct WheelState { public bool grounded; public float load, vLong, vLat, slide, spin, angle; }

    // 地車の物理。PhysX の剛体に、車輪ごとの摩擦・梃子の力を毎ステップ足す。綱の力は RopeSim が衝撃として足す
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DanjiriBody : MonoBehaviour
    {
        public DanjiriSpec spec = new DanjiriSpec();
        [NonSerialized] public Rigidbody rb;
        [NonSerialized] public DanjiriModel model;
        [NonSerialized] public readonly WheelState[] wheels = new WheelState[4];
        // 入力（0..1）。turn は +1 で左へ回す（後梃子）
        [NonSerialized] public float maeL, maeR, rearTurn, brakeAll;
        public event Action<float, Vector3, Collider> OnImpact; // 速さの変化(m/s)、位置、相手
        [NonSerialized] public float scrape;   // こすれている強さ（音用）
        [NonSerialized] public Vector3 accel;  // ローカルの加速度（見た目の揺れ用）
        Vector3 lastVel;
        float lastImpactTime = -9f;

        public void Init(DanjiriModel m)
        {
            model = m;
            rb = GetComponent<Rigidbody>();
            rb.mass = spec.mass;
            rb.automaticCenterOfMass = false; rb.centerOfMass = spec.centerOfMass;
            rb.automaticInertiaTensor = false; rb.inertiaTensor = spec.inertia; rb.inertiaTensorRotation = Quaternion.identity;
            rb.linearDamping = 0.02f; rb.angularDamping = 0.08f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 3f;
            rb.solverIterations = 16;
            var pm = new PhysicsMaterial("danjiri") { dynamicFriction = 0.32f, staticFriction = 0.4f, bounciness = 0.04f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            AddBox("ColBase", new Vector3(0, 0.95f, 0), new Vector3(2.2f, 0.85f, 3.85f), pm);
            AddBox("ColUpper", new Vector3(0, 2.75f, 0.05f), new Vector3(2.6f, 2.1f, 3.9f), pm);
            AddBox("ColRoof", new Vector3(0, 3.55f, 0.5f), new Vector3(2.7f, 0.5f, 2.9f), pm);
        }
        void AddBox(string name, Vector3 c, Vector3 s, PhysicsMaterial pm)
        {
            var go = new GameObject(name) { layer = 9 };
            go.transform.SetParent(transform, false);
            var bc = go.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; bc.sharedMaterial = pm;
        }

        public void Place(Vector3 pos, float yaw)
        {
            rb.position = pos; rb.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            transform.SetPositionAndRotation(rb.position, rb.rotation);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            lastVel = Vector3.zero;
            for (int i = 0; i < 4; i++) wheels[i] = default;
        }

        public float Speed => rb.linearVelocity.magnitude;
        public Vector3 Forward => rb.rotation * Vector3.forward;
        public float Yaw => KMath.Yaw(Forward);
        public float YawRate => rb.angularVelocity.y; // 正で右回り
        public Vector2 PosXZ => new Vector2(rb.position.x, rb.position.z);

        // 車輪の力と梃子の力。Game の FixedUpdate から呼ぶ
        public void Step(float dt)
        {
            var s = spec;
            Quaternion q = rb.rotation;
            Vector3 up = q * Vector3.up, fwdB = q * Vector3.forward;
            float mEff = rb.mass * 0.25f;
            for (int i = 0; i < 4; i++)
            {
                ref var w = ref wheels[i];
                Vector3 hub = rb.position + q * model.wheelLocal[i];
                float r = model.wheelRadius;
                // 地面は y=0 の平面（地形を入れる時はここを高さ関数に）
                float gY = 0f;
                if (up.y < 0.2f) { w.grounded = false; continue; }
                float dist = (hub.y - gY) / up.y;            // 車軸から地面まで（車体の下向き）
                float comp = r + s.travel * 0.5f - dist;      // 沈み込み
                if (comp <= 0f) { w.grounded = false; w.load = 0; continue; }
                comp = Mathf.Min(comp, s.travel * 3f);
                Vector3 cp = hub - up * dist;
                Vector3 v = rb.GetPointVelocity(cp);
                float compRate = -Vector3.Dot(v, up);
                float N = Mathf.Max(0f, s.springK * comp + s.damperC * compRate);
                N = Mathf.Min(N, rb.mass * 9.81f * 2.5f);
                w.grounded = true; w.load = N;

                Vector3 n = Vector3.up;
                Vector3 fw = Vector3.ProjectOnPlane(fwdB, n).normalized, lat = Vector3.Cross(n, fw);
                float vl = Vector3.Dot(v, fw), vs = Vector3.Dot(v, lat);
                w.vLong = vl; w.vLat = vs;

                // 前梃子：前輪にブレーキ
                float brake = (i == 0 ? maeL : i == 1 ? maeR : 0f) * s.maeBrake + brakeAll * s.maeBrake * (i < 2 ? 0.8f : 0.3f);
                float lockAmt = Mathf.Clamp01(brake / (s.muSlide * N + 1f));
                // 転がっている時
                float roll = s.rollResist * N + (lockAmt < 1f ? brake : 0f);
                float fL = -Mathf.Clamp(vl / 0.25f, -1f, 1f) * Mathf.Min(roll, s.muSlide * N);
                float sn = vs / Mathf.Max(Mathf.Abs(vl), 0.7f);
                float fS = -s.muRoll * N * KMath.Tanh(sn / s.slipPeak);
                // 滑っている時：接地点の速さと逆向きに動摩擦
                Vector2 slip = new Vector2(vl, vs); float sp = slip.magnitude;
                Vector2 fSlide = -s.muSlide * N * slip / Mathf.Max(sp, 0.25f);
                Vector2 f = Vector2.Lerp(new Vector2(fL, fS), fSlide, lockAmt * lockAmt);
                // 摩擦円
                float lim = s.muRoll * N, fm = f.magnitude;
                if (fm > lim) f *= lim / fm;
                // 一歩で速度を逆転させない
                float capL = mEff * Mathf.Abs(vl) / dt, capS = mEff * Mathf.Abs(vs) / dt;
                f.x = Mathf.Clamp(f.x, -capL, capL); f.y = Mathf.Clamp(f.y, -capS, capS);
                rb.AddForceAtPosition(n * N + fw * f.x + lat * f.y, cp);

                w.slide = Mathf.Clamp01(Mathf.Max(lockAmt, Mathf.Abs(sn) / 0.25f)) * Mathf.Clamp01(sp / 0.5f);
                w.spin = Mathf.Lerp(vl / r, 0f, lockAmt);
            }

            // 前梃子の衆：前を曲がる側へ押す。後梃子：尻を外へ振る
            Vector3 right = q * Vector3.right;
            float maeSide = (maeR - maeL) * s.maePush;
            if (Mathf.Abs(maeSide) > 1f) rb.AddForceAtPosition(right * maeSide, rb.position + q * new Vector3(0, 0.9f, s.maePushZ));
            if (Mathf.Abs(rearTurn) > 0.01f) rb.AddForceAtPosition(right * (rearTurn * s.rearPush), rb.position + q * new Vector3(0, 0.9f, s.rearArm));
            if (brakeAll > 0.01f)
            {
                float vf = Vector3.Dot(rb.linearVelocity, fwdB);
                rb.AddForceAtPosition(-fwdB * Mathf.Clamp(vf / 0.3f, -1f, 1f) * s.rearBrake * brakeAll, rb.position + q * new Vector3(0, 0.9f, s.rearArm));
            }

            Vector3 vel = rb.linearVelocity;
            accel = Quaternion.Inverse(q) * ((vel - lastVel) / dt);
            lastVel = vel;
            scrape *= Mathf.Exp(-8f * dt);
            for (int i = 0; i < 4; i++) wheels[i].angle += wheels[i].spin * dt;
        }

        void OnCollisionEnter(Collision c) => Hit(c, true);
        void OnCollisionStay(Collision c) => Hit(c, false);
        void Hit(Collision c, bool enter)
        {
            if (c.collider.gameObject.layer == 8 && c.collider is BoxCollider && c.collider.name == "ground") return;
            float dv = c.impulse.magnitude / rb.mass;
            if (!enter) { scrape = Mathf.Max(scrape, Mathf.Clamp01(c.relativeVelocity.magnitude / 3f)); if (dv < 0.9f) return; }
            if (dv < 0.35f || Time.time - lastImpactTime < 0.5f) return;
            lastImpactTime = Time.time;
            OnImpact?.Invoke(dv, c.contactCount > 0 ? c.GetContact(0).point : transform.position, c.collider);
        }
    }
}
