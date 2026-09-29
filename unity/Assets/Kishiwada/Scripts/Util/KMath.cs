using UnityEngine;

namespace Kishiwada
{
    // 再現できる乱数（xorshift）。町並みや人の並びを毎回同じにするため
    public sealed class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed == 0 ? 0x9e3779b9u : seed; }
        public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s / 4294967296f; }
        public float Range(float a, float b) => a + (b - a) * Next();
        public int Int(int n) => Mathf.Min(n - 1, (int)(Next() * n));
        public T Pick<T>(T[] a) => a[Int(a.Length)];
        public bool Chance(float p) => Next() < p;
    }

    public static class KMath
    {
        public static float WrapAngle(float a)
        {
            while (a > Mathf.PI) a -= Mathf.PI * 2f;
            while (a < -Mathf.PI) a += Mathf.PI * 2f;
            return a;
        }
        // 向き（ラジアン、+z から時計回り）→ 水平の単位ベクトル
        public static Vector3 Dir(float yaw) => new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        public static float Yaw(Vector3 d) => Mathf.Atan2(d.x, d.z);
        public static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        public static Vector3 X0Z(Vector2 v, float y = 0f) => new Vector3(v.x, y, v.y);
        // 進行方向 d（xz）の左
        public static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);
        public static float Damp(float k, float dt) => 1f - Mathf.Exp(-k * dt);
        public static Color Hex(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        public static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        public static float Tanh(float x) { if (x > 9f) return 1f; if (x < -9f) return -1f; float e = Mathf.Exp(2f * x); return (e - 1f) / (e + 1f); }

        // 線分 ab 上で p に一番近い点
        public static Vector2 ClosestOnSeg(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 e = b - a; float l2 = Vector2.Dot(e, e);
            t = l2 > 1e-9f ? Mathf.Clamp01(Vector2.Dot(p - a, e) / l2) : 0f;
            return a + e * t;
        }
    }

    // 値ノイズと fBm（テクスチャ生成用、スレッドから呼べるように Unity API を使わない）
    public static class Noise
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }
        // 周期 period で繰り返す値ノイズ（タイル可能なテクスチャ用）
        public static float Value(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float u = fx * fx * (3f - 2f * fx), v = fy * fy * (3f - 2f * fy);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }
        public static float Fbm(float x, float y, int period, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(x, y, period, seed + o * 31) * amp; norm += amp;
                x *= 2f; y *= 2f; period *= 2; amp *= gain;
            }
            return sum / norm;
        }
        public static float Hash01(int x, int y, int seed) => Hash(x, y, seed);
        static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }
    }
}
