using OWR.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace OWR.Cockpit
{
    /// <summary>Rotates the steering wheel with input, drives the shift-light strip and the wheel display.</summary>
    [DefaultExecutionOrder(-100)]
    public class SteeringWheelRig : MonoBehaviour
    {
        public CarController car;
        [Tooltip("Rotates around its local Z axis (Z points from the wheel face toward the dash).")]
        public Transform wheel;
        public float maxRotation = 70f;

        [Header("Shift lights (left to right)")]
        public Renderer[] shiftLights;
        public float lightsStartRpm = 9500f;
        public float lightsFullRpm = 12100f;

        [Header("Display")]
        public Text gearText;
        public Text speedText;
        public Text infoText;
        public Image batteryBar;
        public Text lapTimeText;
        public Text deltaText;
        public WheelControls controls;
        public Text statsText;
        [System.NonSerialized] public int raceLap = 1, raceLaps = 8, racePos = 1, raceCars = 8;
        /// <summary>Fed by the race HUD: current lap time and live delta to the best lap (seconds).</summary>
        [System.NonSerialized] public float raceLapTime, raceDelta;
        [System.NonSerialized] public bool practice;
        float lapStart;
        float delta;

        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseId = Shader.PropertyToID("_BaseColor");

        float wheelAngle, wheelVel;
        void Awake() => mpb = new MaterialPropertyBlock();

        void LateUpdate()
        {
            if (!car) return;
            var t = car.telemetry;
            if (wheel)
            {
                // the wheel follows the real front-wheel angle through a fixed steering ratio (small inputs at speed,
                // big lock only in slow corners), with a weighted, damped hand motion instead of snapping to the key
                // follows the driver's input: big lock in slow corners, calmer at speed (but always clearly visible)
                float target = t.steer * Mathf.Lerp(95f, 50f, Mathf.Clamp01(Mathf.Abs(t.speedMs) / 60f));
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                float k = 160f, c = 2f * 0.95f * Mathf.Sqrt(k);
                wheelVel += (k * (target - wheelAngle) - c * wheelVel) * dt;
                wheelVel = Mathf.Clamp(wheelVel, -540f, 540f);
                wheelAngle += wheelVel * dt;
                wheel.localRotation = Quaternion.Euler(0f, 0f, -wheelAngle);
            }

            if (shiftLights != null && shiftLights.Length > 0)
            {
                int n = shiftLights.Length;
                float f = Mathf.InverseLerp(lightsStartRpm, lightsFullRpm, t.rpm);
                int lit = Mathf.CeilToInt(f * n);
                bool flash = t.revLimiter || t.rpm >= lightsFullRpm;
                bool flashOn = (Time.time * 12f) % 1f < 0.5f;
                for (int i = 0; i < n; i++)
                {
                    Color c = i < n * 0.4f ? new Color(0.1f, 1f, 0.2f) : i < n * 0.8f ? new Color(1f, 0.08f, 0.05f) : new Color(0.25f, 0.4f, 1f);
                    bool on = flash ? flashOn : i < lit;
                    mpb.SetColor(EmissionId, on ? c * 6f : Color.black);
                    mpb.SetColor(BaseId, on ? c : new Color(0.05f, 0.05f, 0.05f));
                    shiftLights[i].SetPropertyBlock(mpb);
                }
            }

            if (gearText) gearText.text = t.gear == -1 ? "R" : t.gear == 0 ? "N" : t.gear.ToString();
            if (speedText) speedText.text = Mathf.RoundToInt(Mathf.Abs(t.SpeedKmh)).ToString();
            if (infoText)
            {
                string tc = car.setup.tractionControl ? "TC <color=#7dff8a>ON</color>" : "TC <color=#ff6a5a>OFF</color>";
                string ers = controls ? WheelControls.ErsNames[controls.ErsMode] : "RACE";
                string extra = "";
                if (controls && controls.OvertakeTimer > 0f) extra += "  <color=#3fa9ff>OVERTAKE</color>";
                if (controls && controls.PitLimiter) extra += "  <color=#ffd400>PIT LIM</color>";
                if (controls && controls.RadioTimer > 0f) extra += "  <color=#ffffff>RADIO</color>";
                infoText.text = $"BB {car.setup.brakeBias * 100f:0.0}   {tc}   ERS {ers}{extra}";
            }
            if (batteryBar) batteryBar.fillAmount = t.batteryFraction;
            if (statsText && statsText.horizontalOverflow != HorizontalWrapMode.Overflow) statsText.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (statsText) statsText.text = practice ? $"LAP  {raceLap}\nPRACTICE\nTYR  INT" : $"LAP  {raceLap}/{raceLaps}\nPOS  {racePos}/{raceCars}\nTYR  INT";
            if (lapTimeText)
            {
                float lt = raceLapTime;
                lapTimeText.text = $"{(int)(lt / 60f)}:{lt % 60f:00.000}";
            }
            if (deltaText)
            {
                // placeholder live delta until lap timing lines exist: drifts with throttle/brake use
                delta = raceDelta;
                deltaText.text = (delta >= 0 ? "+" : "-") + Mathf.Abs(delta).ToString("0.000");
                deltaText.color = delta >= 0 ? new Color(1f, 0.25f, 0.2f) : new Color(0.25f, 1f, 0.35f);
            }
        }
    }
}
