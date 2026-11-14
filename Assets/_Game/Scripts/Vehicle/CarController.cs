using OWR.Input;
using UnityEngine;

namespace OWR.Vehicle
{
    /// <summary>
    /// Owns the Rigidbody and runs the whole vehicle simulation each FixedUpdate:
    /// input -> drivetrain -> wheels (suspension + tyres) -> anti-roll -> aero -> telemetry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        public CarSetup setup;
        public DriverInput input;
        [Tooltip("Order: FL, FR, RL, RR.")]
        public RaycastWheel[] wheels = new RaycastWheel[4];
        [Tooltip("Layers the wheels can stand on. The car itself must not be on these layers.")]
        public LayerMask groundMask = ~(1 << 2);
        [Tooltip("Where the car is placed on reset. If empty, uses the start pose.")]
        public Transform spawnPoint;

        public CarTelemetry telemetry;
        [System.NonSerialized] public bool pitLimiter;
        /// <summary>Holds the car on the grid / after the finish.</summary>
        [System.NonSerialized] public bool parkingBrake;
        public float pitLimitKmh = 80f;
        /// <summary>Optional: where a reset should place the car (race mode puts it back on the track).</summary>
        public System.Func<CarController, (Vector3 pos, Quaternion rot)> resetProvider;
        /// <summary>Last big impact speed change (m/s) for commentary / damage feedback.</summary>
        [System.NonSerialized] public float lastImpact;
        [System.NonSerialized] public float lastImpactTime = -99f;
        public Drivetrain Drivetrain { get; private set; }
        public Rigidbody Body { get; private set; }

        Vector3 startPos;
        Quaternion startRot;
        Vector3 lastVelocity;
        Vector3 frontAxleLocalCache, rearAxleLocalCache;
        Vector3 smoothedAccel;
        float tcFactor = 1f;
        public bool TractionControlActive { get; private set; }

        RaycastWheel FL => wheels[0];
        RaycastWheel FR => wheels[1];
        RaycastWheel RL => wheels[2];
        RaycastWheel RR => wheels[3];

        void Awake()
        {
            // Work on a runtime copy so in-car changes (brake bias, TC...) never overwrite the asset.
            setup = setup == null ? ScriptableObject.CreateInstance<CarSetup>() : Instantiate(setup);
            if (input == null) input = GetComponent<DriverInput>();

            // The tyre model needs a fast fixed step; 240 Hz keeps slip dynamics stable.
            if (Time.fixedDeltaTime > 1.2f / OWR.Race.AdaptiveQuality.PhysicsHz) Time.fixedDeltaTime = 1f / OWR.Race.AdaptiveQuality.PhysicsHz;

            Body = GetComponent<Rigidbody>();
            Body.mass = setup.mass;
            Body.centerOfMass = setup.centerOfMass;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.linearDamping = 0f;
            Body.angularDamping = 0.05f;
            Body.maxAngularVelocity = 30f;

            Drivetrain = new Drivetrain(setup);
            foreach (var w in wheels) w.Init(this);

            frontAxleLocalCache = (FL.transform.localPosition + FR.transform.localPosition) * 0.5f;
            rearAxleLocalCache = (RL.transform.localPosition + RR.transform.localPosition) * 0.5f;
            startPos = spawnPoint ? spawnPoint.position : transform.position;
            startRot = spawnPoint ? spawnPoint.rotation : transform.rotation;
        }

        void Update()
        {
            if (input == null) return;
            if (input.ConsumeReset()) ResetCar();
            if (input.ConsumeAutoShiftToggle()) setup.autoShift = !setup.autoShift;
            if (input.ConsumeTractionToggle()) setup.tractionControl = !setup.tractionControl;
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f4Key.wasPressedThisFrame) setup.stabilityAssist = !setup.stabilityAssist;

