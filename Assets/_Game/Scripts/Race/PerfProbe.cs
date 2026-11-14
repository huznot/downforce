using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OWR.Race
{
    /// <summary>
    /// Web performance probe. Add URL flags to switch systems off one at a time and read "[perf]" lines in the
    /// browser console: ?perf (log only) &amp;scale=0.5 &amp;noshadow &amp;nopost &amp;nocrowd &amp;norain &amp;noaudio &amp;nocars &amp;nophys.
    /// Does nothing unless the page URL contains "perf".
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        string q = "";
        float timer, frames, worst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer) return;
            string url = Application.absoluteURL ?? "";
            if (!url.Contains("perf")) return;
            var go = new GameObject("PerfProbe"); DontDestroyOnLoad(go);
            go.AddComponent<PerfProbe>().q = url.Contains("?") ? url.Substring(url.IndexOf('?')) : "";
        }

        bool Has(string k) => q.Contains(k);

        void Start()
        {
            Debug.Log("[perf] flags " + q);
            Invoke(nameof(ApplyFlags), 1f);
        }

        void ApplyFlags()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int si = q.IndexOf("scale=");
            if (si >= 0 && urp && float.TryParse(q.Substring(si + 6).Split('&')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sc))
            {
                var aq = FindFirstObjectByType<AdaptiveQuality>(); if (aq) aq.enabled = false;
                urp.renderScale = sc;
            }
            if (Has("noshadow"))
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = LightShadows.None;
            if (Has("nopost"))
            {
                foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false;
                foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                { var d = c.GetComponent<UniversalAdditionalCameraData>(); if (d) d.renderPostProcessing = false; }
            }
            if (Has("nocrowd"))
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.sharedMaterial && r.sharedMaterial.shader && r.sharedMaterial.shader.name.Contains("Crowd")) r.enabled = false;
            if (Has("norain"))
            {
                foreach (var p in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
                var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(urp) as ScriptableRendererData[];
                if (list != null) foreach (var data in list) if (data) foreach (var f in data.rendererFeatures) if (f && f.name.Contains("Rain")) f.SetActive(false);
            }
            if (Has("noaudio"))
                foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) a.enabled = false;
            if (Has("nocars"))
            {
                var rm = RaceManager.Instance;
                foreach (var c in FindObjectsByType<Vehicle.CarController>(FindObjectsSortMode.None))
                    if (!rm || rm.Player == null || c != rm.Player.car) c.gameObject.SetActive(false);
            }
            if (Has("nophys")) Time.fixedDeltaTime = 1f / 30f;
            Debug.Log("[perf] flags applied");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            frames++; timer += dt; worst = Mathf.Max(worst, dt);
            if (timer >= 2f)
            {
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Debug.Log($"[perf] fps {frames / timer:F1}  worst {worst * 1000f:F0} ms  scale {(urp ? urp.renderScale : 0f):F2}  fixed {1f / Time.fixedDeltaTime:F0} Hz  " +
                          $"screen {Screen.width}x{Screen.height}  sources {FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length}");
                frames = 0; timer = 0; worst = 0;
            }
        }
    }
}


