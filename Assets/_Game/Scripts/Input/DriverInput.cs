using UnityEngine;
using UnityEngine.InputSystem;

namespace OWR.Input
{
    /// <summary>
    /// Reads gamepad (primary) and keyboard (fallback) through the Input System and outputs smoothed driver controls.
    /// Gamepad: RT throttle, LT brake, left stick steer, RB/LB shift, View/Back reset.
    /// Keyboard: W/S or arrows throttle/brake, A/D steer, E/Q shift, R reset.
    /// </summary>
    public class DriverInput : MonoBehaviour
    {
        [Header("Steering feel")]
        [Tooltip("Stick response curve exponent (>1 = finer control near centre).")]
        public float stickExponent = 2.1f;
        public float steerRate = 1.4f;       // calm, weighted wheel
        public float steerReturnRate = 2.2f;
        [Tooltip("Steering rate is further reduced as speed rises (m/s at which the rate halves).")]
        public float rateHalfSpeed = 55f;
        [System.NonSerialized] public float speedMs;
        /// <summary>When > 0 the throttle is limited so the car can't exceed this speed (m/s), e.g. after the finish.</summary>
        [System.NonSerialized] public float speedCap;
        public float keyboardSteerRate = 3.5f;
        public float keyboardPedalRate = 9f;
        [Range(0f, 0.3f)] public float stickDeadzone = 0.08f;
        [Tooltip("Trigger response curves (>1 = gentler at the start of travel).")]
        public float throttleExponent = 1.7f;
        public float brakeExponent = 1.3f;

        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Steer { get; private set; }

        // One-shot events, consumed by the reader.
        public bool ShiftUpPressed { get; private set; }
        public bool ShiftDownPressed { get; private set; }
        public bool ResetPressed { get; private set; }
        public bool CameraTogglePressed { get; private set; }
        public bool DebugTogglePressed { get; private set; }
        public bool AutoShiftTogglePressed { get; private set; }
        public bool TractionTogglePressed { get; private set; }

        float kbThrottle, kbBrake, kbSteer;

        // Wheel controls (latched until consumed by WheelControls)
        [System.NonSerialized] public bool bbUp, bbDown, tc, ers, overtake, pit, radio;
        public bool Consume(ref bool flag) { bool v = flag; flag = false; return v; }

        /// <summary>When set, device input is ignored and an AI supplies the controls.</summary>
        [System.NonSerialized] public bool aiControlled;
        /// <summary>When set (pause / menus), the player's devices are ignored.</summary>
        [System.NonSerialized] public bool inputBlocked;

        public void SetAI(float throttle, float brake, float steer)
        {
            Throttle = Mathf.Clamp01(throttle);
            Brake = Mathf.Clamp01(brake);
            Steer = Mathf.Clamp(steer, -1f, 1f);
        }

        void Update()
        {
            if (aiControlled) return;
            if (inputBlocked) { Throttle = Brake = 0f; return; }
            var gp = Gamepad.current;
            var kb = Keyboard.current;
            float dt = Time.deltaTime;

            float gpThrottle = 0f, gpBrake = 0f, gpSteer = 0f;
            if (gp != null)
            {
                gpThrottle = Mathf.Pow(gp.rightTrigger.ReadValue(), throttleExponent);
                gpBrake = Mathf.Pow(gp.leftTrigger.ReadValue(), brakeExponent);
                float x = gp.leftStick.ReadValue().x;
                float ax = Mathf.Abs(x);
                ax = ax < stickDeadzone ? 0f : (ax - stickDeadzone) / (1f - stickDeadzone);
                gpSteer = Mathf.Sign(x) * Mathf.Pow(ax, stickExponent);
            }

            if (kb != null)
            {
                bool up = kb.wKey.isPressed || kb.upArrowKey.isPressed;
                bool down = kb.sKey.isPressed || kb.downArrowKey.isPressed;
                int dir = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) -
                          (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
                kbThrottle = Mathf.MoveTowards(kbThrottle, up ? 1f : 0f, keyboardPedalRate * dt);
                kbBrake = Mathf.MoveTowards(kbBrake, down ? 1f : 0f, keyboardPedalRate * 1.4f * dt);
                float rate = (dir == 0 || Mathf.Sign(dir) != Mathf.Sign(kbSteer)) ? keyboardSteerRate * 1.8f : keyboardSteerRate;
                kbSteer = Mathf.MoveTowards(kbSteer, dir, rate * dt);
            }

            Throttle = Mathf.Max(gpThrottle, kbThrottle);
            Brake = Mathf.Max(gpBrake, kbBrake);

            float targetSteer = Mathf.Abs(gpSteer) > Mathf.Abs(kbSteer) ? gpSteer : kbSteer;
            if (TouchControls.Active)
            {
                // touch pads / tilt already shape their own response: use them directly
                Throttle = Mathf.Max(Throttle, TouchControls.Throttle);
                Brake = Mathf.Max(Brake, TouchControls.Brake);
                if (Mathf.Abs(TouchControls.Steer) > Mathf.Abs(targetSteer)) { Steer = TouchControls.Steer; goto Buttons; }
            }
            // optional mouse steering (keyboard players): horizontal mouse position = wheel angle
            var mouse = Mouse.current;
            if (OWR.Race.GameOptions.MouseSteering && mouse != null && !mouse.rightButton.isPressed)
            {
                float mx = (mouse.position.ReadValue().x / Mathf.Max(1f, Screen.width) - 0.5f) / 0.2f;
                float amx = Mathf.Clamp01((Mathf.Abs(mx) - 0.03f) / 0.97f);
                float ms = Mathf.Sign(mx) * Mathf.Pow(amx, 1.5f);
                if (Mathf.Abs(ms) > Mathf.Abs(targetSteer)) targetSteer = ms;
            }
            // keyboard already has its own ramp: don't stack the stick smoothing on top (that felt like lag)
            bool keyboardSteering = Mathf.Abs(kbSteer) >= Mathf.Abs(gpSteer) && Mathf.Abs(kbSteer) > 0.001f;
            if (keyboardSteering && Mathf.Abs(targetSteer - kbSteer) < 1e-4f) { Steer = kbSteer; goto Buttons; }
            bool returning = Mathf.Abs(targetSteer) < Mathf.Abs(Steer);
            // weighted rack: rate slows with speed and eases in/out instead of snapping
            float steerRateNow = (returning ? steerReturnRate : steerRate) / (1f + Mathf.Abs(speedMs) / rateHalfSpeed);
            float err = targetSteer - Steer;
            float step = Mathf.Sign(err) * Mathf.Min(Mathf.Abs(err), steerRateNow * dt * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(Mathf.Abs(err) * 3f)));
            Steer += step;

