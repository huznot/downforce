using System.Collections.Generic;
using UnityEngine;

namespace OWR.Race
{
    /// <summary>Plays commentary lines for race events (voice clip + subtitle), with a queue so lines never overlap.</summary>
    public class Commentary : MonoBehaviour
    {
        [System.Serializable]
        public class Line { public string key; public AudioClip clip; [TextArea] public string text; }

        public Line[] lines;
        public AudioSource source;
        /// <summary>Driver team-radio channel: plays straight away, on top of the commentator.</summary>
        public AudioSource radioSource;
        public float minGap = 0.8f;

        /// <summary>Current subtitle (empty when silent). Read by the HUD.</summary>
        public string Subtitle { get; private set; } = "";

        readonly Queue<Line> queue = new Queue<Line>();
        float busyUntil;
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

        void Start()
        {
            if (RaceManager.Instance) RaceManager.Instance.OnEvent += OnRaceEvent;
        }

        void OnDestroy()
        {
            AudioFX.EngineAudio.Duck = 1f;
            if (RaceManager.Instance) RaceManager.Instance.OnEvent -= OnRaceEvent;
        }

        // only the big moments get a voice line (keeps the onboard sound focused on your car)
        static readonly string[] Important = { "intro", "go", "lead", "fastest", "finallap", "win", "podium", "finish", "crash", "aicrash", "penalty", "penalty_you", "practice", "pb", "yes" };
        static readonly string[] Roar = { "go", "lead", "win", "aicrash", "finallap", "yes" };

        void OnRaceEvent(string ev)
        {
            if (System.Array.IndexOf(Important, ev) < 0)
            {
                // minor events (gain / lose / spin / contact) only occasionally
                if (lastPlayed.TryGetValue("_minor", out float tm) && Time.unscaledTime - tm < 45f) return;
                lastPlayed["_minor"] = Time.unscaledTime;
            }
            // every event has 1..3 recorded variants ("go_1", "go_2" ...): pick one at random
            var options = new List<Line>();
            foreach (var l in lines) if (BaseKey(l.key) == ev) options.Add(l);
            if (options.Count == 0) return;
            var pick = options[Random.Range(0, options.Count)];
            if (System.Array.IndexOf(Roar, ev) >= 0) Track.CrowdHype.Boost(1f, 4f);
            if (ev == "yes")   // driver radio: its own channel, overlaps the commentator
            {
                if (!radioSource) { radioSource = gameObject.AddComponent<AudioSource>(); radioSource.playOnAwake = false; radioSource.spatialBlend = 0f; radioSource.volume = 1f; }
                if (pick.clip) radioSource.PlayOneShot(pick.clip);
                return;
            }
            foreach (var l in new[] { pick })
                {
                    // don't repeat the same line too often
                    if (lastPlayed.TryGetValue(ev, out float t) && Time.unscaledTime - t < 20f && ev != "go") return;
                    lastPlayed[ev] = Time.unscaledTime;
                    if (ev == "go" || ev == "win" || ev == "podium" || ev == "finish") queue.Clear(); // big moments interrupt
                    queue.Enqueue(l);
                    return;
                }
        }

        static string BaseKey(string key)
        {
            int u = key.LastIndexOf('_');
            return u > 0 && int.TryParse(key.Substring(u + 1), out _) ? key.Substring(0, u) : key;
        }

        /// <summary>Play an event line directly (e.g. the driver's radio from the celebration).</summary>
        public void Say(string ev) => OnRaceEvent(ev);

        void Update()
        {
            float duckTarget = Time.unscaledTime < busyUntil - minGap ? 0.42f : 1f;
            AudioFX.EngineAudio.Duck = Mathf.MoveTowards(AudioFX.EngineAudio.Duck, duckTarget, Time.unscaledDeltaTime * 3f);
            if (Time.unscaledTime < busyUntil) return;
            if (queue.Count == 0) { Subtitle = ""; return; }
            var l = queue.Dequeue();
            if (source && l.clip) source.PlayOneShot(l.clip);
            Subtitle = l.text;
            busyUntil = Time.unscaledTime + (l.clip ? l.clip.length : 3f) + minGap;
        }
    }
}
