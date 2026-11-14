using UnityEngine;

namespace OWR.Cockpit
{
    /// <summary>
    /// Poses a rigged hand (WebXR joint naming) into a natural wheel grip: each finger curls at the knuckle (MCP),
    /// middle (PIP) and tip (DIP) joints around the grip; the thumb rests on the wheel face.
    /// Live inputs: indexPull (paddle shift) and thumbReach (pressing a button) blend the pose.
    /// Runs after DriverArms/WheelControls have placed the hand.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [ExecuteAlways]
    public class HandGrip : MonoBehaviour
    {
        [Header("Grip (degrees)")]
        public float mcp = 58f;
        public float pip = 72f;
        public float dip = 40f;
        [Tooltip("Extra curl per finger from index to pinky (pinky wraps tighter).")]
        public float fingerSpreadCurl = 6f;
        public Vector3 thumbMeta = new Vector3(-18f, 0f, -12f);
        public float thumbProx = 18f;
        public float thumbDist = 14f;

        [Header("Contact solver (wraps every finger tight around the grip)")]
        [Tooltip("Transform at the grip centre whose local Y is the grip axis. When set, curls are solved per joint.")]
        public Transform gripAxis;
        [Tooltip("Grip surface radius + half finger thickness (m).")]
        public float contactRadius = 0.044f;
        public float maxCurl = 115f;

        [Tooltip("Where the thumb tip should rest (e.g. on the wheel face next to the grip). Optional.")]
        public Transform thumbTarget;

        [Header("Live blend (0..1)")]
        [Range(0f, 1f)] public float indexPull;
        [Range(0f, 1f)] public float thumbReach;
        [Tooltip("0 = holding the wheel, 1 = clenched fist (celebration).")]
        [Range(0f, 1f)] public float fist;

        static readonly string[] Fingers = { "index-finger", "middle-finger", "ring-finger", "pinky-finger" };
        Transform[,] f = new Transform[4, 3];
        Quaternion[,] fr = new Quaternion[4, 3];
        Transform t0, t1, t2, tTip;
        Transform[] tips = new Transform[4];
        Quaternion r0, r1, r2;
        bool ready;

        void OnEnable() => Bind();

        void Bind()
        {
            ready = false;
            var all = GetComponentsInChildren<Transform>(true);
            Transform Find(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
            for (int i = 0; i < 4; i++)
            {
                f[i, 0] = Find(Fingers[i] + "-phalanx-proximal");
                f[i, 1] = Find(Fingers[i] + "-phalanx-intermediate");
                f[i, 2] = Find(Fingers[i] + "-phalanx-distal");
                tips[i] = Find(Fingers[i] + "-tip");
                for (int j = 0; j < 3; j++) if (!f[i, j]) return;
                if (!tips[i]) return;
            }
            t0 = Find("thumb-metacarpal"); t1 = Find("thumb-phalanx-proximal"); t2 = Find("thumb-phalanx-distal"); tTip = Find("thumb-tip");
            if (!t0 || !t1 || !t2 || !tTip) return;
            // rest = bind pose (captured once; the asset's bind pose is an open flat hand)
            for (int i = 0; i < 4; i++) for (int j = 0; j < 3; j++) fr[i, j] = f[i, j].localRotation;
            r0 = t0.localRotation; r1 = t1.localRotation; r2 = t2.localRotation;
            ready = true;
        }

        void LateUpdate()
        {
            if (!ready) return;
            if (gripAxis) SolveContact();
            else for (int i = 0; i < 4; i++)
            {
                float extra = i * fingerSpreadCurl;
                float a = mcp + extra, b = pip + extra * 0.5f, c = dip;
                if (i == 0)
                {
                    // paddle pull: index straightens a little at the knuckle then hooks harder at the tip joints
                    a -= 22f * indexPull; b += 18f * indexPull; c += 12f * indexPull;
                }
                // curl toward the palm = negative rotation about the joint's local X
                f[i, 0].localRotation = fr[i, 0] * Quaternion.Euler(-a, 0f, 0f);
                f[i, 1].localRotation = fr[i, 1] * Quaternion.Euler(-b, 0f, 0f);
                f[i, 2].localRotation = fr[i, 2] * Quaternion.Euler(-c, 0f, 0f);
            }
            ApplyThumb();
            if (fist > 0.001f) ApplyFist();
        }

        float AxisDist(Vector3 p)
        {
            Vector3 c = gripAxis.position, a = gripAxis.up;
            Vector3 d = p - c;
            return (d - Vector3.Dot(d, a) * a).magnitude;
        }

        /// <summary>
        /// For each joint in turn (knuckle -> tip), pick the smallest curl that brings the next joint onto the grip
        /// surface, so every segment wraps tight. Index finger optionally hooks harder for the paddle.
        /// </summary>
        void SolveContact()
        {
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Transform joint = f[i, j];
                    Transform next = j < 2 ? f[i, j + 1] : tips[i];
                    float target = j < 2 ? contactRadius : contactRadius - 0.004f;
                    float best = 0f, bestErr = float.MaxValue, chosen = -1f;
                    for (float a = 0f; a <= maxCurl; a += 2.5f)
                    {
                        joint.localRotation = fr[i, j] * Quaternion.Euler(-a, 0f, 0f);
                        float d = AxisDist(next.position);
                        float err = Mathf.Abs(d - target);
                        if (err < bestErr) { bestErr = err; best = a; }
                        if (d <= target) { chosen = a; break; }   // first contact = wrapped onto the surface
                    }
                    float ang = chosen >= 0f ? chosen : best;
                    if (i == 0 && j > 0) ang += 14f * indexPull;           // paddle pull hooks the index harder
                    if (i == 0 && j == 0) ang -= 18f * indexPull;
                    joint.localRotation = fr[i, j] * Quaternion.Euler(-ang, 0f, 0f);
                }
            }
        }

        /// <summary>Clench every finger fully and wrap the thumb across the fingers.</summary>
        void ApplyFist()
        {
            float[] a = { 88f, 105f, 72f };
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 3; j++)
                    f[i, j].localRotation = Quaternion.Slerp(f[i, j].localRotation, fr[i, j] * Quaternion.Euler(-a[j], 0f, 0f), fist);
            t0.localRotation = Quaternion.Slerp(t0.localRotation, r0 * Quaternion.Euler(-25f, 0f, -30f), fist);
            t1.localRotation = Quaternion.Slerp(t1.localRotation, r1 * Quaternion.Euler(-45f, 0f, 0f), fist);
            t2.localRotation = Quaternion.Slerp(t2.localRotation, r2 * Quaternion.Euler(-50f, 0f, 0f), fist);
        }

        void ApplyThumb()
        {
            // thumb rests on the wheel face; reaching for a button straightens and pushes it forward
            Vector3 tm = Vector3.Lerp(thumbMeta, thumbMeta + new Vector3(10f, 0f, 14f), thumbReach);
            t0.localRotation = r0 * Quaternion.Euler(tm);
            t1.localRotation = r1 * Quaternion.Euler(-Mathf.Lerp(thumbProx, 4f, thumbReach), 0f, 0f);
            t2.localRotation = r2 * Quaternion.Euler(-Mathf.Lerp(thumbDist, 2f, thumbReach), 0f, 0f);

            // aim the whole thumb so its tip lands on the target (two passes converge with the curl applied)
            if (thumbTarget)
                for (int pass = 0; pass < 2; pass++)
                {
                    Vector3 from = tTip.position - t0.position, to = thumbTarget.position - t0.position;
                    if (from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f)
                        t0.rotation = Quaternion.FromToRotation(from, to) * t0.rotation;
                }
        }
    }
}
