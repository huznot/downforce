using UnityEngine;

namespace OWR.Race
{
    /// <summary>
    /// Closed centreline of the circuit (index 0 = start/finish line). Gives distance-along-lap for any position,
    /// points/tangents by distance, and a physics-based target speed profile for the AI.
    /// </summary>
    public class TrackPath : MonoBehaviour
    {
        public Vector3[] points;
        public float roadWidth = 14f;

        [Header("AI speed profile")]
        public float gripMu = 1.35f;        // usable tyre friction in the wet (conservative)
        public float brakeDecel = 22f;      // m/s^2 the AI plans to brake with
        public float maxSpeed = 88f;

        [System.NonSerialized] public float[] dist;
        [System.NonSerialized] public float[] speed;
        public float Length { get; private set; }
        bool ready;

        void Awake() => Build();

        public void Build()
        {
            if (points == null || points.Length < 4) return;
            int n = points.Length;
            dist = new float[n];
            float d = 0f;
            for (int i = 0; i < n; i++)
            {
                dist[i] = d;
                d += Vector3.Distance(points[i], points[(i + 1) % n]);
            }
            Length = d;

            // curvature-limited speed with downforce: v^2 = mu g R / (1 - mu k R), k = 0.5 rho ClA / m
            speed = new float[n];
            const float k = 0.5f * 1.225f * 5f / 798f;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = points[(i - 4 + n) % n], b = points[i], c = points[(i + 4) % n];
                float R = Circumradius(a, b, c);
                float den = 1f - gripMu * k * R;
                float v = den > 0.05f ? Mathf.Sqrt(gripMu * 9.81f * R / den) : maxSpeed;
                speed[i] = Mathf.Min(v, maxSpeed);
            }
            // backward pass: brake in time for every corner (two sweeps handle the wrap-around)
            for (int sweep = 0; sweep < 2; sweep++)
                for (int i = n - 1; i >= 0; i--)
                {
                    int j = (i + 1) % n;
                    float ds = Vector3.Distance(points[i], points[j]);
                    speed[i] = Mathf.Min(speed[i], Mathf.Sqrt(speed[j] * speed[j] + 2f * brakeDecel * ds));
                }
            ready = true;
        }

        static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
        {
            a.y = b.y = c.y = 0f;
            float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c), ca = Vector3.Distance(c, a);
            float area2 = Vector3.Cross(b - a, c - a).magnitude;
            if (area2 < 1e-4f) return 10000f;
            return ab * bc * ca / (2f * area2);
        }

        /// <summary>Distance along the lap of the closest centreline point, searching near a hint index.</summary>
        public float Project(Vector3 p, ref int hint, out float lateral)
        {
            if (!ready) Build();
            int n = points.Length;
            int best = hint; float bestD = float.MaxValue;
            int window = hint < 0 ? n : 40;
            int start = hint < 0 ? 0 : hint - window;
            for (int k = 0; k < (hint < 0 ? n : window * 2); k++)
            {
                int i = ((start + k) % n + n) % n;
                float d = (points[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            hint = best;
            // refine onto the segment best -> next
            int j = (best + 1) % n;
            Vector3 seg = points[j] - points[best];
            float t = Mathf.Clamp01(Vector3.Dot(p - points[best], seg) / Mathf.Max(seg.sqrMagnitude, 1e-4f));
            Vector3 onLine = points[best] + seg * t;
            Vector3 right = Vector3.Cross(Vector3.up, seg.normalized);
            lateral = Vector3.Dot(p - onLine, right);
            return dist[best] + seg.magnitude * t;
        }

        public int IndexAt(float d)
        {
            d = Mathf.Repeat(d, Length);
            int lo = 0, hi = dist.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (dist[mid] <= d) lo = mid; else hi = mid - 1; }
            return lo;
        }

        public Vector3 PointAt(float d, float lateral = 0f)
        {
            int i = IndexAt(d), j = (i + 1) % points.Length;
            float segLen = Vector3.Distance(points[i], points[j]);
            float t = segLen > 1e-4f ? Mathf.Clamp01((Mathf.Repeat(d, Length) - dist[i]) / segLen) : 0f;
            Vector3 p = Vector3.Lerp(points[i], points[j], t);
            return p + Vector3.Cross(Vector3.up, (points[j] - points[i]).normalized) * lateral;
        }

        public Vector3 TangentAt(float d)
        {
            int i = IndexAt(d), j = (i + 1) % points.Length;
            return (points[j] - points[i]).normalized;
        }

        public float SpeedAt(float d) => speed[IndexAt(d)];
    }
}
