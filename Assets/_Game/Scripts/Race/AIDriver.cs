using OWR.Vehicle;
using UnityEngine;

namespace OWR.Race
{
    /// <summary>
    /// AI racing driver: pure-pursuit steering along a racing lane, throttle/brake from the track speed profile,
    /// simple overtaking (moves across when blocked), paddle shifts like a human, and stuck recovery.
    /// Drives the car through the same DriverInput as the player.
    /// </summary>
    public class AIDriver : MonoBehaviour
    {
        public CarController car;
        public TrackPath path;
        [Range(0.85f, 1.05f)] public float skill = 0.95f;
        public float laneOffset;
        [System.NonSerialized] public bool enabledByRace;
        [Tooltip("Set when the AI drives the player's own car (auto-drive): keeps the player's audio and handling.")]
        public bool drivesPlayer;

        int hint = -1;
        float lane, targetLane;
        float stuckTimer;
        float wheelbase = 3.6f;
        float steerSmoothed;
        float panic;
        float gridBlip;
        /// <summary>Driver error: lifts, locks the wheel and loses it for a moment.</summary>
        public void Panic(float seconds) { panic = seconds; }
        [Tooltip("How fast the AI can move the wheel (full lock per second).")]
        public float steerRate = 2.2f;

        void Start()
        {
            car.input.aiControlled = true;
            lane = targetLane = laneOffset;
            car.setup.autoShift = true;   // AI drivers shift on the ideal points
            // AI-driven cars (rivals and the showcase auto-drive) get a touch more grip and a firmer stability aid
            car.setup.frictionCoefficient *= drivesPlayer ? 1.12f : 1.08f;
            car.setup.stabilityAssist = true;
            car.setup.stabilityStrength *= 1.6f;
            car.setup.tractionControl = true;
            car.setup.tractionControlSlip = 0.09f;
            if (drivesPlayer) { steerRate = 1.8f; return; }   // keep the player's own audio
            // onboard audio should be mostly our own car: rivals are quiet and only heard up close
            var eng = GetComponent<AudioFX.EngineAudio>();
            if (eng) { eng.volume = 0.22f; eng.cullDistance = 70f; eng.harmonics = 4; }
            var src = GetComponent<AudioSource>();
            if (src) { src.minDistance = 4f; src.maxDistance = 70f; src.rolloffMode = AudioRolloffMode.Linear; }
        }

