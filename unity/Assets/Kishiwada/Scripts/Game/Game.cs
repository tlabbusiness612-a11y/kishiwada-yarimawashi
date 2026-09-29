using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Kishiwada
{
    // ゲーム本体：町・地車・綱と曳き手・見物人・音・カメラ・画面を組み立て、進行を管理する
    [DefaultExecutionOrder(-50)]
    public sealed class Game : MonoBehaviour
    {
        [Header("物理（実行中にも調整できる）")]
        public DanjiriSpec danjiriSpec = new DanjiriSpec();
        public RopeSpec ropeSpec = new RopeSpec();
        [Header("その他")]
        public bool forceMobile;
        [Tooltip("前梃子を入れる目安の案内を出す")] public bool guides = true;

        enum Mode { Loading, Title, Count, Run, Finish, Result, Paused }
        Mode mode = Mode.Loading, pausedFrom;
        float modeT;
        bool mobile;

        MaterialLibrary mats; TextPainter gothic, mincho;
        Town town; DanjiriModel dm; DanjiriBody body; DanjiriView dview; RopeSim rope; CrowdView crowd; Spectators spect;
        Narimono audio; CameraRig camRig; EnvironmentRig env; Hud hud; Camera cam;
        readonly GameInput input = new GameInput(); readonly AutoPilot pilot = new AutoPilot(); readonly Scoring sc = new Scoring();
        Material peopleMat; Mesh teamMeshA, teamMeshB, crewMesh, daikuMesh;
        readonly List<SkinnedMeshRenderer> teamRenderers = new List<SkinnedMeshRenderer>();
        float tempo, clock, vRun; int combo;
        string townName = "わが町"; Color happi = KMath.Hex(0x1f2e5a);
        int camMode;
        const string BestKey = "kishiwada-yarimawashi-unity-best-v1";

        // 自動試験（コマンドライン）
        bool autoRun, autoQuit; string logPath, shotDir; StreamWriter log; float logT, dbgT;
        readonly List<(float t, int cam, bool done)> shots = new List<(float, int, bool)>();
        float titleShotAt = -1f; string titleShotPath;

        void Awake()
        {
            mobile = Application.isMobilePlatform || forceMobile;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i], nx = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "-kwAuto": autoRun = true; break;
                    case "-kwQuit": autoQuit = true; break;
                    case "-kwMobile": mobile = true; break;
                    case "-kwLog": logPath = nx; break;
                    case "-kwShotDir": shotDir = nx; break;
                    case "-kwShots":
                        foreach (var part in (nx ?? "").Split(','))
                        {
                            var kv = part.Split(':');
                            if (float.TryParse(kv[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float st)) shots.Add((st, kv.Length > 1 ? int.Parse(kv[1]) : 0, false));
                        }
                        break;
                    case "-kwTitleShot": titleShotPath = nx; titleShotAt = 6f; break;
                }
            }
            if (mobile && QualitySettings.names.Length > 0) QualitySettings.SetQualityLevel(0, true);
            Time.fixedDeltaTime = 1f / 120f;
            Application.targetFrameRate = mobile ? 60 : -1;
            QualitySettings.vSyncCount = mobile ? 0 : 1;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        IEnumerator Start()
        {
            // カメラ
            cam = Camera.main;
            if (cam == null) { var cgo = new GameObject("Main Camera") { tag = "MainCamera" }; cam = cgo.AddComponent<Camera>(); }
            cam.nearClipPlane = 0.25f; cam.farClipPlane = 3200f; cam.fieldOfView = 56f;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = true; cd.renderShadows = true;
            // PC は URP 設定の MSAA 4x、iPhone は軽い FXAA
            cd.antialiasing = mobile ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
            cd.antialiasingQuality = AntialiasingQuality.High;
            cd.dithering = true; cd.stopNaN = false;
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            audio = cam.gameObject.AddComponent<Narimono>();
            camRig = new CameraRig(cam);

            // 画面
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("KishiwadaTheme");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize; ps.referenceResolution = new Vector2Int(1600, 900);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; ps.match = 0.5f;
            var doc = gameObject.AddComponent<UIDocument>(); doc.panelSettings = ps;
            hud = new Hud(doc, mobile) { input = input };
            hud.onStart = StartRun; hud.onAgain = StartRun; hud.onTitle = ToTitle;
            hud.onCamera = () => { camMode = (camMode + 1) % 4; camRig.mode = (CameraRig.Mode)camMode; camRig.Snap(); hud.SetCamera(CameraRig.Names[camMode]); };
            hud.onPause = Pause; hud.onResume = Resume;
            hud.SetLoading("町並みを組み立てています…");
            yield return null; yield return null;

            // 質感・材質
            string chars = "御食事処" + string.Join("", TextureFactory.SignWords) + "わが町岸和田北町本町中町堺町大手町五軒屋町" + "町";
            gothic = new TextPainter(TextPainter.Gothic, chars);
            mincho = new TextPainter(TextPainter.Mincho, chars);
            TextureFactory.Scale = mobile ? 2 : 1;
            var tex = TextureFactory.MakeAll(gothic, mincho);
            mats = new MaterialLibrary(tex, mobile);
            yield return null;
            env = new EnvironmentRig(transform, mobile);

            // 町並み（PLATEAU）
            var data = PlateauData.Load();
            town = TownBuilder.Build(data, mats, transform, mobile);
            hud.SetLoading("地車を組み立てています…");
            yield return null;

            // 地車
            dm = DanjiriBuilder.Build(mats, transform, mobile);
            dm.root.layer = 9;
            dm.root.AddComponent<Rigidbody>();
            body = dm.root.AddComponent<DanjiriBody>();
            body.spec = danjiriSpec;
            body.Init(dm);
            body.OnImpact += OnImpact;

            // 人
            peopleMat = mats["cloth"];
            BuildTeamMeshes();
            dview = new DanjiriView(dm, body, daikuMesh, crewMesh, peopleMat, mats);
            rope = new RopeSim(ropeSpec) { walls = town.walls };
            crowd = new CrowdView(rope, transform, teamMeshA, teamMeshB, peopleMat, mats, mobile);
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) teamRenderers.Add(smr);
            foreach (var smr in dm.root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) teamRenderers.Add(smr);
            hud.SetLoading("見物人を集めています…");
            yield return null;
            var tmp = new GameObject("bake"); tmp.transform.SetParent(transform, false);
            spect = new Spectators(town.spectators, mats["cloth"], mobile, tmp.transform);
            Destroy(tmp);
            ApplyTown();
            sc.onCorner = r => { hud.Stamp(r.grade, r.pts); if (r.pts >= 460) audio.Cheer(); };

            ResetRun();
            hud.SetLoading(null);
            hud.BuildRail();
            if (autoRun) StartRun(); else ToTitle();
            if (logPath != null) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(logPath))); log = new StreamWriter(logPath); log.WriteLine("t,mode,s,x,z,yaw,speed,yawRate,tempo,vRun,maeL,maeR,rear,tension0,tension1,crowd,hits,score,slideMax,fps"); }
        }

        void BuildTeamMeshes()
        {
            teamMeshA = HumanBuilder.BuildMesh(Outfit.Happi(happi, true), mobile);
            teamMeshB = HumanBuilder.BuildMesh(Outfit.Happi(happi, false), mobile);
            var dk = Outfit.Happi(happi, true); dk.band = KMath.Hex(0xd8ac4c);
            daikuMesh = HumanBuilder.BuildMesh(dk, mobile);
            crewMesh = HumanBuilder.BuildMesh(Outfit.Happi(happi * 0.85f, false), mobile);
        }

        // 町名・法被の色を反映（札・旗・法被）
        void ApplyTown()
        {
            dm.plateMat.SetTexture("_BaseMap", TextureFactory.TownPlate(mincho, townName));
            dm.plateMat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            dm.flagMat.SetTexture("_BaseMap", TextureFactory.Flag(mincho, townName, happi));
        }
        void Recolor()
        {
            var oldA = teamMeshA; var oldB = teamMeshB; var oldC = crewMesh; var oldD = daikuMesh;
            BuildTeamMeshes();
            foreach (var smr in teamRenderers)
            {
                if (smr == null) continue;
                if (smr.sharedMesh == oldA) smr.sharedMesh = teamMeshA;
                else if (smr.sharedMesh == oldB) smr.sharedMesh = teamMeshB;
                else if (smr.sharedMesh == oldC) smr.sharedMesh = crewMesh;
                else if (smr.sharedMesh == oldD) smr.sharedMesh = daikuMesh;
            }
        }

        void ResetRun()
        {
            var s0 = Course.Segs[0];
            body.Place(KMath.X0Z(s0.a, 0.02f), s0.yaw);
            rope.Reset(body);
            tempo = 0f; combo = 0; clock = 0f; vRun = 0f;
            sc.Reset();
            camRig.Snap();
            audio.ResetBeat();
        }

        void StartRun()
        {
            if (hud.TownName != townName || hud.HappiColor != happi)
            {
                townName = hud.TownName; happi = hud.HappiColor;
                Recolor(); ApplyTown();
            }
            ResetRun();
            mode = Mode.Count; modeT = 0f; Time.timeScale = 1f;
            camMode = 0; camRig.mode = CameraRig.Mode.Chase; hud.SetCamera(CameraRig.Names[0]);
            hud.ShowScreen("hud"); hud.BuildRail();
            hud.Callout("よーい", townName + "、曳き出し", 1.6f);
            audio.playing = true; audio.master = 0.9f;
        }
        void ToTitle()
        {
            Time.timeScale = 1f;
            ResetRun();
            mode = Mode.Title; modeT = 0f;
            camRig.mode = CameraRig.Mode.Director; camRig.Snap();
            hud.ShowScreen("title");
            audio.playing = true; audio.master = 0.45f;
        }
        void Pause() { if (mode == Mode.Run || mode == Mode.Count || mode == Mode.Finish) { pausedFrom = mode; mode = Mode.Paused; Time.timeScale = 0f; audio.playing = false; hud.ShowScreen("pause"); } }
        void Resume() { if (mode != Mode.Paused) return; mode = pausedFrom; Time.timeScale = 1f; audio.playing = true; hud.ShowScreen("hud"); }

        void OnImpact(float dv, Vector3 at, Collider other)
        {
            if (mode == Mode.Loading) return;
            audio.Thud(dv / 4f);
            camRig.Shake(Mathf.Min(1.1f, 0.25f + dv * 0.25f));
            dview.Kick(Mathf.Min(1f, dv / 3f));
            if (mode == Mode.Run) { sc.Hit(dv); hud.Bang(dv > 2.5f ? "ドーン！" : "ゴツン"); }
        }

        bool Simulating => mode != Mode.Loading && mode != Mode.Paused;

        void FixedUpdate()
        {
            if (!Simulating || body == null) return;
            float dt = Time.fixedDeltaTime;
            body.spec = danjiriSpec; rope.spec.pullerForce = ropeSpec.pullerForce;
            body.Step(dt);
            rope.vRun = vRun;
            rope.Step(body, dt);
        }

        void Update()
        {
            if (mode == Mode.Loading || body == null) return;
            float dt = Time.deltaTime, t = Time.time;
            input.Poll(Time.unscaledDeltaTime);
            if (input.pause) { if (mode == Mode.Paused) Resume(); else Pause(); }
            if (input.usingTouch) hud.ShowTouch(true);
            bool running = mode == Mode.Run;
            bool pilotOn = mode == Mode.Title || (autoRun && (mode == Mode.Run || mode == Mode.Count));
            if (pilotOn) pilot.Drive(input, body, audio, tempo, dt);
            modeT += dt; clock += dt;

            switch (mode)
            {
                case Mode.Title:
                    if (input.confirm) { StartRun(); break; }
                    vRun = 1.2f + 5.6f * tempo;
                    if (sc.maxS > Course.FinishS - 4f || modeT > 110f) { ResetRun(); modeT = 0f; }
                    break;
                case Mode.Count:
                    vRun = 0f;
                    if (modeT > 1.8f) { mode = Mode.Run; modeT = 0f; audio.Whistle(); hud.Callout("曳き出し！", mobile ? "真ん中を鳴物の拍に合わせて連打" : "Space を鳴物の拍に合わせて連打", 2.4f); }
                    break;
                case Mode.Run:
                    vRun = 1.2f + 5.6f * tempo;
                    if (sc.Finished) { mode = Mode.Finish; modeT = 0f; sc.AddTimeBonus(); hud.Callout("ゴール", "紀州街道まで曳ききった", 3f); audio.Whistle(); audio.Cheer(); }
                    break;
                case Mode.Finish:
                    vRun = Mathf.Max(0f, vRun - 2.5f * dt);
                    if (modeT > 3.4f) ShowResult();
                    break;
                case Mode.Result:
                    vRun = 0f;
                    if (autoQuit && modeT > 2.5f) Quit();
                    break;
            }

            // 叩く・跳ぶ（タイトルのデモでも同じ）
            if (mode == Mode.Run || mode == Mode.Title)
            {
                if (input.tap)
                {
                    bool good = audio.OffBeat() < 0.18f;
                    tempo = Mathf.Min(1f, tempo + (good ? 0.19f : 0.11f));
                    combo = good ? combo + 1 : 0;
                    if (combo == 6 && running) hud.Callout("ええ調子", "鳴物に乗ってる", 1.2f);
                    audio.Chant();
                }
                if (input.jump && clock - dview.jumpAt > 0.8f) { dview.jumpAt = clock; if (running) sc.Jump(body.YawRate); }
            }
            tempo = Mathf.Max(0f, tempo - (0.1f + 0.2f * tempo) * dt);
            if (input.camNext && mode != Mode.Title) hud.onCamera?.Invoke();

            // 地車への入力
            bool control = mode == Mode.Run || mode == Mode.Title;
            body.maeL = control ? input.maeL : 0f; body.maeR = control ? input.maeR : 0f;
            body.rearTurn = control ? input.rear : 0f;
            body.brakeAll = mode == Mode.Finish && modeT > 0.6f || mode == Mode.Result ? 1f : 0f;

            sc.Track(dt, body.PosXZ, body.Speed, body.Yaw, body.YawRate, running);
            if (running && guides) Guides();

            // 見た目
            double beat = audio.BeatNow;
            dview.Update(dt, t, beat, clock);
            crowd.Update(dt, t);
            camRig.Update(Time.unscaledDeltaTime > 0 ? dt : 0f, t, body, dview, town);
            spect.Update(body.rb.position, t, mode != Mode.Title || true, Mathf.Clamp01(body.Speed / 4f + (sc.cur != null ? 0.5f : 0f)));
            env.Update(cam.transform.position, dt);
            // 音
            audio.tempo = tempo;
            audio.speed = body.Speed;
            float slideMax = 0f; for (int i = 0; i < 4; i++) slideMax = Mathf.Max(slideMax, body.wheels[i].slide);
            audio.slide = slideMax * Mathf.Clamp01(body.Speed / 2f); audio.scrape = body.scrape;
            audio.excite = Mathf.Clamp01(body.Speed / 8.4f) * 0.6f + (sc.cur != null ? 0.4f : 0f);
            Vector3 toD = body.rb.position - cam.transform.position;
            audio.distance = toD.magnitude;
            audio.pan = Mathf.Clamp(Vector3.Dot(toD.normalized, cam.transform.right), -1f, 1f) * 0.6f;
            // 画面
            if (mode == Mode.Run || mode == Mode.Count || mode == Mode.Finish || mode == Mode.Paused)
                hud.UpdateHud(townName, sc.score, sc.maxS, body.Speed, tempo, beat, body.maeL, body.maeR, body.rearTurn);
            if (mode == Mode.Title && titleShotAt > 0 && modeT > titleShotAt) { titleShotAt = -1; Shot(titleShotPath); StartCoroutine(QuitSoon(1.5f)); }
            Automation(dt, slideMax);
        }

        void Guides()
        {
            string main = "", sub = "";
            hud.Hint(true, false); hud.Hint(false, false);
            for (int k = 0; k < Course.Corners.Length; k++)
            {
                var cn = Course.Corners[k]; float d = cn.s - sc.s;
                if (sc.results[k] != null || d < -16f || d > 110f) continue;
                string side = cn.dir > 0 ? "左" : "右";
                if (d > 40f) { main = $"{cn.name}まで {Mathf.RoundToInt(d)}m"; sub = tempo < 0.6f ? "鳴物を上げろ、曳け！" : cn.sub; }
                else if (d > 16f) { main = $"{side}の前梃子、用意"; sub = "まだ入れるな"; }
                else { main = $"{side}の前梃子！"; sub = "入れて、跳べ"; hud.Hint(cn.dir > 0, true); }
                break;
            }
            if (main == "" && sc.time < 7f) { main = "曳け！"; sub = mobile ? "真ん中を鳴物の拍に合わせて連打" : "Space を鳴物の拍に合わせて連打"; }
            hud.Guide(main, sub);
        }

        void ShowResult()
        {
            mode = Mode.Result; modeT = 0f;
            int best = PlayerPrefs.GetInt(BestKey, 0);
            bool nb = sc.score > best;
            if (nb) { PlayerPrefs.SetInt(BestKey, sc.score); PlayerPrefs.Save(); }
            hud.ShowResult(sc, townName, best, nb);
            hud.ShowScreen("result");
            audio.master = 0.5f;
            if (log != null)
            {
                foreach (var r in sc.results) if (r != null) log.WriteLine($"# corner {r.name} grade={r.grade} pts={r.pts} entry={r.entry} hits={r.hits} jump={r.jump} turn={r.turn:0.00} keep={r.keep:0.00} maxYaw={r.maxYaw:0.00}");
                log.WriteLine($"# total score={sc.score} time={sc.time:0.0} hits={sc.hits}");
                log.Flush();
            }
        }

        void Automation(float dt, float slideMax)
        {
            if (log != null && (mode == Mode.Run || mode == Mode.Finish))
            {
                dbgT += dt;
                if (dbgT >= 1f) { dbgT = 0f; log.WriteLine("# rope" + rope.Describe()); }
                logT += dt;
                if (logT >= 0.1f)
                {
                    logT = 0f;
                    var p = body.rb.position;
                    log.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1},{2:0.0},{3:0.00},{4:0.00},{5:0.000},{6:0.00},{7:0.000},{8:0.00},{9:0.00},{10:0.00},{11:0.00},{12:0.00},{13:0},{14:0},{15:0.00},{16},{17},{18:0.00},{19:0}",
                        sc.time, mode, sc.s, p.x, p.z, body.Yaw, body.Speed, body.YawRate, tempo, vRun, body.maeL, body.maeR, body.rearTurn, rope.tension[0], rope.tension[1], rope.crowdSpeed, sc.hits, sc.score, slideMax, 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime)));
                }
            }
            if (shots.Count > 0 && (mode == Mode.Run || mode == Mode.Finish))
                for (int i = 0; i < shots.Count; i++)
                {
                    var s = shots[i];
                    if (s.done) continue;
                    if (sc.time >= s.t - 0.6f && camRig.mode != (CameraRig.Mode)s.cam) { camRig.mode = (CameraRig.Mode)s.cam; camRig.Snap(); }
                    if (sc.time >= s.t)
                    {
                        Shot(System.IO.Path.Combine(shotDir ?? ".", $"shot_{s.t:000.0}_{CameraRig.Names[Mathf.Clamp(s.cam, 0, 3)]}.png"));
                        shots[i] = (s.t, s.cam, true);
                        camRig.mode = CameraRig.Mode.Chase;
                    }
                }
        }
        static void Shot(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[kishiwada] screenshot " + path);
        }
        IEnumerator QuitSoon(float s) { yield return new WaitForSecondsRealtime(s); Quit(); }
        void Quit()
        {
            log?.Flush(); log?.Close(); log = null;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        void OnDestroy() { log?.Flush(); log?.Close(); }
    }
}
