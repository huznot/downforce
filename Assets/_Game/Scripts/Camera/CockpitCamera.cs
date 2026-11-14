using OWR.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OWR.CameraRig
{
    /// <summary>
    /// First-person driver head. A damped spring pulls the head toward an offset driven by G-forces:
    /// leans out in corners, nods forward under braking, pushes back on throttle; plus kerb/road buzz,
    /// engine vibration, a glance into the corner and a slight FOV widening with speed.
    /// Lives as a child of the car so it inherits Rigidbody interpolation.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CockpitCamera : MonoBehaviour
    {
        public CarController car;
        public Vector3 eyeLocal = new Vector3(0f, 0.745f, -0.05f);
        public float basePitch = 12.5f;

        [Header("G-force response")]
        public float lateralShiftPerG = 0.028f;
        public float longShiftPerG = 0.032f;
        public float verticalShiftPerG = 0.02f;
        public float rollPerG = 2.4f;
        public float pitchPerG = 1.8f;
        [Tooltip("Spring stiffness of the neck (higher = snappier).")]
        public float neckStiffness = 70f;
        [Tooltip("Damping ratio, 1 = critically damped.")]
        public float neckDamping = 0.75f;

        [Header("Vibration")]
        public float roadBuzz = 0.0035f;
        public float kerbBuzzGain = 0.02f;
        public float engineBuzz = 0.0007f;

        [Header("View")]
        public float lookIntoCorner = 5f;
        public float baseFov = 64f;
        public float fovAtSpeed = 68f;
        public float fovSpeedRef = 90f;

        [Header("Free look (right stick / hold right mouse)")]
        public float lookYawMax = 75f;
        public float lookPitchMax = 30f;
        public float lookSpeed = 5f;
        public float lookReturn = 3.5f;
        public float mouseSensitivity = 0.15f;
        Vector2 look;

        Camera cam;
        Vector3 off, offVel;
        Vector2 ang, angVel; // x = pitch, y = roll
        float yaw;
        float[] lastComp = new float[4];
        float buzz;
        float seed;

        void Awake()
        {
            cam = GetComponent<Camera>();
            seed = Random.value * 100f;
        }

        void LateUpdate()
        {
            if (!car) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var t = car.telemetry;
            Vector3 g = t.localAccelG;
            g.x = Mathf.Clamp(g.x, -6f, 6f);
            g.z = Mathf.Clamp(g.z, -6f, 6f);
            g.y = Mathf.Clamp(g.y, -3f, 3f);

            // ---- head offset & rotation targets ----
            Vector3 targetOff = new Vector3(-g.x * lateralShiftPerG, -g.y * verticalShiftPerG, -g.z * longShiftPerG);
            if (g.z < 0f) targetOff.y += g.z * 0.006f; // slight dip under braking
            Vector2 targetAng = new Vector2(-g.z * pitchPerG, g.x * rollPerG);

            float k = neckStiffness, c = 2f * neckDamping * Mathf.Sqrt(neckStiffness);
            offVel += (k * (targetOff - off) - c * offVel) * dt;
            off += offVel * dt;
            angVel += (k * (targetAng - ang) - c * angVel) * dt;
            ang += angVel * dt;

            // ---- vibration: suspension activity (kerbs/bumps) + engine ----
            float activity = 0f;
            for (int i = 0; i < car.wheels.Length && i < 4; i++)
            {
                float comp = car.wheels[i].compression;
                activity += Mathf.Abs(comp - lastComp[i]) / dt;
                lastComp[i] = comp;
            }
            float speedK = Mathf.Clamp01(Mathf.Abs(t.speedMs) / 60f);
            buzz = Mathf.Lerp(buzz, roadBuzz * speedK + activity * kerbBuzzGain * 0.01f, 1f - Mathf.Exp(-dt * 25f));
            float eng = engineBuzz * (0.3f + 0.7f * t.rpm / 12500f) * (0.5f + 0.5f * t.throttle);
            float time = Time.time;
            Vector3 shake = new Vector3(
                (Mathf.PerlinNoise(seed, time * 38f) - 0.5f) * (buzz + eng),
                (Mathf.PerlinNoise(seed + 11f, time * 45f) - 0.5f) * (buzz * 1.6f + eng),
                0f);

            // ---- glance into the corner ----
            float targetYaw = t.steer * lookIntoCorner * Mathf.Clamp01(Mathf.Abs(t.speedMs) / 25f);
            yaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-dt * 3f));

            // free look: stick sets a target angle; releasing eases back to the onboard view
            Vector2 target = Vector2.zero;
            var gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 rs = gp.rightStick.ReadValue();
                if (rs.magnitude > 0.15f) target = new Vector2(rs.x * lookYawMax, -rs.y * lookPitchMax);
            }
            var mouse = Mouse.current;
            bool mouseLook = mouse != null && mouse.rightButton.isPressed;
            if (mouseLook)
            {
                Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
                look += new Vector2(d.x, -d.y);
                look.x = Mathf.Clamp(look.x, -lookYawMax, lookYawMax);
                look.y = Mathf.Clamp(look.y, -lookPitchMax, lookPitchMax);
            }
            else
            {
                float rate = target == Vector2.zero ? lookReturn : lookSpeed;
                look = Vector2.Lerp(look, target, 1f - Mathf.Exp(-rate * dt));
            }
            // turning the head also shifts the eye slightly sideways/forward, like a real neck
            Vector3 neck = new Vector3(Mathf.Sin(look.x * Mathf.Deg2Rad) * 0.06f, 0f, -Mathf.Abs(Mathf.Sin(look.x * Mathf.Deg2Rad)) * 0.03f);

            shake += Random.insideUnitSphere * (OWR.Race.ExplosionFX.Shake * 0.04f);
            transform.localPosition = eyeLocal + off + shake + neck;
            transform.localRotation = Quaternion.Euler(basePitch + ang.x + look.y, yaw + look.x, ang.y);

            if (cam) cam.fieldOfView = Mathf.Lerp(baseFov, fovAtSpeed, Mathf.Clamp01(Mathf.Abs(t.speedMs) / fovSpeedRef));
        }
    }
}
