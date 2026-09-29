using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kishiwada
{
    // 材質をまとめて作る。UV はメートル単位で張り、材質のタイリングで「何 m で 1 枚」かを決める。
    // ビルドでシェーダーの組み合わせが削られないよう、Resources/KMat の雛形（エディタの Kishiwada/Setup が作る）を複製して使う。
    public sealed class MaterialLibrary
    {
        public const string Lit = "Kishiwada/Lit";
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        readonly Dictionary<string, TexPair> tex;
        public bool mobile;

        public MaterialLibrary(Dictionary<string, TexPair> tex, bool mobile)
        {
            this.tex = tex; this.mobile = mobile;
            // 木・金物
            Make("wood", "wood", 1f / 1.2f, 0.9f, bump: 0.8f);
            Make("roofWood", "wood", 1f / 1.2f, 0.95f, bump: 0.6f, cullOff: true);
            Make("gold", "brass", 4f, 1f, metallic: 1f, bump: 0.5f);
            Make("plain", null, 1f, 0.28f);
            Make("gloss", null, 1f, 0.72f);
            Make("black", null, 1f, 0.55f, color: new Color(0.04f, 0.035f, 0.03f));
            Make("cloth", "cloth", 3f, 0.8f, bump: 0.6f, cullOff: true);
            Make("skin", null, 1f, 0.38f);
            Make("hair", null, 1f, 0.45f);
            Make("rope", "rope", 1f / 0.3f, 1f, bump: 1f);
            // 町並み
            Make("tile", "tile", 0.5f, 1f, bump: 1f, cullOff: true);
            Make("plaster", "plaster", 1f, 1f, tilingV: 1f, bump: 0.8f);
            Make("lattice", "lattice", 1f, 1f, bump: 1f);
            Make("house", "house", 1f, 1f, bump: 0.6f);
            Make("office", "office", 1f, 1f, bump: 0.6f);
            Make("shop", "shop", 1f, 1f, bump: 0.5f);
            Make("signs", "signs", 1f, 1f, emission: new Color(0.12f, 0.12f, 0.12f));
            Make("asphalt", "asphalt", 1f / 8f, 1f, bump: 0.8f);
            Make("walk", "walk", 0.5f, 1f, bump: 0.8f);
            Make("concrete", "concrete", 0.25f, 1f, bump: 0.5f);
            Make("kohaku", "kohaku", 1f / 1.8f, 1f, bump: 0.7f, cullOff: true);
            Make("lantern", null, 1f, 0.4f, emission: new Color(0.55f, 0.16f, 0.06f));
            Make("paper", null, 1f, 0.3f, emission: new Color(0.25f, 0.2f, 0.12f), cullOff: true);
            Make("metal", null, 1f, 0.55f, metallic: 0.8f);
            Make("hills", null, 1f, 0.05f);
            Make("plate", null, 1f, 1f);
            Make("flag", null, 1f, 0.3f, cutout: true, cullOff: true);
        }

        public Material this[string key] => Get(key);
        public Material Get(string key)
        {
            if (mats.TryGetValue(key, out var m)) return m;
            Debug.LogWarning("材質がありません: " + key);
            return mats["plain"];
        }
        public IEnumerable<Material> All => mats.Values;

        static Material Template(string name, params string[] keywords)
        {
            var t = Resources.Load<Material>("KMat/" + name);
            if (t != null) return new Material(t);
            var m = new Material(Shader.Find(Lit));
            foreach (var k in keywords) m.EnableKeyword(k);
            return m;
        }

        void Make(string key, string texKey, float tiling, float smoothness, float metallic = 0f, float bump = 1f,
            Color? color = null, Color? emission = null, bool cullOff = false, bool cutout = false, float tilingV = -1f)
        {
            TexPair t = null; if (texKey != null) tex.TryGetValue(texKey, out t);
            bool hasN = t != null && t.normal != null;
            bool alphaSmooth = t != null;
            string tpl = cutout ? "Lit_Cut" : emission.HasValue ? (hasN ? "Lit_NAE" : "Lit_E") : hasN ? "Lit_NA" : alphaSmooth ? "Lit_A" : "Lit";
            var m = Template(tpl, KeywordsFor(tpl));
            m.name = key;
            if (t != null) m.SetTexture("_BaseMap", t.albedo);
            if (hasN) { m.SetTexture("_BumpMap", t.normal); m.SetFloat("_BumpScale", bump); }
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tilingV > 0 ? tilingV : tiling));
            m.SetColor("_BaseColor", color ?? Color.white);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue) { m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            if (cullOff) m.SetFloat("_Cull", (float)CullMode.Off);
            if (cutout) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); }
            m.enableInstancing = true;
            mats[key] = m;
        }

        public static string[] KeywordsFor(string tpl)
        {
            switch (tpl)
            {
                case "Lit_N": return new[] { "_NORMALMAP" };
                case "Lit_NA": return new[] { "_NORMALMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" };
                case "Lit_A": return new[] { "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" };
                case "Lit_E": return new[] { "_EMISSION" };
                case "Lit_NAE": return new[] { "_NORMALMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", "_EMISSION" };
                case "Lit_Cut": return new[] { "_ALPHATEST_ON" };
                default: return new string[0];
            }
        }
        public static readonly string[] Templates = { "Lit", "Lit_N", "Lit_NA", "Lit_A", "Lit_E", "Lit_NAE", "Lit_Cut" };

        // 町名入りの札・旗など、実行中に作る材質
        public Material Custom(string key, Texture2D albedo, float smoothness, bool alphaSmooth, bool cutout, bool cullOff)
        {
            var m = Template(cutout ? "Lit_Cut" : alphaSmooth ? "Lit_A" : "Lit", KeywordsFor(cutout ? "Lit_Cut" : alphaSmooth ? "Lit_A" : "Lit"));
            m.name = key; m.SetTexture("_BaseMap", albedo); m.SetFloat("_Smoothness", smoothness);
            if (cutout) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); }
            if (cullOff) m.SetFloat("_Cull", (float)CullMode.Off);
            mats[key] = m;
            return m;
        }
    }
}
