using System.Collections;
using System.Collections.Generic;
using OWR.Vehicle;
using UnityEngine;

namespace OWR.Race
{
    /// <summary>
    /// Runs the race: grid hold, five-light start, lap counting off the track path, live order and gaps,
    /// lap/fastest-lap timing, finish and results. Raises events for the HUD, commentary and crowd.
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        public enum RaceState { Grid, Lights, Racing, Finished, Crashed }

        [System.Serializable]
        public class Entry
        {
            public CarController car;
            public string driver;
            public string code;
            public string team;
            public Color teamColor = Color.white;
            public bool isPlayer;
            public AIDriver ai;

            [System.NonSerialized] public int hint = -1;
            [System.NonSerialized] public float d, prevD;
            [System.NonSerialized] public int lap;            // 0 = on the grid, 1 = first lap ...
            [System.NonSerialized] public float progress;
            [System.NonSerialized] public bool finished;
            [System.NonSerialized] public float finishTime;
            [System.NonSerialized] public float lapStart, lastLap, bestLap = float.MaxValue;
            [System.NonSerialized] public int pos;
            [System.NonSerialized] public float gapToLeader;
            [System.NonSerialized] public bool dnf;
            [System.NonSerialized] public float penalty;          // seconds added to the race time
            [System.NonSerialized] public float lastPenaltyAt = -99f;
            public float FinalTime => finishTime + penalty;
            [System.NonSerialized] public float mistakeAt = -1f;
            [System.NonSerialized] public int mistakeLap = -1;
            [System.NonSerialized] public float baseSkill = 1f;
            [System.NonSerialized] public int trackLimits;
            [System.NonSerialized] public float offTime;
            [System.NonSerialized] public bool offCounted;
        }

        public static RaceManager Instance { get; private set; }
        public static readonly List<CarController> AllCars = new List<CarController>();

        public TrackPath path;
        public int totalLaps = 8;
        public StartLights lights;
        public List<Entry> entries = new List<Entry>();
        /// <summary>Held true while the main menu is up; the start sequence waits for it.</summary>
        [System.NonSerialized] public bool waitForStart;

        public RaceState State { get; private set; } = RaceState.Grid;
        public float RaceTime { get; private set; }
        public float FastestLap { get; private set; } = float.MaxValue;
        public string FastestLapCode { get; private set; } = "";
        public Entry Player { get; private set; }
        public List<Entry> Order { get; } = new List<Entry>();
        public bool Paused { get; private set; }
        public string LastAccidentCode { get; private set; } = "";
        public string LastPenaltyCode { get; private set; } = "";
        public bool Practice => GameOptions.Practice;
        /// <summary>Preview mode: skip the race and loop the win celebration.</summary>
        public bool CelebrationPreviewMode { get; private set; }
        float resetHeld;
        public float OffTrackTime { get; private set; }

        /// <summary>Race events: "intro", "lights", "go", "gain", "lose", "lead", "fastest", "halfway", "finallap", "spin", "contact", "win", "podium", "finish".</summary>
        public System.Action<string> OnEvent;
        public System.Action<int> OnLightsChanged; // 0..5 lit, -1 = out

        int lastPlayerPos;
        int accidents;
        float lastAccidentTime = -999f;
        public int maxAiAccidents = 2;
        float spinCooldown, contactCooldown;
        bool halfwayDone, finalLapDone;

