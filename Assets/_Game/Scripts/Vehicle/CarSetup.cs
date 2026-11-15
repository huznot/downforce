using UnityEngine;

namespace OWR.Vehicle
{
    /// <summary>All tunable car parameters in one asset, so handling can be tweaked without code changes.</summary>
    [CreateAssetMenu(menuName = "OWR/Car Setup", fileName = "CarSetup")]
    public class CarSetup : ScriptableObject
    {
        [Header("Mass")]
        public float mass = 798f;
        [Tooltip("Local-space center of mass relative to the car root (root sits at ground level).")]
        public Vector3 centerOfMass = new Vector3(0f, 0.26f, -0.2f);

        [Header("Wheels")]
        public float frontRadius = 0.34f;
        public float rearRadius = 0.34f;
        [Tooltip("Rotational inertia of one wheel + brake assembly (kg m^2).")]
        public float wheelInertia = 1.2f;

        [Header("Suspension")]
        public float restLength = 0.12f;
        public float springFront = 120000f;
        public float springRear = 140000f;
        public float damperBump = 5500f;
        public float damperRebound = 7500f;
        public float antiRollFront = 40000f;
        public float antiRollRear = 30000f;

        [Header("Tyres")]
        public float frictionCoefficient = 1.8f;
        [Tooltip("Slip ratio at which longitudinal grip peaks.")]
        public float peakSlipRatio = 0.10f;
        [Tooltip("Slip angle (degrees) at which lateral grip peaks.")]
        public float peakSlipAngle = 6f;
        [Tooltip("Pacejka-style shape. Higher = more grip loss once past the peak.")]
        [Range(1.1f, 1.9f)] public float curveShape = 1.3f;
        [Tooltip("Grip falls as load rises: mu *= 1 - k * (Fz/refLoad - 1).")]
        public float loadSensitivity = 0.12f;
        public float referenceLoad = 3000f;
        [Tooltip("Speed floor (m/s) used when computing slip, keeps low-speed behaviour stable.")]
        public float lowSpeedSlipFloor = 3f;
        public float rollingResistance = 0.012f;

        [Header("Steering")]
        public float maxSteerLowSpeed = 20f;
        public float maxSteerHighSpeed = 4.5f;
        public float steerSpeedReference = 70f;

        [Header("Brakes")]
        public float maxBrakeTorque = 13000f;
        [Range(0.4f, 0.75f)] public float brakeBias = 0.58f;

        [Header("Engine")]
        public float idleRpm = 4000f;
        public float launchRpm = 8000f;
        public float revLimit = 12500f;
        public AnimationCurve torqueCurve = new AnimationCurve(
            new Keyframe(0f, 300f),
            new Keyframe(4000f, 400f),
            new Keyframe(7000f, 530f),
            new Keyframe(10500f, 650f),
            new Keyframe(12000f, 610f),
            new Keyframe(12500f, 560f));
        public float engineBrakeTorque = 70f;
        public float engineInertiaRevRate = 25000f;
        public float drivetrainEfficiency = 0.92f;

        [Header("Gearbox")]
        public float[] gearRatios = { 5.0f, 3.8f, 3.07f, 2.57f, 2.2f, 1.93f, 1.7f, 1.52f };
        public float reverseRatio = 4.5f;
        public float finalDrive = 3.0f;
        public float upshiftTime = 0.03f;
        public float downshiftTime = 0.06f;
        public bool autoShift = false;
        [Tooltip("Driver aid: trims rear torque when the rears spin past the grip peak. F3 toggles in play.")]
        public bool tractionControl = true;
        public float tractionControlSlip = 0.1f;
        [Tooltip("Driver aid: resists yaw that exceeds what the steering asks for (F4 toggles).")]
        public bool stabilityAssist = true;
        [Tooltip("Driver aid: never asks the front tyres for more lateral grip than they have (smooth, no-slide turn-in).")]
        public bool steeringAssist = true;
        public float steeringAssistGrip = 1.35f;
        [Tooltip("Driver aid (Easy): releases brake pressure before the wheels lock.")]
        public bool absAssist = false;
        public float stabilityStrength = 9000f;
        public float autoUpshiftRpm = 11800f;
        public float autoDownshiftRpm = 7000f;

        [Header("Differential")]
        [Tooltip("Limited-slip coupling between the rear wheels (Nm per rad/s of speed difference). Keep below ~140 for stability.")]
        public float diffLockTorque = 110f;
        [Tooltip("Max torque the diff can transfer between wheels.")]
        public float diffMaxTransfer = 1200f;

        [Header("Hybrid (ERS)")]
        public float ersPowerKw = 120f;
        public float batteryCapacityMJ = 4f;
        public float harvestPowerKw = 120f;

        [Header("Aero")]
        public float liftCoefficientArea = 5f;   // ClA, m^2
        public float dragCoefficientArea = 1.1f;   // CdA, m^2
        [Range(0.3f, 0.6f)] public float aeroBalanceFront = 0.44f;
        public float airDensity = 1.225f;

        public float GetRatio(int gear)
        {
            if (gear > 0) return gearRatios[Mathf.Clamp(gear - 1, 0, gearRatios.Length - 1)] * finalDrive;
            if (gear < 0) return -reverseRatio * finalDrive;
            return 0f;
        }
    }
}
