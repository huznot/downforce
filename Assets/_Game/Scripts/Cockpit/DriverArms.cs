using OWR.Vehicle;
using UnityEngine;

namespace OWR.Cockpit
{
    /// <summary>
    /// Two-bone IK for the driver's arms: shoulders are fixed in the car, wrists follow the hand anchors
    /// on the steering wheel grips (so the hands turn with the wheel). Elbows bend outward/down via a pole.
    /// Paddle shifts flick the index finger and nudge the hand. Runs after the wheel rotates, before links draw.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class DriverArms : MonoBehaviour
    {
        [System.Serializable]
        public class Arm
        {
            public Transform shoulder;   // fixed in car
            public Transform wrist;      // anchor on the steering wheel grip
            public Transform elbow;      // driven by IK (links stretch between these)
            public Transform hand;       // glove root (child of wheel); nudged on flicks
            public Transform indexFinger;// pivot rotated on paddle pull
            public Transform paddle;     // shift paddle behind the wheel (pivots at its top edge)
            public HandGrip grip;        // posed hand: index finger pulls the paddle
            [HideInInspector] public Quaternion paddleRest;
            public float upperLength = 0.30f;
            public float foreLength = 0.29f;
            public Vector3 poleLocal = new Vector3(1f, -0.6f, -0.4f); // car-local direction elbows point to (x mirrored per side)
            [HideInInspector] public float flick;
            [HideInInspector] public Vector3 handRestPos;
            [HideInInspector] public Quaternion fingerRest;
        }

        public CarController car;
        public Arm left = new Arm();
        public Arm right = new Arm();
        public float flickDuration = 0.16f;
        public float fingerPullDeg = 28f;

        void Start()
        {
            Init(left);
            Init(right);
            if (car && car.Drivetrain != null) car.Drivetrain.OnShift += OnShift;
        }

        void OnDestroy()
        {
            if (car && car.Drivetrain != null) car.Drivetrain.OnShift -= OnShift;
        }

        static void Init(Arm a)
        {
            if (a.hand) a.handRestPos = a.hand.localPosition;
            if (a.indexFinger) a.fingerRest = a.indexFinger.localRotation;
            if (a.paddle) a.paddleRest = a.paddle.localRotation;
        }

        void OnShift(int gear, bool up)
        {
            (up ? right : left).flick = 1f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Solve(left, -1f, dt);
            Solve(right, 1f, dt);
        }

        void Solve(Arm a, float side, float dt)
        {
            // ---- paddle flick: finger pulls, hand pulls slightly toward the driver ----
            if (a.flick > 0f)
            {
                a.flick = Mathf.Max(0f, a.flick - dt / flickDuration);
                float k = Mathf.Sin(a.flick * Mathf.PI);
                if (a.indexFinger) a.indexFinger.localRotation = a.fingerRest * Quaternion.Euler(0f, -fingerPullDeg * k, 0f);
                if (a.hand && a.hand.localRotation == Quaternion.identity) a.hand.localPosition = a.handRestPos + new Vector3(0f, 0f, -0.006f * k);
                if (a.paddle) a.paddle.localRotation = a.paddleRest * Quaternion.Euler(-9f * k, 0f, 0f);
                if (a.grip) a.grip.indexPull = k;
            }

            // ---- two-bone IK (only when arms are present) ----
            if (!a.shoulder || !a.wrist || !a.elbow) return;
            Vector3 s = a.shoulder.position, w = a.wrist.position;
            Vector3 sw = w - s;
            float d = Mathf.Clamp(sw.magnitude, 0.05f, a.upperLength + a.foreLength - 0.001f);
            Vector3 dir = sw.normalized;
            float x = (a.upperLength * a.upperLength - a.foreLength * a.foreLength + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, a.upperLength * a.upperLength - x * x));
            Vector3 pole = transform.TransformDirection(new Vector3(a.poleLocal.x * side, a.poleLocal.y, a.poleLocal.z));
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir).normalized;
            a.elbow.position = s + dir * x + bend * h;
        }
    }
}