        void FixedUpdate()
        {
            if (!car || !path || path.points == null) return;
            if (!enabledByRace && drivesPlayer && RaceManager.Instance && RaceManager.Instance.State == RaceManager.RaceState.Finished)
            {
                car.input.SetAI(0f, Mathf.Clamp01(car.telemetry.speedMs / 25f) + 0.3f, 0f); // controlled stop after the line
                return;
            }
            if (!enabledByRace)
            {
                // on the grid: idle (car held in neutral by the race manager). Once the lights start, hold steady
                // launch revs like a real start - a constant throttle, no blipping.
                var rmg = RaceManager.Instance;
                bool lights = rmg && rmg.State == RaceManager.RaceState.Lights;
                if (lights && gridBlip <= 0f) gridBlip = Random.Range(0.42f, 0.5f);   // each driver's launch throttle
                if (!lights) gridBlip = 0f;
                car.input.SetAI(lights ? gridBlip : 0f, 1f, 0f);
                return;
            }

            float v = car.telemetry.speedMs;
            float d = path.Project(car.transform.position, ref hint, out float lat);

            // ---- traffic awareness: never drive into a car, only pass where there is room ----
            targetLane = laneOffset;
            float followSpeed = float.MaxValue;
            bool leftBlocked = false, rightBlocked = false;
            float passSide = 0f;
            foreach (var other in RaceManager.AllCars)
            {
                if (!other || other == car) continue;
                Vector3 rel = car.transform.InverseTransformPoint(other.transform.position);
                float ov = other.telemetry.speedMs;
                // anything alongside (or just behind/ahead beside us) occupies that side
                if (rel.z > -7f && rel.z < 9f)
                {
                    if (rel.x < -1.0f && rel.x > -5.5f) leftBlocked = true;
                    if (rel.x > 1.0f && rel.x < 5.5f) rightBlocked = true;
                }
                // a car in our path ahead: keep a safe, speed-dependent gap and brake for the time-to-impact
                if (rel.z > 1.5f && rel.z < 45f && Mathf.Abs(rel.x) < 2.7f)
                {
                    float closing = v - ov;
                    float gapNeeded = 7f + v * 0.32f;
                    if (RaceManager.Instance && RaceManager.Instance.Player != null && RaceManager.Instance.Player.car == other) gapNeeded *= 1.4f;
                    float room = Mathf.Max(rel.z - 5.5f, 0.5f);
                    bool mustBrake = closing > 0f && closing * closing / (2f * room) > 5f;
                    if (rel.z < gapNeeded || mustBrake) followSpeed = Mathf.Min(followSpeed, ov - Mathf.Max(0f, gapNeeded - rel.z) * 0.5f);
                    if (ov < v - 1f && rel.z < 30f) passSide = rel.x > 0f ? -1f : 1f; // go round the other side
                }
            }
            if (passSide != 0f)
            {
                bool blocked = passSide < 0f ? leftBlocked : rightBlocked;
                bool otherBlocked = passSide < 0f ? rightBlocked : leftBlocked;
                if (blocked && !otherBlocked) passSide = -passSide;
                if (!(passSide < 0f ? leftBlocked : rightBlocked)) targetLane = lane + passSide * 3.6f;
                else targetLane = lane; // boxed in: hold the line and follow
            }
            // never move across into an occupied side
            if (leftBlocked && targetLane < lane) targetLane = lane;
            if (rightBlocked && targetLane > lane) targetLane = lane;
            targetLane = Mathf.Clamp(targetLane, -path.roadWidth * 0.5f + 1.6f, path.roadWidth * 0.5f - 1.6f);
            lane = Mathf.MoveTowards(lane, targetLane, Time.fixedDeltaTime * 1.6f);

            // ---- steering: pure pursuit to a look-ahead point on our lane ----
            float look = 7f + Mathf.Abs(v) * 0.42f;
            Vector3 target = path.PointAt(d + look, lane);
            Vector3 local = car.transform.InverseTransformPoint(target);
            float L2 = local.sqrMagnitude;
            float curvature = 2f * local.x / Mathf.Max(L2, 1f);
            float steerDeg = Mathf.Atan(wheelbase * curvature) * Mathf.Rad2Deg;
            var s = car.setup;
            float lockDeg = Mathf.Lerp(s.maxSteerLowSpeed, s.maxSteerHighSpeed, Mathf.Clamp01(Mathf.Abs(v) / s.steerSpeedReference));
            float steer = Mathf.Clamp(steerDeg / lockDeg, -1f, 1f);

            // ---- speed: follow the profile a little ahead (braking points), scaled by skill ----
            // look further ahead for braking points and plan conservatively in the wet
            float vt = Mathf.Min(Mathf.Min(path.SpeedAt(d + Mathf.Max(v, 0f) * 0.6f), path.SpeedAt(d + Mathf.Max(v, 0f) * 1.1f) + 6f), path.SpeedAt(d + 10f)) * skill * 0.98f;
            vt = Mathf.Min(vt, followSpeed);
            // calmer launch: hold a moderate pace for the first seconds so the field doesn't pile into turn one
            if (RaceManager.Instance && RaceManager.Instance.RaceTime < 6f) vt = Mathf.Min(vt, 20f + RaceManager.Instance.RaceTime * 9f);
            float err = vt - v;
            float throttle = Mathf.Clamp01(0.3f + err * 0.15f);
            float brake = err < -1.0f ? Mathf.Clamp01(-err * 0.14f) : 0f;
            if (brake > 0f) throttle = 0f;
            // ease off when sliding
            if (Mathf.Abs(car.wheels[2].slipAngleDeg) > 9f) throttle *= 0.5f;

            if (panic > 0f) { panic -= Time.fixedDeltaTime; throttle = 0.6f; brake = 0f; steer = Mathf.Sign(steerSmoothed + 0.01f); }
            // human-like wheel speed: no instant snaps of lock
            steerSmoothed = Mathf.MoveTowards(steerSmoothed, steer, steerRate * Time.fixedDeltaTime);
            car.input.SetAI(throttle, brake, steerSmoothed);

            // ---- stuck recovery (spun, beached, facing the wrong way) ----
            bool wrongWay = Vector3.Dot(car.transform.forward, path.TangentAt(d)) < 0.2f;
            if ((Mathf.Abs(v) < 2f || wrongWay) && RaceManager.Instance && RaceManager.Instance.State == RaceManager.RaceState.Racing) stuckTimer += Time.fixedDeltaTime;
            else stuckTimer = 0f;
            if (stuckTimer > 3.5f) { stuckTimer = 0f; car.ResetCar(); }
        }
    }
}
