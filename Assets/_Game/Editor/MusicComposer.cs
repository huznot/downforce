using System.IO;
using UnityEditor;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>
    /// Composes the original title-screen theme offline (no samples): a calm dusk piece in D major —
    /// warm detuned pads, soft sub bass, plucked arpeggios, a breath of wind — through a lush reverb, as a seamless loop.
    /// Menu: OWR > Compose Menu Music.
    /// </summary>
    public static class MusicComposer
    {
        public const string OutPath = "Assets/_Game/Audio/Music/MenuTheme.wav";
        const int SR = 44100;

        // MIDI note numbers. Progression (2 bars each): Dmaj9, Bm11, Gmaj9, Asus4 -> A
        static readonly int[][] Pads = { new[] { 57, 61, 64, 66, 69 }, new[] { 54, 57, 62, 64, 66 }, new[] { 54, 57, 59, 62, 66 }, new[] { 52, 57, 59, 62, 64 } };
        static readonly int[] Bass = { 38, 35, 31, 33 };
        static readonly int[][] Arps = { new[] { 74, 76, 78, 81, 78, 76 }, new[] { 71, 74, 76, 78, 76, 74 }, new[] { 71, 74, 78, 79, 78, 74 }, new[] { 69, 71, 74, 76, 74, 73 } };

        [MenuItem("OWR/Compose Menu Music")]
        public static void Compose()
        {
            float bpm = 72f, beat = 60f / bpm, bar = beat * 4f, chordLen = bar * 2f;
            int cycles = 2;
            float total = chordLen * Pads.Length * cycles;
            int n = Mathf.CeilToInt(total * SR);
            var L = new float[n]; var R = new float[n];
            var rnd = new System.Random(11);

            for (int c = 0; c < Pads.Length * cycles; c++)
            {
                int ci = c % Pads.Length;
                float t0 = c * chordLen;
                // ---- pad voices: 3 detuned band-limited saws per note, slow swell, long release (overlaps next chord)
                foreach (int note in Pads[ci])
                    for (int d = 0; d < 3; d++)
                    {
                        float f = Freq(note) * (1f + (d - 1) * 0.0035f);
                        float pan = 0.5f + (d - 1) * 0.3f;
                        Voice(L, R, t0 - 0.6f, chordLen + 2.6f, f, 0.026f, pan, 1.6f, 2.6f, 5, 0.55f);
                    }
                // ---- sub bass
                Voice(L, R, t0, chordLen + 0.8f, Freq(Bass[ci]), 0.09f, 0.5f, 0.8f, 1.2f, 2, 1f);
                // ---- plucked arpeggio (eighth notes), sparser in the first cycle
                float eighth = beat / 2f;
                int steps = Mathf.RoundToInt(chordLen / eighth);
                for (int s = 0; s < steps; s++)
                {
                    bool play = c < Pads.Length ? (s % 2 == 0 || rnd.NextDouble() < 0.25) : rnd.NextDouble() < 0.85;
                    if (!play) continue;
                    int note = Arps[ci][s % Arps[ci].Length] + (rnd.NextDouble() < 0.12 ? 12 : 0);
                    float vel = 0.05f + (float)rnd.NextDouble() * 0.03f;
                    Pluck(L, R, t0 + s * eighth + (float)rnd.NextDouble() * 0.012f, Freq(note), vel, s % 2 == 0 ? 0.35f : 0.65f);
                }
            }

            // ---- wind breath: slowly swelling filtered noise
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float nz = (float)(rnd.NextDouble() * 2 - 1);
                lp += (nz - lp) * 0.02f; lp2 += (lp - lp2) * 0.02f;
                float sw = 0.5f + 0.5f * Mathf.Sin(t * 0.21f) * Mathf.Sin(t * 0.13f + 1f);
                L[i] += lp2 * 0.35f * sw; R[i] += lp2 * 0.35f * (1f - sw * 0.5f);
            }

            // ---- reverb (Schroeder: 4 combs + 2 allpasses per channel), tail wrapped for a seamless loop
            var wl = Reverb(L, new[] { 1557, 1617, 1491, 1422 }, 0.84f);
            var wr = Reverb(R, new[] { 1580, 1640, 1514, 1445 }, 0.84f);
            var outL = new float[n]; var outR = new float[n];
            for (int i = 0; i < n; i++) { outL[i] = L[i] * 0.75f + wl[i] * 0.45f; outR[i] = R[i] * 0.75f + wr[i] * 0.45f; }
            // second pass over the wrapped tail so the loop point is smooth
            for (int i = 0; i < Mathf.Min(SR * 4, n); i++) { float k = 1f - i / (float)(SR * 4); outL[i] += wl[(n - SR * 4 + i) % n] * 0.0f + L[(n - SR * 4 + i) % n] * 0.12f * k; outR[i] += R[(n - SR * 4 + i) % n] * 0.12f * k; }

            // ---- master: soft limit + gentle fade at the loop seam
            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(outL[i]), Mathf.Abs(outR[i]));
            float g = 0.9f / Mathf.Max(peak, 1e-4f);
            for (int i = 0; i < n; i++)
            {
                outL[i] = (float)System.Math.Tanh(outL[i] * g * 1.2f);
                outR[i] = (float)System.Math.Tanh(outR[i] * g * 1.2f);
            }
            WriteWav(OutPath, outL, outR);
            AssetDatabase.ImportAsset(OutPath);
            var imp = (AudioImporter)AssetImporter.GetAtPath(OutPath);
            var st = imp.defaultSampleSettings; st.loadType = AudioClipLoadType.Streaming; st.compressionFormat = AudioCompressionFormat.Vorbis; st.quality = 0.7f;
            imp.defaultSampleSettings = st; imp.SaveAndReimport();
            Debug.Log($"[OWR] Menu theme composed: {total:0.0}s -> {OutPath}");
        }

        static float Freq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        static void Voice(float[] L, float[] R, float start, float dur, float f, float amp, float pan, float attack, float release, int harmonics, float bright)
        {
            int s0 = Mathf.Max(0, Mathf.RoundToInt(start * SR)), s1 = Mathf.Min(L.Length, Mathf.RoundToInt((start + dur) * SR));
            double ph = 0, dph = f / SR;
            float lpState = 0f;
            for (int i = s0; i < s1; i++)
            {
                float t = (i - start * SR) / SR;
                float env = Mathf.Clamp01(t / attack) * Mathf.Clamp01((dur - t) / release);
                env = env * env * (3f - 2f * env);
                float v = 0f;
                for (int h = 1; h <= harmonics; h++) v += (float)System.Math.Sin(ph * 2.0 * System.Math.PI * h) / h * (h == 1 ? 1f : bright);
                lpState += (v - lpState) * 0.25f;
                ph += dph;
                float o = lpState * amp * env;
                L[i] += o * (1f - pan) * 2f * 0.5f; R[i] += o * pan * 2f * 0.5f;
            }
        }

        static void Pluck(float[] L, float[] R, float start, float f, float amp, float pan)
        {
            int s0 = Mathf.Max(0, Mathf.RoundToInt(start * SR)), s1 = Mathf.Min(L.Length, s0 + SR * 2);
            for (int i = s0; i < s1; i++)
            {
                float t = (i - s0) / (float)SR;
                float env = Mathf.Exp(-t * 3.2f) * Mathf.Clamp01(t * 400f);
                float v = Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * t) * Mathf.Exp(-t * 6f) + 0.12f * Mathf.Sin(6f * Mathf.PI * f * t) * Mathf.Exp(-t * 9f);
                float o = v * env * amp;
                L[i] += o * (1f - pan); R[i] += o * pan;
            }
        }

        static float[] Reverb(float[] x, int[] combs, float fb)
        {
            int n = x.Length;
            var y = new float[n];
            foreach (int d in combs)
            {
                var buf = new float[d]; int idx = 0; float damp = 0f;
                for (int pass = 0; pass < 2; pass++)          // pass 0 primes the tail so it wraps into the loop start
                    for (int i = 0; i < n; i++)
                    {
                        float o = buf[idx];
                        damp = o * 0.7f + damp * 0.3f;
                        buf[idx] = x[i] + damp * fb;
                        idx = (idx + 1) % d;
                        if (pass == 1) y[i] += o * 0.25f;
                    }
            }
            foreach (int d in new[] { 556, 441 })
            {
                var buf = new float[d]; int idx = 0;
                for (int i = 0; i < n; i++)
                {
                    float b = buf[idx];
                    float o = -y[i] + b;
                    buf[idx] = y[i] + b * 0.5f;
                    idx = (idx + 1) % d;
                    y[i] = o;
                }
            }
            return y;
        }

        static void WriteWav(string assetPath, float[] L, float[] R)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            using var fs = new FileStream(assetPath, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int n = L.Length, bytes = n * 4;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(SR); w.Write(SR * 4); w.Write((short)4); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            for (int i = 0; i < n; i++)
            {
                w.Write((short)Mathf.Clamp(L[i] * 32767f, -32767f, 32767f));
                w.Write((short)Mathf.Clamp(R[i] * 32767f, -32767f, 32767f));
            }
        }
    }
}
