using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OWR.Race
{
    /// <summary>
    /// Web + mobile performance governor. Physics runs at 120 Hz (desktop keeps 240), frame catch-up is capped so a
    /// slow frame can't snowball, and the 3D render resolution adapts every half second to hold ~55-60 fps:
    /// full sharpness on a strong GPU, gracefully softer on a weak one. UI always renders at native resolution.
    /// </summary>
    public class AdaptiveQuality : MonoBehaviour
    {
        public static bool LightPlatform =>
            Application.platform == RuntimePlatform.WebGLPlayer || Application.isMobilePlatform;
        public static float PhysicsHz => LightPlatform ? 120f : 240f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (!LightPlatform) return;
            Time.fixedDeltaTime = 1f / PhysicsHz;
            Time.maximumDeltaTime = 1f / 20f;
            if (Application.isMobilePlatform)
            {
                Application.targetFrameRate = 60;
                QualitySettings.vSyncCount = 0;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            var go = new GameObject("AdaptiveQuality"); DontDestroyOnLoad(go); go.AddComponent<AdaptiveQuality>();
        }

        /// <summary>
        /// Browsers compile each shader the first time it is drawn, which froze the game for ~1 s whenever a new
        /// material came into view. Compile everything up front while the title screen is showing instead.
        /// </summary>
        System.Collections.IEnumerator Warmup()
        {
            yield return null;
            var cam = Camera.main;
            if (!cam) foreach (var c in Camera.allCameras) if (c.enabled) { cam = c; break; }
            if (!cam) yield break;
            float t0 = Time.realtimeSinceStartup;
            var mats = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials) if (m) mats.Add(m);
            var fx = FindFirstObjectByType<ExplosionFX>();
            if (fx)
                foreach (var f in typeof(ExplosionFX).GetFields())
                    if (f.FieldType == typeof(Material) && f.GetValue(fx) is Material m && m) mats.Add(m);
            // one tiny quad per material just in front of the lens for two frames: each shader compiles now,
            // on the title screen, instead of freezing the race the first time it appears
            var root = new GameObject("ShaderWarmup").transform;
            root.SetParent(cam.transform, false);
            root.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.05f);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<MeshFilter>().sharedMesh;
            int i = 0;
            foreach (var m in mats)
            {
                var g = new GameObject("w", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(root, false);
                g.transform.localPosition = new Vector3((i % 40) * 0.0004f - 0.008f, (i / 40) * 0.0004f - 0.004f, 0f);
                g.transform.localScale = Vector3.one * 0.0003f;
                g.GetComponent<MeshFilter>().sharedMesh = quad;
                var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.On;
                i++;
            }
            yield return null; yield return null; yield return null;
            Destroy(root.gameObject);
            Debug.Log($"[perf] warmed {mats.Count} materials in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
        }

        UniversalRenderPipelineAsset urp;
        float scale = 1f, avg = 1f / 60f, timer, goodFor;
        float baseShadowDistance;

        void Start()
        {
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!urp) { enabled = false; return; }
            baseShadowDistance = urp.shadowDistance;
            scale = Application.isMobilePlatform ? 0.8f : 1f;
            // the costly extras that barely show at race speed in the rain: SSAO off, 2 shadow cascades, shorter range
            urp.shadowCascadeCount = 2;
            urp.shadowDistance = Mathf.Min(baseShadowDistance, Application.isMobilePlatform ? 70f : 110f);
            if (Application.isMobilePlatform) urp.msaaSampleCount = 1;
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(urp) as ScriptableRendererData[];
            if (list != null)
                foreach (var data in list)
                    if (data) foreach (var f in data.rendererFeatures)
                            if (f && f.name.Contains("AmbientOcclusion")) f.SetActive(false);
            Apply();
            StartCoroutine(Warmup());
        }

        void OnDestroy() { if (urp) { urp.renderScale = 1f; urp.shadowDistance = baseShadowDistance; } }

        void Apply() => urp.renderScale = scale;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.5f) return;               // ignore load hitches / tab switches
            avg = Mathf.Lerp(avg, dt, 0.08f);
            timer += dt;
            if (timer < 0.5f) return;
            timer = 0f;
            float fps = 1f / avg;
            if (fps < 48f && scale > 0.55f) { scale = Mathf.Max(0.55f, scale - (fps < 35f ? 0.1f : 0.05f)); goodFor = 0f; Apply(); }
            else if (fps > 58f) { goodFor += 0.5f; if (goodFor >= 3f && scale < 1f) { scale = Mathf.Min(1f, scale + 0.05f); goodFor = 0f; Apply(); } }
            else goodFor = 0f;
            // if the GPU is still struggling at the lowest resolution, shorten the shadow range too
            if (scale <= 0.56f && fps < 40f) urp.shadowDistance = Mathf.Max(40f, urp.shadowDistance - 10f);
        }
    }
}
