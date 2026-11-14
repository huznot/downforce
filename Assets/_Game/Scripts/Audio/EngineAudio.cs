using OWR.Vehicle;
using UnityEngine;

namespace OWR.AudioFX
{
    /// <summary>
    /// Procedural hybrid-V6 engine (no samples): firing-frequency harmonic stack with per-cycle combustion jitter,
    /// load-dependent brightness and rasp, turbo whistle, MGU-K electric whine, gear-change cut,
    /// overrun crackle/pops on lift-off and a stuttering rev limiter. Synthesised on the audio thread.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudio : MonoBehaviour
    {
        public CarController car;
        [Range(0f, 1f)] public float volume = 0.55f;
        public int cylinders = 6;
        [Tooltip("Harmonics synthesised (AI cars use fewer to keep the audio thread light).")]
        [Range(2, 10)] public int harmonics = 10;
        [Tooltip("Silence (and stop synthesising) beyond this distance from the listener; 0 = never.")]
        public float cullDistance = 0f;
        /// <summary>Global volume multiplier (commentary ducks everything under the voice).</summary>
        public static float Duck = 1f;
        /// <summary>True while the main menu is up: every engine is silent.</summary>
        public static bool MenuMute;
        float lump = 1f, lumpTarget = 1f;
        AudioSource src;
        Transform listener;

        // parameters handed from the main thread to the audio thread
        volatile float pRpm, pThrottle, pSpeed, pErs, pLimiter, pDuck = 1f;
        volatile int pShiftCount, pPopCount;

        int sampleRate;
        System.Random rng;
        double phase, turboPhase, mgukPhase, mguk2Phase;
        float rpmS = 4000f, thrS, ersS, speedS;
        float cutEnv, popEnv, popTimer;
        float lp1, lp2, hp, hpPrev;
        int seenShift, seenPop;
        float[] jitter = new float[10];
        static readonly float[] Harm = { 1f, 0.7f, 0.52f, 0.4f, 0.33f, 0.25f, 0.18f, 0.13f, 0.09f, 0.06f };

        double limPhase;
        float lastThrottle;
        float overrunTimer;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            rng = new System.Random(1234);
            src = GetComponent<AudioSource>();
            if (AudioBank.UseSampled && AudioBank.Load())
            {
                // WebGL / mobile: hand over to the pre-rendered loops (no audio-thread synthesis)
                src.Stop(); src.enabled = false;
                sampledMode = true;
                return;
            }
            src.clip = AudioClip.Create("engine_carrier", sampleRate, 1, sampleRate, false);
            src.loop = true;
            src.playOnAwake = true;
            src.volume = 1f;
            if (!src.isPlaying) src.Play();
        }

        bool sampledMode;

        void Start()
        {
            if (sampledMode)
            {
                gameObject.AddComponent<SampledEngineAudio>().Init(src, car, volume, cullDistance);
                enabled = false;
                return;
            }
            if (car && car.Drivetrain != null) car.Drivetrain.OnShift += (g, up) => pShiftCount++;
        }

