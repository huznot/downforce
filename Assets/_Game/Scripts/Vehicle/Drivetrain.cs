using UnityEngine;

namespace OWR.Vehicle
{
    /// <summary>Engine + 8-speed seamless gearbox + launch clutch + simple ERS. Plain class owned by CarController.</summary>
    public class Drivetrain
    {
        readonly CarSetup s;
        public int Gear { get; private set; } = 1;
        public float Rpm { get; private set; }
        public bool Shifting => shiftTimer > 0f;
        public bool RevLimiter { get; private set; }
        public bool ErsDeploying { get; private set; }
        public float BatteryJ { get; private set; }
        public float BatteryFraction => BatteryJ / (s.batteryCapacityMJ * 1e6f);

        /// <summary>Raised on each gear change: (newGear, isUpshift).</summary>
        public System.Action<int, bool> OnShift;

        float shiftTimer;
        float autoShiftCooldown;
        int pendingGear;
        /// <summary>Set by the car each step: true while the driven wheels are spinning (blocks auto upshifts).</summary>
        public bool DrivenWheelsSpinning;
        /// <summary>ERS deployment multiplier set by the wheel's ERS mode / overtake button.</summary>
        public float ErsScale = 1f;
        /// <summary>Holds the car in neutral (grid / pause): engine revs with throttle, no drive.</summary>
        public bool ForceNeutral;
        int MaxGear => s.gearRatios.Length;

        public Drivetrain(CarSetup setup)
        {
            s = setup;
            Rpm = s.idleRpm;
            BatteryJ = s.batteryCapacityMJ * 1e6f * 0.8f;
        }

        public void ResetState()
        {
            Gear = 1; Rpm = s.idleRpm; shiftTimer = 0f; pendingGear = 1; autoShiftCooldown = 0f;
        }

        public void RequestUpshift(float drivenOmega)
        {
            if (Shifting) return;
            int target = Gear == -1 ? 1 : Mathf.Min(Gear + 1, MaxGear);
            if (target == Gear) return;
            StartShift(target, true);
        }

        public void RequestDownshift(float drivenOmega, float speedMs)
        {
            if (Shifting) return;
            if (Gear == 1)
            {
                if (speedMs < 1.5f) StartShift(-1, false);
                return;
            }
            if (Gear <= -1) return;
            int target = Gear - 1;
            // Over-rev protection: refuse the downshift if it would blow past the limiter.
            float rpmAfter = Mathf.Abs(drivenOmega * s.GetRatio(target)) * 60f / (2f * Mathf.PI);
            if (rpmAfter > s.revLimit + 300f) return;
            StartShift(target, false);
        }

        void StartShift(int target, bool up)
        {
            pendingGear = target;
            shiftTimer = up ? s.upshiftTime : s.downshiftTime;
            Gear = target; // seamless shift: ratio changes immediately, torque is briefly cut
            OnShift?.Invoke(target, up);
        }

        /// <summary>Returns total drive torque at the driven axle.</summary>
        public float Step(float dt, float throttle, float brake, float drivenOmega, float speedMs)
        {
            ErsDeploying = false;
            RevLimiter = false;
            if (shiftTimer > 0f) shiftTimer -= dt;

            if (autoShiftCooldown > 0f) autoShiftCooldown -= dt;
            if (s.autoShift && !Shifting && Gear > 0 && autoShiftCooldown <= 0f && !(DrivenWheelsSpinning && Rpm > s.autoDownshiftRpm))
            {
                autoShiftCooldown = 0.45f;
                if (Rpm > s.autoUpshiftRpm && Gear < MaxGear) RequestUpshift(drivenOmega);
                else if (Rpm < s.autoDownshiftRpm && Gear > 1) RequestDownshift(drivenOmega, speedMs);
            }

            float ratio = s.GetRatio(Gear);
            if (ForceNeutral || Gear == 0 || Mathf.Approximately(ratio, 0f))
            {
                Rpm = Mathf.MoveTowards(Rpm, Mathf.Lerp(s.idleRpm, s.revLimit, throttle), s.engineInertiaRevRate * dt);
                return 0f;
            }

            float wheelRpm = Mathf.Abs(drivenOmega * ratio) * 60f / (2f * Mathf.PI);

            // Launch clutch: below launch rpm the clutch slips and the engine holds its own revs.
            if (wheelRpm < s.launchRpm)
            {
                float target = Mathf.Max(wheelRpm, Mathf.Lerp(s.idleRpm, s.launchRpm, throttle));
                Rpm = Mathf.MoveTowards(Rpm, target, s.engineInertiaRevRate * dt);
            }
            else
            {
                Rpm = wheelRpm;
            }

            float torque = s.torqueCurve.Evaluate(Rpm) * throttle;
            if (Rpm >= s.revLimit)
            {
                torque = 0f;
                RevLimiter = true;
            }

            // ERS: deploy on full throttle, harvest under braking.
            float engineOmega = Mathf.Max(Rpm, 1000f) * 2f * Mathf.PI / 60f;
            if (throttle > 0.9f && BatteryJ > 0f && !RevLimiter && ErsScale > 0f)
            {
                float p = s.ersPowerKw * 1000f * ErsScale;
                torque += p / engineOmega;
                BatteryJ = Mathf.Max(0f, BatteryJ - p * dt);
                ErsDeploying = true;
            }
            else if (brake > 0.1f && speedMs > 5f)
            {
                BatteryJ = Mathf.Min(s.batteryCapacityMJ * 1e6f, BatteryJ + s.harvestPowerKw * 1000f * brake * dt);
            }

            // Engine braking when off throttle (not when the clutch is slipping at a standstill).
            if (wheelRpm >= s.launchRpm * 0.6f)
                torque -= (1f - throttle) * s.engineBrakeTorque * (Rpm / s.revLimit);

            if (Shifting) torque *= 0.15f;

            return torque * ratio * s.drivetrainEfficiency;
        }
    }
}
