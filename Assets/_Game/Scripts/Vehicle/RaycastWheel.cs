using UnityEngine;

namespace OWR.Vehicle
{
    /// <summary>
    /// One corner of the car: raycast spring/damper suspension and a combined-slip tyre.
    /// The transform marks the top of the suspension travel (the wheel hangs below it).
    /// Driven by CarController in two phases: Probe() then Simulate().
    /// </summary>
    public class RaycastWheel : MonoBehaviour
    {
        public bool isFront;
        public bool isLeft;
        [Tooltip("Visual root, moved for suspension and rotated for steering.")]
        public Transform steerVisual;
        [Tooltip("Child of steerVisual that spins with wheel speed.")]
        public Transform spinVisual;

        // ---- runtime state (public for telemetry / debug) ----
        [System.NonSerialized] public bool grounded;
        [System.NonSerialized] public RaycastHit hit;
        [System.NonSerialized] public float compression;
        [System.NonSerialized] public float load;
        [System.NonSerialized] public float omega;          // rad/s
        [System.NonSerialized] public float slipRatio;
        [System.NonSerialized] public float slipAngleDeg;
        [System.NonSerialized] public float steerAngle;     // degrees
        [System.NonSerialized] public float driveTorque;
        [System.NonSerialized] public float brakeTorque;
        [System.NonSerialized] public float antiRollForce;
        [System.NonSerialized] public float forceLong, forceLat;
        [System.NonSerialized] public float surfaceGrip = 1f;
        [System.NonSerialized] public float combinedSlip;   // 1 = at peak grip, >1 = sliding

        /// <summary>Track-wide grip multiplier (weather). 1 = dry.</summary>
        public static float GlobalGripScale = 1f;

        CarController car;
        CarSetup s;
        float prevCompression;
        float spinAngle;
        const int Substeps = 4;

        public float Radius => isFront ? s.frontRadius : s.rearRadius;
        public float SpringRate => isFront ? s.springFront : s.springRear;

        public void Init(CarController owner)
        {
            car = owner;
            s = owner.setup;
            omega = 0f;
            prevCompression = 0f;
        }

        public void ResetState()
        {
            omega = 0f;
            compression = prevCompression = 0f;
        }

        /// <summary>Phase 1: raycast and suspension compression.</summary>
        public void Probe()
        {
            float R = Radius;
            Vector3 up = car.transform.up;
            grounded = Physics.Raycast(transform.position, -up, out hit, s.restLength + R,
                car.groundMask, QueryTriggerInteraction.Ignore);
            compression = grounded ? Mathf.Clamp(s.restLength - (hit.distance - R), 0f, s.restLength) : 0f;
        }

