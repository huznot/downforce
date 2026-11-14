using OWR.Vehicle;
using UnityEngine;

namespace OWR.AudioFX
{
    /// <summary>
    /// Procedural noise beds (no samples): rain on the car, wind rush, wet-tyre hiss + kerb rumble, and a stadium
    /// crowd murmur. Parameters come from the car on the main thread and are synthesised on the audio thread.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AmbientNoise : MonoBehaviour
    {
        public enum Mode { Rain, Wind, Tyres, Crowd }
        public Mode mode;
        public CarController car;
        [Range(0f, 1f)] public float volume = 0.5f;
        [Range(0f, 1f)] public float rain = 0.75f;

        volatile float pSpeed, pKerb, pAccent, pDuck = 1f;
        [Tooltip("Crowd sources: silence beyond this distance from the listener (saves the audio thread).")]
        public float cullDistance = 0f;
        AudioSource srcRef; Transform listener;
        int sampleRate;
        System.Random rng;
        float lp1, lp2, lp3, bp1, bp2, hpS, hpPrev;
        float tickEnv, tickLp;
        double lfo1, lfo2, lfo3, rumblePhase;
        float[] lastComp = new float[4];
        float cheer;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            rng = new System.Random(GetInstanceID());
            var src = GetComponent<AudioSource>();
            srcRef = src;
            if (AudioBank.UseSampled && AudioBank.Load())
            {
                src.Stop(); src.enabled = false;
                gameObject.AddComponent<SampledAmbient>().Init(this, src);
                enabled = false;
                return;
            }
            src.clip = AudioClip.Create("noise_carrier", sampleRate, 1, sampleRate, false);
            src.loop = true;
            src.playOnAwake = true;
            if (!src.isPlaying) src.Play();
        }

        void Update()
        {
            pDuck = EngineAudio.Duck;
            if (cullDistance > 0f)
            {
                if (!listener) { var l = FindFirstObjectByType<AudioListener>(); if (l) listener = l.transform; }
                bool near = listener && (listener.position - transform.position).sqrMagnitude < cullDistance * cullDistance;
                if (srcRef.enabled != near) srcRef.enabled = near;
                if (!near) return;
            }
            if (car)
            {
                pSpeed = Mathf.Abs(car.telemetry.speedMs);
                float activity = 0f;
                for (int i = 0; i < car.wheels.Length && i < 4; i++)
                {
                    float c = car.wheels[i].compression;
                    activity += Mathf.Abs(c - lastComp[i]);
                    lastComp[i] = c;
                }
                pKerb = Mathf.Clamp01(activity / Mathf.Max(Time.deltaTime, 1e-3f) * 0.6f);
            }
            if (mode == Mode.Crowd)
            {
                // the crowd swells when the car is close and fast
                float d = car ? Vector3.Distance(car.transform.position, transform.position) : 999f;
                float target = car ? Mathf.Clamp01(1f - d / 140f) * Mathf.Clamp01(pSpeed / 50f) : 0f;
                cheer = Mathf.MoveTowards(cheer, target, Time.deltaTime * (target > cheer ? 0.8f : 0.25f));
                pAccent = cheer;
            }
        }

        float N() => (float)(rng.NextDouble() * 2.0 - 1.0);

        void OnAudioFilterRead(float[] data, int channels)
        {
            float sp = pSpeed, kerb = pKerb, accent = pAccent;
            float dt = 1f / sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                float o = 0f;
                float n = N();
                switch (mode)
                {
                    case Mode.Rain:
                    {
                        // steady hiss (high-passed noise) + random drop ticks on the bodywork
                        hpS = 0.97f * (hpS + n - hpPrev); hpPrev = n;
                        lp1 += (hpS - lp1) * 0.5f;
                        o = lp1 * 0.18f * rain;
                        if (rng.NextDouble() < 70.0 * rain / sampleRate) tickEnv = 0.4f + (float)rng.NextDouble() * 0.6f;
                        tickLp += (N() * tickEnv - tickLp) * 0.35f;
                        o += tickLp * 0.25f * rain;
                        tickEnv *= 0.993f;
                        break;
                    }
                    case Mode.Wind:
                    {
                        // low rumble -> roaring rush as speed rises
                        float k = Mathf.Clamp01(sp / 85f);
                        float a = Mathf.Lerp(0.01f, 0.12f, k);
                        lp1 += (n - lp1) * a; lp2 += (lp1 - lp2) * a;
                        lfo1 += 0.37 * dt;
                        float gust = 0.85f + 0.15f * (float)System.Math.Sin(lfo1 * 6.283);
                        o = lp2 * k * k * 0.9f * gust;
                        break;
                    }
                    case Mode.Tyres:
                    {
                        // wet hiss (band around 2-4 kHz) scaled by speed + low kerb rumble
                        float k = Mathf.Clamp01(sp / 70f);
                        hpS = 0.9f * (hpS + n - hpPrev); hpPrev = n;
                        lp1 += (hpS - lp1) * 0.45f;
                        o = lp1 * k * 0.35f * (0.4f + rain);
                        rumblePhase += (28.0 + sp * 1.6) * dt;
                        float sq = (rumblePhase % 1.0) < 0.5 ? 1f : -1f;
                        lp3 += (sq - lp3) * 0.05f;
                        o += lp3 * kerb * 0.6f;
                        break;
                    }
                    case Mode.Crowd:
                    {
                        // many voices = band-passed noise with slow overlapping swells; louder "cheer" near the car
                        lp1 += (n - lp1) * 0.12f; lp2 += (lp1 - lp2) * 0.12f;
                        float band = lp1 - lp2;
                        bp1 += (n - bp1) * 0.04f; bp2 += (bp1 - bp2) * 0.04f;
                        lfo1 += 0.11 * dt; lfo2 += 0.23 * dt; lfo3 += 0.07 * dt;
                        float swell = 0.6f + 0.2f * (float)System.Math.Sin(lfo1 * 6.283) + 0.12f * (float)System.Math.Sin(lfo2 * 6.283 + 1.3) + 0.08f * (float)System.Math.Sin(lfo3 * 6.283 + 2.1);
                        o = (band * 1.6f + (bp1 - bp2) * 0.8f) * swell * (0.45f + accent * 1.4f);
                        break;
                    }
                }
                o *= volume * pDuck * (mode == Mode.Crowd ? 0.32f : 1f);
                for (int c = 0; c < channels; c++) data[i + c] = o;
            }
        }
    }
}
