using System.Collections.Generic;
using UnityEngine;

namespace Kishiwada
{
    // リプレイ：曳行中の地車・綱・梃子を 30 回/秒で記録し、あとで角を中心に見せる
    public sealed class Replay
    {
        struct Frame
        {
            public float t, speed, maeL, maeR, rear, tempo, jumpAt, s;
            public Vector3 pos, vel, accel; public Quaternion rot;
            public float w0, w1, w2, w3;
        }
        readonly List<Frame> frames = new List<Frame>(4096);
        readonly List<Vector3> nodes = new List<Vector3>(4096 * 98);
        int per; float nextT;
        public int Count => frames.Count;
        public float Start => frames.Count > 0 ? frames[0].t : 0f;
        public float End => frames.Count > 0 ? frames[frames.Count - 1].t : 0f;

        public void Clear() { frames.Clear(); nodes.Clear(); nextT = 0f; }

        public void Record(float t, DanjiriBody body, RopeSim rope, float tempo, float jumpAt, float s)
        {
            if (t < nextT) return;
            nextT = t + 1f / 30f;
            var rb = body.rb;
            frames.Add(new Frame
            {
                t = t, pos = rb.position, rot = rb.rotation, vel = rb.linearVelocity, accel = body.accel, speed = rb.linearVelocity.magnitude,
                maeL = body.maeL, maeR = body.maeR, rear = body.rearTurn, tempo = tempo, jumpAt = jumpAt, s = s,
                w0 = body.wheels[0].angle, w1 = body.wheels[1].angle, w2 = body.wheels[2].angle, w3 = body.wheels[3].angle,
            });
            per = rope.N * 2 + 2;
            for (int k = 0; k < rope.N * 2; k++) nodes.Add(rope.x[k]);
            nodes.Add(rope.anchorWorld[0]); nodes.Add(rope.anchorWorld[1]);
        }

        // 時刻 t の姿を地車・綱に書き戻す。戻り値：その時の速さ・コース上の位置など
        public void Apply(float t, DanjiriBody body, RopeSim rope, out float speed, out float tempo, out float jumpAt, out float s, out Vector3 vel)
        {
            int i = 0;
            int lo = 0, hi = frames.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (frames[mid].t <= t) lo = mid; else hi = mid - 1; }
            i = lo;
            int j = Mathf.Min(frames.Count - 1, i + 1);
            var a = frames[i]; var b = frames[j];
            float u = b.t > a.t ? Mathf.Clamp01((t - a.t) / (b.t - a.t)) : 0f;
            var rb = body.rb;
            Vector3 p = Vector3.Lerp(a.pos, b.pos, u); Quaternion q = Quaternion.Slerp(a.rot, b.rot, u);
            rb.position = p; rb.rotation = q; body.transform.SetPositionAndRotation(p, q);
            body.maeL = Mathf.Lerp(a.maeL, b.maeL, u); body.maeR = Mathf.Lerp(a.maeR, b.maeR, u); body.rearTurn = Mathf.Lerp(a.rear, b.rear, u);
            body.accel = Vector3.Lerp(a.accel, b.accel, u);
            body.wheels[0].angle = Mathf.Lerp(a.w0, b.w0, u); body.wheels[1].angle = Mathf.Lerp(a.w1, b.w1, u);
            body.wheels[2].angle = Mathf.Lerp(a.w2, b.w2, u); body.wheels[3].angle = Mathf.Lerp(a.w3, b.w3, u);
            int n = rope.N * 2;
            for (int k = 0; k < n; k++) rope.x[k] = Vector3.Lerp(nodes[i * per + k], nodes[j * per + k], u);
            rope.anchorWorld[0] = Vector3.Lerp(nodes[i * per + n], nodes[j * per + n], u);
            rope.anchorWorld[1] = Vector3.Lerp(nodes[i * per + n + 1], nodes[j * per + n + 1], u);
            speed = Mathf.Lerp(a.speed, b.speed, u); tempo = Mathf.Lerp(a.tempo, b.tempo, u); jumpAt = b.jumpAt; s = Mathf.Lerp(a.s, b.s, u);
            vel = Vector3.Lerp(a.vel, b.vel, u);
        }

        // 見せ場：一つ目の角の少し手前から
        public float FirstHighlight()
        {
            float target = Course.Corners[0].s - 70f;
            foreach (var f in frames) if (f.s >= target) return f.t;
            return Start;
        }
    }
}
