using OWR.Vehicle;
using UnityEngine;

namespace OWR.Cockpit
{
    /// <summary>
    /// Steering-wheel controls with physical feedback: the nearest hand leaves the grip, the thumb reaches the
    /// button/rotary, presses or turns it, and returns. Each control also changes the car (brake bias, TC, ERS mode,
    /// overtake boost, pit limiter, radio).
    /// Pad: D-pad L/R brake bias, D-pad Down TC, D-pad Up ERS mode, B overtake, X pit limiter, R3 radio.
    /// Keys: [ ] brake bias, T TC, M ERS mode, O overtake, P pit limiter, V radio.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class WheelControls : MonoBehaviour
    {
        public enum Kind { Press, Rotary }

        [System.Serializable]
        public class Control
        {
            public string id;
            public Transform target;   // button or rotary on the wheel face (child of the wheel)
            public Kind kind;
            public bool rightHand;
            [HideInInspector] public Vector3 restLocal;
            [HideInInspector] public Quaternion restRot;
            [HideInInspector] public float angle;
        }

        public CarController car;
        public DriverArms arms;
        public Control[] controls;
        public float reachTime = 0.17f, holdTime = 0.09f, returnTime = 0.2f;
        [Tooltip("Thumb tip position relative to the hand origin (right hand, wheel space).")]
        public Vector3 thumbTip = new Vector3(-0.006f, 0.032f, -0.021f);

        // runtime state for each hand
        class HandAnim { public Control c; public float t; public int dir; public bool active; public Vector3 rest; }
        readonly HandAnim left = new HandAnim(), right = new HandAnim();

        public int ErsMode { get; private set; } = 2;      // 0 harvest, 1 balanced, 2 race, 3 hotlap
        public static readonly string[] ErsNames = { "HARV", "BAL", "RACE", "HOT" };
        public float OvertakeTimer { get; private set; }
        public bool PitLimiter { get; private set; }
        public float RadioTimer { get; private set; }

        void Start()
        {
            foreach (var c in controls)
            {
                if (!c.target) continue;
                c.restLocal = c.target.localPosition;
                c.restRot = c.target.localRotation;
            }
            if (arms)
            {
                if (arms.left.hand) left.rest = arms.left.hand.localPosition;
                if (arms.right.hand) right.rest = arms.right.hand.localPosition;
            }
            ApplyErs();
        }

        Control Find(string id)
        {
            foreach (var c in controls) if (c.id == id) return c;
            return null;
        }

        void Update()
        {
            var inp = car ? car.input : null;
            if (inp == null) return;
            if (inp.Consume(ref inp.bbUp)) Trigger("BB", +1);
            if (inp.Consume(ref inp.bbDown)) Trigger("BB", -1);
            if (inp.Consume(ref inp.tc)) Trigger("TC", +1);
            if (inp.Consume(ref inp.ers)) Trigger("ERS", +1);
            if (inp.Consume(ref inp.overtake)) Trigger("OVT", 0);
            if (inp.Consume(ref inp.pit)) Trigger("PIT", 0);
            if (inp.Consume(ref inp.radio)) Trigger("RAD", 0);

            if (OvertakeTimer > 0f) { OvertakeTimer -= Time.deltaTime; if (OvertakeTimer <= 0f) ApplyErs(); }
            if (RadioTimer > 0f) RadioTimer -= Time.deltaTime;
            car.pitLimiter = PitLimiter;
        }

        void Trigger(string id, int dir)
        {
            var c = Find(id);
            if (c == null) return;
            var h = c.rightHand ? right : left;
            if (h.active) return;
            h.c = c; h.t = 0f; h.dir = dir; h.active = true;
        }

        /// <summary>Called at the moment the thumb makes contact.</summary>
        void Activate(Control c, int dir)
        {
            var s = car.setup;
            switch (c.id)
            {
                case "BB": s.brakeBias = Mathf.Clamp(s.brakeBias + dir * 0.005f, 0.50f, 0.65f); break;
                case "TC": s.tractionControl = !s.tractionControl; break;
                case "ERS": ErsMode = (ErsMode + 1) % 4; ApplyErs(); break;
                case "OVT": OvertakeTimer = 4f; car.Drivetrain.ErsScale = 1.5f; break;
                case "PIT": PitLimiter = !PitLimiter; break;
                case "RAD": RadioTimer = 2.5f; break;
            }
        }

        void ApplyErs()
        {
            if (!car || car.Drivetrain == null) return;
            car.Drivetrain.ErsScale = new[] { 0f, 0.6f, 1f, 1.25f }[ErsMode];
        }

        void LateUpdate()
        {
            Animate(left, arms ? arms.left.hand : null, -1f);
            Animate(right, arms ? arms.right.hand : null, 1f);
        }

        void Animate(HandAnim h, Transform hand, float side)
        {
            if (!h.active || !hand || h.c == null || !h.c.target) return;
            var c = h.c;
            float total = reachTime + holdTime + returnTime;
            float prevT = h.t;
            h.t += Time.deltaTime;

            // where the hand must be so the thumb tip sits on the control
            Vector3 tip = new Vector3(thumbTip.x * side, thumbTip.y, thumbTip.z);
            Vector3 goal = c.restLocal - tip;
            goal.z = h.rest.z - 0.006f;

            float reach;
            if (h.t < reachTime) reach = Smooth(h.t / reachTime);
            else if (h.t < reachTime + holdTime) reach = 1f;
            else reach = 1f - Smooth((h.t - reachTime - holdTime) / returnTime);

            float press = (h.t >= reachTime && h.t < reachTime + holdTime) ? Mathf.Sin((h.t - reachTime) / holdTime * Mathf.PI) : 0f;
            hand.localPosition = Vector3.Lerp(h.rest, goal, reach) + new Vector3(0f, 0f, 0.007f * press);
            hand.localRotation = Quaternion.Euler(0f, 0f, -side * 14f * reach);
            var armRef = side < 0 ? arms.left : arms.right;
            if (armRef.grip) armRef.grip.thumbReach = reach;

            if (prevT < reachTime && h.t >= reachTime) Activate(c, h.dir);

            if (c.kind == Kind.Press)
                c.target.localPosition = c.restLocal + new Vector3(0f, 0f, 0.004f * press);
            else
            {
                if (prevT < reachTime && h.t >= reachTime) c.angle += (h.dir < 0 ? -30f : 30f);
                Quaternion goalRot = c.restRot * Quaternion.Euler(0f, 0f, -c.angle);
                c.target.localRotation = Quaternion.Slerp(c.target.localRotation, goalRot, 1f - Mathf.Exp(-Time.deltaTime * 30f));
            }

            if (h.t >= total)
            {
                h.active = false;
                hand.localPosition = h.rest;
                hand.localRotation = Quaternion.identity;
            }
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
