using System.Collections.Generic;
using OWR.UI;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OWR.EditorTools
{
    /// <summary>Firewatch-style title scene + build helpers. Menus: OWR > Build Menu Scene / Build Everything / Build Windows Player.</summary>
    public static class MenuSceneBuilder
    {
        const string Root = "Assets/_Game";
        public const string MenuPath = Root + "/Scenes/Menu.unity";
        public const string RacePath = Root + "/Scenes/Race.unity";

        [MenuItem("OWR/Build Menu Scene")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);

            // camera (drifts slowly via MainMenu)
            var rig = new GameObject("CameraRig");
            rig.transform.position = new Vector3(0f, 5f, -30f);
            var camGo = new GameObject("MenuCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localRotation = Quaternion.Euler(-4f, 0f, 0f);
            var cam = camGo.GetComponent<Camera>();
            cam.fieldOfView = 38f; cam.farClipPlane = 6000f; cam.nearClipPlane = 0.3f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.55f, 0.55f);
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // gradient sky: big quad far behind everything
            var skyTex = Gradient("MenuSky", new[] { new Color(0.97f, 0.76f, 0.56f), new Color(0.86f, 0.74f, 0.6f), new Color(0.48f, 0.66f, 0.64f), new Color(0.22f, 0.5f, 0.52f) });
            var skyMat = EnvironmentBuilder.GetMat(Root + "/Materials/Menu_Sky.mat", "Universal Render Pipeline/Unlit");
            skyMat.SetTexture("_BaseMap", skyTex); skyMat.SetColor("_BaseColor", Color.white);
            var sky = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.name = "Sky";
            sky.transform.position = new Vector3(0f, 900f, 4500f);
            sky.transform.localScale = new Vector3(9000f, 3200f, 1f);
            sky.GetComponent<Renderer>().sharedMaterial = skyMat;

            // low sun disc
            var sunMat = EnvironmentBuilder.GetMat(Root + "/Materials/Menu_Sun.mat", "Universal Render Pipeline/Unlit");
            sunMat.SetTexture("_BaseMap", EnvironmentBuilder.SoftDot());
            sunMat.SetColor("_BaseColor", new Color(1f, 0.93f, 0.8f, 1f));
            sunMat.SetFloat("_Surface", 1f); sunMat.SetFloat("_Blend", 0f);
            sunMat.SetOverrideTag("RenderType", "Transparent");
            sunMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); sunMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            sunMat.SetFloat("_ZWrite", 0f); sunMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); sunMat.renderQueue = 2999;
            var sun = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(sun.GetComponent<Collider>());
            sun.name = "Sun";
            sun.transform.position = new Vector3(-700f, 120f, 4000f);
            sun.transform.localScale = Vector3.one * 420f;
            sun.GetComponent<Renderer>().sharedMaterial = sunMat;

            // layered ridges, far -> near (haze -> dark)
            (float r, float hmin, float hmax, Color top, Color bottom, int seed)[] layers =
            {
                (3200f, 180f, 520f, new Color(0.56f, 0.66f, 0.63f), new Color(0.78f, 0.74f, 0.64f), 11),
                (2100f, 120f, 340f, new Color(0.38f, 0.5f, 0.49f), new Color(0.62f, 0.64f, 0.58f), 12),
                (1100f, 50f, 190f, new Color(0.24f, 0.34f, 0.35f), new Color(0.42f, 0.47f, 0.45f), 13),
                (420f, 8f, 70f, new Color(0.14f, 0.2f, 0.22f), new Color(0.22f, 0.28f, 0.29f), 14),
            };
            var land = new GameObject("Landscape").transform;
            foreach (var l in layers)
            {
                var mat = EnvironmentBuilder.GetMat($"{Root}/Materials/Menu_Ridge_{l.seed}.mat", "OWR/Skyline");
                mat.SetColor("_ColorTop", l.top); mat.SetColor("_ColorBottom", l.bottom);
                mat.SetFloat("_HeightMin", -40f); mat.SetFloat("_HeightMax", l.hmax);
                var mesh = RaceBuilder.Ridge(l.r, l.hmin, l.hmax, l.seed, 140f, -70f);
                string mp = $"{Root}/Meshes/MenuRidge_{l.seed}.asset"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
                var g = new GameObject("Ridge_" + l.seed);
                g.transform.SetParent(land, false);
                g.transform.position = new Vector3(0f, -10f, -30f);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            // foreground plateau the car stands on (flat-topped, darkest)
            var fgMat = EnvironmentBuilder.GetMat(Root + "/Materials/Menu_Foreground.mat", "OWR/Skyline");
            fgMat.SetColor("_ColorTop", new Color(0.07f, 0.09f, 0.11f)); fgMat.SetColor("_ColorBottom", new Color(0.07f, 0.09f, 0.11f));
            var fg = RaceBuilder.Ridge(60f, 2.6f, 3.4f, 21, 70f, -5f);
            string fp = Root + "/Meshes/MenuForeground.asset"; AssetDatabase.DeleteAsset(fp); AssetDatabase.CreateAsset(fg, fp);
            var fgo = new GameObject("Foreground");
            fgo.transform.position = new Vector3(0f, -4f, -30f);
            fgo.AddComponent<MeshFilter>().sharedMesh = fg;
            fgo.AddComponent<MeshRenderer>().sharedMaterial = fgMat;

            // the hero object: our car as a dark silhouette on the right of frame
            var silMat = EnvironmentBuilder.GetMat(Root + "/Materials/Menu_Silhouette.mat", "Universal Render Pipeline/Unlit");
            silMat.SetColor("_BaseColor", new Color(0.06f, 0.07f, 0.09f));
            var car = new GameObject("CarSilhouette");
            car.transform.position = new Vector3(14f, -1.4f + 0.0f, 26f);
            car.transform.rotation = Quaternion.Euler(0f, -58f, 0f);
            car.transform.localScale = Vector3.one * 1.7f;
            AddMesh(car.transform, "CarBody", Vector3.zero, silMat);
            foreach (var (x, z, front) in new[] { (-0.8f, 1.75f, true), (0.8f, 1.75f, true), (-0.78f, -1.85f, false), (0.78f, -1.85f, false) })
                AddMesh(car.transform, front ? "Wheel_Front" : "Wheel_Rear", new Vector3(x, 0.34f, z), silMat, x < 0);

            // menu UI
            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            menu.titleFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/BebasNeue-Regular.ttf");
            menu.uiFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/BarlowCondensed-Medium.ttf");
            menu.cameraRig = rig.transform;
            menu.raceScene = "Race";
            // original title theme
            if (!AssetDatabase.LoadAssetAtPath<AudioClip>(MusicComposer.OutPath)) MusicComposer.Compose();
            var mus = menu.gameObject.AddComponent<AudioSource>();
            mus.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicComposer.OutPath);
            mus.loop = true; mus.playOnAwake = false; mus.spatialBlend = 0f; mus.volume = 0f;
            menu.music = mus;

            // a touch of grading
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            string pp = Root + "/Settings/Post_Menu.asset"; AssetDatabase.DeleteAsset(pp); AssetDatabase.CreateAsset(prof, pp);
            var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.28f);
            var ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(-6f); ca.contrast.Override(6f);
            var fgrain = prof.Add<FilmGrain>(true); fgrain.intensity.Override(0.18f);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            var vol = new GameObject("Post").AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = prof;

            EditorSceneManager.SaveScene(scene, MenuPath);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[OWR] Menu scene built: " + MenuPath);
        }

        [MenuItem("OWR/Build Everything (Race + Menu)")]
        public static void BuildEverything()
        {
            TestSceneBuilder.Build();
            SetBuildScenes();
            Debug.Log("[OWR] Everything built (race scene with in-scene menu)");
        }

        [MenuItem("OWR/Build Windows Player")]
        public static void BuildPlayer()
        {
            SetBuildScenes();
            PlayerSettings.productName = "Downforce";
            PlayerSettings.companyName = "OWR";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.resizableWindow = true;
            var opts = new BuildPlayerOptions
            {
                scenes = BuildScenes(),
                locationPathName = "Builds/Windows/Downforce.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[OWR] Player build: {report.summary.result}  size {report.summary.totalSize / (1024 * 1024)} MB  errors {report.summary.totalErrors}");
        }

        static void SetBuildScenes()
        {
            // the menu now runs inside the race scene (drone shots of the real grid), so the build is one scene
            EditorBuildSettings.scenes = System.Array.ConvertAll(BuildScenes(), p => new EditorBuildSettingsScene(p, true));
        }

        static void AddMesh(Transform parent, string meshName, Vector3 pos, Material m, bool mirror = false)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Meshes/{meshName}.asset");
            if (!mesh) return;
            var g = new GameObject(meshName);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            if (mirror) g.transform.localScale = new Vector3(-1f, 1f, 1f);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterials = Enumerable(m, mesh.subMeshCount);
        }

        static Material[] Enumerable(Material m, int n) { var a = new Material[n]; for (int i = 0; i < n; i++) a[i] = m; return a; }

        static Texture2D Gradient(string name, Color[] stops)
        {
            string p = $"{Root}/Textures/{name}.asset";
            AssetDatabase.DeleteAsset(p);
            var t = new Texture2D(4, 256, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 256; y++)
            {
                float f = y / 255f * (stops.Length - 1);
                int i = Mathf.Min((int)f, stops.Length - 2);
                Color c = Color.Lerp(stops[i], stops[i + 1], Mathf.SmoothStep(0f, 1f, f - i));
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, c);
            }
            t.Apply();
            AssetDatabase.CreateAsset(t, p);
            return t;
        }
    
        /// <summary>Interlagos first (the default circuit), then the classic track.</summary>
        public static string[] BuildScenes()
        {
            const string interlagos = "Assets/_Game/Scenes/Interlagos.unity";
            return System.IO.File.Exists(interlagos) ? new[] { interlagos, "Assets/_Game/Scenes/Race.unity" } : new[] { "Assets/_Game/Scenes/Race.unity" };
        }
    }
}
