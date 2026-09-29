using System;
using UnityEngine;

namespace Kishiwada
{
    [Serializable]
    public class RopeSpec
    {
        public int nodes = 48;               // 綱 1 本の点の数（1m おき）
        public float segment = 1.0f;
        public int firstPuller = 4;          // 地車から何本目の点から人が持つか
        public float lane = 1.25f;           // 道の中心から綱までの横の距離
        public float compliance = 4e-7f;     // 綱の伸び（m/N、1m あたり）
        public float ropeNodeMass = 2f;
        [Header("曳き手（1 点 ≒ 3 人）")]
        public float pullerMass = 210f;
        public float pullerForce = 720f;     // 立ち止まって引く時の力
        public float runExtra = 2.0f;        // 目標の速さに上乗せで出せる速さ（これで力がゼロになる）
        public float tau = 0.25f;            // 速さを合わせる時定数
        public float handHeight = 0.95f;
        public float lookAhead = 3.5f;
        public int substeps = 8;
    }

    // 2 本の綱と曳き手。XPBD（小さな刻みで 1 回ずつ解く）で、綱の端は地車の剛体につながる
    public sealed class RopeSim
    {
        public readonly RopeSpec spec;
        public readonly int N;
        public readonly Vector3[] x, v, prevX;
        readonly Vector3[] p, corr; // corr：その刻みで拘束が動かした量
        readonly float[] w;         // 逆質量
        public readonly bool[] puller;
        public readonly float[] lane;
        readonly int[] hint;
        public readonly float[] segTension; // 各点から地車側の区間の張力（N）
        public readonly float[] tension = new float[2]; // 結び目の張力（N、1 ステップ平均）
        public readonly Vector3[] anchorWorld = new Vector3[2];
        readonly Vector2[] driveDir, driveTarget;
        public float vRun;          // 曳き手が走ろうとする速さ（m/s）
        public float crowdSpeed;    // 綱先の実際の速さ
        public WallGrid walls;
        Path path => Course.Smooth;
        static readonly Vector3 G = new Vector3(0, -9.81f, 0);

        public RopeSim(RopeSpec spec)
        {
            this.spec = spec; N = spec.nodes;
            int n = N * 2;
            x = new Vector3[n]; v = new Vector3[n]; p = new Vector3[n]; corr = new Vector3[n]; prevX = new Vector3[n];
            w = new float[n]; puller = new bool[n]; lane = new float[n]; hint = new int[n];
            segTension = new float[n];
            driveDir = new Vector2[n]; driveTarget = new Vector2[n];
            var r = new Rng(77);
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    puller[k] = i >= spec.firstPuller;
                    w[k] = 1f / (puller[k] ? spec.pullerMass : spec.ropeNodeMass);
                    lane[k] = (j == 0 ? 1f : -1f) * spec.lane + r.Range(-0.2f, 0.2f);
                }
        }

