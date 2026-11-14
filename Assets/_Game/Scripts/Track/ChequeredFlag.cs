using OWR.Race;
using UnityEngine;

namespace OWR.Track
{
    /// <summary>
    /// Flag marshal on the start/finish platform. Holds the chequered flag furled at their side during the race;
    /// when the leader is coming round to finish (final lap, last ~300 m) the arm goes up and the flag is waved in
    /// big figure-of-eight sweeps until every car is home. The cloth is a small grid mesh deformed every frame:
    /// a travelling wave that grows toward the free edge plus drag that trails behind the pole's motion.
    /// </summary>
    public class ChequeredFlag : MonoBehaviour
    {
        public Transform shoulder;     // rotates the whole arm (local z = raise, local x = sweep)
        public Transform pole;         // child of the hand; the cloth hangs from it
        public MeshFilter cloth;
        public GameObject furled;      // rolled-up flag shown while waiting
        public float clothLength = 1.7f, clothHeight = 1.15f;
        public int segX = 16, segY = 10;
        [Tooltip("Start waving when the leader is this close to the line on the final lap (m).")]
        public float waveFromDistance = 300f;

        Mesh mesh;
        Vector3[] baseV, v;
        float raise, t, prevSweep, sweepVel, waveAmt;
        bool waving;

        void Start()
        {
            mesh = new Mesh { name = "FlagCloth" };
            int nx = segX + 1, ny = segY + 1;
            baseV = new Vector3[nx * ny]; v = new Vector3[nx * ny];
            var uv = new Vector2[nx * ny];
            var tris = new int[segX * segY * 6];
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int i = y * nx + x;
                    float u = x / (float)segX, w = y / (float)segY;
                    baseV[i] = new Vector3(u * clothLength, -w * clothHeight, 0f);
                    uv[i] = new Vector2(u, 1f - w);
                }
            int k = 0;
            for (int y = 0; y < segY; y++)
                for (int x = 0; x < segX; x++)
                {
                    int i = y * nx + x;
                    tris[k++] = i; tris[k++] = i + nx; tris[k++] = i + 1;
                    tris[k++] = i + 1; tris[k++] = i + nx; tris[k++] = i + nx + 1;
                }
            mesh.vertices = baseV; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh.MarkDynamic();
            if (cloth) cloth.sharedMesh = mesh;
        }

        bool ShouldWave()
        {
            var rm = RaceManager.Instance;
            if (!rm) return false;
            if (rm.CelebrationPreviewMode) return true;
            if (rm.Practice || rm.Order.Count == 0) return false;
            var leader = rm.Order[0];
            if (leader.finished) return true;   // flag stays out until the race is over
            return leader.lap == rm.totalLaps && leader.d > rm.path.Length - waveFromDistance;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            t += dt;
            waving = ShouldWave();
            raise = Mathf.MoveTowards(raise, waving ? 1f : 0f, dt * 1.6f);
            waveAmt = Mathf.MoveTowards(waveAmt, waving ? 1f : 0f, dt * 1.2f);

            // ---- arm: hanging at the side -> raised high, sweeping a figure of eight above the head ----
            float sweep = Mathf.Sin(t * 7.2f) * 62f * waveAmt;             // ~1.15 Hz side to side
            float lift = Mathf.Sin(t * 14.4f) * 10f * waveAmt;              // double frequency = figure of eight
            if (shoulder)
                shoulder.localRotation = Quaternion.Euler(lift * raise, 0f, Mathf.Lerp(-8f, -165f, Mathf.SmoothStep(0f, 1f, raise)) + sweep);
            sweepVel = Mathf.Lerp(sweepVel, (sweep - prevSweep) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 10f));
            prevSweep = sweep;

            // ---- cloth: rolled up on the pole until the arm is on its way up ----
            bool open = raise > 0.35f;
            if (cloth) cloth.GetComponent<Renderer>().enabled = open;
            if (furled) furled.SetActive(!open);
            if (mesh == null || !open) return;
            float drag = Mathf.Clamp(-sweepVel * 0.004f, -1.1f, 1.1f);       // trails behind the pole
            float flutter = 0.06f + 0.16f * waveAmt;
            int nx = segX + 1;
            for (int i = 0; i < baseV.Length; i++)
            {
                Vector3 b = baseV[i];
                float u = b.x / clothLength, w = -b.y / clothHeight;
                float free = u * u;                                           // pinned at the pole, free at the edge
                float wave = Mathf.Sin(u * 7.5f - t * (9f + 9f * waveAmt) + w * 1.3f) * flutter * u
                           + Mathf.Sin(u * 13f - t * 17f + w * 3f) * 0.025f * u * waveAmt;
                // furled: the cloth hangs down along the pole; waving: it flies out sideways
                Vector3 p = new Vector3(b.x, b.y, wave + drag * free * 1.3f);
                p.x -= Mathf.Abs(drag) * free * 0.35f;                         // swinging shortens the reach a bit
                p.y -= free * 0.12f * (1f - waveAmt);                          // droop when slow
                v[i] = p;
            }
            mesh.vertices = v;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