            Buttons:
            // cool-down lap after the flag: throttle only up to the cap (the car coasts down from race speed)
            if (speedCap > 0f) Throttle = Mathf.Min(Throttle, Mathf.Clamp01((speedCap - Mathf.Abs(speedMs)) * 0.12f));
            ShiftUpPressed |= (gp != null && gp.rightShoulder.wasPressedThisFrame) || (kb != null && kb.eKey.wasPressedThisFrame);
            ShiftDownPressed |= (gp != null && gp.leftShoulder.wasPressedThisFrame) || (kb != null && kb.qKey.wasPressedThisFrame);
            ResetPressed |= (gp != null && gp.selectButton.wasPressedThisFrame) || (kb != null && kb.rKey.wasPressedThisFrame) || TouchControls.ConsumeReset();
            CameraTogglePressed |= (gp != null && gp.buttonNorth.wasPressedThisFrame) || (kb != null && kb.cKey.wasPressedThisFrame) || TouchControls.ConsumeCamera();
            DebugTogglePressed |= kb != null && kb.f1Key.wasPressedThisFrame;
            AutoShiftTogglePressed |= kb != null && kb.f2Key.wasPressedThisFrame;
            TractionTogglePressed |= kb != null && kb.f3Key.wasPressedThisFrame;

            bbUp |= (gp != null && gp.dpad.right.wasPressedThisFrame) || (kb != null && kb.rightBracketKey.wasPressedThisFrame);
            bbDown |= (gp != null && gp.dpad.left.wasPressedThisFrame) || (kb != null && kb.leftBracketKey.wasPressedThisFrame);
            tc |= (gp != null && gp.dpad.down.wasPressedThisFrame) || (kb != null && kb.tKey.wasPressedThisFrame);
            ers |= (gp != null && gp.dpad.up.wasPressedThisFrame) || (kb != null && kb.mKey.wasPressedThisFrame);
            overtake |= (gp != null && gp.buttonEast.wasPressedThisFrame) || (kb != null && kb.oKey.wasPressedThisFrame);
            pit |= (gp != null && gp.buttonWest.wasPressedThisFrame) || (kb != null && kb.pKey.wasPressedThisFrame);
            radio |= (gp != null && gp.rightStickButton.wasPressedThisFrame) || (kb != null && kb.vKey.wasPressedThisFrame);
        }

        public bool ConsumeShiftUp() { bool v = ShiftUpPressed; ShiftUpPressed = false; return v; }
        public bool ConsumeShiftDown() { bool v = ShiftDownPressed; ShiftDownPressed = false; return v; }
        public bool ConsumeReset() { bool v = ResetPressed; ResetPressed = false; return v; }
        public bool ConsumeCameraToggle() { bool v = CameraTogglePressed; CameraTogglePressed = false; return v; }
        public bool ConsumeDebugToggle() { bool v = DebugTogglePressed; DebugTogglePressed = false; return v; }
        public bool ConsumeTractionToggle() { bool v = TractionTogglePressed; TractionTogglePressed = false; return v; }
        public bool ConsumeAutoShiftToggle() { bool v = AutoShiftTogglePressed; AutoShiftTogglePressed = false; return v; }
    }
}
