using OWR.Vehicle;
using UnityEngine;

namespace OWR.Weather
{
    /// <summary>
    /// Drives the wet-race atmosphere: falling rain around the active camera, tyre spray that grows with speed,
    /// lens droplets (global shader params read by the LensRain full-screen pass), a camera-following reflection
    /// probe for the wet track, and reduced tyre grip.
    /// </summary>
    public class WeatherController : MonoBehaviour
    {
        public CarController car;
        [Range(0f, 1f)] public float rain = 0.75f;
        [Range(0f, 1f)] public float wetness = 0.85f;
        [Tooltip("Tyre grip multiplier on a fully wet track.")]
        public float wetGrip = 0.90f;

        public ParticleSystem rainSystem;
        public ParticleSystem[] spraySystems;   // one per wheel, children of the wheel steer pivots
        public float maxRainRate = 9000f;
        public float maxSprayRate = 220f;
        public ReflectionProbe trackProbe;

        static readonly int RainId = Shader.PropertyToID("_OWR_Rain");
        static readonly int SpeedId = Shader.PropertyToID("_OWR_Speed");
        static readonly int SteerId = Shader.PropertyToID("_OWR_Steer");

        void Update()
        {
            Camera cam = ActiveCamera();
            float speed = car ? Mathf.Abs(car.telemetry.speedMs) : 0f;

            RaycastWheel.GlobalGripScale = Mathf.Lerp(1f, wetGrip, wetness);

            Shader.SetGlobalFloat(RainId, rain);
            Shader.SetGlobalFloat(SpeedId, speed);
            Shader.SetGlobalFloat(SteerId, car ? car.telemetry.steer : 0f);

            if (rainSystem && cam)
            {
                Vector3 ahead = car ? car.Body.linearVelocity * 0.6f : Vector3.zero;
                rainSystem.transform.position = cam.transform.position + ahead + Vector3.up * 12f;
                var em = rainSystem.emission;
                em.rateOverTime = maxRainRate * rain;
            }

            if (spraySystems != null && car)
            {
                for (int i = 0; i < spraySystems.Length; i++)
                {
                    var ps = spraySystems[i];
                    if (!ps) continue;
                    bool grounded = i < car.wheels.Length && car.wheels[i].grounded;
                    float k = grounded ? Mathf.Clamp01((speed - 8f) / 60f) : 0f;
                    float rearBoost = (i >= 2) ? 1f : 0.45f;
                    var em = ps.emission;
                    em.rateOverTime = maxSprayRate * k * k * wetness * rearBoost;
                }
            }

            if (trackProbe && cam) trackProbe.transform.position = cam.transform.position + Vector3.down * 0.6f;
        }

        static Camera ActiveCamera()
        {
            Camera best = null;
            foreach (var c in Camera.allCameras)
                if (c.enabled && (best == null || c.depth >= best.depth)) best = c;
            return best;
        }

        void OnDisable() => RaycastWheel.GlobalGripScale = 1f;
    }
}
