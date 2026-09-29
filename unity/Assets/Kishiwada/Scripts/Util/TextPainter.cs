using System.Collections.Generic;
using UnityEngine;

namespace Kishiwada
{
    // OS の日本語フォントから字形を取り出し、CPU の画像（Color32[]）に文字を描く。
    // 看板・町名の札・後ろ旗の縦書きに使う。フォントのテクスチャは GPU 側にしかないので一度だけ読み戻す。
    public sealed class TextPainter
    {
        public static readonly string[] Gothic = { "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Noto Sans CJK JP", "Arial Unicode MS" };
        public static readonly string[] Mincho = { "Hiragino Mincho ProN", "Yu Mincho", "YuMincho", "MS Mincho", "Noto Serif CJK JP", "Hiragino Sans" };

        readonly Font font;
        const int Size = 96;
        Color32[] atlas; int aw, ah;
        readonly Dictionary<char, CharacterInfo> infos = new Dictionary<char, CharacterInfo>();

        public TextPainter(string[] names, string chars)
        {
            font = Font.CreateDynamicFontFromOSFont(names, Size);
            Prepare(chars);
        }

        // 使う文字を全部先に要求してから、アトラスを一度だけ読み戻す
        public void Prepare(string chars)
        {
            font.RequestCharactersInTexture(chars, Size, FontStyle.Bold);
            var tex = font.material.mainTexture;
            aw = tex.width; ah = tex.height;
            var rt = RenderTexture.GetTemporary(aw, ah, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(aw, ah, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, aw, ah), 0, 0);
            read.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            atlas = read.GetPixels32();
            Object.Destroy(read);
            infos.Clear();
            foreach (char c in chars)
                if (!infos.ContainsKey(c) && font.GetCharacterInfo(c, out var ci, Size, FontStyle.Bold)) infos[c] = ci;
        }

        float Coverage(float u, float v)
        {
            float x = u * aw - 0.5f, y = v * ah - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, aw - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, ah - 1);
            int x1 = Mathf.Min(aw - 1, x0 + 1), y1 = Mathf.Min(ah - 1, y0 + 1);
            float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0);
            float a = atlas[y0 * aw + x0].a, b = atlas[y0 * aw + x1].a, c = atlas[y1 * aw + x0].a, d = atlas[y1 * aw + x1].a;
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy) / 255f;
        }

        // 1 文字を、画像の (cx, cy) を中心に高さ px ピクセルで描く（y は上向き＝Texture2D と同じ）
        public void DrawChar(Color32[] img, int w, int h, char ch, float cx, float cy, float px, Color color)
        {
            if (!infos.TryGetValue(ch, out var ci)) return;
            float s = px / Size;
            float gw = ci.glyphWidth * s, gh = ci.glyphHeight * s;
            if (gw <= 0 || gh <= 0) return;
            // 字面の中心を (cx, cy) に合わせる（和文は全角なので箱の中心でそろう）
            float x0 = cx - gw * 0.5f, y0 = cy - gh * 0.5f;
            int ix0 = Mathf.Max(0, Mathf.FloorToInt(x0)), ix1 = Mathf.Min(w - 1, Mathf.CeilToInt(x0 + gw));
            int iy0 = Mathf.Max(0, Mathf.FloorToInt(y0)), iy1 = Mathf.Min(h - 1, Mathf.CeilToInt(y0 + gh));
            for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    float gx = (x + 0.5f - x0) / gw, gy = (y + 0.5f - y0) / gh; // 0..1（左下原点）
                    if (gx < 0 || gx > 1 || gy < 0 || gy > 1) continue;
                    // 字形の UV（回転して格納されている場合もある）
                    Vector2 uv = Bilerp(ci.uvBottomLeft, ci.uvBottomRight, ci.uvTopLeft, ci.uvTopRight, gx, gy);
                    float a = Coverage(uv.x, uv.y) * color.a;
                    if (a <= 0.003f) continue;
                    int i = y * w + x; Color32 d = img[i];
                    img[i] = new Color32(
                        (byte)Mathf.Lerp(d.r, color.r * 255f, a), (byte)Mathf.Lerp(d.g, color.g * 255f, a),
                        (byte)Mathf.Lerp(d.b, color.b * 255f, a), (byte)Mathf.Max(d.a, a * 255f));
                }
        }
        static Vector2 Bilerp(Vector2 bl, Vector2 br, Vector2 tl, Vector2 tr, float x, float y)
            => Vector2.Lerp(Vector2.Lerp(bl, br, x), Vector2.Lerp(tl, tr, x), y);

        // 横書き。幅 maxW に収まるよう字の大きさを詰める
        public void DrawText(Color32[] img, int w, int h, string text, float cx, float cy, float px, Color color, float maxW = float.MaxValue)
        {
            int n = text.Length; if (n == 0) return;
            float adv = px * 1.02f;
            if (adv * n > maxW) { adv = maxW / n; px = Mathf.Min(px, adv / 1.02f); }
            float x = cx - adv * (n - 1) * 0.5f;
            for (int i = 0; i < n; i++) DrawChar(img, w, h, text[i], x + adv * i, cy, px, color);
        }
        // 縦書き（上から下へ）。cy は一文字目の中心
        public void DrawVertical(Color32[] img, int w, int h, string text, float cx, float topCy, float px, Color color)
        {
            for (int i = 0; i < text.Length; i++) DrawChar(img, w, h, text[i], cx, topCy - px * 1.04f * i, px, color);
        }
    }
}
