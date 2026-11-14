using OWR.Vehicle;
using UnityEngine;

namespace OWR.Cockpit
{
    /// <summary>
    /// Moves the driver's head (helmet pivot at the neck) like a real driver: looks into the corner ahead of the
    /// steering input, leans against lateral G, nods forward under braking and back on throttle, jiggles over kerbs,
    /// and glances around now and then on straights. Spring-damper neck so it settles naturally.
    /// </summary>
    public class HelmetSway : MonoBehaviour
    {
        public CarController car;
        public Transform pivot;                 // helmet pivot (neck)
        public float lookYaw = 18f;             // max glance into a corner (deg)
        public float rollPerG = 3.2f, pitchPerG = 2.6f;
        public float stiffness = 60f, damping = 0.75f;
        Vector3 ang, vel;                       // x pitch, y yaw, z roll
        float glance, glanceTarget, glanceTimer, kerb;
        float[] lastComp = new float[4];
        Quaternion rest;

        void Start() { if (pivot) rest = pivot.localRotation; glanceTimer = Random.Range(3f, 8f); }

        void LateUpdate()
        {
            if (!car || !pivot) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var t = car.telemetry;
            Vector3 g = t.localAccelG;
            float lat = Mathf.Clamp(g.x, -5f, 5f), lon = Mathf.Clamp(g.z, -5f, 5f);
            float speedK = Mathf.Clamp01(Mathf.Abs(t.speedMs) / 30f);

            // occasional glance (mirror check) on straights
            glanceTimer -= dt;
            if (glanceTimer <= 0f)
            {
                bool straight = Mathf.Abs(t.steer) < 0.08f && speedK > 0.5f;
                glanceTarget = straight && glanceTarget == 0f ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(8f, 14f) : 0f;
                glanceTimer = glanceTarget != 0f ? Random.Range(0.5f, 0.9f) : Random.Range(4f, 9f);
            }
            glance = Mathf.Lerp(glance, glanceTarget, 1f - Mathf.Exp(-dt * 6f));

            float activity = 0f;
            for (int i = 0; i < car.wheels.Length && i < 4; i++)
            { float c = car.wheels[i].compression; activity += Mathf.Abs(c - lastComp[i]) / dt; lastComp[i] = c; }
            kerb = Mathf.Lerp(kerb, activity, 1f - Mathf.Exp(-dt * 15f));
            float jig = Mathf.Min(kerb * 0.06f, 2.5f);

            Vector3 target = new Vector3(
                -lon * pitchPerG + (Mathf.PerlinNoise(Time.time * 18f, 3f) - 0.5f) * jig,       // nod: braking -> forward
                t.steer * lookYaw * speedK + glance,                                               // look into the corner
                lat * rollPerG + (Mathf.PerlinNoise(5f, Time.time * 18f) - 0.5f) * jig);           // lean against lateral G
            float c2 = 2f * damping * Mathf.Sqrt(stiffness);
            vel += (stiffness * (target - ang) - c2 * vel) * dt;
            ang += vel * dt;
            pivot.localRotation = rest * Quaternion.Euler(ang.x, ang.y, ang.z);
        }
    }
}
