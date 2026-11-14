using UnityEngine;

namespace OWR.Track
{
    /// <summary>
    /// Drives a stand's crowd excitement: idles low, erupts when a car passes close and fast,
    /// and goes wild on race events (lights out, final lap, finish) via the static Boost.
    /// </summary>
    public class CrowdHype : MonoBehaviour
    {
        public Renderer crowd;
        public float idle = 0.12f;
        public float range = 90f;

        static float globalBoost;
        static float boostUntil;
        /// <summary>Make every stand go wild for a few seconds.</summary>
        public static void Boost(float amount, float seconds) { globalBoost = amount; boostUntil = Time.time + seconds; }
        /// <summary>Current global boost (0 when none).</summary>
        public static float Level => Time.time < boostUntil ? globalBoost : 0f;

        static readonly int HypeId = Shader.PropertyToID("_Hype");
        MaterialPropertyBlock mpb;
        float hype;
        Vehicle.CarController[] cars;
        float refreshTimer;

        void Start() { mpb = new MaterialPropertyBlock(); }

        void Update()
        {
            if (!crowd) return;
            refreshTimer -= Time.deltaTime;
            if (cars == null || refreshTimer <= 0f) { cars = FindObjectsByType<Vehicle.CarController>(FindObjectsSortMode.None); refreshTimer = 2f; }

            float target = idle;
            Vector3 p = crowd.bounds.center;
            foreach (var c in cars)
            {
                if (!c) continue;
                float d = Vector3.Distance(c.transform.position, p);
                float k = Mathf.Clamp01(1f - d / range) * Mathf.Clamp01(Mathf.Abs(c.telemetry.speedMs) / 40f);
                target = Mathf.Max(target, idle + k);
            }
            if (Time.time < boostUntil) target = Mathf.Max(target, globalBoost);
            hype = Mathf.MoveTowards(hype, Mathf.Clamp01(target), Time.deltaTime * (target > hype ? 1.5f : 0.35f));
            crowd.GetPropertyBlock(mpb);
            mpb.SetFloat(HypeId, hype);
            crowd.SetPropertyBlock(mpb);
        }
    }
}
