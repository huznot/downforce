using OWR.Vehicle;
using UnityEngine;

namespace OWR.CameraRig
{
    /// <summary>
    /// T-cam: the broadcast camera on top of the roll hoop, looking forward over the driver's helmet. Hard-mounted,
    /// so it shakes with the chassis (engine buzz, kerbs, bumps) and its field of view opens up slightly with speed.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TCamCamera : MonoBehaviour
    {
        public CarController car;
        public Vector3 mountLocal = new Vector3(0f, 1.16f, -0.66f);
        public float pitch = 7.5f;
        public float baseFov = 66f, fovAtSpeed = 74f;
        public float buzz = 0.0016f, kerbShake = 0.006f;
        Camera cam;
        float[] lastComp = new float[4];
        float shakeAmt, seed;

        void Awake() { cam = GetComponent<Camera>(); seed = Random.value * 50f; }

        void LateUpdate()
        {
            if (!car) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var t = car.telemetry;
            float activity = 0f;
            for (int i = 0; i < car.wheels.Length && i < 4; i++)
            {
                float c = car.wheels[i].compression;
                activity += Mathf.Abs(c - lastComp[i]) / dt;
                lastComp[i] = c;
            }
            float speedK = Mathf.Clamp01(Mathf.Abs(t.speedMs) / 70f);
            shakeAmt = Mathf.Lerp(shakeAmt, buzz * (0.4f + speedK) + activity * kerbShake * 0.01f, 1f - Mathf.Exp(-dt * 20f));
            float tm = Time.time;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(seed, tm * 40f) - 0.5f, Mathf.PerlinNoise(seed + 7f, tm * 47f) - 0.5f, 0f) * shakeAmt
                          + Random.insideUnitSphere * (Race.ExplosionFX.Shake * 0.05f);
            transform.localPosition = mountLocal + shake;
            transform.localRotation = Quaternion.Euler(pitch + shake.y * 40f, 0f, shake.x * 30f);
            if (cam) cam.fieldOfView = Mathf.Lerp(baseFov, fovAtSpeed, speedK);
        }
    }
}