        /// <summary>Phase 2: forces and wheel spin.</summary>
        public void Simulate(float dt, Rigidbody rb)
        {
            float R = Radius;
            float I = s.wheelInertia;
            float compVel = (compression - prevCompression) / dt;
            prevCompression = compression;

            if (!grounded)
            {
                load = forceLong = forceLat = 0f;
                slipRatio = slipAngleDeg = combinedSlip = 0f;
                omega += driveTorque / I * dt;
                omega = ApplyBrake(omega, brakeTorque + 2f, dt, I);
                return;
            }

            Vector3 up = car.transform.up;

            // ---- suspension ----
            float damper = compVel > 0f ? s.damperBump : s.damperRebound;
            float spring = SpringRate * compression + damper * compVel + antiRollForce;
            // Progressive bump stop in the last 1.5 cm of travel.
            float bumpZone = s.restLength - 0.015f;
            if (compression > bumpZone) spring += (compression - bumpZone) * SpringRate * 12f;
            load = Mathf.Max(0f, spring);
            rb.AddForceAtPosition(up * load, hit.point);

            // ---- tyre frame ----
            Vector3 n = hit.normal;
            Vector3 wheelFwd = Quaternion.AngleAxis(steerAngle, up) * car.transform.forward;
            Vector3 fwd = Vector3.ProjectOnPlane(wheelFwd, n).normalized;
            Vector3 right = Vector3.Cross(n, fwd);
            Vector3 v = rb.GetPointVelocity(hit.point);
            float vLong = Vector3.Dot(v, fwd);
            float vLat = Vector3.Dot(v, right);

            // ---- grip available ----
            float mu = s.frictionCoefficient * surfaceGrip * GlobalGripScale *
                       Mathf.Clamp(1f - s.loadSensitivity * (load / s.referenceLoad - 1f), 0.6f, 1.15f);
            float fMax = load * mu;

            float denom = Mathf.Max(Mathf.Abs(vLong), s.lowSpeedSlipFloor);
            float alpha = Mathf.Atan2(vLat, denom);                 // radians
            float alphaPeak = s.peakSlipAngle * Mathf.Deg2Rad;
            float sy = alpha / alphaPeak;

            float C = s.curveShape;
            float B = Mathf.Tan(Mathf.PI * 0.5f / C);                // puts the curve peak at s = 1
            float rollTorque = s.rollingResistance * load * R;

            // ---- wheel spin, implicit substeps with secant tyre stiffness ----
            float h = dt / Substeps;
            float fxSum = 0f, fySum = 0f;
            for (int i = 0; i < Substeps; i++)
            {
                float kappa = (omega * R - vLong) / denom;
                float sx = kappa / s.peakSlipRatio;
                float sCombined = Mathf.Sqrt(sx * sx + sy * sy);
                float g = Shape(sCombined, B, C);                    // F / (fMax * s)

                // Fx = kSec * (omega*R - vLong) / denom, solved implicitly for omega.
                float kSec = fMax * g / s.peakSlipRatio;
                float a = h * R * kSec / (I * denom);
                omega = (omega + h / I * driveTorque + a * vLong) / (1f + a * R);
                omega = ApplyBrake(omega, brakeTorque + rollTorque, h, I);

                kappa = (omega * R - vLong) / denom;
                sx = kappa / s.peakSlipRatio;
                sCombined = Mathf.Sqrt(sx * sx + sy * sy);
                g = Shape(sCombined, B, C);
                fxSum += fMax * g * sx;
                fySum += -fMax * g * sy;
                combinedSlip = sCombined;
                slipRatio = kappa;
            }

            forceLong = fxSum / Substeps;
            forceLat = fySum / Substeps;
            slipAngleDeg = alpha * Mathf.Rad2Deg;

            rb.AddForceAtPosition(fwd * forceLong + right * forceLat, hit.point);
        }

        /// <summary>sin(C*atan(B*s)) / s, with the s -> 0 limit B*C.</summary>
        static float Shape(float sC, float B, float C)
        {
            if (sC < 1e-4f) return B * C;
            return Mathf.Sin(C * Mathf.Atan(B * sC)) / sC;
        }

        static float ApplyBrake(float w, float torque, float dt, float I)
        {
            float dw = torque / I * dt;
            if (Mathf.Abs(w) <= dw) return 0f;
            return w - Mathf.Sign(w) * dw;
        }

        void LateUpdate()
        {
            if (car == null || steerVisual == null) return;
            float travel = s.restLength - compression;
            steerVisual.position = transform.position - car.transform.up * travel;
            steerVisual.localRotation = Quaternion.Euler(0f, steerAngle, 0f);
            if (spinVisual != null)
            {
                spinAngle = Mathf.Repeat(spinAngle + omega * Mathf.Rad2Deg * Time.deltaTime, 360f);
                spinVisual.localRotation = Quaternion.Euler(spinAngle, 0f, 0f);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = grounded ? Color.green : Color.red;
            float len = (s != null ? s.restLength + Radius : 0.46f);
            Gizmos.DrawLine(transform.position, transform.position - transform.up * len);
        }
    }
}
