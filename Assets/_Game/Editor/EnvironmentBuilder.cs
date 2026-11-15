using System.Collections.Generic;
using OWR.Vehicle;
using OWR.Weather;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace OWR.EditorTools
{
    /// <summary>
    /// Wet overcast race-day environment: HDRI sky + lighting + post, wet scanned asphalt, rain, tyre spray,
    /// lens rain renderer feature, tree line, grandstand with crowd, sponsor boards (fictional brands), catch fencing.
    /// </summary>
    public static class EnvironmentBuilder
    {
        const string Root = "Assets/_Game";
        const string PH = Root + "/External/PolyHaven";

        static readonly string[] Brands = { "VELTRO", "NORDAX", "KAIRO", "ALTUS", "QUANTA", "MERIDIAN", "HALCYON", "ORBITA", "SOLENNE", "BRAVEN" };
        static readonly Color[] BrandBg =
        {
            new Color(0.75f, 0.06f, 0.08f), new Color(0.05f, 0.12f, 0.35f), new Color(0.95f, 0.75f, 0.05f), new Color(0.06f, 0.06f, 0.07f),
            new Color(0.0f, 0.45f, 0.42f), new Color(0.92f, 0.92f, 0.92f), new Color(0.35f, 0.08f, 0.45f), new Color(0.9f, 0.4f, 0.05f),
            new Color(0.1f, 0.5f, 0.15f), new Color(0.2f, 0.22f, 0.25f),
        };
        static readonly Color[] BrandFg =
        {
            Color.white, Color.white, new Color(0.08f, 0.08f, 0.1f), new Color(0.95f, 0.2f, 0.15f),
            Color.white, new Color(0.75f, 0.05f, 0.08f), Color.white, Color.white, Color.white, new Color(1f, 0.8f, 0.1f),
        };

        // ================================================================= sky / light / post
        public static void SetupOvercastLighting()
        {
            string hdrPath = PH + "/overcast_soil_puresky_2k.hdr";
            var hdrImp = (TextureImporter)AssetImporter.GetAtPath(hdrPath);
            if (hdrImp && (hdrImp.mipmapEnabled || hdrImp.wrapModeU != TextureWrapMode.Repeat))
            {
                hdrImp.mipmapEnabled = false;
                hdrImp.textureCompression = TextureImporterCompression.CompressedHQ;
                hdrImp.SaveAndReimport();
            }
            var hdr = AssetDatabase.LoadAssetAtPath<Texture2D>(hdrPath);

            var sky = GetMat(Root + "/Materials/Sky_Overcast.mat", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", hdr);
            sky.SetFloat("_Mapping", 1f);
            sky.SetFloat("_ImageType", 0f);
            sky.SetFloat("_Exposure", 1.0f);
            sky.SetFloat("_Rotation", 40f);
            sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
            RenderSettings.skybox = sky;

            var sun = Object.FindFirstObjectByType<Light>();
            sun.name = "Sun_Overcast";
            sun.transform.rotation = Quaternion.Euler(58f, -30f, 0f);
            sun.color = new Color(0.86f, 0.89f, 0.94f);
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.45f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.92f, 0.95f, 1.0f) * 1.25f;
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.74f, 0.77f) * 1.1f;
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.32f, 0.31f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0062f;
            RenderSettings.fogColor = new Color(0.60f, 0.63f, 0.66f);
            DynamicGI.UpdateEnvironment();
        }

        public static void SetupWetPost()
        {
            string path = Root + "/Settings/Post_WetOvercast.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(0.95f); bloom.intensity.Override(0.4f); bloom.scatter.Override(0.75f);
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.55f); ca.contrast.Override(8f); ca.saturation.Override(-14f);
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(-6f); wb.tint.Override(2f);
            var lgg = profile.Add<LiftGammaGain>(true); lgg.lift.Override(new Vector4(1f, 1f, 1.02f, 0.01f));
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.22f); vig.smoothness.Override(0.5f);
            var mb = profile.Add<MotionBlur>(true); mb.intensity.Override(0.18f); mb.quality.Override(MotionBlurQuality.Medium);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            var vol = new GameObject("PostProcess").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // Onboard-camera exposure: exposes for the cockpit (sky blows out to white, hands/wheel readable)
            string cpath = Root + "/Settings/Post_CockpitExposure.asset";
            AssetDatabase.DeleteAsset(cpath);
            var cprof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(cprof, cpath);
            var cca = cprof.Add<ColorAdjustments>(true); cca.postExposure.Override(1.25f); cca.contrast.Override(14f);
            var csmh = cprof.Add<ShadowsMidtonesHighlights>(true);
            csmh.shadows.Override(new Vector4(1f, 1f, 1.03f, 0.18f));
            foreach (var c in cprof.components) AssetDatabase.AddObjectToAsset(c, cprof);
            EditorUtility.SetDirty(cprof);
            var cvol = new GameObject("CockpitExposure").AddComponent<Volume>();
            cvol.isGlobal = true;
            cvol.priority = 10f;
            cvol.weight = 1f;
            cvol.sharedProfile = cprof;
        }

        // ================================================================= wet asphalt
        public static Material WetAsphalt(float wetness)
        {
            string diffP = PH + "/asphalt_track_diff_2k.jpg", roughP = PH + "/asphalt_track_rough_2k.jpg", norP = PH + "/asphalt_track_nor_gl_2k.jpg";
            SetReadable(diffP, true, false);
            SetReadable(roughP, true, false, linear: true);
            var nImp = (TextureImporter)AssetImporter.GetAtPath(norP);
            if (nImp && nImp.textureType != TextureImporterType.NormalMap) { nImp.textureType = TextureImporterType.NormalMap; nImp.SaveAndReimport(); }

            string outPath = Root + "/Textures/Asphalt_Wet_AlbedoSmooth.asset";
            var composed = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (!composed)
            {
                var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(diffP);
                var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(roughP);
                int W = diff.width, H = diff.height;
                var dp = diff.GetPixels();
                var rp = rough.GetPixels();
                var outp = new Color[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        float u = x / (float)W, v = y / (float)H;
                        // tileable low-frequency puddle mask
                        float pn = TileNoise(u, v, 3f) * 0.65f + TileNoise(u, v, 7f) * 0.35f;
                        float puddle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.56f, 0.70f, pn));
                        float baseSmooth = 1f - rp[i].r;
                        float wetSmooth = Mathf.Lerp(baseSmooth, 0.80f, wetness);
                        float smooth = Mathf.Lerp(wetSmooth, 0.96f, puddle * wetness);
                        Color c = dp[i] * Mathf.Lerp(1f, 0.52f, wetness) * Mathf.Lerp(1f, 0.85f, puddle);
                        c.a = smooth;
                        outp[i] = c;
                    }
                composed = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "Asphalt_Wet_AlbedoSmooth", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
                composed.SetPixels(outp);
                composed.Apply(true);
                AssetDatabase.CreateAsset(composed, outPath);
            }

            var m = GetMat(Root + "/Materials/Asphalt_Wet.mat", "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", composed);
            m.SetTextureScale("_BaseMap", new Vector2(3.5f, 3.5f));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_Metallic", 0f);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(norP));
            m.SetFloat("_BumpScale", 0.8f);
            m.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Scanned PBR set (Poly Haven naming) packed into albedo+smoothness, darkened/glossed for wet weather.</summary>
        public static Material Scanned(string prefix, string matName, Vector2 tiling, float wetness, float wetGloss)
        {
            string diffP = $"{PH}/{prefix}_diff_2k.jpg", roughP = $"{PH}/{prefix}_rough_2k.jpg", norP = $"{PH}/{prefix}_nor_gl_2k.jpg";
            if (prefix.StartsWith("ACG_"))
            {
                string id = prefix.Substring(4), A = Root + "/External/AmbientCG";
                diffP = $"{A}/{id}_2K-JPG_Color.jpg"; roughP = $"{A}/{id}_2K-JPG_Roughness.jpg"; norP = $"{A}/{id}_2K-JPG_NormalGL.jpg";
            }
            SetReadable(diffP, true, false);
            SetReadable(roughP, true, false, linear: true);
            var nImp = (TextureImporter)AssetImporter.GetAtPath(norP);
            if (nImp && nImp.textureType != TextureImporterType.NormalMap) { nImp.textureType = TextureImporterType.NormalMap; nImp.SaveAndReimport(); }
            string outPath = $"{Root}/Textures/{prefix}_Wet_AlbedoSmooth.asset";
            var composed = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (!composed)
            {
                var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(diffP);
                var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(roughP);
                var dp = diff.GetPixels(); var rp = rough.GetPixels();
                var o = new Color[dp.Length];
                for (int i = 0; i < dp.Length; i++)
                {
                    Color c = dp[i] * Mathf.Lerp(1f, 0.62f, wetness);
                    c.a = Mathf.Lerp(1f - rp[i].r, wetGloss, wetness);
                    o[i] = c;
                }
                composed = new Texture2D(diff.width, diff.height, TextureFormat.RGBA32, true, false) { name = prefix + "_Wet", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
                composed.SetPixels(o);
                composed.Apply(true);
                AssetDatabase.CreateAsset(composed, outPath);
            }
            var m = GetMat($"{Root}/Materials/{matName}.mat", "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", composed);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(norP));
            m.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(m);
            return m;
        }

        static float TileNoise(float u, float v, float f)
        {
            float a = Mathf.PI * 2f;
            // sample Perlin on a torus projection so it tiles
            float x = Mathf.Cos(u * a) * f, y = Mathf.Sin(u * a) * f, z = Mathf.Cos(v * a) * f, w = Mathf.Sin(v * a) * f;
            return (Mathf.PerlinNoise(x + 10f, z + 20f) + Mathf.PerlinNoise(y + 30f, w + 40f)) * 0.5f;
        }

        static void SetReadable(string path, bool readable, bool normal, bool linear = false)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!imp) return;
            if (imp.isReadable == readable && imp.sRGBTexture == !linear) return;
            imp.isReadable = readable;
            imp.sRGBTexture = !linear;
            imp.SaveAndReimport();
        }

        // ================================================================= weather (rain, spray, lens)
        public static WeatherController SetupWeather(CarController car, Material particleMat, Material sprayMat)
        {
            var go = new GameObject("Weather");
            var wc = go.AddComponent<WeatherController>();
            wc.car = car;

            // rain
            var rain = new GameObject("Rain").AddComponent<ParticleSystem>();
            rain.transform.SetParent(go.transform, false);
            rain.transform.rotation = Quaternion.identity;
            var main = rain.main;
            main.loop = true;
            main.startLifetime = 1.3f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.0075f);
            main.startColor = new Color(0.86f, 0.89f, 0.94f, 0.22f);
            main.maxParticles = 22000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            var em = rain.emission; em.rateOverTime = 7000f;
            var sh = rain.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(44f, 1f, 44f);
            var vel = rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.2f, -0.6f); vel.y = new ParticleSystem.MinMaxCurve(-24f, -20f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var rr = rain.GetComponent<ParticleSystemRenderer>();
            rr.renderMode = ParticleSystemRenderMode.Stretch;
            rr.velocityScale = 0.05f;
            rr.lengthScale = 1f;
            rr.sharedMaterial = particleMat;
            rr.shadowCastingMode = ShadowCastingMode.Off;
            wc.rainSystem = rain;

            // spray per wheel
            var sprays = new List<ParticleSystem>();
            foreach (var w in car.wheels)
            {
                var ps = new GameObject("Spray").AddComponent<ParticleSystem>();
                ps.transform.SetParent(w.steerVisual, false);
                ps.transform.localPosition = new Vector3(0f, -0.26f, -0.30f);
                ps.transform.localRotation = Quaternion.Euler(-12f, 180f, 0f); // backward, slightly up
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startColor = new Color(0.78f, 0.8f, 0.83f, 0.22f);
                m.maxParticles = 900;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                m.gravityModifier = 0.05f;
                var e = ps.emission; e.rateOverTime = 0f;
                var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 22f; s.radius = 0.15f;
                var inh = ps.inheritVelocity; inh.enabled = true; inh.mode = ParticleSystemInheritVelocityMode.Initial; inh.curve = 0.55f;
                var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 4.5f));
                var col = ps.colorOverLifetime; col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = grad;
                var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 1.6f; lim.multiplyDragByParticleSize = true;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = sprayMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.sortingFudge = 10f;
                sprays.Add(ps);
            }
            wc.spraySystems = sprays.ToArray();

            // reflection probe that follows the camera, so the wet track mirrors sky, stands and the car
            var probeGo = new GameObject("TrackReflectionProbe");
            probeGo.transform.SetParent(go.transform, false);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128;
            probe.size = new Vector3(600f, 200f, 600f);
            probe.importance = 1;
            probe.hdr = true;
            probe.farClipPlane = 600f;
            wc.trackProbe = probe;
            return wc;
        }

        public static void InstallLensRain()
        {
            var shader = Shader.Find("OWR/LensRain");
            if (!shader) { Debug.LogWarning("[OWR] LensRain shader not found"); return; }
            var mat = GetMat(Root + "/Materials/LensRain.mat", "OWR/LensRain");

            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = listField?.GetValue(urp) as ScriptableRendererData[];
            if (list == null || list.Length == 0) return;
            var data = list[0];

            foreach (var f in data.rendererFeatures)
                if (f && f.name == "LensRain") return; // already installed

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "LensRain";
            feature.passMaterial = mat;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (mapField?.GetValue(data) is List<long> map && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId))
                map.Add(localId);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        // ================================================================= trackside
        public static void BuildTrackside(List<Vector3> pts, float wallOffset, Transform parent, Material fenceMat, Material postMat,
            Material bark, Material leaves, Material concrete, Material roof, Material[] seatMats, Material[] crowdMats, Material skin)
        {
            BuildCatchFence(pts, wallOffset, parent, fenceMat, postMat);
            BuildSponsorBoards(pts, (i, s) => wallOffset, parent);

            var keepOut = new List<Vector4>(); // x, z, radius (trees avoid stands)
            BuildGrandstand(parent, "Grandstand_Main", new Vector3(-(wallOffset + 5f), 0f, 30f), Quaternion.identity, 180f, 14, true,
                concrete, roof, seatMats, crowdMats, skin);
            keepOut.Add(new Vector4(-(wallOffset + 15f), 30f, 100f, 0f));
            // second tier behind the main stand
            BuildGrandstand(parent, "Grandstand_MainBack", new Vector3(-(wallOffset + 26f), 4f, 30f), Quaternion.identity, 160f, 10, true,
                concrete, roof, seatMats, crowdMats, skin);
            // opposite the main stand (inside of the straight) if there is room
            BuildGrandstand(parent, "Grandstand_MainOpp", new Vector3(wallOffset + 5f, 0f, 60f), Quaternion.Euler(0f, 180f, 0f), 120f, 9, true,
                concrete, roof, seatMats, crowdMats, skin);
            keepOut.Add(new Vector4(wallOffset + 15f, 60f, 75f, 0f));
            // bleachers all around: every ~230 m on the outside of the circuit (side away from the centroid)
            Vector3 centroid = Vector3.zero;
            foreach (var q in pts) centroid += q;
            centroid /= pts.Count;
            int n = pts.Count;
            float acc = 0f; int standIdx = 0;
            for (int i = 1; i < n; i++)
            {
                acc += Vector3.Distance(pts[i], pts[i - 1]);
                if (acc < 95f) continue;
                acc = 0f;
                Vector3 p0 = pts[i];
                if (Mathf.Abs(p0.x) < 40f && p0.z > -90f && p0.z < 150f) continue; // main stands already cover this
                Vector3 r = Right(pts, i);
                Vector3 tan = (pts[(i + 1) % n] - pts[(i - 1 + n) % n]).normalized;
                int sideOut = Vector3.Dot(r, p0 - centroid) > 0f ? 1 : -1;
                Vector3 pos = p0 + r * sideOut * (wallOffset + 4.5f);
                // stand local +x must face the track (-r * side)
                Quaternion rot = Quaternion.LookRotation(sideOut > 0 ? -tan : tan, Vector3.up);
                float len = 55f + (standIdx % 3) * 15f;
                int rows = 9 + (standIdx % 3) * 2;
                if (!ClearOfTrack(pts, pos, rot, len, rows, wallOffset)) continue;
                BuildGrandstand(parent, "Grandstand_" + standIdx, pos, rot, len, rows, standIdx % 2 == 0, concrete, roof, seatMats, crowdMats, skin);
                keepOut.Add(new Vector4(pos.x, pos.z, len * 0.5f + 15f, 0f));
                standIdx++;
                // inner-side bleacher on every other slot (only where it does not overlap the circuit)
                Vector3 posIn = p0 - r * sideOut * (wallOffset + 4.5f);
                Quaternion rotIn = Quaternion.LookRotation(sideOut > 0 ? tan : -tan, Vector3.up);
                if (standIdx % 2 == 0 && ClearOfTrack(pts, posIn, rotIn, 45f, 7, wallOffset))
                {
                    BuildGrandstand(parent, "Grandstand_In" + standIdx, posIn, rotIn, 45f, 7, false, concrete, roof, seatMats, crowdMats, skin);
                    keepOut.Add(new Vector4(posIn.x, posIn.z, 38f, 0f));
                }
            }

            BuildTyreWalls(pts, (i, s) => wallOffset, parent);
            BuildMarshalPosts(pts, (i, s) => wallOffset, parent, crowdMats);
            BuildPoles(pts, (i, s) => wallOffset, parent);
            BuildTrees(pts, wallOffset, parent, bark, leaves, keepOut);
        }

        static Vector3 Right(List<Vector3> pts, int i)
        {
            int n = pts.Count;
            Vector3 tan = (pts[(i + 1) % n] - pts[(i - 1 + n) % n]).normalized;
            return Vector3.Cross(Vector3.up, tan).normalized;
        }

        /// <summary>True when a stand's footprint stays clear of every part of the circuit.</summary>
        public static bool ClearOfTrack(List<Vector3> pts, Vector3 pos, Quaternion rot, float length, int rows, float wallOffset)
        {
            float depth = rows * 0.95f + 2f;
            for (float z = -length / 2f; z <= length / 2f; z += 6f)
                for (float x = 0f; x >= -depth; x -= 3f)
                {
                    Vector3 w = pos + rot * new Vector3(x, 0f, z);
                    for (int i = 0; i < pts.Count; i += 3)
                        if ((pts[i] - w).sqrMagnitude < (wallOffset + 1.5f) * (wallOffset + 1.5f)) return false;
                }
            return true;
        }

        static void BuildCatchFence(List<Vector3> pts, float wallOffset, Transform parent, Material fenceMat, Material postMat)
        {
            int n = pts.Count;
            for (int side = -1; side <= 1; side += 2)
            {
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                float dist = 0f;
                for (int k = 0; k <= n; k++)
                {
                    int i = k % n;
                    if (k > 0) dist += Vector3.Distance(pts[i], pts[(k - 1) % n]);
                    Vector3 p = pts[i] + Right(pts, i) * (side * (wallOffset + 0.1f));
                    verts.Add(p + Vector3.up * 1.0f); uvs.Add(new Vector2(dist / 1.2f, 0f));
                    verts.Add(p + Vector3.up * 4.4f); uvs.Add(new Vector2(dist / 1.2f, 3.4f / 1.2f));
                }
                for (int k = 0; k < n; k++) { int v = k * 2; tris.AddRange(new[] { v, v + 1, v + 3, v, v + 3, v + 2 }); }
                var mesh = new Mesh { name = "Fence", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                Obj("CatchFence_" + (side < 0 ? "L" : "R"), parent, mesh, fenceMat);

                // posts every ~6 m, leaning slightly toward the track
                var pk = new MeshKit(1);
                float acc = 0f;
                for (int i = 1; i < n; i++)
                {
                    acc += Vector3.Distance(pts[i], pts[i - 1]);
                    if (acc < 6f) continue;
                    acc = 0f;
                    Vector3 r = Right(pts, i);
                    Vector3 basePos = pts[i] + r * (side * (wallOffset + 0.18f)) + Vector3.up * 1.0f;
                    Vector3 top = basePos + Vector3.up * 3.5f - r * side * 0.5f;
                    pk.Tube(0, new List<Vector3> { basePos, top }, 0.045f, 6, 1f, false);
                }
                Obj("FencePosts_" + (side < 0 ? "L" : "R"), parent, pk.Build("FencePosts"), postMat);
            }
        }

        public static void BuildSponsorBoards(List<Vector3> pts, System.Func<int, int, float> wallAt, Transform parent)
        {
            int n = pts.Count;
            var root = new GameObject("SponsorBoards").transform;
            root.SetParent(parent, false);
            var frame = BrandKit.FrameMat();
            var kit = new BrandKit();
            var fk = new MeshKit(1);
            // each board is a straight chord fitted to the barrier: as long as the wall stays within 6 cm of it
            // (up to 7.6 m), shorter on bends, none on tight corners - so boards sit flush on the wall face
            int idx = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                System.Func<int, Vector3> W = j => { j = ((j % n) + n) % n; return pts[j] + Right(pts, j) * (side * (wallAt(j, side) - 0.27f)); };
                int i = 0;
                while (i < n)
                {
                    int best = 0;
                    for (int h = 1; h <= 20; h++)   // samples each side of the centre (~2 m spacing)
                    {
                        Vector3 a0 = W(i - h), a1 = W(i + h);
                        if (Vector3.Distance(a0, a1) > 7.7f) break;
                        bool ok = true;
                        for (int q = -h + 1; q < h && ok; q++)
                        {
                            Vector3 w = W(i + q), ab = a1 - a0;
                            float t = Mathf.Clamp01(Vector3.Dot(w - a0, ab) / ab.sqrMagnitude);
                            if (Vector3.Distance(a0 + ab * t, w) > 0.14f) ok = false;
                        }
                        if (!ok) break;
                        best = h;
                    }
                    Vector3 p0 = W(i - best), p1 = W(i + best);
                    float len = Vector3.Distance(p0, p1) - 0.3f;
                    if (best == 0 || len < 3.6f) { i += 1; continue; }
                    Vector3 along = (p1 - p0).normalized;
                    Vector3 towardTrack = Vector3.Cross(Vector3.up, along).normalized * -side;
                    if (Vector3.Dot(towardTrack, -Right(pts, i) * side) < 0f) towardTrack = -towardTrack;
                    Vector3 pos = (p0 + p1) * 0.5f + Vector3.up * 0.55f;
                    float hgt = Mathf.Min(0.85f, len / 8.9f * 1.0f + 0.0f);
                    hgt = Mathf.Max(hgt, 0.6f);
                    int b = (idx++ * 7 + side + 3) % BrandKit.Count;
                    fk.Box(0, pos - towardTrack * 0.03f, new Vector3(len + 0.1f, hgt + 0.1f, 0.05f), Quaternion.LookRotation(-towardTrack, Vector3.up));
                    kit.Quad(pos + towardTrack * 0.002f, Vector3.Cross(towardTrack, Vector3.up).normalized, Vector3.up, len, hgt, b, false);
                    i += best * 2;
                }
            }
            var fr = Obj("BoardFrames", root, fk.Build("BoardFrames"), frame);
            fr.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            kit.Build("BoardLogos", root, BrandKit.BoardMat());
        }

        /// <summary>Tiered stand with crowd. Local +x faces the track, tiers rise toward -x, length runs along local z.</summary>
        public static void BuildGrandstand(Transform parent, string name, Vector3 position, Quaternion rotation, float length, int rows, bool withRoof,
            Material concrete, Material roof, Material[] seatMats, Material[] crowdMats, Material skin)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = position;
            root.rotation = rotation;
            float stepD = 0.95f, stepH = 0.48f;

            var k = new MeshKit(2 + seatMats.Length);
            // tiers (concrete) and seat rows, rising away from the track (-x)
            for (int r = 0; r < rows; r++)
            {
                float x = -r * stepD, y = 1.2f + r * stepH;
                k.Box(0, new Vector3(x - stepD * 0.5f, y * 0.5f, 0f), new Vector3(stepD, y, length), Quaternion.identity);
                k.Box(2 + (r % seatMats.Length), new Vector3(x - stepD * 0.35f, y + 0.22f, 0f), new Vector3(0.45f, 0.42f, length - 0.4f), Quaternion.identity);
            }
            // back wall, roof and columns
            float backX = -rows * stepD - 0.3f, topY = 1.2f + rows * stepH;
            k.Box(0, new Vector3(backX, (topY + (withRoof ? 4.5f : 1.2f)) * 0.5f, 0f), new Vector3(0.4f, topY + (withRoof ? 4.5f : 1.2f), length), Quaternion.identity);
            if (withRoof)
            {
                k.Box(1, new Vector3(backX * 0.5f + 0.8f, topY + 4.6f, 0f), new Vector3(-backX + 3.2f, 0.3f, length + 2f), Quaternion.Euler(0f, 0f, -4f));
                for (float z = -length / 2f + 5f; z <= length / 2f - 5f; z += 15f)
                    k.Box(1, new Vector3(backX + 0.4f, (topY + 4.6f) * 0.5f, z), new Vector3(0.35f, topY + 4.6f, 0.35f), Quaternion.identity);
            }
            else
            {
                // open bleacher: steel handrail along the back and the front
                for (float z = -length / 2f; z <= length / 2f; z += 4f)
                    k.Box(1, new Vector3(backX + 0.3f, topY + 0.55f, z), new Vector3(0.06f, 1.1f, 0.06f), Quaternion.identity);
                k.Box(1, new Vector3(backX + 0.3f, topY + 1.1f, 0f), new Vector3(0.06f, 0.06f, length), Quaternion.identity);
            }
            var stand = Obj("Stand", root, k.Build(name), null);
            var mats = new List<Material> { concrete, roof };
            mats.AddRange(seatMats);
            stand.GetComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            stand.AddComponent<MeshCollider>();

            // crowd: simple seated figures (torso + head), randomised colours, merged into one mesh
            var ck = new MeshKit(crowdMats.Length + 1);
            var rnd = new System.Random(name.GetHashCode());
            for (int r = 0; r < rows; r++)
            {
                float x = -r * stepD - stepD * 0.45f, y = 1.2f + r * stepH + 0.42f;
                for (float z = -length / 2f + 1f; z < length / 2f - 1f; z += 0.62f)
                {
                    if (rnd.NextDouble() < 0.18) continue; // empty seats
                    float jx = (float)(rnd.NextDouble() - 0.5) * 0.08f, jz = (float)(rnd.NextDouble() - 0.5) * 0.15f;
                    float h = 0.5f + (float)rnd.NextDouble() * 0.12f;
                    int c = rnd.Next(crowdMats.Length);
                    ck.uv2Value = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                    ck.Box(c, new Vector3(x + jx, y + h * 0.5f, z + jz), new Vector3(0.32f, h, 0.42f), Quaternion.Euler(0f, 0f, (float)(rnd.NextDouble() - 0.5) * 8f));
                    ck.Box(crowdMats.Length, new Vector3(x + jx + 0.02f, y + h + 0.12f, z + jz), new Vector3(0.2f, 0.22f, 0.18f), Quaternion.identity);
                }
            }
            var crowd = Obj("Crowd", root, ck.Build(name + "_Crowd"), null);
            var cm = new List<Material>();
            foreach (var src in crowdMats) cm.Add(CrowdMat(src));
            cm.Add(CrowdMat(skin));
            crowd.GetComponent<MeshRenderer>().sharedMaterials = cm.ToArray();
            var hype = crowd.AddComponent<OWR.Track.CrowdHype>();
            hype.crowd = crowd.GetComponent<MeshRenderer>();
            crowd.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // big brand fascia on the roof edge, facing the track (+x)
            int fascias = withRoof ? Mathf.Max(1, Mathf.FloorToInt(length / 45f)) : 0;
            if (fascias > 0)
            {
                var kit = new BrandKit();
                for (int i = 0; i < fascias; i++)
                {
                    float z = -length / 2f + 22f + i * 45f;
                    float w = Mathf.Min(26f, length * 0.8f);
                    var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(panel.GetComponent<Collider>());
                    panel.name = "FasciaPanel";
                    panel.transform.SetParent(root, false);
                    panel.transform.localPosition = new Vector3(2.3f, topY + 4.0f, z);
                    panel.transform.localScale = new Vector3(0.1f, w / 8f + 0.3f, w + 0.4f);
                    panel.GetComponent<Renderer>().sharedMaterial = BrandKit.FrameMat();
                    kit.Quad(root.TransformPoint(new Vector3(2.36f, topY + 4.0f, z)), root.TransformDirection(Vector3.forward), Vector3.up, w, w / 8f, i * 3 + 1 + name.Length, false);
                }
                kit.Build("Fascia", root, BrandKit.BoardMat());
            }
        }

        static void BuildTrees(List<Vector3> pts, float wallOffset, Transform parent, Material bark, Material leaves, List<Vector4> keepOut)
        {
            var variants = new Mesh[3];
            for (int v = 0; v < 3; v++) variants[v] = TreeMesh(v);
            var root = new GameObject("Trees").transform;
            root.SetParent(parent, false);
            var rnd = new System.Random(7);
            int placed = 0;
            int n = pts.Count;
            // grid-check distance to the track (coarse samples)
            var coarse = new List<Vector3>();
            for (int i = 0; i < n; i += 5) coarse.Add(pts[i]);
            for (int attempt = 0; attempt < 6000 && placed < 520; attempt++)
            {
                var p = new Vector3((float)rnd.NextDouble() * 760f - 230f, 0f, (float)rnd.NextDouble() * 900f - 330f);
                float best = float.MaxValue;
                foreach (var c in coarse) best = Mathf.Min(best, (c - p).sqrMagnitude);
                float d = Mathf.Sqrt(best);
                if (d < wallOffset + 16f || d > wallOffset + 170f) continue;
                bool blocked = false;
                foreach (var ko in keepOut) if (new Vector2(p.x - ko.x, p.z - ko.y).magnitude < ko.z) { blocked = true; break; }
                if (blocked) continue;
                int v = rnd.Next(3);
                var g = new GameObject("Tree");
                g.transform.SetParent(root, false);
                g.transform.position = p;
                g.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                float sc = 0.8f + (float)rnd.NextDouble() * 0.7f;
                g.transform.localScale = new Vector3(sc, sc * (0.85f + (float)rnd.NextDouble() * 0.4f), sc);
                g.AddComponent<MeshFilter>().sharedMesh = variants[v];
                g.AddComponent<MeshRenderer>().sharedMaterials = new[] { bark, leaves };
                g.isStatic = true;
                placed++;
            }
        }

        /// <summary>Broadleaf tree: tapered trunk with a couple of branches and a clumped canopy of lumpy ellipsoids.</summary>
        public static Mesh TreeMesh(int seed)
        {
            string path = $"{Root}/Meshes/Tree_{seed}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            var rnd = new System.Random(100 + seed);
            var k = new MeshKit(2);
            float h = 7f + seed * 1.5f;
            k.Tube(0, new List<Vector3> { Vector3.zero, new Vector3(0.1f, h * 0.4f, 0f), new Vector3(-0.1f, h * 0.75f, 0.1f) }, 0.28f, 8, 1f, false, t => Mathf.Lerp(1f, 0.45f, t));
            for (int b = 0; b < 3; b++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                Vector3 s = new Vector3(0f, h * (0.45f + b * 0.1f), 0f);
                Vector3 e = s + new Vector3(Mathf.Cos(a) * 2f, 1.6f, Mathf.Sin(a) * 2f);
                k.Tube(0, new List<Vector3> { s, (s + e) * 0.5f + Vector3.up * 0.3f, e }, 0.12f, 6, 1f, false, t => Mathf.Lerp(1f, 0.5f, t));
            }
            int clumps = 7 + seed * 2;
            for (int c = 0; c < clumps; c++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, rr = (float)rnd.NextDouble() * 2.4f;
                Vector3 ctr = new Vector3(Mathf.Cos(a) * rr, h * 0.72f + (float)rnd.NextDouble() * h * 0.35f, Mathf.Sin(a) * rr);
                float sz = 1.6f + (float)rnd.NextDouble() * 1.4f;
                k.M = Matrix4x4.TRS(ctr, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), Vector3.one);
                var secs = new List<MeshKit.Sec>
                {
                    new MeshKit.Sec(sz, 0, 0, sz * 0.2f, sz * 0.2f, sz * 0.15f, 2f),
                    new MeshKit.Sec(sz * 0.55f, 0, 0.05f * sz, sz * 0.85f, sz * 0.75f, sz * 0.55f, 2.1f),
                    new MeshKit.Sec(0f, 0, 0.1f * sz, sz, sz * 0.85f, sz * 0.6f, 2.2f),
                    new MeshKit.Sec(-sz * 0.55f, 0, 0.05f * sz, sz * 0.8f, sz * 0.7f, sz * 0.5f, 2.1f),
                    new MeshKit.Sec(-sz, 0, 0, sz * 0.2f, sz * 0.2f, sz * 0.15f, 2f),
                };
                k.Loft(1, secs, 10, 2, capStartSub: 1, capEndSub: 1);
            }
            k.M = Matrix4x4.identity;
            var mesh = k.Build("Tree_" + seed);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }



        // ================================================================= imported pack props (BEDRILL)
        const string Pack = "Assets/BEDRILL/Track_Environment_Free";

        static Material PackMaterial()
        {
            var m = GetMat(Root + "/Materials/Pack_Palette_URP.mat", "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Pack + "/Textures/Track_Texture.png"));
            m.SetColor("_BaseColor", new Color(0.82f, 0.82f, 0.82f));
            m.SetFloat("_Smoothness", 0.45f); // wet
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Places a pack model so its long side runs along the track and its low (front) side faces the track,
        /// scaled to targetLength. Works without knowing the model's authoring orientation.
        /// </summary>
        static GameObject PlacePackModel(string file, Transform parent, Vector3 pos, Vector3 towardTrack, float targetLength, Material mat, bool collider)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/Models/{file}");
            if (!src) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.SetParent(parent, false);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            // measure in the model's own space
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var mfs = go.GetComponentsInChildren<MeshFilter>();
            Bounds b = new Bounds(); bool first = true;
            var verts = new List<Vector3>();
            foreach (var mf in mfs)
            {
                if (!mf.sharedMesh) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 w = mf.transform.TransformPoint(v);
                    verts.Add(w);
                    if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
                }
            }
            bool longX = b.size.x >= b.size.z;
            Vector3 longAxis = longX ? Vector3.right : Vector3.forward;
            Vector3 shortAxis = longX ? Vector3.forward : Vector3.right;
            // which half of the short axis is taller? that is the back; the front points the other way
            float hiPos = 0f, hiNeg = 0f; int nPos = 0, nNeg = 0;
            foreach (var w in verts)
            {
                float s = Vector3.Dot(w - b.center, shortAxis);
                if (s > 0f) { hiPos += w.y; nPos++; } else { hiNeg += w.y; nNeg++; }
            }
            Vector3 frontLocal = (nPos > 0 && nNeg > 0 && hiPos / nPos > hiNeg / nNeg) ? -shortAxis : shortAxis;
            float scale = targetLength / Mathf.Max(longX ? b.size.x : b.size.z, 0.01f);

            towardTrack.y = 0f; towardTrack.Normalize();
            Quaternion rot = Quaternion.FromToRotation(frontLocal, towardTrack);
            go.transform.localScale = Vector3.one * scale;
            go.transform.rotation = rot;
            Vector3 centreOffset = rot * (new Vector3(b.center.x, b.min.y, b.center.z) * scale);
            go.transform.position = pos - centreOffset;
            if (collider) foreach (var mf in mfs) if (mf.sharedMesh && !mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        /// <summary>Seats a muted crowd on any stand by raycasting down onto its steps.</summary>
        static void SeatCrowd(GameObject stand, Material[] crowdMats, Material skin, int seed)
        {
            Physics.SyncTransforms();
            var cols = stand.GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return;
            Bounds b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
            var k = new MeshKit(crowdMats.Length + 1);
            var rnd = new System.Random(seed);
            var root = stand.transform;
            int people = 0;
            for (float x = b.min.x + 0.4f; x < b.max.x; x += 0.6f)
                for (float z = b.min.z + 0.4f; z < b.max.z; z += 0.6f)
                {
                    if (rnd.NextDouble() < 0.22) continue;
                    var ray = new Ray(new Vector3(x, b.max.y + 2f, z), Vector3.down);
                    bool hitStand = false; RaycastHit hit = default;
                    foreach (var c in cols) if (c.Raycast(ray, out var h, b.size.y + 4f) && (!hitStand || h.distance < hit.distance)) { hit = h; hitStand = true; }
                    if (!hitStand || hit.normal.y < 0.85f || hit.point.y < b.min.y + 0.6f || hit.point.y > b.max.y - 1.0f) continue;
                    float h2 = 0.5f + (float)rnd.NextDouble() * 0.15f;
                    int cm = rnd.Next(crowdMats.Length);
                    Vector3 p = hit.point;
                    k.Box(cm, p + Vector3.up * (h2 * 0.5f + 0.25f), new Vector3(0.36f, h2, 0.32f), Quaternion.Euler(0f, (float)rnd.NextDouble() * 30f - 15f, 0f));
                    k.Box(crowdMats.Length, p + Vector3.up * (h2 + 0.37f), new Vector3(0.19f, 0.22f, 0.19f), Quaternion.identity);
                    people++;
                }
            if (people == 0) return;
            var g = new GameObject(stand.name + "_Crowd");
            g.transform.SetParent(root.parent, false); // world-space mesh under an identity parent
            g.AddComponent<MeshFilter>().sharedMesh = k.Build("PackCrowd_" + seed);
            var mr = g.AddComponent<MeshRenderer>();
            var mats = new List<Material>(crowdMats) { skin };
            mr.sharedMaterials = mats.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Pack tribunes all around the circuit (outside of the walls) + the start/finish arch.</summary>
        static void BuildPackStands(List<Vector3> pts, float wallOffset, Transform parent, Material[] crowdMats, Material skin, List<Vector4> keepOut)
        {
            var mat = PackMaterial();
            var root = new GameObject("Grandstands").transform; root.SetParent(parent, false);
            Vector3 centroid = Vector3.zero; foreach (var q in pts) centroid += q; centroid /= pts.Count;
            int n = pts.Count; float acc = 0f; int idx = 0;
            for (int i = 1; i < n; i++)
            {
                acc += Vector3.Distance(pts[i], pts[i - 1]);
                bool mainStraight = Mathf.Abs(pts[i].x) < 30f && pts[i].z > -90f && pts[i].z < 330f;
                if (acc < (mainStraight ? 48f : 140f)) continue;
                acc = 0f;
                Vector3 r = Right(pts, i);
                int side = mainStraight ? -1 : (Vector3.Dot(r, pts[i] - centroid) > 0f ? 1 : -1);
                Vector3 pos = pts[i] + r * side * (wallOffset + 8f);
                var stand = PlacePackModel("Tribune_free.fbx", root, pos, -r * side, mainStraight ? 46f : 34f, mat, true);
                if (!stand) return;
                stand.name = "Grandstand_" + idx;
                SeatCrowd(stand, crowdMats, skin, 100 + idx);
                keepOut.Add(new Vector4(pos.x, pos.z, 32f, 0f));
                idx++;
            }
            // start / finish arch over the straight
            int a = 30;
            Vector3 tan = (pts[a + 1] - pts[a - 1]).normalized;
            var arch = PlacePackModel("Arch_banner_finish_free.fbx", parent, pts[a], tan, 2f * wallOffset - 6f, mat, false);
            if (arch) arch.name = "StartFinishArch";
        }

        // ================================================================= trackside life
        public static float[] Curv(List<Vector3> pts)
        {
            int n = pts.Count;
            var c = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = (pts[i] - pts[(i - 3 + n) % n]).normalized, b = (pts[(i + 3) % n] - pts[i]).normalized;
                c[i] = Vector3.SignedAngle(a, b, Vector3.up) * Mathf.Deg2Rad / Mathf.Max(Vector3.Distance(pts[(i - 3 + n) % n], pts[(i + 3) % n]) * 0.5f, 0.01f);
            }
            return c;
        }

        /// <summary>Stacked tyre barriers (with a coloured belt) in front of the wall on the outside of corners.</summary>
        public static void BuildTyreWalls(List<Vector3> pts, System.Func<int, int, float> wallAt, Transform parent, float groundY = 0f)
        {
            var rubber = GetMat(Root + "/Materials/TyreWall_Rubber.mat", "Universal Render Pipeline/Lit");
            rubber.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.055f)); rubber.SetFloat("_Smoothness", 0.55f);
            var belt = GetMat(Root + "/Materials/TyreWall_Belt.mat", "Universal Render Pipeline/Lit");
            belt.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f)); belt.SetFloat("_Smoothness", 0.5f);
            // one tyre-stack mesh (3 high), instanced along the barrier
            var k = new MeshKit(2);
            for (int h = 0; h < 3; h++)
            {
                k.M = Matrix4x4.TRS(new Vector3(0f, 0.12f + h * 0.24f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
                k.Lathe(0, new List<Vector2> { new Vector2(-0.11f, 0.22f), new Vector2(-0.12f, 0.29f), new Vector2(-0.08f, 0.32f), new Vector2(0.08f, 0.32f), new Vector2(0.12f, 0.29f), new Vector2(0.11f, 0.22f), new Vector2(-0.11f, 0.22f) }, 14);
            }
            k.M = Matrix4x4.identity;
            k.Box(1, new Vector3(0f, 0.36f, -0.33f), new Vector3(0.66f, 0.22f, 0.02f), Quaternion.identity);
            var stack = k.Build("TyreStack");
            string mp = Root + "/Meshes/TyreStack.asset"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(stack, mp);

            var root = new GameObject("TyreWalls").transform; root.SetParent(parent, false);
            var c = Curv(pts);
            int n = pts.Count;
            // merged into chunks (one object per ~400 stacks) instead of one object per stack
            var rubberParts = new List<CombineInstance>(); var beltParts = new List<CombineInstance>();
            int chunk = 0;
            System.Action flush = () =>
            {
                if (rubberParts.Count == 0) return;
                var a = new Mesh { indexFormat = IndexFormat.UInt32 }; a.CombineMeshes(rubberParts.ToArray(), true, true);
                var b = new Mesh { indexFormat = IndexFormat.UInt32 }; b.CombineMeshes(beltParts.ToArray(), true, true);
                var m = new Mesh { name = "TyreWall_" + chunk, indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(new[] { new CombineInstance { mesh = a }, new CombineInstance { mesh = b } }, false, false);
                var g = Obj("TyreWall_" + chunk++, root, m, null);
                g.GetComponent<MeshRenderer>().sharedMaterials = new[] { rubber, belt };
                rubberParts.Clear(); beltParts.Clear();
            };
            for (int i = 0; i < n; i += 1)
            {
                if (Mathf.Abs(c[i]) < 1f / 140f) continue;
                int outer = c[i] > 0f ? -1 : 1; // turning right -> outside is left
                Vector3 r = Right(pts, i);
                for (int row = 0; row < 2; row++)
                {
                    var mtx = Matrix4x4.TRS(pts[i] + r * outer * (wallAt(i, outer) - 0.75f - row * 0.62f) + Vector3.up * groundY, Quaternion.LookRotation(-r * outer, Vector3.up), Vector3.one);
                    rubberParts.Add(new CombineInstance { mesh = stack, subMeshIndex = 0, transform = mtx });
                    beltParts.Add(new CombineInstance { mesh = stack, subMeshIndex = 1, transform = mtx });
                }
                if (rubberParts.Count >= 400) flush();
            }
            flush();
        }

        /// <summary>Marshal posts: small hut, two marshals in orange, yellow flag on a pole.</summary>
        public static void BuildMarshalPosts(List<Vector3> pts, System.Func<int, int, float> wallAt, Transform parent, Material[] crowdMats, float groundY = 0f)
        {
            var orange = GetMat(Root + "/Materials/Marshal_Orange.mat", "Universal Render Pipeline/Lit");
            orange.SetColor("_BaseColor", new Color(1f, 0.42f, 0.05f)); orange.SetFloat("_Smoothness", 0.5f);
            var hut = GetMat(Root + "/Materials/Marshal_Hut.mat", "Universal Render Pipeline/Lit");
            hut.SetColor("_BaseColor", new Color(0.85f, 0.86f, 0.88f)); hut.SetFloat("_Smoothness", 0.4f);
            var flag = GetMat(Root + "/Materials/Flag_Yellow.mat", "Universal Render Pipeline/Lit");
            flag.SetColor("_BaseColor", new Color(1f, 0.85f, 0.05f)); flag.SetFloat("_Cull", 0f);
            var k = new MeshKit(3);
            k.Box(1, new Vector3(0f, 1.2f, 0f), new Vector3(2.2f, 2.4f, 2.0f), Quaternion.identity);
            k.Box(1, new Vector3(0f, 2.5f, 0.2f), new Vector3(2.6f, 0.15f, 2.6f), Quaternion.identity);
            for (int m = 0; m < 2; m++)
            {
                float z = -0.6f + m * 1.2f;
                k.Box(0, new Vector3(1.6f, 0.45f, z), new Vector3(0.32f, 0.9f, 0.25f), Quaternion.identity);
                k.Box(0, new Vector3(1.6f, 1.2f, z), new Vector3(0.38f, 0.65f, 0.3f), Quaternion.identity);
                k.Box(0, new Vector3(1.6f, 1.66f, z), new Vector3(0.2f, 0.24f, 0.2f), Quaternion.identity);
            }
            k.Tube(1, new List<Vector3> { new Vector3(1.9f, 0f, 1.1f), new Vector3(1.9f, 3.2f, 1.1f) }, 0.03f, 6);
            k.Box(2, new Vector3(1.9f, 2.85f, 1.55f), new Vector3(0.01f, 0.6f, 0.85f), Quaternion.Euler(0f, 0f, 4f));
            var mesh = k.Build("MarshalPost");
            string mp = Root + "/Meshes/MarshalPost.asset"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var root = new GameObject("MarshalPosts").transform; root.SetParent(parent, false);
            int n = pts.Count; float acc = 0f; int side = 1;
            for (int i = 1; i < n; i++)
            {
                acc += Vector3.Distance(pts[i], pts[i - 1]);
                if (acc < 190f) continue;
                acc = 0f; side = -side;
                Vector3 r = Right(pts, i);
                var g = new GameObject("MarshalPost");
                g.transform.SetParent(root, false);
                g.transform.position = pts[i] + r * side * (wallAt(i, side) + 3.0f) + Vector3.up * groundY;
                g.transform.rotation = Quaternion.LookRotation(Vector3.Cross(-r * side, Vector3.up) * -1f, Vector3.up);
                // make local +x face the track
                g.transform.rotation = Quaternion.LookRotation(side > 0 ? -(Vector3.Cross(r, Vector3.up)) : Vector3.Cross(r, Vector3.up), Vector3.up);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = new[] { orange, hut, flag };
                g.isStatic = true;
            }
        }

        /// <summary>Tall floodlight / TV camera poles behind the fence.</summary>
        public static void BuildPoles(List<Vector3> pts, System.Func<int, int, float> wallAt, Transform parent, float groundY = 0f)
        {
            var steel = GetMat(Root + "/Materials/Pole_Steel.mat", "Universal Render Pipeline/Lit");
            steel.SetColor("_BaseColor", new Color(0.55f, 0.57f, 0.6f)); steel.SetFloat("_Metallic", 0.8f); steel.SetFloat("_Smoothness", 0.55f);
            var k = new MeshKit(1);
            k.Tube(0, new List<Vector3> { Vector3.zero, new Vector3(0f, 18f, 0f) }, 0.22f, 10, 1f, true, t => Mathf.Lerp(1f, 0.55f, t));
            k.Box(0, new Vector3(0f, 18.2f, 0.6f), new Vector3(3.2f, 1.6f, 0.3f), Quaternion.Euler(-15f, 0f, 0f));
            k.Box(0, new Vector3(0f, 12f, 0.5f), new Vector3(1.2f, 0.2f, 1.2f), Quaternion.identity); // camera platform
            var mesh = k.Build("TrackPole");
            string mp = Root + "/Meshes/TrackPole.asset"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var root = new GameObject("Poles").transform; root.SetParent(parent, false);
            int n = pts.Count; float acc = 0f; int side = -1;
            for (int i = 1; i < n; i++)
            {
                acc += Vector3.Distance(pts[i], pts[i - 1]);
                if (acc < 120f) continue;
                acc = 0f; side = -side;
                Vector3 r = Right(pts, i);
                var g = new GameObject("Pole");
                g.transform.SetParent(root, false);
                g.transform.position = pts[i] + r * side * (wallAt(i, side) + 6.5f) + Vector3.up * groundY;
                g.transform.rotation = Quaternion.LookRotation(-r * side, Vector3.up);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = steel;
                g.isStatic = true;
            }
        }

        /// <summary>Start-light gantry spanning the main straight, with five pairs of lights and a sponsor face.</summary>
        static void BuildStartGantry(List<Vector3> pts, Transform parent)
        {
            var steel = GetMat(Root + "/Materials/Pole_Steel.mat", "Universal Render Pipeline/Lit");
            var lamp = GetMat(Root + "/Materials/Gantry_Lamp.mat", "Universal Render Pipeline/Lit");
            lamp.SetColor("_BaseColor", new Color(0.2f, 0.02f, 0.02f)); lamp.SetFloat("_Smoothness", 0.9f);
            var face = GetMat($"{Root}/Materials/Board_{Brands[0]}.mat", "Universal Render Pipeline/Lit");
            var k = new MeshKit(3);
            float half = 13f;
            for (int s = -1; s <= 1; s += 2) k.Box(0, new Vector3(s * half, 3.5f, 0f), new Vector3(0.5f, 7f, 0.5f), Quaternion.identity);
            k.Box(0, new Vector3(0f, 6.6f, 0f), new Vector3(half * 2f + 0.5f, 0.7f, 0.6f), Quaternion.identity);
            k.Box(2, new Vector3(0f, 7.4f, -0.05f), new Vector3(10f, 0.9f, 0.4f), Quaternion.identity);
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 2; j++)
                {
                    k.M = Matrix4x4.TRS(new Vector3(-1.6f + i * 0.8f, 6.4f + j * 0.42f, -0.32f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
                    k.Lathe(1, new List<Vector2> { new Vector2(0f, 0.01f), new Vector2(0f, 0.16f), new Vector2(0.05f, 0.16f), new Vector2(0.05f, 0.01f), new Vector2(0f, 0.01f) }, 14);
                }
            k.M = Matrix4x4.identity;
            var mesh = k.Build("StartGantry");
            string mp = Root + "/Meshes/StartGantry.asset"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var g = new GameObject("StartGantry");
            g.transform.SetParent(parent, false);
            int i0 = 60;
            Vector3 tan = (pts[i0 + 1] - pts[i0 - 1]).normalized;
            g.transform.position = pts[i0];
            g.transform.rotation = Quaternion.LookRotation(tan, Vector3.up);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterials = new[] { steel, lamp, face };
            g.AddComponent<MeshCollider>();
        }

        /// <summary>Animated (jumping) crowd material with the colour of the given lit material.</summary>
        public static Material CrowdMat(Material src)
        {
            var m = GetMat($"{Root}/Materials/CrowdAnim_{src.name}.mat", "OWR/Crowd");
            m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ================================================================= helpers
        public static Material GetMat(string path, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader.name != shader)
            {
                if (m != null) AssetDatabase.DeleteAsset(path);
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        public static GameObject Obj(string name, Transform parent, Mesh mesh, Material mat)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            if (mat) r.sharedMaterial = mat;
            g.isStatic = true;
            return g;
        }

        /// <summary>Soft round particle sprite and a chain-link alpha texture.</summary>
        public static Texture2D SoftDot()
        {
            string p = Root + "/Textures/SoftDot.asset";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t) return t;
            const int S = 64;
            t = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply(true);
            AssetDatabase.CreateAsset(t, p);
            return t;
        }

        public static Texture2D ChainLink()
        {
            string p = Root + "/Textures/ChainLink.asset";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t) return t;
            const int S = 128;
            t = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "ChainLink", wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // diamond mesh: 8 x 8 cells per tile
                    float u = x / (float)S * 8f, v = y / (float)S * 8f;
                    float a1 = Mathf.Abs(Mathf.Repeat(u + v, 1f) - 0.5f), a2 = Mathf.Abs(Mathf.Repeat(u - v, 1f) - 0.5f);
                    float wire = Mathf.Max(Mathf.SmoothStep(0.44f, 0.5f, a1), Mathf.SmoothStep(0.44f, 0.5f, a2));
                    t.SetPixel(x, y, new Color(0.7f, 0.72f, 0.74f, wire));
                }
            t.Apply(true);
            AssetDatabase.CreateAsset(t, p);
            return t;
        }
    }
}
