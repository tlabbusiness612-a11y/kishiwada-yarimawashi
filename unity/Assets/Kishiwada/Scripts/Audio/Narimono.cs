using System.Threading;
using UnityEngine;

namespace Kishiwada
{
    // 鳴物（太鼓・鉦・笛）と掛け声・歓声・車輪の音を、音声スレッドでその場で合成する（録音素材なし）。
    // AudioListener と同じ GameObject に付ける（最後のミックスに足す）
    public sealed class Narimono : MonoBehaviour
    {
        // ---- 本体側から設定する値 ----
        public float tempo;       // 0..1
        public float excite;      // 沿道の盛り上がり 0..1
        public float speed;       // 地車の速さ m/s
        public float slide;       // 車輪の滑り 0..1
        public float scrape;      // 家にこすれる 0..1
        public float distance = 10f; // カメラから地車まで
        public float pan;         // -1..1（左右）
        public bool playing;
        public float master = 0.9f;

        int chantReq, chantDone, whistleReq, whistleDone, cheerReq, cheerDone, thudReq, thudDone;
        float thudPower;

        int sr = 48000;
        double beatPos, beatPosDsp; // 拍（四分音符）の位置と、それを計算した時刻
        int lastStep = -1;
        float latency;
        uint rng = 12345;

        static readonly float[] FUE = { 880, 987.8f, 1174.7f, 987.8f, 880, 784, 659.3f, 784, 880, 880, 987.8f, 880, 784, 659.3f, 587.3f, 659.3f };

        void Awake()
        {
            sr = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int len, out int num);
            latency = len * Mathf.Max(2, num) / (float)sr;
        }

        public float Bpm => 86f + tempo * 116f;
        public double BeatNow => Volatile.Read(ref beatPos) + (AudioSettings.dspTime - Volatile.Read(ref beatPosDsp)) * Bpm / 60.0;
        // 叩いた瞬間と、聞こえている拍とのずれ（拍の長さに対する割合 0..0.5）
        public float OffBeat()
        {
            if (!playing) return 0.5f;
            double heard = BeatNow - latency * Bpm / 60.0;
            double f = heard - System.Math.Floor(heard);
            return (float)System.Math.Min(f, 1.0 - f);
        }

        public void Chant() => Interlocked.Increment(ref chantReq);
        public void Whistle() => Interlocked.Increment(ref whistleReq);
        public void Cheer() => Interlocked.Increment(ref cheerReq);
        public void Thud(float power) { thudPower = power; Interlocked.Increment(ref thudReq); }

        // ---------- 声の置き場（音声スレッドだけが触る） ----------
        struct Hit { public float t, v, f0; public bool on; }
        readonly Hit[] taiko = new Hit[8], kane = new Hit[8], thuds = new Hit[4];
        struct ChantV { public float t; public bool on; public float b0, b1, b2, b3, b4, pan; }
        readonly ChantV[] chants = new ChantV[4];
        float chantCool;
        float fueF, fueTarget, fueAmp, fueNoteT, fuePhase, vibPhase;
        Biquad fueBreath, crowdBp, crowdBp2, rumbleLp, scrapeBp, chantF1, chantF2, cheerBp;
        float whistleT = -1f, cheerT = -1f, cheerGain, crowdGain, rumbleGain, scrapeGain, brown;
        readonly float[] chantPh = new float[5];

        struct Biquad
        {
            float a0, a1, a2, b1, b2, z1, z2;
            public void BandPass(float f, float q, float sr)
            {
                float w = 2f * Mathf.PI * f / sr, al = Mathf.Sin(w) / (2f * q), c = Mathf.Cos(w), n = 1f + al;
                a0 = al / n; a1 = 0; a2 = -al / n; b1 = -2f * c / n; b2 = (1f - al) / n;
            }
            public void LowPass(float f, float q, float sr)
            {
                float w = 2f * Mathf.PI * f / sr, al = Mathf.Sin(w) / (2f * q), c = Mathf.Cos(w), n = 1f + al;
                a0 = (1f - c) * 0.5f / n; a1 = (1f - c) / n; a2 = a0; b1 = -2f * c / n; b2 = (1f - al) / n;
            }
            public float Run(float x) { float y = a0 * x + z1; z1 = a1 * x - b1 * y + z2; z2 = a2 * x - b2 * y; return y; }
        }

