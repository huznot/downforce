using System.IO;
using System.Reflection;
using OWR.AudioFX;
using UnityEditor;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>
    /// OWR > Bake Audio Bank: runs the procedural engine / ambience synths offline (same code as at runtime) and
    /// saves seamless loops into Resources/AudioBank for the WebGL and mobile builds.
    /// </summary>
    public static class AudioBankBaker
    {
        const string Dir = "Assets/_Game/Resources/AudioBank";
        const int Rate = 44100;
        static readonly float[] Bands = { 3500f, 5000f, 6500f, 8000f, 9500f, 11000f, 12500f };

        [MenuItem("OWR/Bake Audio Bank (WebGL + Mobile)")]
        public static void Bake()
        {
            Directory.CreateDirectory(Dir);
            var go = new GameObject("AudioBake") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var bank = ScriptableObject.CreateInstance<AudioBank>();
                bank.engineRpm = Bands;
                bank.engineOn = new AudioClip[Bands.Length];
                bank.engineOff = new AudioClip[Bands.Length];
                for (int b = 0; b < Bands.Length; b++)
                {
                    bank.engineOn[b] = Save($"eng_on_{(int)Bands[b]}", Engine(go, Bands[b], 1f, 2.4f));
                    bank.engineOff[b] = Save($"eng_off_{(int)Bands[b]}", Engine(go, Bands[b], 0f, 2.4f));
                }
                bank.pops = new AudioClip[3];
                for (int i = 0; i < 3; i++) bank.pops[i] = Save("pop_" + i, Pop(i));
                bank.rain = Save("amb_rain", Ambient(go, AmbientNoise.Mode.Rain, 0f, 0f, 0f, 0.75f, 6f));
                bank.wind = Save("amb_wind", Ambient(go, AmbientNoise.Mode.Wind, 85f, 0f, 0f, 0.75f, 6f));
                bank.tyres = Save("amb_tyres", Ambient(go, AmbientNoise.Mode.Tyres, 70f, 0f, 0f, 0.75f, 5f));
                bank.kerb = Save("amb_kerb", Ambient(go, AmbientNoise.Mode.Tyres, 30f, 1f, 0f, -0.4f, 3f));
                bank.crowd = Save("amb_crowd", Ambient(go, AmbientNoise.Mode.Crowd, 0f, 0f, 0.3f, 0.75f, 10f));
                AssetDatabase.DeleteAsset("Assets/_Game/Resources/AudioBank.asset");
                AssetDatabase.CreateAsset(bank, "Assets/_Game/Resources/AudioBank.asset");
                AssetDatabase.SaveAssets();
                Debug.Log("[OWR] Audio bank baked: " + Bands.Length * 2 + " engine loops + ambience");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Set(object o, string field, object v)
        {
            var f = o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            f.SetValue(o, v);
        }

        static float[] Run(MonoBehaviour m, float seconds)
        {
            var mi = m.GetType().GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.NonPublic);
            int n = (int)(Rate * seconds);
            var outp = new float[n];
            var buf = new float[1024];
            for (int i = 0; i < n; i += buf.Length)
            {
                System.Array.Clear(buf, 0, buf.Length);
                mi.Invoke(m, new object[] { buf, 1 });
                System.Array.Copy(buf, 0, outp, i, Mathf.Min(buf.Length, n - i));
            }
            return outp;
        }

        /// <summary>Renders warm-up + loop + crossfade tail and folds the tail into the head for a seamless loop.</summary>
        static float[] Loop(float[] raw, float warm, float len, float fade)
        {
            int w = (int)(warm * Rate), L = (int)(len * Rate), X = (int)(fade * Rate);
            var o = new float[L];
            for (int i = 0; i < L; i++) o[i] = raw[w + i];
            for (int i = 0; i < X; i++)
            {
                float k = i / (float)X;
                o[i] = raw[w + i] * Mathf.Sqrt(k) + raw[w + L + i] * Mathf.Sqrt(1f - k);
            }
            return o;
        }

        static float[] Engine(GameObject go, float rpm, float thr, float len)
        {
            var e = go.AddComponent<EngineAudio>();
            Set(e, "sampleRate", Rate); Set(e, "rng", new System.Random((int)rpm + (int)(thr * 7)));
            e.volume = 1f; e.harmonics = 10;
            Set(e, "pRpm", rpm); Set(e, "pThrottle", thr); Set(e, "pSpeed", 40f); Set(e, "pErs", 0f);
            Set(e, "pLimiter", 0f); Set(e, "pDuck", 1f);
            Set(e, "rpmS", rpm); Set(e, "thrS", thr); Set(e, "speedS", 40f);
            var raw = Run(e, 0.6f + len + 0.35f);
            Object.DestroyImmediate(e);
            return Loop(raw, 0.6f, len, 0.3f);
        }

        static float[] Ambient(GameObject go, AmbientNoise.Mode mode, float speed, float kerb, float accent, float rain, float len)
        {
            var a = go.AddComponent<AmbientNoise>();
            a.mode = mode; a.volume = 1f; a.rain = rain;
            Set(a, "sampleRate", Rate); Set(a, "rng", new System.Random(77 + (int)mode));
            Set(a, "pSpeed", speed); Set(a, "pKerb", kerb); Set(a, "pAccent", accent); Set(a, "pDuck", 1f);
            var raw = Run(a, 1f + len + 0.6f);
            Object.DestroyImmediate(a);
            return Loop(raw, 1f, len, 0.5f);
        }

        static float[] Pop(int seed)
        {
            var r = new System.Random(900 + seed);
            int n = (int)(Rate * 0.35f);
            var o = new float[n];
            float env = 0.8f + seed * 0.15f, lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(r.NextDouble() * 2.0 - 1.0);
                float bang = noise * env * (t < 0.004f ? 2.2f : 1f);
                lp += (bang - lp) * 0.35f; lp2 += (lp - lp2) * 0.35f;
                o[i] = (float)System.Math.Tanh(lp2 * 1.6f) * 0.6f;
                env *= 0.9965f;
            }
            return o;
        }

        static AudioClip Save(string name, float[] data)
        {
            float peak = 0f;
            foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            string path = Dir + "/" + name + ".wav";
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int bytes = data.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var v in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(path);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.forceToMono = true;
            imp.SaveAndReimport();
            if (peak > 1f) Debug.LogWarning($"[OWR] {name} clipped (peak {peak:F2})");
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
