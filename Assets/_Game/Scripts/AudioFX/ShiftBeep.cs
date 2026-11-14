using UnityEngine;

namespace OWR.AudioFX
{
    /// <summary>
    /// Shift tone in the driver's earpiece: a short double beep just before the upshift point, once per gear,
    /// like the shift-tone some teams pipe into the radio. Plays in auto gears too. Player car only.
    /// The clip is generated once in code (works in WebGL, no filter callbacks).
    /// </summary>
    [RequireComponent(typeof(Vehicle.CarController))]
    public class ShiftBeep : MonoBehaviour
    {
        public float rpmBeforeShift = 450f;
        public float volume = 0.32f;
        Vehicle.CarController car;
        AudioSource src;
        static AudioClip clip;
        int beepedGear = -99;
        bool isPlayer, checkedPlayer;

        void Start()
        {
            car = GetComponent<Vehicle.CarController>();
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.volume = volume; src.priority = 10;
            if (!clip) clip = MakeClip();
        }

        static AudioClip MakeClip()
        {
            const int rate = 44100;
            float len = 0.2f;
            var data = new float[(int)(rate * len)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                // two 60 ms beeps, 40 ms apart, 2.2 kHz with a soft edge
                float local = t < 0.06f ? t : (t > 0.1f && t < 0.16f ? t - 0.1f : -1f);
                if (local < 0f) continue;
                float env = Mathf.Clamp01(local / 0.004f) * Mathf.Clamp01((0.06f - local) / 0.008f);
                data[i] = env * (0.8f * Mathf.Sin(2f * Mathf.PI * 2200f * t) + 0.2f * Mathf.Sin(2f * Mathf.PI * 4400f * t));
            }
            var c = AudioClip.Create("ShiftBeep", data.Length, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        void Update()
        {
            if (!checkedPlayer)
            {
                var rm = Race.RaceManager.Instance;
                if (rm && rm.Player != null) { isPlayer = rm.Player.car == car; checkedPlayer = true; }
                else if (!rm) { isPlayer = true; checkedPlayer = true; }   // test scenes without a race
                if (!checkedPlayer) return;
            }
            if (!isPlayer || car.Drivetrain == null) return;
            var dt = car.Drivetrain;
            int maxGear = car.setup.gearRatios.Length;
            if (dt.Gear < 1 || dt.Gear >= maxGear || dt.Shifting) return;
            if (dt.Gear != beepedGear && dt.Rpm > car.setup.autoUpshiftRpm - rpmBeforeShift)
            {
                beepedGear = dt.Gear;
                src.PlayOneShot(clip);
            }
            // re-arm when the revs drop well away (downshift, lift)
            if (dt.Gear == beepedGear && dt.Rpm < car.setup.autoUpshiftRpm - 2500f) beepedGear = -99;
        }
    }
}
