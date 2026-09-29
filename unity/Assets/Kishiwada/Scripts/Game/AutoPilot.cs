using UnityEngine;

namespace Kishiwada
{
    // 自動で曳く：拍に合わせて叩き、道筋の先を見て前梃子・後梃子を入れる。タイトル画面のデモと物理の試験に使う
    public sealed class AutoPilot
    {
        public float targetTempo = 0.85f;
        public float aggression = 1f;   // 前梃子の効かせ方
        public bool jumpInCorners = true;
        public bool steer = true;       // false：梃子を使わない（試験用）
        float tapCool;
        int hint = -1;
        float jumpCool;

        public void Drive(GameInput inp, DanjiriBody body, Narimono audio, float tempo, float dt)
        {
            inp.tap = false; inp.jump = false;
            // 叩く（聞こえている拍に合わせる）
            tapCool -= dt;
            if (tempo < targetTempo && audio.OffBeat() < 0.07f && tapCool <= 0f) { inp.tap = true; tapCool = 60f / audio.Bpm * 0.6f; }
            // 向き：道筋の先（速さに応じて遠く）を狙う
            Vector2 pos = body.PosXZ;
            float s = Course.Smooth.Project(pos, ref hint);
            float speed = body.Speed;
            float look = 7f + speed * 1.3f;
            Vector2 tgt = Course.Smooth.PointAt(s + look);
            float want = Mathf.Atan2(tgt.x - pos.x, tgt.y - pos.y);
            float err = KMath.WrapAngle(want - body.Yaw); // 正なら右へ
            float yr = body.YawRate;
            float cmd = Mathf.Clamp((-err * 2.6f + yr * 0.55f) * aggression, -1f, 1f); // 正で左へ
            if (Mathf.Abs(cmd) < 0.08f || !steer) cmd = 0f;
            inp.maeL = Mathf.Max(0f, cmd); inp.maeR = Mathf.Max(0f, -cmd);
            inp.rear = Mathf.Clamp(cmd * 0.9f, -1f, 1f); inp.rearManual = true;
            jumpCool -= dt;
            if (jumpInCorners && Mathf.Abs(yr) > 0.35f && jumpCool <= 0f) { inp.jump = true; jumpCool = 1.2f; }
        }
    }
}