            input.speedMs = telemetry.speedMs;
            float drivenOmega = (RL.omega + RR.omega) * 0.5f;
            if (input.ConsumeShiftUp()) Drivetrain.RequestUpshift(drivenOmega);
            if (input.ConsumeShiftDown()) Drivetrain.RequestDownshift(drivenOmega, telemetry.speedMs);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float throttle = input ? input.Throttle : 0f;
            if (pitLimiter)
            {
                float over = Body.linearVelocity.magnitude * 3.6f - pitLimitKmh;
                throttle = Mathf.Min(throttle, Mathf.Clamp01(1f - over * 0.25f));
            }
            float brake = input ? input.Brake : 0f;
            if (parkingBrake) brake = 1f;
            float steer = input ? input.Steer : 0f;

            Vector3 vel = Body.linearVelocity;
            float fwdSpeed = Vector3.Dot(vel, transform.forward);
            float speed = vel.magnitude;

            // ---- steering (speed sensitive) ----
            float lockDeg = Mathf.Lerp(setup.maxSteerLowSpeed, setup.maxSteerHighSpeed,
                Mathf.Clamp01(Mathf.Abs(fwdSpeed) / setup.steerSpeedReference));
            float steerAngle = steer * lockDeg;
            // steering assist: cap the lock to what the tyres can turn at this speed (incl. downforce), like a calm driver
            if (setup.steeringAssist && !input.aiControlled && Mathf.Abs(fwdSpeed) > 6f)
            {
                float wb = Vector3.Distance(FL.transform.localPosition, RL.transform.localPosition);
                float down = 0.5f * setup.airDensity * fwdSpeed * fwdSpeed * setup.liftCoefficientArea;
                float aMax = setup.steeringAssistGrip * RaycastWheel.GlobalGripScale * 9.81f * (1f + down / (setup.mass * 9.81f));
                float maxDeg = Mathf.Atan(wb * aMax / (fwdSpeed * fwdSpeed)) * Mathf.Rad2Deg * 1.15f;
                steerAngle = Mathf.Clamp(steerAngle, -maxDeg, maxDeg);
            }
            FL.steerAngle = FR.steerAngle = steerAngle;
            RL.steerAngle = RR.steerAngle = 0f;

            // ---- drivetrain (rear wheel drive, locked spool split) ----
            float drivenOmega = (RL.omega + RR.omega) * 0.5f;
            Drivetrain.DrivenWheelsSpinning = Mathf.Max(RL.slipRatio, RR.slipRatio) > setup.peakSlipRatio * 1.5f;
            float axleTorque = Drivetrain.Step(dt, throttle, brake, drivenOmega, speed);
            FL.driveTorque = FR.driveTorque = 0f;
            if (setup.tractionControl && axleTorque > 0f)
            {
                float rearSlip = Mathf.Max(RL.slipRatio, RR.slipRatio);
                float target = rearSlip > setup.tractionControlSlip ? Mathf.Clamp01(1f - (rearSlip - setup.tractionControlSlip) * 6f) : 1f;
                tcFactor = target < tcFactor ? Mathf.MoveTowards(tcFactor, target, dt * 20f) : Mathf.MoveTowards(tcFactor, 1f, dt * 4f);
                axleTorque *= tcFactor;
            }
            else tcFactor = 1f;
            TractionControlActive = tcFactor < 0.98f;
            // Limited-slip diff: push torque from the faster rear wheel to the slower one.
            float transfer = Mathf.Clamp((RR.omega - RL.omega) * setup.diffLockTorque, -setup.diffMaxTransfer, setup.diffMaxTransfer);
            RL.driveTorque = axleTorque * 0.5f + transfer;
            RR.driveTorque = axleTorque * 0.5f - transfer;

            // ---- brakes ----
            // ABS assist: back off the pressure when a front wheel starts to lock
            if (setup.absAssist && brake > 0.05f && Mathf.Min(FL.slipRatio, FR.slipRatio) < -0.12f) brake *= 0.55f;
            float bt = brake * setup.maxBrakeTorque;
            FL.brakeTorque = FR.brakeTorque = bt * setup.brakeBias * 0.5f;
            RL.brakeTorque = RR.brakeTorque = bt * (1f - setup.brakeBias) * 0.5f;

