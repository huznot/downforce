using UnityEngine;

namespace OWR.AudioFX
{
    /// <summary>
    /// Pre-rendered loops of the procedural engine/ambience (baked in the editor by OWR > Bake Audio Bank).
    /// Used where the audio-thread synth can't run or is too heavy: WebGL (no OnAudioFilterRead) and mobile.
    /// Lives in Resources so it loads without any scene wiring.
    /// </summary>
    public class AudioBank : ScriptableObject
    {
        public float[] engineRpm;          // rpm each band was rendered at
        public AudioClip[] engineOn;       // full throttle
        public AudioClip[] engineOff;      // closed throttle / overrun
        public AudioClip[] pops;
        public AudioClip rain, wind, tyres, kerb, crowd;

        static AudioBank cached; static bool tried;
        public static AudioBank Load()
        {
            if (!tried) { tried = true; cached = Resources.Load<AudioBank>("AudioBank"); }
            return cached;
        }

        /// <summary>True when the sampled path should replace the audio-thread synth.</summary>
        public static bool UseSampled
        {
            get
            {
#if UNITY_WEBGL
                return true;
#else
                return ForceSampled || Application.isMobilePlatform;
#endif
            }
        }
        /// <summary>Editor/testing switch to hear the sampled path on desktop.</summary>
        public static bool ForceSampled;
    }
}
