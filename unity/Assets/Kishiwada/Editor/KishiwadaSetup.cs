using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Kishiwada.EditorTools
{
    // メニュー「Kishiwada」：材質の雛形とシーンを作る・ビルドする。コマンドラインからも呼べる
    //   Unity.exe -batchmode -projectPath unity -executeMethod Kishiwada.EditorTools.KishiwadaSetup.Run -quit
    public static class KishiwadaSetup
    {
        const string MatDir = "Assets/Kishiwada/Resources/KMat";
        public const string ScenePath = "Assets/Kishiwada/Scenes/Main.unity";

        [MenuItem("Kishiwada/セットアップ（材質の雛形とシーン）")]
        public static void Run()
        {
            Templates();
            MakeScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[kishiwada] setup done");
        }

        // 実行時に複製する材質の雛形。キーワードの組み合わせごとに 1 つ置き、ビルドで削られないようにする
        static void Templates()
        {
            Directory.CreateDirectory(MatDir);
            var sh = Shader.Find(MaterialLibrary.Lit);
            if (sh == null) { Debug.LogError("[kishiwada] Kishiwada/Lit シェーダーが見つかりません"); return; }
            foreach (var name in MaterialLibrary.Templates)
            {
                string path = $"{MatDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = m == null;
                if (create) m = new Material(sh);
                m.shader = sh;
                foreach (var k in m.shaderKeywords) m.DisableKeyword(k);
                foreach (var k in MaterialLibrary.KeywordsFor(name)) m.EnableKeyword(k);
                m.enableInstancing = true;
                if (name == "Lit_Cut")
                {
                    m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cull", 0f);
                    m.SetOverrideTag("RenderType", "TransparentCutout");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                }
                if (name.EndsWith("E")) { m.SetColor("_EmissionColor", new Color(0.5f, 0.2f, 0.1f)); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
                if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            }
            string sky = $"{MatDir}/Sky.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(sky) == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Skybox/Procedural")), sky);
        }

        static void MakeScene()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = cgo.AddComponent<Camera>();
                cam.fieldOfView = 56f; cam.nearClipPlane = 0.25f; cam.farClipPlane = 3200f;
                cgo.AddComponent<AudioListener>();
                cgo.AddComponent<UniversalAdditionalCameraData>();
                cgo.transform.SetPositionAndRotation(new Vector3(52f, 6.4f, 252f), Quaternion.Euler(18f, 200f, 0f));
                new GameObject("Game").AddComponent<Game>();
                RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Sky.mat");
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }

    public static class KishiwadaBuild
    {
        [MenuItem("Kishiwada/ビルド/Windows")]
        public static void BuildWindows()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Build(BuildTarget.StandaloneWindows64, "Build/Windows/KishiwadaYarimawashi.exe");
        }

        // Xcode プロジェクトを書き出す（Mac の Xcode で iPhone に入れる）
        [MenuItem("Kishiwada/ビルド/iOS（Xcode プロジェクト）")]
        public static void BuildIOS()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            Build(BuildTarget.iOS, "Build/iOS");
        }

        static void Build(BuildTarget target, string path)
        {
            // 先に作業中の対象を切り替える（切り替えずに作ると URP の SSAO の資源が抜けて真っ暗になることがあった）
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (EditorUserBuildSettings.activeBuildTarget != target) EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { KishiwadaSetup.ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[kishiwada] build {target}: {s.result} errors={s.totalErrors} size={s.totalSize / 1048576}MB time={s.totalTime}");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