        float Noise() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng / 4294967296f) * 2f - 1f; }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float dt = 1f / sr;
            float bpm = Bpm, bps = bpm / 60f;
            float gDist = 1f / (1f + Mathf.Max(0f, distance - 6f) / 22f);
            float panL = Mathf.Clamp01(1f - pan) * 0.5f + 0.5f, panR = Mathf.Clamp01(1f + pan) * 0.5f + 0.5f;
            fueBreath.BandPass(Mathf.Max(200f, fueF), 3f, sr);
            crowdBp.BandPass(650f, 0.6f, sr); crowdBp2.BandPass(1500f, 0.8f, sr);
            rumbleLp.LowPass(90f + speed * 12f, 0.9f, sr);
            scrapeBp.BandPass(820f, 1.4f, sr);
            cheerBp.BandPass(900f, 0.7f, sr);
            chantF1.BandPass(520f, 5f, sr); chantF2.BandPass(880f, 6f, sr);
            // 要求を取り込む
            while (chantDone < Volatile.Read(ref chantReq)) { chantDone++; if (chantCool <= 0f) { StartChant(); chantCool = 0.5f; } }
            while (whistleDone < Volatile.Read(ref whistleReq)) { whistleDone++; whistleT = 0f; }
            while (cheerDone < Volatile.Read(ref cheerReq)) { cheerDone++; cheerT = 0f; }
            while (thudDone < Volatile.Read(ref thudReq)) { thudDone++; Spawn(thuds, 0f, Mathf.Clamp01(0.4f + thudPower)); }

            int frames = data.Length / channels;
            for (int n = 0; n < frames; n++)
            {
                float l = 0f, r = 0f;
                if (playing)
                {
                    beatPos += bps * dt;
                    int step = (int)(beatPos * 2.0);
                    if (step != lastStep) { lastStep = step; OnStep(step); }
                }
                // 太鼓
                float drums = 0f;
                for (int i = 0; i < taiko.Length; i++)
                {
                    ref var h = ref taiko[i]; if (!h.on) continue;
                    float t = h.t;
                    float f = 52f + 98f * Mathf.Exp(-t / 0.05f);
                    float env = Mathf.Exp(-t / 0.3f) * Mathf.Min(1f, t / 0.003f);
                    float ph = 2f * Mathf.PI * (52f * t + 98f * 0.05f * (1f - Mathf.Exp(-t / 0.05f)));
                    float body = Mathf.Sin(ph) + 0.35f * Mathf.Sin(ph * 1.59f) * Mathf.Exp(-t / 0.08f) + 0.2f * Mathf.Sin(ph * 2.14f) * Mathf.Exp(-t / 0.05f);
                    float stick = Noise() * Mathf.Exp(-t / 0.012f) * 0.5f;
                    drums += (body * env * 0.8f + stick) * h.v;
                    h.t += dt; if (h.t > 0.9f) h.on = false;
                }
                // 鉦
                float bell = 0f;
                for (int i = 0; i < kane.Length; i++)
                {
                    ref var h = ref kane[i]; if (!h.on) continue;
                    float t = h.t, w = 2f * Mathf.PI * t * h.f0;
                    float s = Mathf.Sin(w) * Mathf.Exp(-t / (0.2f + 0.2f * h.v)) + 0.7f * Mathf.Sin(w * 1.506f) * Mathf.Exp(-t / 0.15f)
                            + 0.5f * Mathf.Sin(w * 2.248f) * Mathf.Exp(-t / 0.1f) + 0.3f * Mathf.Sin(w * 2.91f) * Mathf.Exp(-t / 0.06f);
                    bell += (s * 0.13f + Noise() * Mathf.Exp(-t / 0.004f) * 0.12f) * h.v;
                    h.t += dt; if (h.t > 0.7f) h.on = false;
                }
                // 笛
                float fue = 0f;
                if (fueAmp > 0.0005f || fueTarget > 0f)
                {
                    fueF += (fueTarget - fueF) * Mathf.Min(1f, dt * 40f);
                    vibPhase += dt * 5.5f * 2f * Mathf.PI;
                    fuePhase += (fueF * (1f + 0.008f * Mathf.Sin(vibPhase))) * dt;
                    fuePhase -= Mathf.Floor(fuePhase);
                    float tri = 1f - 4f * Mathf.Abs(fuePhase - 0.5f);
                    float want = playing && tempo > 0.1f ? 0.035f + tempo * 0.045f : 0f;
                    fueNoteT += dt;
                    float art = Mathf.Min(1f, fueNoteT / 0.03f);
                    fueAmp += (want * art - fueAmp) * Mathf.Min(1f, dt * 30f);
                    fue = (tri * 0.8f + Mathf.Sin(fuePhase * 2f * Mathf.PI) * 0.4f) * fueAmp + fueBreath.Run(Noise()) * fueAmp * 0.9f;
                }
                // 掛け声
                // 声の源（のこぎり波の群声）を全部足してから、母音の山（フォルマント）に通す
                float chantSrc = 0f, hissSum = 0f, vowel = 0f, gsum = 0f;
                chantCool -= dt;
                for (int i = 0; i < chants.Length; i++)
                {
                    ref var c = ref chants[i]; if (!c.on) continue;
                    float t = c.t;
                    float g = t < 0.06f ? t / 0.06f : t < 0.3f ? 1f : t < 0.36f ? Mathf.Lerp(1f, 0.25f, (t - 0.3f) / 0.06f) : t < 0.42f ? Mathf.Lerp(0.25f, 1.1f, (t - 0.36f) / 0.06f) : Mathf.Max(0f, 1.1f - (t - 0.42f) / 0.24f);
                    float src = 0f;
                    for (int k = 0; k < 5; k++)
                    {
                        float bf = k == 0 ? c.b0 : k == 1 ? c.b1 : k == 2 ? c.b2 : k == 3 ? c.b3 : c.b4;
                        float pitch = bf * (t < 0.34f ? 1f + 0.12f * t / 0.34f : 1.12f - 0.27f * Mathf.Clamp01((t - 0.34f) / 0.28f));
                        chantPh[k] += pitch * dt; chantPh[k] -= Mathf.Floor(chantPh[k]);
                        src += chantPh[k] * 2f - 1f;
                    }
                    chantSrc += src * 0.2f * g;
                    hissSum += t < 0.08f ? Noise() * 0.3f * (1f - t / 0.08f) * g : 0f;
                    vowel += Mathf.Clamp01((t - 0.3f) / 0.12f) * g; gsum += g; // 母音 o → a
                    c.t += dt; if (c.t > 0.7f) c.on = false;
                }
                vowel = gsum > 0f ? vowel / gsum : 0f;
                float chant = ((chantF1.Run(chantSrc) * (1f - vowel) + chantF2.Run(chantSrc) * vowel) * 2.2f + hissSum) * 0.35f;
                // 沿道のざわめき・歓声
                crowdGain += ((0.015f + excite * 0.22f) - crowdGain) * dt * 3f;
                float nz = Noise();
                float crowd = (crowdBp.Run(nz) * 0.8f + crowdBp2.Run(nz) * 0.4f) * crowdGain;
                if (cheerT >= 0f)
                {
                    float t = cheerT;
                    cheerGain = t < 0.3f ? t / 0.3f : Mathf.Max(0f, 1f - (t - 0.3f) / 2.2f);
                    crowd += cheerBp.Run(Noise()) * cheerGain * 0.9f * (0.8f + 0.2f * Mathf.Sin(t * 23f));
                    cheerT += dt; if (cheerT > 2.6f) cheerT = -1f;
                }
                // 車輪・こすれ
                rumbleGain += (Mathf.Clamp01(speed / 7f) * 0.5f - rumbleGain) * dt * 6f;
                brown = brown * 0.985f + Noise() * 0.06f;
                wheelPh += speed / 0.33f * dt; if (wheelPh > 6.2832f) wheelPh -= 6.2832f; // 車輪のひと回りごとのごとつき
                float thump = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(wheelPh)), 12f);
                float rumble = rumbleLp.Run(brown) * rumbleGain * 2.2f * (1f + thump * 0.6f);
                scrapeGain += (Mathf.Clamp01(slide * 0.7f + scrape) * 0.4f - scrapeGain) * dt * 10f;
                float grind = scrapeBp.Run(Noise() * (0.6f + 0.4f * Noise())) * scrapeGain;
                // ドーン
                float th = 0f;
                for (int i = 0; i < thuds.Length; i++)
                {
                    ref var h = ref thuds[i]; if (!h.on) continue;
                    float t = h.t;
                    float f = 30f + 60f * Mathf.Exp(-t / 0.1f);
                    th += (Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t / 0.35f) + Noise() * Mathf.Exp(-t / 0.08f) * 0.6f) * h.v;
                    h.t += dt; if (h.t > 0.8f) h.on = false;
                }
                // 呼子
                float wh = 0f;
                if (whistleT >= 0f)
                {
                    float f = ((int)(whistleT / 0.05f) % 2 == 0) ? 2350f : 2100f;
                    wh = Mathf.Sin(2f * Mathf.PI * f * whistleT) * (whistleT < 0.5f ? 0.12f : Mathf.Max(0f, 0.12f * (1f - (whistleT - 0.5f) / 0.1f)));
                    whistleT += dt; if (whistleT > 0.6f) whistleT = -1f;
                }
                float nari = (drums + bell + fue) * gDist;
                l = nari * panL + chant * gDist * panL + crowd + rumble * gDist + grind * gDist + th * gDist + wh;
                r = nari * panR + chant * gDist * panR + crowd * 0.95f + rumble * gDist + grind * gDist + th * gDist + wh;
                l = Soft(l * master); r = Soft(r * master);
                for (int ch = 0; ch < channels; ch++) data[n * channels + ch] += ch % 2 == 0 ? l : r;
            }
            Volatile.Write(ref beatPosDsp, AudioSettings.dspTime);
        }
        float wheelPh;
        static float Soft(float x) => x / (1f + Mathf.Abs(x) * 0.6f);

        void OnStep(int step)
        {
            int s = ((step % 8) + 8) % 8; bool hi = tempo > 0.62f;
            if (s == 0 || s == 2 || s == 4 || s == 5 || (hi && (s == 6 || s == 7))) Spawn(taiko, 0f, s == 0 ? 1f : 0.75f);
            if (hi || s % 2 == 1) Spawn(kane, 1650f * (0.995f + (Noise() + 1f) * 0.005f), s == 3 || s == 7 ? 1f : 0.55f);
            if (tempo > 0.12f && s % 2 == 0) { fueTarget = FUE[((step / 2) % 16 + 16) % 16]; fueNoteT = 0f; }
            if (tempo <= 0.12f) fueTarget = 0f;
        }
        void Spawn(Hit[] pool, float f0, float v)
        {
            int best = 0; float oldest = -1f;
            for (int i = 0; i < pool.Length; i++) { if (!pool[i].on) { best = i; oldest = 1e9f; break; } if (pool[i].t > oldest) { oldest = pool[i].t; best = i; } }
            pool[best] = new Hit { on = true, t = 0f, v = v, f0 = f0 };
        }
        void StartChant()
        {
            for (int i = 0; i < chants.Length; i++)
                if (!chants[i].on)
                {
                    float R() => 140f + (Noise() + 1f) * 45f;
                    chants[i] = new ChantV { on = true, t = 0f, b0 = R(), b1 = R(), b2 = R(), b3 = R(), b4 = R() };
                    return;
                }
        }

        public void ResetBeat() { beatPos = 0; lastStep = -1; }
    }
}
