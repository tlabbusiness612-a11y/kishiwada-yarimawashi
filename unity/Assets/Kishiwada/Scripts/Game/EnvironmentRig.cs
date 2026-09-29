using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kishiwada
{
    // 九月の午後、南西からの日差し。空・環境光・反射・霧・色調補正
    public sealed class EnvironmentRig
    {
        public Light sun;
        public Volume volume;
        public ReflectionProbe probe;
        public DepthOfField dof;
        public MotionBlur motionBlur;
        Vector3 probeAt = new Vector3(9999, 0, 0);
        float probeTimer;
        readonly bool mobile;

        public EnvironmentRig(Transform parent, bool mobile)
        {
            this.mobile = mobile;
            // 太陽：高度 34°、方位 240°（西南西）から
            var sgo = new GameObject("Sun");
            sgo.transform.SetParent(parent, false);
            sun = sgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.76f);
            sun.intensity = 2.4f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sgo.transform.rotation = Quaternion.Euler(34f, 240f + 180f, 0f);
            RenderSettings.sun = sun;

            // 空
            var skyTpl = Resources.Load<Material>("KMat/Sky");
            var sky = skyTpl != null ? new Material(skyTpl) : new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.82f);
            sky.SetColor("_SkyTint", new Color(0.5f, 0.56f, 0.64f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.41f, 0.39f));
            sky.SetFloat("_Exposure", 1.2f);
            RenderSettings.skybox = sky;

            // 環境光（空の青・地面の照り返し）を球面調和で直接作る
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.34f, 0.37f, 0.42f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.38f, 0.48f, 0.62f), 0.9f);
            sh.AddDirectionalLight(Vector3.down, new Color(0.36f, 0.31f, 0.25f), 0.45f);
            sh.AddDirectionalLight(-sgo.transform.forward, new Color(0.5f, 0.42f, 0.32f), 0.35f);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
            RenderSettings.reflectionIntensity = 1f;

            // 霧（遠くの山がかすむ程度）
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.00085f;
            RenderSettings.fogColor = new Color(0.74f, 0.78f, 0.82f);

            // 反射：町ごと映す実時間の反射プローブ（カメラに付いて時々描き直す）
            var pgo = new GameObject("Reflection");
            pgo.transform.SetParent(parent, false);
            probe = pgo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = mobile ? 64 : 256;
            probe.size = new Vector3(3000, 600, 3000);
            probe.boxProjection = false;
            probe.hdr = true;
            probe.cullingMask = (1 << 8) | 1;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.importance = 1;

            // 汚れ・むら（Kishiwada/Lit）
            Shader.SetGlobalVector("_KGrime", new Vector4(0.32f, 1.3f, 0.16f, 0f));

            // 色調補正
            var vgo = new GameObject("PostFX");
            vgo.transform.SetParent(parent, false);
            volume = vgo.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = prof.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.15f); ca.contrast.Override(10f); ca.saturation.Override(6f);
            var bl = prof.Add<Bloom>(true);
            bl.threshold.Override(1.05f); bl.intensity.Override(mobile ? 0.2f : 0.35f); bl.scatter.Override(0.65f); bl.highQualityFiltering.Override(!mobile);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.22f); vg.smoothness.Override(0.42f);
            var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(6f); wb.tint.Override(2f);
            var smh = prof.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.96f, 0.98f, 1.04f, -0.02f)); smh.highlights.Override(new Vector4(1.03f, 1.0f, 0.96f, 0f));
            dof = prof.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Off);
            motionBlur = prof.Add<MotionBlur>(true); motionBlur.intensity.Override(mobile ? 0f : 0.18f); motionBlur.quality.Override(MotionBlurQuality.Medium);
            volume.sharedProfile = prof;
        }

        // 反射を撮り直す（カメラがある程度動いたら）
        public void Update(Vector3 camPos, float dt)
        {
            probeTimer -= dt;
            if ((camPos - probeAt).sqrMagnitude > 45f * 45f && probeTimer <= 0f)
            {
                probeAt = camPos;
                probe.transform.position = new Vector3(camPos.x, 6f, camPos.z);
                probe.RenderProbe();
                probeTimer = 1.5f;
            }
        }
    }
}