        void Update()
        {
            if (!car) return;
            if (cullDistance > 0f)
            {
                if (!listener) { var l = FindFirstObjectByType<AudioListener>(); if (l) listener = l.transform; }
                bool near = listener && (listener.position - transform.position).sqrMagnitude < cullDistance * cullDistance;
                if (src.enabled != near) src.enabled = near;
                if (!near) return;
            }
            pDuck = MenuMute ? 0f : Duck;
            var t = car.telemetry;
            pRpm = t.rpm;
            pThrottle = t.throttle;
            pSpeed = Mathf.Abs(t.speedMs);
            pErs = t.ersDeploying ? 1f : (t.brake > 0.2f ? 0.5f : 0f);
            pLimiter = t.revLimiter ? 1f : 0f;

            // overrun: lifting off at high revs schedules a burst of pops/crackles
            if (lastThrottle > 0.55f && t.throttle < 0.15f && t.rpm > 7500f) overrunTimer = 0.9f;
            if (overrunTimer > 0f)
            {
                overrunTimer -= Time.deltaTime;
                if (Random.value < Time.deltaTime * 14f) pPopCount++;
            }
            lastThrottle = t.throttle;
        }

        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        void OnAudioFilterRead(float[] data, int channels)
        {
            float dt = 1f / sampleRate;
            float rpmT = pRpm, thrT = pThrottle, ersT = pErs, spT = pSpeed;
            bool limiter = pLimiter > 0.5f;
            if (pShiftCount != seenShift) { seenShift = pShiftCount; cutEnv = 1f; }
            if (pPopCount != seenPop) { seenPop = pPopCount; popEnv = 0.6f + (float)rng.NextDouble() * 0.6f; popTimer = 0f; }

            for (int i = 0; i < data.Length; i += channels)
            {
                rpmS += (rpmT - rpmS) * 0.002f;
                thrS += (thrT - thrS) * 0.0008f;
                ersS += (ersT - ersS) * 0.0005f;
                speedS += (spT - speedS) * 0.0005f;

                float fire = rpmS / 60f * (cylinders * 0.5f);   // firing frequency (4-stroke)
                double prev = phase;
                phase += fire * dt;
                if ((int)phase != (int)prev)
                {
                    for (int k = 0; k < jitter.Length; k++) jitter[k] = Noise() * 0.06f * (0.4f + thrS);
                    // idle is lumpy: each combustion cycle gets an uneven strength (fades out as revs rise)
                    float idleK = Mathf.Clamp01(1f - (rpmS - 4000f) / 3000f) * (1f - thrS);
                    lumpTarget = 1f - idleK * (0.25f + 0.55f * (float)rng.NextDouble());
                }
                lump += (lumpTarget - lump) * 0.02f;
                if (phase > 1e6) phase -= 1e6;

                // harmonic stack, upper harmonics grow with load
                float s = 0f;
                float bright = Mathf.Lerp(0.35f, 1.05f, thrS);
                double tp = phase * 2.0 * System.Math.PI;
                int hc = Mathf.Min(harmonics, Harm.Length);
                for (int k = 0; k < hc; k++)
                {
                    float w = k < 3 ? 1f : bright * (1f - k * 0.04f);
                    s += Harm[k] * w * (float)System.Math.Sin(tp * (k + 1) + jitter[k] * (k + 1));
                }
                // half-order (uneven firing) and rasp: noise gated by the combustion pulses
                s += 0.22f * (float)System.Math.Sin(tp * 0.5);
                float pulse = Mathf.Max(0f, (float)System.Math.Sin(tp));
                pulse *= pulse; pulse *= pulse;
                s += Noise() * pulse * (0.35f + 0.9f * thrS);

                // turbo whistle and MGU-K whine
                turboPhase += (2600.0 + rpmS * 0.42) * dt;
                s += 0.035f * thrS * (float)System.Math.Sin(turboPhase * 2.0 * System.Math.PI);
                mgukPhase += (180.0 + speedS * 42.0) * dt;
                mguk2Phase += (360.0 + speedS * 84.0) * dt;
                s += ersS * (0.07f * (float)System.Math.Sin(mgukPhase * 2.0 * System.Math.PI) + 0.03f * (float)System.Math.Sin(mguk2Phase * 2.0 * System.Math.PI));

                // quieter, choppier idle; full voice under load and at high revs
                float revK = Mathf.Clamp01((rpmS - 4500f) / 7000f);
                float level = (0.14f + 0.18f * revK + 0.68f * thrS) * lump;
                // gear change: 40 ms torque cut dip
                if (cutEnv > 0f) { level *= 1f - 0.75f * cutEnv; cutEnv -= dt / 0.045f; }
                // rev limiter stutter
                limPhase += 22.0 * dt;
                if (limiter && (limPhase % 1.0) < 0.5) level *= 0.25f;
                s *= level;

                // overrun pops: short noisy bangs with fast decay
                if (popEnv > 0.001f)
                {
                    popTimer += dt;
                    float bang = Noise() * popEnv * (popTimer < 0.004f ? 2.2f : 1f);
                    s += bang * 1.4f;
                    popEnv *= 0.9965f;
                }

                // tone shaping: dc block + gentle low-pass that opens with load, then soft clip
                hp = 0.995f * (hp + s - hpPrev); hpPrev = s; s = hp;
                float a = Mathf.Lerp(0.18f, 0.42f, thrS);
                lp1 += (s - lp1) * a; lp2 += (lp1 - lp2) * a;
                float o = (float)System.Math.Tanh(lp2 * 1.4f) * volume * 0.45f * pDuck;

                for (int c = 0; c < channels; c++) data[i + c] = o;
            }
        }
    }
}
