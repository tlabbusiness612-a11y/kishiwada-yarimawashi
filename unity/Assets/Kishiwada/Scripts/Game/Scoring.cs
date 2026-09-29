using System.Collections.Generic;
using UnityEngine;

namespace Kishiwada
{
    // 角ごとの採点：入りの速さ・速さの保ち・キレ（向きを変える速さ）・接触・跳
    public sealed class Scoring
    {
        public sealed class Run { public int k; public float entry, min, sum, t, h0, tA = -1, tB = -1, maxYaw; public int hits, pen; public bool jump; }
        public sealed class Result { public string name, grade; public int pts, entry, hits; public bool jump; public float turn, keep, maxYaw; }

        public readonly Result[] results = new Result[2];
        public Run cur;
        public int score, timeBonus;
        public float time, s, maxS;
        public int hits;
        public System.Action<Result> onCorner;

        public void Reset() { results[0] = results[1] = null; cur = null; score = 0; timeBonus = 0; time = 0; s = 0; maxS = 0; hits = 0; }

        public void Track(float dt, Vector2 pos, float speed, float yaw, float yawRate, bool running)
        {
            var pr = Course.Project(pos);
            s = pr.s; maxS = Mathf.Max(maxS, pr.s);
            if (running) time += dt;
            for (int k = 0; k < Course.Corners.Length; k++)
            {
                if (results[k] != null) continue;
                var cn = Course.Corners[k];
                if (cur == null && pr.s > cn.s - 26f && pr.s < cn.s) cur = new Run { k = k, entry = speed, min = speed, h0 = Course.Segs[k].yaw };
                if (cur != null && cur.k == k)
                {
                    cur.min = Mathf.Min(cur.min, speed); cur.sum += speed * dt; cur.t += dt;
                    cur.maxYaw = Mathf.Max(cur.maxYaw, Mathf.Abs(yawRate));
                    float turned = Mathf.Abs(KMath.WrapAngle(yaw - cur.h0));
                    if (cur.tA < 0 && turned > 0.26f) cur.tA = cur.t;
                    if (cur.tB < 0 && turned > 1.2f) cur.tB = cur.t;
                    if (pr.s > cn.s + (k > 0 ? 14f : 22f) && pr.seg == k + 1) FinishCorner();
                    else if (cur.t > 25f) FinishCorner();
                }
            }
        }

        void FinishCorner()
        {
            var c = cur;
            float entry = c.entry * 3.6f, avg = c.sum / Mathf.Max(c.t, 0.01f) * 3.6f, keep = c.min / Mathf.Max(c.entry, 0.5f);
            float turnT = c.tA >= 0 && c.tB >= 0 ? c.tB - c.tA : 4f, kire = Mathf.Clamp01((3.0f - turnT) / 2.1f);
            int pts = Mathf.Max(0, Mathf.RoundToInt(entry * 7 + avg * 8 + Mathf.Min(1f, keep) * 120 + kire * 170 + (c.hits > 0 ? 0 : 90) + (c.jump ? 110 : 0) - c.pen));
            string g = pts >= 800 ? "天晴れ" : pts >= 640 ? "見事" : pts >= 460 ? "ええで" : pts >= 280 ? "まずまず" : c.hits > 0 ? "当てた" : "遅い";
            var r = new Result { name = Course.Corners[c.k].name, grade = g, pts = pts, entry = Mathf.RoundToInt(entry), hits = c.hits, jump = c.jump, turn = turnT, keep = keep, maxYaw = c.maxYaw };
            results[c.k] = r; score += pts; cur = null;
            onCorner?.Invoke(r);
        }

        public void Hit(float dv)
        {
            hits++;
            if (cur != null) { cur.hits++; cur.pen += Mathf.RoundToInt(40 + dv * 30f); }
            else score = Mathf.Max(0, score - Mathf.RoundToInt(dv * 10f));
        }
        public void Jump(float yawRate) { if (cur != null && Mathf.Abs(yawRate) > 0.2f) cur.jump = true; }
        public bool Finished => maxS > Course.FinishS;
        public void AddTimeBonus() { timeBonus = Mathf.Max(0, Mathf.RoundToInt((95f - time) * 6f)); score += timeBonus; }

        public static string PlaceName(float s)
        {
            var c = Course.Corners;
            if (Mathf.Abs(s - c[0].s) < 28f) return "カンカン場（岸和田港交差点）";
            if (Mathf.Abs(s - c[1].s) < 14f) return "紀州街道の辻";
            if (s < c[0].s) return "大阪臨海線";
            if (s < c[1].s) return "北町の路地";
            return "紀州街道";
        }
    }
}
