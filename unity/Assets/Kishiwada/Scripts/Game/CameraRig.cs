using UnityEngine;

namespace Kishiwada
{
    // カメラ：追走・大工方・桟敷・空撮、タイトルの周回、デモの自動切り替え
    public sealed class CameraRig
    {
        public enum Mode { Chase = 0, Daiku = 1, Stand = 2, Aerial = 3, Orbit = 10, Director = 11, Replay = 12 }
        public float courseS;   // リプレイの画作り用：コース上の位置
        Mode lastReplayShot = Mode.Orbit;
        public static readonly string[] Names = { "追走", "大工方", "桟敷", "空撮" };
        public Mode mode = Mode.Chase;
        public readonly Camera cam;
        readonly Transform t;
        Vector3 pos, look, dir = Vector3.forward;
        bool init;
        float shake, directorT; int directorShot;
        public float fovBase = 56f;
        const int WorldMask = 1 << 8;

        public CameraRig(Camera cam) { this.cam = cam; t = cam.transform; }
        public void Snap() => init = false;
        public void Shake(float a) => shake = Mathf.Max(shake, a);
        public void Next() { mode = (Mode)(((int)mode + 1) % 4); init = false; }

        public void Update(float dt, float time, DanjiriBody body, DanjiriView view, Town town, Vector3 vel)
        {
            var rb = body.rb;
            Vector3 p = rb.position, f = body.Forward; f.y = 0; f.Normalize();
            Vector3 v = vel; v.y = 0;
            float spd = v.magnitude;
            Vector3 d = spd > 1.5f ? v / spd : f;
            dir = Vector3.Slerp(dir, d, KMath.Damp(2f, dt)); dir.y = 0; dir.Normalize();
            Vector3 want, lookW; bool snap = false; float fov = fovBase;
            Mode m = mode;
            bool firstPerson = m == Mode.Daiku;
            if (view.daikuFront.smr.enabled == firstPerson) { view.daikuFront.smr.enabled = !firstPerson; view.daikuFront.prop.gameObject.SetActive(!firstPerson); }
            if (m == Mode.Director)
            {
                directorT += dt;
                if (directorT > 7f) { directorT = 0; directorShot = (directorShot + 1) % 4; init = false; }
                m = directorShot switch { 0 => Mode.Chase, 1 => Mode.Aerial, 2 => Mode.Stand, _ => Mode.Orbit };
            }
            else if (m == Mode.Replay)
            {
                // 直線は空から、角は桟敷から、路地は低い追走
                float c0 = Course.Corners[0].s, c1 = Course.Corners[1].s, s = courseS;
                m = Mathf.Abs(s - c0) <= 45f || Mathf.Abs(s - c1) <= 25f ? Mode.Stand : s > c0 && s < c1 ? Mode.Chase : Mode.Aerial;
                if (m != lastReplayShot) { lastReplayShot = m; init = false; }
            }
            switch (m)
            {
                case Mode.Orbit:
                    {
                        float a = time * 0.16f;
                        want = p + new Vector3(Mathf.Cos(a) * 8.5f, 3.1f, Mathf.Sin(a) * 8.5f);
                        lookW = p + Vector3.up * 2.2f;
                        want = Collide(p + Vector3.up * 2.4f, want);
                        break;
                    }
                case Mode.Daiku:
                    {
                        // 大工方の目から（本人の体は隠す）
                        var head = view.daikuFront.b[Bone.Head];
                        want = head.position + Vector3.up * 0.08f + f * 0.12f;
                        lookW = want + f * 20f + Vector3.down * 3.2f;
                        snap = true; fov = 66f;
                        break;
                    }
                case Mode.Stand:
                    {
                        Vector3 best = Vector3.zero; float bd = 1e9f;
                        foreach (var s in town.spots) { float dd = (new Vector2(s.x, s.z) - new Vector2(p.x, p.z)).magnitude; if (dd < bd) { bd = dd; best = s; } }
                        if (bd < 80f) { want = best; snap = true; fov = Mathf.Lerp(24f, 48f, Mathf.Clamp01(bd / 60f)); }
                        else { want = Collide(p + Vector3.up * 2f, p + new Vector3(f.z, 0, -f.x) * 6f + f * 15f + Vector3.up * 2.8f); }
                        lookW = p + Vector3.up * 2.2f;
                        break;
                    }
                case Mode.Aerial:
                    want = p - dir * 16f + Vector3.up * 21f;
                    lookW = p + dir * 9f;
                    fov = 50f;
                    break;
                default:
                    want = p - dir * 11.5f + Vector3.up * 6.4f;
                    lookW = p + f * 4f + Vector3.up * 1.7f;
                    want = Collide(p + Vector3.up * 3f, want);
                    fov = fovBase + Mathf.Clamp(spd, 0, 8f) * 0.8f;
                    break;
            }
            if (!init || snap) { pos = want; look = lookW; init = true; }
            else { pos = Vector3.Lerp(pos, want, KMath.Damp(6f, dt)); look = Vector3.Lerp(look, lookW, KMath.Damp(9f, dt)); }
            Vector3 sp = pos;
            if (shake > 0.01f) { sp += new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) * shake; shake *= Mathf.Exp(-5f * dt); }
            t.position = sp;
            Vector3 fw = look - sp; if (fw.sqrMagnitude < 1e-4f) fw = Vector3.forward;
            Vector3 up = Vector3.up;
            if (m == Mode.Daiku) up = Quaternion.AngleAxis(-view.daikuFront.b[Bone.Hips].localEulerAngles.z * 0.3f, fw) * Vector3.up;
            t.rotation = Quaternion.LookRotation(fw, up);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, AspectFov(fov), KMath.Damp(4f, dt));
        }

        // 縦長の画面では横の見える幅を保つ
        float AspectFov(float fov)
        {
            float aspect = cam.aspect;
            if (aspect >= 1.4f) return fov;
            float hfov = 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1.6f);
            return Mathf.Min(90f, 2f * Mathf.Atan(Mathf.Tan(hfov * 0.5f) / aspect) * Mathf.Rad2Deg);
        }

        static Vector3 Collide(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; float len = d.magnitude;
            if (len < 0.01f) return to;
            if (Physics.SphereCast(from, 0.5f, d / len, out var hit, len, WorldMask, QueryTriggerInteraction.Ignore))
                return from + d / len * Mathf.Max(1.5f, hit.distance - 0.2f);
            return to;
        }
    }
}