        // 地車の前から道なりに綱を並べる
        public void Reset(DanjiriBody body)
        {
            var q = body.rb.rotation;
            for (int j = 0; j < 2; j++)
            {
                Vector3 a = body.rb.position + q * body.model.ropeAnchors[j];
                anchorWorld[j] = a;
                int h = -1;
                float s0 = path.Project(new Vector2(a.x, a.z), ref h);
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    float s = s0 + (i + 1) * spec.segment * 0.97f;
                    Vector2 c = path.PointAt(s), lf = KMath.Left(path.TangentAt(s));
                    float side = Mathf.Lerp((j == 0 ? 1f : -1f) * 0.48f, lane[k], Mathf.Clamp01((i + 1) / 5f));
                    Vector2 pos = c + lf * side;
                    x[k] = new Vector3(pos.x, puller[k] ? spec.handHeight : 0.5f, pos.y);
                    v[k] = Vector3.zero; prevX[k] = x[k]; hint[k] = -1; segTension[k] = 0;
                }
                tension[j] = 0;
            }
            crowdSpeed = 0;
        }

        public void Step(DanjiriBody body, float dt)
        {
            var rb = body.rb;
            int S = Mathf.Max(1, spec.substeps);
            float h = dt / S, h2 = h * h, alpha = spec.compliance / h2;
            hCur = h; subCount = S; steps++;
            float invM = 1f / rb.mass;
            Quaternion Qi = rb.rotation * rb.inertiaTensorRotation; Vector3 I = rb.inertiaTensor;
            Vector3 InvI(Vector3 vec) { Vector3 l = Quaternion.Inverse(Qi) * vec; l = new Vector3(l.x / I.x, l.y / I.y, l.z / I.z); return Qi * l; }
            Vector3 V = rb.linearVelocity, Wv = rb.angularVelocity, X = rb.worldCenterOfMass;
            var r = new Vector3[2];
            for (int j = 0; j < 2; j++) r[j] = rb.position + rb.rotation * body.model.ropeAnchors[j] - X;
            Vector3 J = Vector3.zero, L = Vector3.zero;
            tension[0] = tension[1] = 0f;
            for (int k = 0; k < 2 * N; k++) { segTension[k] = 0f; prevX[k] = x[k]; }

            // 曳き手の行き先（1 ステップに 1 回）
            for (int k = 0; k < 2 * N; k++)
            {
                if (!puller[k]) continue;
                Vector2 pos = new Vector2(x[k].x, x[k].z);
                int hh = hint[k];
                float s = path.Project(pos, ref hh); hint[k] = hh;
                float sa = s + spec.lookAhead;
                Vector2 tgt = path.PointAt(sa) + KMath.Left(path.TangentAt(sa)) * lane[k];
                Vector2 d = tgt - pos; float dl = d.magnitude;
                driveDir[k] = dl > 1e-4f ? d / dl : path.TangentAt(s);
            }

            for (int sub = 0; sub < S; sub++)
            {
                // 地車（影の状態）を進める
                X += V * h;
                for (int j = 0; j < 2; j++) r[j] += Vector3.Cross(Wv, r[j]) * h;
                // 点を予測
                for (int k = 0; k < 2 * N; k++)
                {
                    if (puller[k])
                    {
                        Vector2 dir = driveDir[k];
                        Vector2 vel = new Vector2(v[k].x, v[k].z);
                        Vector2 F = (dir * vRun - vel) * (spec.pullerMass / spec.tau);
                        float along = Vector2.Dot(vel, dir);
                        float cap = spec.pullerForce * Mathf.Max(0.04f, 1f - Mathf.Max(0f, along) / (vRun + spec.runExtra));
                        float fa = Vector2.Dot(F, dir); Vector2 fp = F - dir * fa;
                        fa = Mathf.Clamp(fa, -spec.pullerForce * 1.3f, cap);
                        fp = Vector2.ClampMagnitude(fp, spec.pullerForce * 0.8f);
                        F = dir * fa + fp;
                        if (k == N - 1) { dbgF = F; dbgW = w[k]; dbgH = h; dbgCap = cap; }
                        vel += F * (w[k] * h);
                        v[k] = new Vector3(vel.x, 0f, vel.y);
                        p[k] = x[k] + v[k] * h; p[k].y = spec.handHeight;
                    }
                    else
                    {
                        v[k] += G * h; v[k] *= 0.9995f;
                        p[k] = x[k] + v[k] * h;
                    }
                }
                // 拘束（向きを毎回入れ替える）
                bool fwd = (sub & 1) == 0;
                for (int j = 0; j < 2; j++)
                {
                    int b0 = j * N;
                    if (fwd)
                    {
                        Anchor(j, b0);
                        for (int i = 0; i < N - 1; i++) Dist(b0 + i, b0 + i + 1);
                    }
                    else
                    {
                        for (int i = N - 2; i >= 0; i--) Dist(b0 + i, b0 + i + 1);
                        Anchor(j, b0);
                    }
                }
                // 地面：めり込みを戻し、摩擦は押し返した量×μ まで（張った綱は浮くので摩擦はかからない）
                for (int k = 0; k < 2 * N; k++)
                {
                    if (puller[k] || p[k].y >= 0.035f) continue;
                    float pen = 0.035f - p[k].y; p[k].y = 0.035f; corr[k].y += pen;
                    Vector3 d = v[k] * h + corr[k]; d.y = 0f; float dl = d.magnitude, lim = 0.6f * pen;
                    Vector3 f = dl <= lim ? d : dl > 1e-9f ? d * (lim / dl) : Vector3.zero;
                    p[k] -= f; corr[k] -= f;
                }
                // 速度を戻す：(p-x)/h は原点から 200m 離れると float の桁落ちで小さな加速が消えるので、
                // 予測の速度に拘束で動かした分（corr）だけを足す
                for (int k = 0; k < 2 * N; k++)
                {
                    v[k] += corr[k] / h; corr[k] = Vector3.zero;
                    if (puller[k]) v[k].y = 0f;
                    x[k] = p[k];
                }

                void Anchor(int j, int b)
                {
                    Vector3 A = X + r[j];
                    Vector3 d = p[b] - A; float len = d.magnitude;
                    if (len <= spec.segment || len < 1e-6f) return;
                    Vector3 n = d / len; float C = len - spec.segment;
                    Vector3 rn = Vector3.Cross(r[j], n);
                    float wA = invM + Vector3.Dot(rn, InvI(rn));
                    float wb = W(b, n);
                    float dlam = -C / (wA + wb + alpha);
                    Vector3 db = WVec(b, n) * dlam; p[b] += db; corr[b] += db;
                    Vector3 P = -n * dlam; // 地車への位置の衝撃（綱の方へ）
                    V += P * (invM / h);
                    Wv += InvI(Vector3.Cross(r[j], P)) / h;
                    X += P * invM;
                    J += P / h; L += Vector3.Cross(r[j], P) / h;
                    float f = -dlam / h2; tension[j] += f / S; segTension[b] += f / S;
                }
            }
            for (int j = 0; j < 2; j++) anchorWorld[j] = X + r[j];

            rb.AddForce(J, ForceMode.Impulse);
            rb.AddTorque(L, ForceMode.Impulse);

            // 家の壁から外へ（曳き手だけ、1 ステップに 1 回）
            lastPush = 0f;
            if (walls != null)
                for (int k = 0; k < 2 * N; k++)
                {
                    if (!puller[k]) continue;
                    Vector2 q = new Vector2(x[k].x, x[k].z);
                    Vector2 push = walls.Push(ref q, 0.45f);
                    if (push.sqrMagnitude > 0) { x[k].x = q.x; x[k].z = q.y; lastPush += push.magnitude; }
                }
            // 綱先の速さ
            int tip = N - 1; crowdSpeed = 0.5f * (new Vector2(v[tip].x, v[tip].z).magnitude + new Vector2(v[N + tip].x, v[N + tip].z).magnitude);
        }

        float W(int k, Vector3 n) => puller[k] ? w[k] * (n.x * n.x + n.z * n.z) : w[k];
        Vector3 WVec(int k, Vector3 n) => puller[k] ? new Vector3(n.x, 0f, n.z) * w[k] : n * w[k];

        void Dist(int a, int b)
        {
            Vector3 d = p[b] - p[a]; float len = d.magnitude;
            if (len <= spec.segment || len < 1e-6f) return; // 綱は押せない（たるむ）
            Vector3 n = d / len; float C = len - spec.segment;
            float wa = W(a, n), wb = W(b, n);
            float hh = hCur * hCur;
            float dlam = -C / (wa + wb + spec.compliance / hh);
            Vector3 da = WVec(a, n) * dlam, db = WVec(b, n) * dlam;
            p[a] -= da; corr[a] -= da;
            p[b] += db; corr[b] += db;
            segTension[b] += -dlam / hh / subCount;
        }
        float hCur = 1f / 960f; int subCount = 8;

        // 調べもの用：いくつかの点の位置・速さ・向かう向き・壁に押された量
        public float lastPush;
        Vector2 dbgF; float dbgW, dbgH, dbgCap; public int steps;
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, " steps={0} F=({1:0.0},{2:0.0}) w={3:0.00000} h={4:0.000000} cap={5:0.0}", steps, dbgF.x, dbgF.y, dbgW, dbgH, dbgCap);
            steps = 0;
            foreach (int i in new[] { 0, spec.firstPuller, N / 2, N - 1 })
            {
                int k = i; var d = driveDir[k];
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, " [{0}] p=({1:0.0},{2:0.00},{3:0.0}) v=({4:0.00},{5:0.00},{6:0.00}) dir=({7:0.00},{8:0.00}) T={9:0}", i, x[k].x, x[k].y, x[k].z, v[k].x, v[k].y, v[k].z, d.x, d.y, segTension[k]);
            }
            sb.AppendFormat(" vRun={0:0.0} push={1:0.000}", vRun, lastPush);
            return sb.ToString();
        }

        // 表示用：綱の中心線上の点（地車の結び目から先端まで）
        public Vector3 Node(int rope, int i) => x[rope * N + i];
        public float NodeTension(int rope, int i) => segTension[rope * N + i];
    }
}
