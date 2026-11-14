using System.Collections.Generic;
using OWR.CameraRig;
using OWR.DebugTools;
using OWR.Input;
using OWR.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OWR.EditorTools
{
    /// <summary>Builds the test circuit scene with the detailed car, cockpit + chase cameras, golden-hour lighting and post. Menu: OWR > Build Test Scene.</summary>
    public static class TestSceneBuilder
    {
        const string Root = "Assets/_Game";
        const string ScenePath = Root + "/Scenes/Race.unity";
        const int CarLayer = 2; // Ignore Raycast: wheels never hit their own car

        // Original layout (metres, x/z). Closed loop: long straight, hairpin, sweepers, chicane.
        static readonly Vector2[] Layout =
        {
            new Vector2(0, -100), new Vector2(0, 150), new Vector2(0, 350), new Vector2(25, 430),
            new Vector2(100, 445), new Vector2(160, 390), new Vector2(255, 335), new Vector2(330, 255),
            new Vector2(330, 130), new Vector2(265, 80), new Vector2(240, 10), new Vector2(270, -70),
            new Vector2(215, -175), new Vector2(130, -195), new Vector2(45, -175),
        };
        const float RoadWidth = 14f;
        const float KerbWidth = 1.3f;
        const float WallOffset = 22f;

        [MenuItem("OWR/Build Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Settings");
            EnsureFolder(Root + "/Meshes");
            EnsureFolder(Root + "/Textures");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // ---------------- textures & materials ----------------
            var asphalt = ProcTextures.Asphalt();
            var grass = ProcTextures.Grass();
            var kerb = ProcTextures.Kerb();
            var carbon = ProcTextures.Carbon();

            var mGrass = EnvironmentBuilder.Scanned("ACG_Grass001", "Grass_Scanned", new Vector2(900f, 900f), 0.45f, 0.38f);
            var mGravel = EnvironmentBuilder.Scanned("gravelly_sand", "Gravel_Scanned", new Vector2(1f, 1f), 0.6f, 0.5f);
            var mRoad = EnvironmentBuilder.WetAsphalt(0.85f);
            var mKerb = Lit("Kerb", new Color(0.8f, 0.8f, 0.8f), 0.86f, 0f, kerb.albedo, kerb.normal, new Vector2(1f, KerbWidth / 2.0f));
            var mLine = Lit("TrackLine", new Color(0.8f, 0.8f, 0.78f), 0.82f, 0f);
            var mWall = Lit("ConcreteWall", new Color(0.5f, 0.5f, 0.49f), 0.55f, 0f, asphalt.albedo, asphalt.normal, new Vector2(1f, 1f));
            var mWallTop = Lit("WallStripe", new Color(0.08f, 0.32f, 0.55f), 0.4f, 0f);
            mWall.SetFloat("_Cull", 0f);
            mWallTop.SetFloat("_Cull", 0f);

            var mats = new CarModelBuilder.Mats
            {
                paint = Paint("Car_Paint", new Color(0.02f, 0.12f, 0.22f), 0.35f),
                carbon = Paint("Car_Carbon", Color.white, 0.2f, carbon.albedo, carbon.normal, new Vector2(10f, 10f)),
                accent = Paint("Car_Accent", new Color(1f, 0.36f, 0.04f), 0.2f),
                white = Paint("Car_White", new Color(0.86f, 0.87f, 0.88f), 0.1f),
                interior = Lit("Car_Interior", new Color(1.6f, 1.6f, 1.7f), 0.45f, 0f, carbon.albedo, carbon.normal, new Vector2(14f, 14f)),
                metal = Lit("Car_Metal", new Color(0.62f, 0.63f, 0.66f), 0.82f, 1f),
                gloss = Lit("Car_GlossBlack", new Color(0.02f, 0.02f, 0.025f), 0.88f, 0f),
                rubber = Lit("Car_Rubber", new Color(0.06f, 0.06f, 0.065f), 0.28f, 0f),
                grip = Lit("Car_Grip", new Color(0.12f, 0.12f, 0.13f), 0.18f, 0f, null, kerb.normal, new Vector2(8f, 8f)),
                rearLight = Emissive("Car_RearLight", new Color(1f, 0.05f, 0.03f), 4f),
                shiftLight = Emissive("Car_ShiftLight", Color.black, 0f),
                suit = Lit("Driver_Suit", new Color(0.13f, 0.17f, 0.30f), 0.28f, 0f),
                glove = Lit("Driver_Glove", new Color(0.07f, 0.08f, 0.12f), 0.35f, 0f),
                glovePalm = Lit("Driver_GlovePalm", new Color(0.55f, 0.56f, 0.58f), 0.25f, 0f),
            };

            // ---------------- ground & track ----------------
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(300f, 1f, 300f);
            ground.GetComponent<Renderer>().sharedMaterial = mGrass;
            ground.isStatic = true;

            var trackRoot = new GameObject("Track").transform;
            List<Vector3> pts = SampleClosedCatmullRom(Layout, 2f);
            int n = pts.Count;
            var road = MeshObject("Road", trackRoot, BuildRibbon(pts, 0, n, true, -RoadWidth / 2f, RoadWidth / 2f, 0.02f, 0.02f), mRoad, true);
            SaveMesh(road.GetComponent<MeshFilter>().sharedMesh, "Road");
            MeshObject("Line_L", trackRoot, BuildRibbon(pts, 0, n, true, -RoadWidth / 2f + 0.25f, -RoadWidth / 2f + 0.5f, 0.024f, 0.024f), mLine, false);
            MeshObject("Line_R", trackRoot, BuildRibbon(pts, 0, n, true, RoadWidth / 2f - 0.5f, RoadWidth / 2f - 0.25f, 0.024f, 0.024f), mLine, false);

            // kerbs where the track curves (both sides), slightly raised so the car feels them
            float[] curv = Curvature(pts);
            int kerbCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (curv[i] < 1f / 140f || curv[(i - 1 + n) % n] >= 1f / 140f) continue;
                int j = i;
                while (curv[j % n] >= 1f / 140f && j - i < n) j++;
                int a = Mathf.Max(i - 6, 0), b = Mathf.Min(j + 6, n - 1);
                if (b - a < 6) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    float inner = RoadWidth / 2f, outer = RoadWidth / 2f + KerbWidth;
                    var mesh = side < 0 ? BuildRibbon(pts, a, b, false, -outer, -inner, 0.02f, 0.045f)
                                        : BuildRibbon(pts, a, b, false, inner, outer, 0.045f, 0.02f);
                    MeshObject($"Kerb_{kerbCount}_{(side < 0 ? "L" : "R")}", trackRoot, mesh, mKerb, true);
                }
                // gravel trap on the outside of the corner
                Vector3 ta = (pts[Mathf.Min(a + 3, n - 1)] - pts[a]).normalized, tb = (pts[b] - pts[Mathf.Max(b - 3, 0)]).normalized;
                float turn = Vector3.Cross(ta, tb).y; // >0 = turning right
                int outerSide = turn > 0f ? -1 : 1;
                float g0 = RoadWidth / 2f + KerbWidth + 2.5f, g1 = WallOffset - 1.2f;
                var gmesh = outerSide < 0 ? BuildRibbon(pts, Mathf.Max(a - 10, 0), Mathf.Min(b + 25, n - 1), false, -g1, -g0, 0.012f, 0.012f)
                                      : BuildRibbon(pts, Mathf.Max(a - 10, 0), Mathf.Min(b + 25, n - 1), false, g0, g1, 0.012f, 0.012f);
                var gobj = MeshObject($"GravelTrap_{kerbCount}", trackRoot, gmesh, mGravel, true);
                gobj.GetComponent<MeshRenderer>().sharedMaterial.SetTextureScale("_BaseMap", new Vector2((g1 - g0) / 4f, (g1 - g0) / 4f));
                kerbCount++;
            }

            // concrete barrier walls with a painted top stripe
            for (int side = -1; side <= 1; side += 2)
            {
                float off = side * WallOffset;
                MeshObject(side < 0 ? "Wall_L" : "Wall_R", trackRoot, BuildWall(pts, off, 0f, 1.0f, 0.5f), mWall, true);
                MeshObject(side < 0 ? "WallStripe_L" : "WallStripe_R", trackRoot, BuildWall(pts, off - side * 0.01f, 0.75f, 1.0f, 0.502f), mWallTop, false);
            }

            // ---------------- trackside: catch fence, sponsor boards, grandstand + crowd, trees ----------------
            var mFence = Lit("CatchFence", new Color(0.75f, 0.77f, 0.8f), 0.55f, 0.8f, EnvironmentBuilder.ChainLink(), null, Vector2.one);
            mFence.SetFloat("_AlphaClip", 1f); mFence.SetFloat("_Cutoff", 0.5f); mFence.EnableKeyword("_ALPHATEST_ON"); mFence.SetFloat("_Cull", 0f);
            mFence.renderQueue = (int)RenderQueue.AlphaTest;
            var mPost = Lit("FencePost", new Color(0.5f, 0.52f, 0.55f), 0.6f, 0.9f);
            var mBark = Lit("Tree_Bark", new Color(0.16f, 0.13f, 0.11f), 0.15f, 0f);
            var mLeaves = Lit("Tree_Leaves", new Color(0.12f, 0.2f, 0.1f), 0.25f, 0f, grass.albedo, grass.normal, new Vector2(2f, 2f));
            var mConcrete = Lit("Stand_Concrete", new Color(0.55f, 0.55f, 0.54f), 0.35f, 0f, asphalt.albedo, asphalt.normal, new Vector2(2f, 2f));
            var mRoof = Lit("Stand_Roof", new Color(0.82f, 0.83f, 0.85f), 0.5f, 0.6f);
            var seats = new[] { Lit("Seat_Blue", new Color(0.05f, 0.2f, 0.55f), 0.5f, 0f), Lit("Seat_White", new Color(0.85f, 0.85f, 0.85f), 0.5f, 0f), Lit("Seat_Orange", new Color(0.9f, 0.4f, 0.05f), 0.5f, 0f) };
            var crowd = new[]
            {
                Lit("Crowd_Red", new Color(0.22f, 0.06f, 0.06f), 0.2f, 0f), Lit("Crowd_Navy", new Color(0.05f, 0.07f, 0.16f), 0.2f, 0f),
                Lit("Crowd_Yellow", new Color(0.12f, 0.12f, 0.13f), 0.2f, 0f), Lit("Crowd_Grey", new Color(0.32f, 0.33f, 0.35f), 0.2f, 0f),
                Lit("Crowd_Green", new Color(0.08f, 0.16f, 0.1f), 0.2f, 0f), Lit("Crowd_PonchoClear", new Color(0.7f, 0.73f, 0.77f), 0.75f, 0f),
            };
            var mSkin = Lit("Crowd_Skin", new Color(0.62f, 0.45f, 0.35f), 0.3f, 0f);
            EnvironmentBuilder.BuildTrackside(pts, WallOffset, trackRoot, mFence, mPost, mBark, mLeaves, mConcrete, mRoof, seats, crowd, mSkin);

            // ---------------- spawn ----------------
            Vector3 spawnTan = (pts[12] - pts[10]).normalized;
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.position = pts[10] + Vector3.up * 0.15f;
            spawn.rotation = Quaternion.LookRotation(spawnTan, Vector3.up);

            // ---------------- car ----------------
            var setup = LoadOrCreateSetup();
            setup.autoShift = false;
            EditorUtility.SetDirty(setup);
            var car = BuildCarRoot(setup);
            car.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var ctrl = car.GetComponent<CarController>();
            ctrl.spawnPoint = spawn;
            var model = CarModelBuilder.Build(car, ctrl, setup, mats);
            LiveryBuilder.Apply(car, ctrl);

            // ---------------- cameras ----------------
            var chaseCam = Camera.main;
            chaseCam.name = "ChaseCamera";
            SetupCamera(chaseCam, 0.1f);
            var chase = chaseCam.gameObject.AddComponent<ChaseCamera>();
            chase.target = ctrl;
            chaseCam.transform.position = spawn.position - spawnTan * 6.5f + Vector3.up * 1.9f;
            chaseCam.transform.LookAt(spawn.position);

            var cockpitGo = new GameObject("CockpitCamera", typeof(Camera), typeof(AudioListener));
            cockpitGo.tag = "MainCamera";
            cockpitGo.transform.SetParent(car.transform, false);
            var cockpitCam = cockpitGo.GetComponent<Camera>();
            SetupCamera(cockpitCam, 0.02f);
            cockpitCam.cullingMask &= ~(1 << CarModelBuilder.DriverHiddenLayer);
            var cc = cockpitGo.AddComponent<CockpitCamera>();
            cc.car = ctrl;
            cockpitGo.transform.localPosition = cc.eyeLocal;
            var celebration = car.GetComponent<OWR.Cockpit.Celebration>();
            if (celebration) celebration.head = cockpitGo.transform;

            // T-cam on the roll hoop (sees the helmet, so it renders every layer)
            var tcamGo = new GameObject("TCamCamera", typeof(Camera), typeof(AudioListener));
            tcamGo.transform.SetParent(car.transform, false);
            var tcam = tcamGo.GetComponent<Camera>();
            SetupCamera(tcam, 0.05f);
            var tc = tcamGo.AddComponent<TCamCamera>();
            tc.car = ctrl;
            tcamGo.transform.localPosition = tc.mountLocal;
            tcam.enabled = false; tcamGo.GetComponent<AudioListener>().enabled = false;

            var director = new GameObject("CameraDirector").AddComponent<CameraDirector>();
            director.tCamera = tcam;
            director.car = ctrl;
            director.cockpitCamera = cockpitCam;
            director.chaseCamera = chaseCam;
            director.hideInCockpit = new[] { model.helmet, model.shoulders };

            var overlay = new GameObject("DebugOverlay").AddComponent<VehicleDebugOverlay>();
            overlay.car = ctrl;
            overlay.showFull = false;

            // ---------------- weather, lighting & post (wet overcast race day) ----------------
            var rainMat = ParticleMat("FX_Rain", EnvironmentBuilder.SoftDot(), new Color(0.85f, 0.88f, 0.95f, 0.45f));
            var sprayMat = ParticleMat("FX_Spray", EnvironmentBuilder.SoftDot(), new Color(0.82f, 0.84f, 0.87f, 0.35f));
            EnvironmentBuilder.SetupWeather(ctrl, rainMat, sprayMat);
            EnvironmentBuilder.SetupOvercastLighting();
            EnvironmentBuilder.SetupWetPost();
            director.cockpitExposure = GameObject.Find("CockpitExposure")?.GetComponent<Volume>();
            EnvironmentBuilder.InstallLensRain();
            ConfigureUrpAsset();

            // ---------------- sound: procedural engine, wind, tyres, rain, crowd ----------------
            var engSrc = car.AddComponent<AudioSource>();
            engSrc.spatialBlend = 0f; engSrc.priority = 0; engSrc.dopplerLevel = 0f;
            var eng = car.AddComponent<OWR.AudioFX.EngineAudio>(); eng.car = ctrl; eng.volume = 0.85f;
            car.AddComponent<OWR.AudioFX.ShiftBeep>();
            AddNoise(car.transform, "Audio_Wind", OWR.AudioFX.AmbientNoise.Mode.Wind, ctrl, 0.05f, 0f);
            AddNoise(car.transform, "Audio_Tyres", OWR.AudioFX.AmbientNoise.Mode.Tyres, ctrl, 0.12f, 0f);
            AddNoise(car.transform, "Audio_Rain", OWR.AudioFX.AmbientNoise.Mode.Rain, ctrl, 0.06f, 0f);
            foreach (var stand in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (stand.name.StartsWith("Grandstand"))
                    AddNoise(stand, "Audio_Crowd", OWR.AudioFX.AmbientNoise.Mode.Crowd, ctrl, 0.9f, 1f);

            // ---------------- race: path, start/finish, lights, AI field, managers, HUD; distant mountains ----------------
            RaceBuilder.Setup(car, ctrl, pts, trackRoot, RoadWidth, spawn);
            Vector3 centre = Vector3.zero; foreach (var q in pts) centre += q; centre /= pts.Count;
            RaceBuilder.BuildMountains(new Vector3(centre.x, 0f, centre.z), trackRoot);

            PlayerSettings.runInBackground = true; // keeps play mode ticking while the editor is unfocused
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = car;
            Debug.Log($"[OWR] Scene built: {ScenePath}  track {TrackLength(pts):0} m, {kerbCount} kerb sections");
        }

        // ------------------------------------------------------------------ car root (physics)
        static GameObject BuildCarRoot(CarSetup s)
        {
            var root = new GameObject("PlayerCar");
            root.layer = CarLayer;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass;

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.45f, -0.1f);
            col.size = new Vector3(1.4f, 0.55f, 4.9f);

            var input = root.AddComponent<DriverInput>();
            var ctrl = root.AddComponent<CarController>();
            ctrl.setup = s;
            ctrl.input = input;
            ctrl.groundMask = ~(1 << CarLayer);

            float mountY = s.restLength + s.frontRadius - 0.02f;
            ctrl.wheels = new RaycastWheel[4];
            ctrl.wheels[0] = Wheel(root.transform, "Wheel_FL", new Vector3(-0.80f, mountY, 1.75f), true, true);
            ctrl.wheels[1] = Wheel(root.transform, "Wheel_FR", new Vector3(0.80f, mountY, 1.75f), true, false);
            ctrl.wheels[2] = Wheel(root.transform, "Wheel_RL", new Vector3(-0.78f, mountY, -1.85f), false, true);
            ctrl.wheels[3] = Wheel(root.transform, "Wheel_RR", new Vector3(0.78f, mountY, -1.85f), false, false);
            return root;
        }

        static RaycastWheel Wheel(Transform parent, string name, Vector3 localPos, bool front, bool left)
        {
            var mount = new GameObject(name) { layer = CarLayer };
            mount.transform.SetParent(parent, false);
            mount.transform.localPosition = localPos;
            var w = mount.AddComponent<RaycastWheel>();
            w.isFront = front;
            w.isLeft = left;
            var steer = new GameObject("SteerPivot") { layer = CarLayer }.transform;
            steer.SetParent(mount.transform, false);
            var spin = new GameObject("Spin") { layer = CarLayer }.transform;
            spin.SetParent(steer, false);
            w.steerVisual = steer;
            w.spinVisual = spin;
            return w;
        }

        static void SetupCamera(Camera cam, float near)
        {
            cam.nearClipPlane = near;
            cam.farClipPlane = 4000f;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (!data) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        // ------------------------------------------------------------------ lighting
        static void SetupLighting()
        {
            var sun = Object.FindFirstObjectByType<Light>();
            sun.name = "Sun";
            sun.transform.rotation = Quaternion.Euler(17f, -58f, 0f);
            sun.color = new Color(1f, 0.83f, 0.66f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;

            var sky = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Sky_GoldenHour.mat");
            if (!sky)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, Root + "/Materials/Sky_GoldenHour.mat");
            }
            sky.SetFloat("_SunSize", 0.03f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.36f, 0.48f, 0.66f));
            sky.SetColor("_GroundColor", new Color(0.30f, 0.27f, 0.25f));
            sky.SetFloat("_Exposure", 1.25f);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0016f;
            RenderSettings.fogColor = new Color(0.66f, 0.62f, 0.6f);
            DynamicGI.UpdateEnvironment();
        }

        static void SetupPost()
        {
            string path = Root + "/Settings/Post_GoldenHour.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.72f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.35f);
            ca.contrast.Override(14f);
            ca.saturation.Override(6f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(8f);
            var lgg = profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(0.98f, 1f, 1.04f, 0f));
            lgg.gain.Override(new Vector4(1.04f, 1.0f, 0.95f, 0f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static void ConfigureUrpAsset()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 220f;
                urp.shadowCascadeCount = 4;
                urp.supportsHDR = true;
                EditorUtility.SetDirty(urp);
            }
        }

        // ------------------------------------------------------------------ track geometry
        static List<Vector3> SampleClosedCatmullRom(Vector2[] ctrl, float spacing)
        {
            var dense = new List<Vector3>();
            int n = ctrl.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = V(ctrl[(i - 1 + n) % n]), p1 = V(ctrl[i]), p2 = V(ctrl[(i + 1) % n]), p3 = V(ctrl[(i + 2) % n]);
                int steps = Mathf.Max(4, Mathf.CeilToInt(Vector3.Distance(p1, p2) / spacing));
                for (int k = 0; k < steps; k++) dense.Add(CatmullRomCentripetal(p0, p1, p2, p3, k / (float)steps));
            }
            return dense;
        }

        static Vector3 V(Vector2 v) => new Vector3(v.x, 0f, v.y);

        static Vector3 CatmullRomCentripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Sqrt(Vector3.Distance(p0, p1));
            float t2 = t1 + Mathf.Sqrt(Vector3.Distance(p1, p2));
            float t3 = t2 + Mathf.Sqrt(Vector3.Distance(p2, p3));
            float tt = Mathf.Lerp(t1, t2, t);
            Vector3 a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3;
            return (t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2;
        }

        static Vector3 Right(List<Vector3> pts, int i)
        {
            int n = pts.Count;
            Vector3 tan = (pts[(i + 1) % n] - pts[(i - 1 + n) % n]).normalized;
            return Vector3.Cross(Vector3.up, tan).normalized;
        }

        static float[] Curvature(List<Vector3> pts)
        {
            int n = pts.Count;
            var c = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = (pts[i] - pts[(i - 3 + n) % n]).normalized, b = (pts[(i + 3) % n] - pts[i]).normalized;
                float ds = Vector3.Distance(pts[(i - 3 + n) % n], pts[(i + 3) % n]) * 0.5f;
                c[i] = Vector3.Angle(a, b) * Mathf.Deg2Rad / Mathf.Max(ds, 0.01f);
            }
            return c;
        }

        /// <summary>Ribbon from index a to b (closed loops wrap) between lateral offsets, with inner/outer heights.</summary>
        static Mesh BuildRibbon(List<Vector3> pts, int a, int b, bool closed, float off0, float off1, float y0, float y1)
        {
            int n = pts.Count;
            int count = closed ? n + 1 : b - a + 1;
            var verts = new Vector3[count * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new List<int>();
            float dist = 0f, width = off1 - off0;
            for (int k = 0; k < count; k++)
            {
                int i = closed ? k % n : a + k;
                Vector3 p = pts[i];
                if (k > 0) dist += Vector3.Distance(p, pts[closed ? (k - 1) % n : i - 1]);
                Vector3 r = Right(pts, i);
                verts[k * 2] = p + r * off0 + Vector3.up * y0;
                verts[k * 2 + 1] = p + r * off1 + Vector3.up * y1;
                uvs[k * 2] = new Vector2(0f, dist / width);
                uvs[k * 2 + 1] = new Vector2(1f, dist / width);
            }
            for (int k = 0; k < count - 1; k++)
            {
                int v = k * 2;
                tris.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
            }
            var m = new Mesh { name = "Ribbon", indexFormat = IndexFormat.UInt32 };
            m.vertices = verts; m.uv = uvs; m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Closed vertical wall loop with thickness, at lateral offset off, from y0 to y1.</summary>
        static Mesh BuildWall(List<Vector3> pts, float off, float y0, float y1, float thick)
        {
            int n = pts.Count;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float dist = 0f;
            float[] laterals = { off - thick / 2f, off + thick / 2f };
            for (int k = 0; k <= n; k++)
            {
                int i = k % n;
                if (k > 0) dist += Vector3.Distance(pts[i], pts[(k - 1) % n]);
                Vector3 r = Right(pts, i);
                foreach (float lat in laterals)
                {
                    verts.Add(pts[i] + r * lat + Vector3.up * y0); uvs.Add(new Vector2(dist / 3f, 0f));
                    verts.Add(pts[i] + r * lat + Vector3.up * y1); uvs.Add(new Vector2(dist / 3f, (y1 - y0) / 3f));
                }
            }
            for (int k = 0; k < n; k++)
            {
                int v = k * 4, w = (k + 1) * 4;
                // side facing -right (v+0,v+1) and +right (v+2,v+3), plus top (v+1,v+3)
                tris.AddRange(new[] { v, v + 1, w + 1, v, w + 1, w });
                tris.AddRange(new[] { v + 2, w + 2, w + 3, v + 2, w + 3, v + 3 });
                tris.AddRange(new[] { v + 1, v + 3, w + 3, v + 1, w + 3, w + 1 });
            }
            var m = new Mesh { name = "Wall", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool collider)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) g.AddComponent<MeshCollider>().sharedMesh = mesh;
            g.isStatic = true;
            return g;
        }

        static float TrackLength(List<Vector3> pts)
        {
            float d = 0f;
            for (int i = 0; i < pts.Count; i++) d += Vector3.Distance(pts[i], pts[(i + 1) % pts.Count]);
            return d;
        }

        // ------------------------------------------------------------------ materials
        static Material GetOrCreate(string name, string shader)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader.name != shader)
            {
                if (m != null) AssetDatabase.DeleteAsset(path);
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static void ApplyMaps(Material m, Texture2D albedo, Texture2D normal, Vector2 tiling)
        {
            if (albedo) { m.SetTexture("_BaseMap", albedo); m.SetTextureScale("_BaseMap", tiling); }
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
        }

        static Material Lit(string name, Color c, float smooth, float metal, Texture2D albedo = null, Texture2D normal = null, Vector2 tiling = default)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            ApplyMaps(m, albedo, normal, tiling == default ? Vector2.one : tiling);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Clear-coated paint / lacquered carbon (URP Complex Lit).</summary>
        static Material Paint(string name, Color c, float metal, Texture2D albedo = null, Texture2D normal = null, Vector2 tiling = default)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Complex Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.55f);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_ClearCoatMask", 1f);
            m.SetFloat("_ClearCoatSmoothness", 0.95f);
            m.SetFloat("_ClearCoat", 1f);
            m.EnableKeyword("_CLEARCOAT");
            ApplyMaps(m, albedo, normal, tiling == default ? Vector2.one : tiling);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void AddNoise(Transform parent, string name, OWR.AudioFX.AmbientNoise.Mode mode, CarController car, float vol, float spatial)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            var src = g.AddComponent<AudioSource>();
            src.spatialBlend = spatial;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 10f; src.maxDistance = 260f;
            src.dopplerLevel = 0f;
            var n = g.AddComponent<OWR.AudioFX.AmbientNoise>();
            n.mode = mode; n.car = car; n.volume = vol;
            if (mode == OWR.AudioFX.AmbientNoise.Mode.Crowd) n.cullDistance = 220f;
        }

        static Material ParticleMat(string name, Texture2D tex, Color c)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Emissive(string name, Color c, float intensity)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.8f);
            m.SetColor("_EmissionColor", c * intensity);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ assets
        static void SaveMesh(Mesh m, string name)
        {
            string path = $"{Root}/Meshes/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }

        static CarSetup LoadOrCreateSetup()
        {
            string path = Root + "/Settings/CarSetup_Default.asset";
            var s = AssetDatabase.LoadAssetAtPath<CarSetup>(path);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<CarSetup>();
                AssetDatabase.CreateAsset(s, path);
            }
            return s;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