        void Awake()
        {
            Instance = this;
            AllCars.Clear();
            foreach (var e in entries)
            {
                AllCars.Add(e.car);
                if (e.isPlayer) Player = e;
            }
            // showcase mode: the AI drives our car (with the cockpit view, hands and wheel all live)
            if (GameOptions.AutoDrive && Player != null && Player.ai == null)
            {
                var ai = Player.car.gameObject.AddComponent<AIDriver>();
                ai.car = Player.car; ai.path = path; ai.skill = 1.0f; ai.drivesPlayer = true;
                Player.ai = ai;
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; Time.timeScale = 1f; AudioListener.pause = false; }

        IEnumerator Start()
        {
            foreach (var e in entries)
            {
                var entry = e;
                e.car.OnCarContact += OnContact;
                e.car.resetProvider = c => ResetPose(entry);
                e.car.parkingBrake = true;
                e.car.Drivetrain.ForceNeutral = true;
            }
            Order.AddRange(entries);
            UpdateOrder();
            lastPlayerPos = Player != null ? Player.pos : 0;

            while (waitForStart) yield return null;
            if (GameOptions.CelebrationPreview)
            {
                GameOptions.CelebrationPreview = false;
                CelebrationPreviewMode = true;
                yield return new WaitForSeconds(1.2f);
                Raise("win");
                var cel = Player != null ? Player.car.GetComponent<Cockpit.Celebration>() : null;
                if (cel) cel.Begin();
                yield break;
            }
            ApplyDifficulty();
            Raise("begin");
            if (Practice)
            {
                yield return new WaitForSeconds(2.5f);
                State = RaceState.Racing;
                RaceTime = 0f;
                foreach (var e in entries) { e.car.parkingBrake = false; e.car.Drivetrain.ForceNeutral = false; e.lapStart = 0f; }
                Raise("practice");
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            Raise("intro");
            yield return new WaitForSeconds(5.5f);
            State = RaceState.Lights;
            Raise("lights");
            for (int i = 1; i <= 5; i++)
            {
                if (lights) lights.SetLit(i);
                OnLightsChanged?.Invoke(i);
                yield return new WaitForSeconds(1f);
            }
            yield return new WaitForSeconds(Random.Range(0.4f, 2.2f));
            if (lights) lights.SetLit(0);
            OnLightsChanged?.Invoke(-1);

            State = RaceState.Racing;
            RaceTime = 0f;
            foreach (var e in entries)
            {
                e.car.parkingBrake = false;
                e.car.Drivetrain.ForceNeutral = false;
                e.lapStart = 0f;
                if (e.ai) e.ai.enabledByRace = true;
            }
            Track.CrowdHype.Boost(1f, 6f);
            Raise("go");
        }

        (Vector3, Quaternion) ResetPose(Entry e)
        {
            float d = path.Project(e.car.transform.position, ref e.hint, out _);
            Vector3 p = path.PointAt(d, 0f);
            return (p + Vector3.up * 0.1f, Quaternion.LookRotation(path.TangentAt(d), Vector3.up));
        }

        void Update()
        {
            if (State == RaceState.Racing || State == RaceState.Finished || State == RaceState.Crashed) RaceTime += Time.deltaTime;
            float L = path.Length;

            foreach (var e in entries)
            {
                e.prevD = e.d;
                e.d = path.Project(e.car.transform.position, ref e.hint, out _);
                if (State == RaceState.Grid || State == RaceState.Lights) { e.prevD = e.d; e.progress = e.d - L; continue; }

                if (e.dnf) { e.progress = -1e6f + e.progress * 0f + (e.lap - 1) * L + e.d; continue; }
                CheckAccident(e);
                if (e.dnf) continue;
                PlanMistake(e, L);

                bool crossedForward = e.prevD > L * 0.75f && e.d < L * 0.25f;
                bool crossedBack = e.prevD < L * 0.25f && e.d > L * 0.75f;
                if (crossedBack && !e.finished) e.lap--;
                if (crossedForward && !e.finished)
                {
                    e.lap++;
                    if (e.lap >= 2)
                    {
                        e.lastLap = RaceTime - e.lapStart;
                        if (e.lastLap < e.bestLap) { e.bestLap = e.lastLap; if (e.isPlayer && Practice && e.lap > 2) Raise("pb"); }
                        if (e.lastLap < FastestLap)
                        {
                            FastestLap = e.lastLap; FastestLapCode = e.code;
                            if (e.isPlayer && e.lap > 2) Raise("fastest");
                        }
                    }
                    e.lapStart = RaceTime;
                    if (e.lap > totalLaps && !Practice)
                    {
                        e.finished = true;
                        e.finishTime = RaceTime;
                        if (e.isPlayer) PlayerFinished();
                    }
                    if (!Practice && e.isPlayer && e.lap == totalLaps && !finalLapDone) { finalLapDone = true; Raise("finallap"); Track.CrowdHype.Boost(0.8f, 4f); }
                    if (!Practice && e.isPlayer && e.lap == totalLaps / 2 + 1 && !halfwayDone) { halfwayDone = true; Raise("halfway"); }
                }
                e.progress = e.finished ? 1e7f - e.FinalTime : (e.lap - 1) * L + e.d;
            }

            PracticeHelpers(L);
            UpdateOrder();
            PlayerEvents();
            ShowcasePacing();
            RivalPacing();
            TrackLimits();
        }

        void UpdateOrder()
        {
            Order.Sort((a, b) => b.progress.CompareTo(a.progress));
            float leaderSpeed = Mathf.Max(30f, RaceTime > 5f && Order[0].lap > 0 ? ((Order[0].lap - 1) * path.Length + Order[0].d) / RaceTime : 40f);
            for (int i = 0; i < Order.Count; i++)
            {
                Order[i].pos = i + 1;
                if (Order[i].finished && Order[0].finished) Order[i].gapToLeader = Order[i].FinalTime - Order[0].FinalTime;
                else if (Order[0].finished && !Order[i].dnf)
                {
                    // leader is home: gap = time since the leader finished + estimated time to cover what's left
                    float leaderAvg = totalLaps * path.Length / Mathf.Max(1f, Order[0].finishTime);
                    float left = Mathf.Max(0f, totalLaps * path.Length - ((Order[i].lap - 1) * path.Length + Order[i].d));
                    Order[i].gapToLeader = (RaceTime - Order[0].finishTime) + left / Mathf.Max(20f, leaderAvg);
                }
                else Order[i].gapToLeader = i == 0 ? 0f : Mathf.Max(0f, (Order[0].progress - Order[i].progress) / leaderSpeed);
            }
        }

        // ================================================================= accidents
        void CheckAccident(Entry e)
        {
            if (e.finished || Practice) return;   // practice: no wrecks, just press R
            if (e.isPlayer && GameOptions.AutoDrive) return; // keep the showcase run going to the flag
            var c = e.car;
            float speed = c.Body.linearVelocity.magnitude;
            bool easy = e.isPlayer;   // same crash tolerance for the player on every difficulty
            bool bigHit = Time.time - c.lastImpactTime < 0.1f && c.lastImpact > (easy ? 32f : 22f);
            float yaw = Mathf.Abs(Vector3.Dot(c.Body.angularVelocity, c.transform.up));
            bool backwards = Vector3.Dot(c.transform.forward, path.TangentAt(e.d)) < -0.35f;
            bool spunOut = yaw > (easy ? 3.4f : 2.6f) && speed > (easy ? 14f : 9f) && backwards;
            bool flipped = Vector3.Dot(c.transform.up, Vector3.up) < 0.2f;
            if (!(bigHit || spunOut || flipped)) return;

            // opening laps are for racing, not wreckage: rivals that get it wrong early (or beyond the
            // per-race budget) are simply put back on the track
            if (!e.isPlayer && (RaceTime < 25f || accidents >= maxAiAccidents || RaceTime - lastAccidentTime < 40f))
            {
                c.ResetCar();
                return;
            }
            if (e.isPlayer && RaceTime < 6f && !flipped) return; // launch-phase bumps never end your race
            if (!e.isPlayer) { accidents++; lastAccidentTime = RaceTime; }

            e.dnf = true;
            if (ExplosionFX.Instance) ExplosionFX.Instance.Explode(c);
            if (e.ai) e.ai.enabledByRace = false;
            Track.CrowdHype.Boost(1f, 5f);
            LastAccidentCode = e.code;
            if (e.isPlayer) StartCoroutine(PlayerCrashed());
            else Raise("aicrash");
        }

        /// <summary>Now and then a rival gets it wrong: a snap of oversteer mid-lap (limited per race).</summary>
        void PlanMistake(Entry e, float L)
        {
            if (e.ai == null || e.ai.drivesPlayer || State != RaceState.Racing || accidents >= maxAiAccidents || RaceTime < 40f) return;
            if (e.mistakeLap != e.lap && e.lap >= 2)
            {
                e.mistakeLap = e.lap;
                e.mistakeAt = Random.value < GameOptions.MistakeChance ? Random.Range(150f, L - 150f) : -1f;
            }
            if (e.mistakeAt > 0f && e.d > e.mistakeAt && e.d < e.mistakeAt + 30f)
            {
                e.mistakeAt = -1f;
                var rb = e.car.Body;
                float sign = Random.value < 0.5f ? -1f : 1f;
                rb.AddTorque(e.car.transform.up * sign * rb.mass * 5.5f, ForceMode.Impulse);
                e.ai.Panic(2.5f);
            }
        }

        /// <summary>
        /// Applies the menu choices when the race begins (the screen is black during the hand-over):
        /// field size (cars removed from the front so the player starts higher up), rival pace,
        /// accident budget, gearbox mode and Easy-mode driving help.
        /// </summary>
        void ApplyDifficulty()
        {
            maxAiAccidents = GameOptions.MaxRivalAccidents;
            totalLaps = Practice ? 999 : GameOptions.Laps;
            var byGrid = new List<Entry>(entries);
            foreach (var e in byGrid) e.d = path.Project(e.car.transform.position, ref e.hint, out _);
            byGrid.Sort((a, b) => b.d.CompareTo(a.d));
            var slots = new List<(Vector3 p, Quaternion r)>();
            foreach (var e in byGrid) slots.Add((e.car.transform.position, e.car.transform.rotation));

            int rivals = 0;
            foreach (var e in entries) if (!e.isPlayer) rivals++;
            int remove = Practice ? rivals : Mathf.Max(0, rivals - GameOptions.Rivals);
            foreach (var e in new List<Entry>(byGrid))
            {
                if (remove <= 0) break;
                if (e.isPlayer) continue;
                entries.Remove(e); Order.Remove(e); AllCars.Remove(e.car); byGrid.Remove(e);
                Destroy(e.car.gameObject);
                remove--;
            }
            // random grid every race (Fisher-Yates); the recorded showcase keeps its fixed P3 below
            if (!GameOptions.AutoDrive)
                for (int i = byGrid.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (byGrid[i], byGrid[j]) = (byGrid[j], byGrid[i]);
                }
            // close up the grid: cars take the front slots in (shuffled) order
            for (int i = 0; i < byGrid.Count; i++)
            {
                var c = byGrid[i].car;
                c.Body.position = slots[i].p; c.Body.rotation = slots[i].r;
                c.transform.SetPositionAndRotation(slots[i].p, slots[i].r);
                c.Body.linearVelocity = Vector3.zero; c.Body.angularVelocity = Vector3.zero;
                byGrid[i].hint = -1;
            }
            // showcase: start third so the fight is at the front
            if (GameOptions.AutoDrive && Player != null)
            {
                int pi = byGrid.IndexOf(Player);
                if (pi > 2)
                {
                    var other = byGrid[2].car; var me = Player.car;
                    Vector3 p0 = other.transform.position; Quaternion r0 = other.transform.rotation;
                    other.Body.position = me.transform.position; other.Body.rotation = me.transform.rotation;
                    other.transform.SetPositionAndRotation(me.transform.position, me.transform.rotation);
                    me.Body.position = p0; me.Body.rotation = r0;
                    me.transform.SetPositionAndRotation(p0, r0);
                }
            }
            foreach (var e in entries)
            {
                if (e.ai && !e.ai.drivesPlayer) { e.ai.skill *= GameOptions.RivalPace; e.baseSkill = e.ai.skill; }
                if (!e.isPlayer) continue;
                var s = e.car.setup;
                s.autoShift = GameOptions.AutoGears || GameOptions.AutoDrive || OWR.Input.TouchControls.Active;
                // the player's car and aids are the same on every difficulty; difficulty only changes the opponents
                s.stabilityAssist = true; s.tractionControl = true;
                s.tractionControlSlip = 0.09f; s.absAssist = true;
            }
            UpdateOrder();
            lastPlayerPos = Player != null ? Player.pos : 0;
        }

        IEnumerator PlayerCrashed()
        {
            State = RaceState.Crashed;
            Raise("crash");
            var dir = FindFirstObjectByType<CameraRig.CameraDirector>();
            if (dir) dir.SetCockpit(false);
            yield return null;
        }

        void PlayerEvents()
        {
            if (Player == null || State != RaceState.Racing) return;
            if (Player.pos != lastPlayerPos && RaceTime > 4f)
            {
                if (Player.pos < lastPlayerPos) Raise(Player.pos == 1 ? "lead" : (Random.value < 0.5f ? "gain" : "gain2"));
                else Raise("lose");
            }
            lastPlayerPos = Player.pos;

            var c = Player.car;
            spinCooldown -= Time.deltaTime; contactCooldown -= Time.deltaTime;
            if (spinCooldown <= 0f && Mathf.Abs(Vector3.Dot(c.Body.angularVelocity, c.transform.up)) > 2.2f && c.Body.linearVelocity.magnitude > 8f)
            { Raise("spin"); spinCooldown = 12f; }
            if (contactCooldown <= 0f && Time.time - c.lastImpactTime < 0.2f && c.lastImpact > 9f)
            { Raise("contact"); contactCooldown = 10f; }
        }

        /// <summary>
        /// Auto-drive showcase: keep the race tight. The auto-driver eases off when it builds a gap and pushes when it
        /// falls back, so the field stays together and it comes down to the final laps.
        /// </summary>
        void ShowcasePacing()
        {
            if (!GameOptions.AutoDrive || Player == null || Player.ai == null || State != RaceState.Racing) return;
            float gap;
            if (Player.pos == 1) gap = Order.Count > 1 ? -Order[1].gapToLeader : 0f; // negative = our lead
            else gap = Player.gapToLeader;
            bool lastLaps = Player.lap >= totalLaps - 1;
            float target = lastLaps ? 0.2f : 0.9f;             // run ~1 s behind the leader, close right up at the end
            float k = Mathf.Clamp((gap - target) * 0.025f, -0.06f, 0.06f);
            Player.ai.skill = Mathf.Clamp(0.97f + k, 0.9f, 1.04f);
        }

        // ================================================================= rival race craft
        /// <summary>
        /// Keeps the race alive around the player like real racecraft: rivals behind you push harder the more you
        /// pull away, rivals far up the road manage their pace so you can reel them in, anyone within a second of
        /// the car ahead attacks, and everyone pushes over the last two laps. The size of each effect scales with
        /// difficulty. Pace never goes beyond what the car can do through the corners.
        /// </summary>
        void RivalPacing()
        {
            if (GameOptions.AutoDrive || Practice || Player == null || State != RaceState.Racing) return;
            float push, ease;
            switch (GameOptions.Difficulty)
            {
                case Difficulty.Easy: push = 0.06f; ease = 0.07f; break;
                case Difficulty.Normal: push = 0.08f; ease = 0.04f; break;
                default: push = 0.10f; ease = 0.02f; break;
            }
            float avg = Mathf.Max(25f, Mathf.Abs(Player.car.telemetry.speedMs));
            bool lastLaps = Player.lap >= totalLaps - 1;
            for (int i = 0; i < Order.Count; i++)
            {
                var e = Order[i];
                if (e.isPlayer || e.ai == null || e.dnf || e.finished) continue;
                float gap = (Player.progress - e.progress) / avg;   // seconds; > 0 = rival behind the player
                float k = 0f;
                if (gap > 0.4f) k += Mathf.Clamp01((gap - 0.4f) / 6f) * push;          // chase the player down
                else if (gap < -2f) k -= Mathf.Clamp01((-gap - 2f) / 10f) * ease;      // don't vanish up the road
                if (i > 0)
                {
                    float toAhead = (Order[i - 1].progress - e.progress) / avg;
                    if (toAhead < 1.2f) k += 0.02f;                                     // attack mode (ERS, late braking)
                }
                if (lastLaps) k += 0.015f;
                float target = Mathf.Clamp(e.baseSkill * (1f + k), 0.8f, 1.1f);
                e.ai.skill = Mathf.MoveTowards(e.ai.skill, target, Time.deltaTime * 0.02f);
            }
        }

        // ================================================================= track limits
        /// <summary>
        /// Track limits like the real thing: going fully past the white line (all four wheels off, over 50 km/h) counts
        /// as a violation. Three warnings, then a 5 s time penalty on the 4th and every 3rd after that.
        /// </summary>
        void TrackLimits()
        {
            if (Practice || State != RaceState.Racing) return;
            float limit = path.roadWidth * 0.5f + 1.3f;   // car centre beyond the white line by more than half the car width
            foreach (var e in entries)
            {
                if (e.dnf || e.finished) continue;
                path.Project(e.car.transform.position, ref e.hint, out float lat);
                bool off = Mathf.Abs(lat) > limit && Mathf.Abs(e.car.telemetry.speedMs) > 14f;
                if (off) e.offTime += Time.deltaTime; else { e.offTime = 0f; e.offCounted = false; }
                if (e.offCounted || e.offTime < 0.35f) continue;
                e.offCounted = true;
                e.trackLimits++;
                if (e.trackLimits >= 4 && (e.trackLimits - 4) % 3 == 0)
                {
                    e.penalty += 5f; e.lastPenaltyAt = Time.time;
                    LastPenaltyCode = e.code; LastPenaltySeconds = 5f; LastPenaltyReason = "TRACK LIMITS";
                    Raise(e.isPlayer ? "penalty_you" : "penalty");
                }
                else if (e.isPlayer) Raise("tracklimits");
            }
        }
        public string LastPenaltyReason { get; private set; } = "CONTACT";

        // ================================================================= penalties
        /// <summary>
        /// Contact between two race cars: the car that runs into the back of another (closing fast) or swipes
        /// sideways into a car alongside is at fault and gets +5 s. Each car reports its own collisions, so a
        /// car is only ever penalised from its own point of view.
        /// </summary>
        void OnContact(CarController self, CarController other)
        {
            if (State != RaceState.Racing || Practice) return;
            var e = entries.Find(x => x.car == self);
            if (e == null || e.dnf || e.finished) return;
            Vector3 rel = self.transform.InverseTransformPoint(other.transform.position);
            Vector3 relV = self.Body.linearVelocity - other.Body.linearVelocity;
            bool rearEnd = rel.z > 1.2f && Mathf.Abs(rel.x) < 1.9f && Vector3.Dot(relV, self.transform.forward) > 3f;
            Vector3 toOther = (other.transform.position - self.transform.position).normalized;
            bool sideSwipe = Mathf.Abs(rel.z) < 3f && Mathf.Abs(rel.x) > 0.8f && Vector3.Dot(relV, toOther) > 3f && rel.z > -0.5f;
            if (!(rearEnd || sideSwipe)) return;
            // brake test: the car in front stamped on the brakes where the racing line needs no braking
            // (already well under the safe speed for the next ~60 m) -> the car in front is penalised instead
            if (rearEnd)
            {
                var victim = entries.Find(x => x.car == other);
                if (victim != null && !victim.dnf && !victim.finished && other.telemetry.brake > 0.4f)
                {
                    float need = float.MaxValue;
                    for (float a = 0f; a <= 60f; a += 10f) need = Mathf.Min(need, path.SpeedAt(victim.d + a));
                    if (Mathf.Abs(other.telemetry.speedMs) < need * 0.8f) e = victim;
                }
            }
            if (Time.time - e.lastPenaltyAt < 3f) return;
            float add = Time.time - e.lastPenaltyAt < 10f ? 10f : 5f;
            e.lastPenaltyAt = Time.time;
            e.penalty += add;
            LastPenaltyCode = e.code;
            LastPenaltySeconds = add;
            LastPenaltyReason = "CONTACT";
            Raise(e.isPlayer ? "penalty_you" : "penalty");
        }
        public float LastPenaltySeconds { get; private set; }

        // ================================================================= free practice
        void PracticeHelpers(float L)
        {
            if (!Practice || Player == null || State != RaceState.Racing) { OffTrackTime = 0f; return; }
            var c = Player.car;
            path.Project(c.transform.position, ref Player.hint, out float lat);
            OffTrackTime = Mathf.Abs(lat) > path.roadWidth * 0.5f + 2.5f ? OffTrackTime + Time.deltaTime : 0f;
            // hold reset (R / View) for a second: back to the start line for a fresh lap
            var kb = UnityEngine.InputSystem.Keyboard.current; var gp = UnityEngine.InputSystem.Gamepad.current;
            bool held = (kb != null && kb.rKey.isPressed) || (gp != null && gp.selectButton.isPressed);
            resetHeld = held ? resetHeld + Time.deltaTime : 0f;
            if (resetHeld > 1f)
            {
                resetHeld = -999f;
                Vector3 p = path.PointAt(L - 12f) + Vector3.up * 0.15f;
                Quaternion r = Quaternion.LookRotation(path.TangentAt(L - 12f), Vector3.up);
                c.Body.linearVelocity = Vector3.zero; c.Body.angularVelocity = Vector3.zero;
                c.Body.position = p; c.Body.rotation = r; c.transform.SetPositionAndRotation(p, r);
                Player.hint = -1; Player.d = Player.prevD = L - 12f;
                Raise("practice_restart");
            }
        }

        IEnumerator CelebrateWhenStopped()
        {
            float t = 0f;
            while (Player.car.Body.linearVelocity.magnitude > 2.5f && t < 8f) { t += Time.deltaTime; yield return null; }
            var cel = Player.car.GetComponent<Cockpit.Celebration>();
            if (cel) cel.Begin();
        }

        void PlayerFinished()
        {
            if (Player.ai != null) Player.ai.enabledByRace = false; // brake to a stop after the flag
            State = RaceState.Finished;
            Track.CrowdHype.Boost(1f, 15f);
            Raise(Player.pos == 1 ? "win" : Player.pos <= 3 ? "podium" : "finish");
            if (Player.pos == 1)
            {
                var cel = Player.car.GetComponent<Cockpit.Celebration>();
                if (cel && !GameOptions.AutoDrive) cel.BeginOneHand();
                StartCoroutine(CelebrateWhenStopped());
            }
            // cool-down: the player keeps steering and braking, but the throttle can only hold a gentle pace
            if (!GameOptions.AutoDrive) Player.car.input.speedCap = 22f;
        }

        void Raise(string ev) => OnEvent?.Invoke(ev);

        public void SetPaused(bool paused)
        {
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            if (Player != null && State != RaceState.Finished) Player.car.input.inputBlocked = paused;
        }

        public static string FormatTime(float t)
        {
            if (t >= float.MaxValue * 0.5f || t <= 0f) return "--:--.---";
            int m = (int)(t / 60f);
            return $"{m}:{t - m * 60f:00.000}";
        }
    }
}
