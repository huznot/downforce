using OWR.Vehicle;
using UnityEngine;

namespace OWR.AudioFX
{
    /// <summary>
    /// Sample-based engine for WebGL/mobile. Four looping sources: two adjacent RPM bands (slot = band % 2, so a slot
    /// is only re-assigned while silent) x on/off throttle, pitch-shifted to the exact RPM and crossfaded.
    /// Gear-change cut, rev-limiter stutter and overrun pops are reproduced with volume shaping and one-shots.
    /// </summary>
    public class SampledEngineAudio : MonoBehaviour
    {
        public CarController car;
        public float volume = 0.55f;
        public float cullDistance;
        AudioBank bank;
        readonly AudioSource[] on = new AudioSource[2], off = new AudioSource[2];
        readonly int[] slotBand = { -1, -1 };
        AudioSource popSrc;
        Transform listener;
        float thr, cut, lastThrottle, overrun, limT;
        bool lite, paused;

        public void Init(AudioSource template, CarController c, float vol, float cull)
        {
            car = c; volume = vol; cullDistance = cull;
            bank = AudioBank.Load();
            if (!bank || bank.engineRpm == null || bank.engineRpm.Length < 2) { enabled = false; return; }
            lite = cull > 0f;   // rivals: one band pair only, no pops (the player's own car gets the full set)
            for (int s = 0; s < (lite ? 1 : 2); s++) { on[s] = Make(template); off[s] = Make(template); }
            if (!lite) { popSrc = Make(template); popSrc.loop = false; }
            if (car && car.Drivetrain != null) car.Drivetrain.OnShift += (g, up) => cut = 1f;
        }

        AudioSource Make(AudioSource t)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false; a.loop = true; a.volume = 0f;
            a.spatialBlend = t.spatialBlend; a.minDistance = t.minDistance; a.maxDistance = t.maxDistance;
            a.rolloffMode = t.rolloffMode; a.dopplerLevel = t.dopplerLevel; a.spread = t.spread; a.priority = t.priority;
            a.outputAudioMixerGroup = t.outputAudioMixerGroup;
            return a;
        }

        void SetBand(int slot, int band)
        {
            if (slotBand[slot] == band) return;
            slotBand[slot] = band;
            float start = Random.value;
            foreach (var a in new[] { on[slot], off[slot] })
            {
                a.clip = a == on[slot] ? bank.engineOn[band] : bank.engineOff[band];
                a.time = start * a.clip.length * 0.9f;
                a.Play();
            }
        }