            // ---- suspension probe + anti-roll bars ----
            foreach (var w in wheels) w.Probe();
            ApplyAntiRoll(FL, FR, setup.antiRollFront);
            ApplyAntiRoll(RL, RR, setup.antiRollRear);

            int grounded = 0;
            foreach (var w in wheels)
            {
                w.Simulate(dt, Body);
                if (w.grounded) grounded++;
            }

            // ---- stability assist: counter yaw beyond what a neutral car would do at this steer & speed ----
            if (setup.stabilityAssist && Mathf.Abs(fwdSpeed) > 5f)
            {
                float wheelbase = Vector3.Distance(frontAxleLocalCache, rearAxleLocalCache);
                float targetYaw = fwdSpeed * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / Mathf.Max(wheelbase, 1f);
                float yaw = Vector3.Dot(Body.angularVelocity, transform.up);
                float excess = yaw - targetYaw;
                if (Mathf.Abs(excess) > 0.08f)
                    Body.AddTorque(transform.up * (-excess * setup.stabilityStrength * Mathf.Clamp01(Mathf.Abs(fwdSpeed) / 20f)));
            }

            // ---- aero ----
            Vector3 frontAxle = (FL.transform.position + FR.transform.position) * 0.5f;
            Vector3 rearAxle = (RL.transform.position + RR.transform.position) * 0.5f;
            float downforce = Aero.Apply(Body, transform, setup, frontAxle, rearAxle);

            // ---- telemetry ----
            Vector3 accelWorld = (vel - lastVelocity) / dt;
            lastVelocity = vel;
            Vector3 localG = transform.InverseTransformDirection(accelWorld) / 9.81f;
            smoothedAccel = Vector3.Lerp(smoothedAccel, localG, 1f - Mathf.Exp(-dt * 20f));

            telemetry.speedMs = fwdSpeed;
            telemetry.rpm = Drivetrain.Rpm;
            telemetry.gear = Drivetrain.Gear;
            telemetry.shifting = Drivetrain.Shifting;
            telemetry.throttle = throttle;
            telemetry.brake = brake;
            telemetry.steer = steer;
            telemetry.steerAngleDeg = steerAngle;
            telemetry.localAccelG = smoothedAccel;
            telemetry.downforceN = downforce;
            telemetry.batteryFraction = Drivetrain.BatteryFraction;
            telemetry.ersDeploying = Drivetrain.ErsDeploying;
            telemetry.revLimiter = Drivetrain.RevLimiter;
            telemetry.groundedWheels = grounded;
        }

        static void ApplyAntiRoll(RaycastWheel left, RaycastWheel right, float stiffness)
        {
            float diff = left.compression - right.compression;
            float f = diff * stiffness;
            left.antiRollForce = left.grounded ? f : 0f;
            right.antiRollForce = right.grounded ? -f : 0f;
        }

        /// <summary>Raised when this car touches another race car (for contact penalties).</summary>
        public System.Action<CarController, CarController> OnCarContact;

        void OnCollisionEnter(Collision c)
        {
            float imp = c.relativeVelocity.magnitude;
            if (imp > 6f) { lastImpact = imp; lastImpactTime = Time.time; }
            var other = c.rigidbody ? c.rigidbody.GetComponent<CarController>() : null;
            if (other) OnCarContact?.Invoke(this, other);
        }

        public void ResetCar()
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            var target = resetProvider != null ? resetProvider(this) : (startPos, startRot);
            Body.position = target.Item1 + Vector3.up * 0.3f;
            Body.rotation = target.Item2;
            transform.SetPositionAndRotation(Body.position, Body.rotation);
            lastVelocity = Vector3.zero;
            smoothedAccel = Vector3.zero;
            Drivetrain.ResetState();
            foreach (var w in wheels) w.ResetState();
        }
    }
}