        void Update()
        {
            if (!car || bank == null) return;
            bool audible = true;
            if (cullDistance > 0f)
            {
                if (!listener) { var l = FindFirstObjectByType<AudioListener>(); if (l) listener = l.transform; }
                audible = listener && (listener.position - transform.position).sqrMagnitude < cullDistance * cullDistance;
            }
            // out of earshot: pause the voices outright (a silent playing source still costs mixing time)
            bool wantPause = !audible || EngineAudio.MenuMute;
            if (wantPause != paused)
            {
                paused = wantPause;
                foreach (var a in new[] { on[0], off[0], on[1], off[1] }) if (a) { if (paused) a.Pause(); else a.UnPause(); }
            }
            if (paused) return;
            float master = volume * EngineAudio.Duck;
            var t = car.telemetry;
            float rpm = Mathf.Max(t.rpm, 1000f);
            var R = bank.engineRpm;
            int i = 0;
            while (i < R.Length - 2 && rpm > R[i + 1]) i++;
            float f = Mathf.Clamp01((rpm - R[i]) / (R[i + 1] - R[i]));
            if (lite)
            {
                int nb = f < 0.5f ? i : i + 1;
                SetBand(0, nb);
                float lvl = master * (1f - 0.7f * Mathf.Max(0f, cut));
                thr = Mathf.MoveTowards(thr, t.throttle, Time.deltaTime * 6f);
                if (cut > 0f) cut -= Time.deltaTime / 0.06f;
                on[0].pitch = off[0].pitch = rpm / R[nb];
                on[0].volume = lvl * Mathf.Sqrt(thr); off[0].volume = lvl * Mathf.Sqrt(1f - thr);
                return;
            }
            SetBand(i % 2, i); SetBand((i + 1) % 2, i + 1);

            thr = Mathf.MoveTowards(thr, t.throttle, Time.deltaTime * 6f);
            if (cut > 0f) cut -= Time.deltaTime / 0.06f;
            float level = master * (1f - 0.7f * Mathf.Max(0f, cut));
            limT += Time.deltaTime * 22f;
            if (t.revLimiter && (limT % 1f) < 0.5f) level *= 0.3f;

            for (int s = 0; s < 2; s++)
            {
                int band = slotBand[s];
                float w = band == i ? 1f - f : 1f - Mathf.Abs(f - 1f);
                w = Mathf.Sqrt(Mathf.Clamp01(w));      // equal-power crossfade
                float pitch = rpm / R[band];
                on[s].pitch = off[s].pitch = pitch;
                on[s].volume = level * w * Mathf.Sqrt(thr);
                off[s].volume = level * w * Mathf.Sqrt(1f - thr);
            }

            // overrun crackle on lift-off at high revs
            if (lastThrottle > 0.55f && t.throttle < 0.15f && t.rpm > 7500f) overrun = 0.9f;
            if (overrun > 0f && audible)
            {
                overrun -= Time.deltaTime;
                if (bank.pops != null && bank.pops.Length > 0 && Random.value < Time.deltaTime * 14f)
                    popSrc.PlayOneShot(bank.pops[Random.Range(0, bank.pops.Length)], master * Random.Range(0.5f, 1f));
            }
            lastThrottle = t.throttle;
        }
    }

    /// <summary>Sample-based version of <see cref="AmbientNoise"/> (rain / wind / tyres+kerb / crowd beds).</summary>
    public class SampledAmbient : MonoBehaviour
    {
        AmbientNoise src;
        AudioSource main, kerbSrc;
        bool ambPaused;
        float[] lastComp = new float[4];
        Transform listener;

        public void Init(AmbientNoise n, AudioSource template)
        {
            src = n;
            var bank = AudioBank.Load();
            if (!bank) { enabled = false; return; }
            AudioClip clip = n.mode switch
            {
                AmbientNoise.Mode.Rain => bank.rain,
                AmbientNoise.Mode.Wind => bank.wind,
                AmbientNoise.Mode.Tyres => bank.tyres,
                _ => bank.crowd,
            };
            main = Make(template, clip);
            if (n.mode == AmbientNoise.Mode.Tyres) kerbSrc = Make(template, bank.kerb);
        }

        AudioSource Make(AudioSource t, AudioClip c)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = c; a.loop = true; a.playOnAwake = false; a.volume = 0f;
            a.spatialBlend = t.spatialBlend; a.minDistance = t.minDistance; a.maxDistance = t.maxDistance;
            a.rolloffMode = t.rolloffMode; a.dopplerLevel = 0f; a.priority = t.priority;
            if (c) { a.time = Random.value * c.length * 0.9f; a.Play(); }
            return a;
        }

        void Update()
        {
            if (!src || !main) return;
            float duck = EngineAudio.Duck;
            float sp = src.car ? Mathf.Abs(src.car.telemetry.speedMs) : 0f;
            bool audible = true;
            if (src.cullDistance > 0f)
            {
                if (!listener) { var l = FindFirstObjectByType<AudioListener>(); if (l) listener = l.transform; }
                audible = listener && (listener.position - transform.position).sqrMagnitude < src.cullDistance * src.cullDistance;
            }
            if (audible == ambPaused)
            {
                ambPaused = !audible;
                if (ambPaused) { main.Pause(); if (kerbSrc) kerbSrc.Pause(); } else { main.UnPause(); if (kerbSrc) kerbSrc.UnPause(); }
            }
            if (ambPaused) return;
            float v = src.volume * duck;
            switch (src.mode)
            {
                case AmbientNoise.Mode.Rain:
                    main.volume = v * src.rain / 0.75f; break;
                case AmbientNoise.Mode.Wind:
                {
                    float k = Mathf.Clamp01(sp / 85f);
                    main.volume = v * k * k; main.pitch = Mathf.Lerp(0.55f, 1f, k); break;
                }
                case AmbientNoise.Mode.Tyres:
                {
                    float k = Mathf.Clamp01(sp / 70f);
                    main.volume = v * k * (0.4f + src.rain) / 1.15f;
                    float activity = 0f;
                    if (src.car)
                        for (int i = 0; i < src.car.wheels.Length && i < 4; i++)
                        { float c = src.car.wheels[i].compression; activity += Mathf.Abs(c - lastComp[i]); lastComp[i] = c; }
                    float kerb = Mathf.Clamp01(activity / Mathf.Max(Time.deltaTime, 1e-3f) * 0.6f);
                    if (kerbSrc) { kerbSrc.volume = v * kerb; kerbSrc.pitch = Mathf.Clamp((28f + sp * 1.6f) / 76f, 0.4f, 2.5f); }
                    break;
                }
                default:
                {
                    float d = src.car ? Vector3.Distance(src.car.transform.position, transform.position) : 999f;
                    float target = src.car ? Mathf.Clamp01(1f - d / 140f) * Mathf.Clamp01(sp / 50f) : 0f;
                    float boost = Track.CrowdHype.Level;
                    main.volume = v * (0.45f + Mathf.Max(target, boost) * 1.4f) / 0.87f; break;
                }
            }
        }
    }
}
