using System.Collections.Generic;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>
    /// Small procedural modelling kit: lofts (superellipse sections), airfoil wings, tubes, lathes and
    /// extruded polygons, accumulated into one mesh with several submeshes (one per material).
    /// Every face is oriented against a "desired outward" direction, so winding never has to be reasoned about.
    /// </summary>
    public class MeshKit
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Vector2> uv2s = new List<Vector2>();
        /// <summary>Value written to UV2 for every new vertex (e.g. per-spectator random id).</summary>
        public Vector2 uv2Value;
        readonly List<int>[] subs;
        readonly List<(int, int)> seams = new List<(int, int)>();
        public Matrix4x4 M = Matrix4x4.identity;

        public MeshKit(int submeshCount)
        {
            subs = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) subs[i] = new List<int>();
        }

        // ------------------------------------------------------------------ primitives
        public int V(Vector3 localPos, Vector2 uv)
        {
            verts.Add(M.MultiplyPoint3x4(localPos));
            uvs.Add(uv);
            uv2s.Add(uv2Value);
            return verts.Count - 1;
        }

        Vector3 W(Vector3 localDir) => M.MultiplyVector(localDir);

        public void Tri(int s, int a, int b, int c, Vector3 localOut)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (Vector3.Dot(n, W(localOut)) < 0f) { int t = b; b = c; c = t; }
            subs[s].Add(a); subs[s].Add(b); subs[s].Add(c);
        }

        public void Quad(int s, int a, int b, int c, int d, Vector3 localOut)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]) + Vector3.Cross(verts[c] - verts[a], verts[d] - verts[a]);
            if (Vector3.Dot(n, W(localOut)) < 0f)
            {
                subs[s].Add(a); subs[s].Add(c); subs[s].Add(b);
                subs[s].Add(a); subs[s].Add(d); subs[s].Add(c);
            }
            else
            {
                subs[s].Add(a); subs[s].Add(b); subs[s].Add(c);
                subs[s].Add(a); subs[s].Add(c); subs[s].Add(d);
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetUVs(1, uv2s);
            m.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) m.SetTriangles(subs[i], i);
            m.RecalculateNormals();
            var n = m.normals;
            foreach (var (a, b) in seams)
            {
                Vector3 avg = (n[a] + n[b]).normalized;
                n[a] = n[b] = avg;
            }
            m.normals = n;
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ loft
        public struct Sec
        {
            public float z, cx, cy, hw, hTop, hBot, n;
            public Sec(float z, float cx, float cy, float hw, float hTop, float hBot, float n = 2.6f)
            { this.z = z; this.cx = cx; this.cy = cy; this.hw = hw; this.hTop = hTop; this.hBot = hBot; this.n = n; }

            public Vector3 Point(float phi)
            {
                float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
                float e = 2f / n;
                float x = hw * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);
                float y = (s >= 0 ? hTop : hBot) * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e);
                return new Vector3(cx + x, cy + y, z);
            }
            public Vector3 Center => new Vector3(cx, cy, z);
        }

        static float CR(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Catmull-Rom densify a list of key sections so the surface flows smoothly.</summary>
        public static List<Sec> Densify(IList<Sec> k, int sub)
        {
            var o = new List<Sec>();
            for (int i = 0; i < k.Count - 1; i++)
            {
                Sec a = k[Mathf.Max(i - 1, 0)], b = k[i], c = k[i + 1], d = k[Mathf.Min(i + 2, k.Count - 1)];
                for (int j = 0; j < sub; j++)
                {
                    float t = j / (float)sub;
                    o.Add(new Sec(CR(a.z, b.z, c.z, d.z, t), CR(a.cx, b.cx, c.cx, d.cx, t), CR(a.cy, b.cy, c.cy, d.cy, t),
                        Mathf.Max(0.002f, CR(a.hw, b.hw, c.hw, d.hw, t)), Mathf.Max(0.002f, CR(a.hTop, b.hTop, c.hTop, d.hTop, t)),
                        Mathf.Max(0.002f, CR(a.hBot, b.hBot, c.hBot, d.hBot, t)), CR(a.n, b.n, c.n, d.n, t)));
                }
            }
            o.Add(k[k.Count - 1]);
            return o;
        }

        /// <summary>
        /// Loft through sections along local z. phi range lets you build open shells (cockpit).
        /// inward = true flips faces to point inside (interior liners).
        /// Returns the ring vertex positions (local) for edge trims.
        /// </summary>
        public List<Vector3[]> Loft(int s, IList<Sec> keys, int ringSeg = 40, int subdiv = 5,
            float phi0 = 0f, float phi1 = Mathf.PI * 2f, int capStartSub = -1, int capEndSub = -1, bool inward = false)
        {
            var secs = Densify(keys, subdiv);
            bool closed = Mathf.Abs(phi1 - phi0 - Mathf.PI * 2f) < 1e-3f;
            int cols = ringSeg + 1;
            var rings = new List<Vector3[]>();
            int[,] idx = new int[secs.Count, cols];
            float vDist = 0f;
            for (int i = 0; i < secs.Count; i++)
            {
                if (i > 0) vDist += Vector3.Distance(secs[i].Center, secs[i - 1].Center);
                var ring = new Vector3[cols];
                float uDist = 0f;
                for (int j = 0; j < cols; j++)
                {
                    float phi = Mathf.Lerp(phi0, phi1, j / (float)ringSeg);
                    ring[j] = secs[i].Point(phi);
                    if (j > 0) uDist += Vector3.Distance(ring[j], ring[j - 1]);
                    idx[i, j] = V(ring[j], new Vector2(uDist, vDist));
                }
                if (closed) seams.Add((idx[i, 0], idx[i, ringSeg]));
                rings.Add(ring);
            }
            for (int i = 0; i < secs.Count - 1; i++)
            {
                for (int j = 0; j < ringSeg; j++)
                {
                    Vector3 mid = (rings[i][j] + rings[i + 1][j + 1]) * 0.5f;
                    Vector3 c = Vector3.Lerp(secs[i].Center, secs[i + 1].Center, 0.5f);
                    Vector3 outDir = mid - c;
                    outDir.z = 0f;
                    if (inward) outDir = -outDir;
                    Quad(s, idx[i, j], idx[i, j + 1], idx[i + 1, j + 1], idx[i + 1, j], outDir);
                }
            }
            if (closed && capStartSub >= 0) Cap(capStartSub, secs[0], rings[0], Mathf.Sign(secs[0].z - secs[1].z));
            if (closed && capEndSub >= 0) Cap(capEndSub, secs[secs.Count - 1], rings[rings.Count - 1], Mathf.Sign(secs[secs.Count - 1].z - secs[secs.Count - 2].z));
            return rings;
        }

        void Cap(int s, Sec sec, Vector3[] ring, float dirZ)
        {
            int c = V(sec.Center, new Vector2(0.5f, 0.5f));
            var ids = new int[ring.Length];
            for (int j = 0; j < ring.Length; j++)
                ids[j] = V(ring[j], new Vector2((ring[j].x - sec.cx) / (2f * sec.hw) + 0.5f, (ring[j].y - sec.cy) / (2f * sec.hTop) + 0.5f));
            for (int j = 0; j < ring.Length - 1; j++) Tri(s, c, ids[j], ids[j + 1], new Vector3(0, 0, dirZ));
        }

        // ------------------------------------------------------------------ wing
        /// <summary>
        /// Airfoil extruded along local +x from x0 to x1. Leading edge at z = zLE (chord runs toward -z).
        /// camber &lt; 0 gives a downforce (inverted) section. aoaDeg raises the trailing edge.
        /// yOfSpan lets the element curve (front wing "spoon" shape).
        /// </summary>
        public void Wing(int s, float x0, float x1, float zLE, float y, float chord, float thick = 0.10f, float camber = -0.06f,
            float aoaDeg = 12f, int profilePts = 14, int spanSteps = 8, System.Func<float, float> yOfSpan = null,
            System.Func<float, float> chordOfSpan = null, int capSub = -1)
        {
            if (capSub < 0) capSub = s;
            var prof = Airfoil(thick, camber, profilePts);
            float aoa = aoaDeg * Mathf.Deg2Rad;
            int pc = prof.Count;
            int[,] idx = new int[spanSteps + 1, pc];
            var pos = new Vector3[spanSteps + 1, pc];
            var centers = new Vector3[spanSteps + 1];
            for (int i = 0; i <= spanSteps; i++)
            {
                float f = i / (float)spanSteps;
                float x = Mathf.Lerp(x0, x1, f);
                float ch = chord * (chordOfSpan != null ? chordOfSpan(f) : 1f);
                float yy = y + (yOfSpan != null ? yOfSpan(f) : 0f);
                Vector3 cSum = Vector3.zero;
                float u = 0f;
                for (int j = 0; j < pc; j++)
                {
                    // profile: (t along chord 0..1, h normal) -> rotate by AoA about LE
                    float t = prof[j].x * ch, h = prof[j].y * ch;
                    // chord dir (z,y) = (-cos a, sin a); thickness normal = (sin a, cos a)
                    float zz = zLE - t * Mathf.Cos(aoa) + h * Mathf.Sin(aoa);
                    float yv = yy + t * Mathf.Sin(aoa) + h * Mathf.Cos(aoa);
                    var p = new Vector3(x, yv, zz);
                    if (j > 0) u += Vector3.Distance(p, pos[i, j - 1]);
                    pos[i, j] = p;
                    idx[i, j] = V(p, new Vector2(u, x));
                    cSum += p;
                }
                centers[i] = cSum / pc;
            }
            for (int i = 0; i < spanSteps; i++)
                for (int j = 0; j < pc - 1; j++)
                {
                    Vector3 mid = (pos[i, j] + pos[i + 1, j + 1]) * 0.5f;
                    Vector3 c = (centers[i] + centers[i + 1]) * 0.5f;
                    Vector3 o = mid - c; o.x = 0f;
                    Quad(s, idx[i, j], idx[i, j + 1], idx[i + 1, j + 1], idx[i + 1, j], o);
                }
            // end caps
            for (int e = 0; e <= 1; e++)
            {
                int i = e == 0 ? 0 : spanSteps;
                float dir = e == 0 ? Mathf.Sign(x0 - x1) : Mathf.Sign(x1 - x0);
                int c = V(centers[i], Vector2.zero);
                var ids = new int[pc];
                for (int j = 0; j < pc; j++) ids[j] = V(pos[i, j], new Vector2(pos[i, j].z, pos[i, j].y));
                for (int j = 0; j < pc - 1; j++) Tri(capSub, c, ids[j], ids[j + 1], new Vector3(dir, 0, 0));
            }
        }

        /// <summary>Closed NACA-style profile, x = chord fraction (0 = LE), y = thickness/camber.</summary>
        static List<Vector2> Airfoil(float th, float m, int n)
        {
            const float p = 0.4f;
            var upper = new List<Vector2>();
            var lower = new List<Vector2>();
            for (int i = 0; i <= n; i++)
            {
                float x = 0.5f * (1f - Mathf.Cos(Mathf.PI * i / n));
                float yt = 5f * th * (0.2969f * Mathf.Sqrt(x) - 0.126f * x - 0.3516f * x * x + 0.2843f * x * x * x - 0.1036f * x * x * x * x);
                float yc = x < p ? m / (p * p) * (2f * p * x - x * x) : m / ((1f - p) * (1f - p)) * ((1f - 2f * p) + 2f * p * x - x * x);
                upper.Add(new Vector2(x, yc + yt));
                lower.Add(new Vector2(x, yc - yt));
            }
            var loop = new List<Vector2>();
            for (int i = n; i >= 0; i--) loop.Add(upper[i]);
            for (int i = 1; i <= n; i++) loop.Add(lower[i]);
            return loop;
        }

        // ------------------------------------------------------------------ tube
        public void Tube(int s, IList<Vector3> path, float radius, int seg = 12, float flatten = 1f, bool caps = true,
            System.Func<float, float> radiusOfT = null)
        {
            int n = path.Count;
            var frames = new (Vector3 t, Vector3 nrm, Vector3 bin)[n];
            Vector3 t0 = (path[1] - path[0]).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(t0, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 nrm = Vector3.Cross(t0, up).normalized;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                nrm = Vector3.ProjectOnPlane(nrm, t).normalized;
                frames[i] = (t, nrm, Vector3.Cross(t, nrm));
            }
            int[,] idx = new int[n, seg + 1];
            float v = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) v += Vector3.Distance(path[i], path[i - 1]);
                float r = radius * (radiusOfT != null ? radiusOfT(i / (float)(n - 1)) : 1f);
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    Vector3 p = path[i] + frames[i].nrm * Mathf.Cos(a) * r + frames[i].bin * Mathf.Sin(a) * r * flatten;
                    idx[i, j] = V(p, new Vector2(j / (float)seg, v));
                }
                seams.Add((idx[i, 0], idx[i, seg]));
            }
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    Vector3 mid = (verts[idx[i, j]] + verts[idx[i + 1, j + 1]]) * 0.5f; // world
                    Vector3 c = M.MultiplyPoint3x4((path[i] + path[i + 1]) * 0.5f);
                    Vector3 outLocal = M.inverse.MultiplyVector(mid - c);
                    Quad(s, idx[i, j], idx[i, j + 1], idx[i + 1, j + 1], idx[i + 1, j], outLocal);
                }
            if (!caps) return;
            for (int e = 0; e <= 1; e++)
            {
                int i = e == 0 ? 0 : n - 1;
                Vector3 dir = e == 0 ? -frames[0].t : frames[n - 1].t;
                int c = V(path[i], Vector2.zero);
                for (int j = 0; j < seg; j++) Tri(s, c, idx[i, j], idx[i, j + 1], dir);
            }
        }

        // ------------------------------------------------------------------ lathe (around local X axis)
        /// <summary>Revolve a closed profile (x = along axle, y = radius) around the X axis.</summary>
        public void Lathe(int s, IList<Vector2> profile, int seg = 48, float angle0 = 0f, float angle1 = Mathf.PI * 2f)
        {
            int pc = profile.Count;
            Vector2 cen = Vector2.zero;
            foreach (var p in profile) cen += p;
            cen /= pc;
            bool full = Mathf.Abs(angle1 - angle0 - Mathf.PI * 2f) < 1e-3f;
            int[,] idx = new int[seg + 1, pc];
            var pos = new Vector3[seg + 1, pc];
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(angle0, angle1, i / (float)seg);
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                for (int j = 0; j < pc; j++)
                {
                    var p = new Vector3(profile[j].x, profile[j].y * ca, profile[j].y * sa);
                    pos[i, j] = p;
                    idx[i, j] = V(p, new Vector2(i / (float)seg * 4f, j / (float)(pc - 1)));
                }
            }
            if (full) for (int j = 0; j < pc; j++) seams.Add((idx[0, j], idx[seg, j]));
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.Lerp(angle0, angle1, (i + 0.5f) / seg);
                Vector3 cRing = new Vector3(cen.x, cen.y * Mathf.Cos(a), cen.y * Mathf.Sin(a));
                for (int j = 0; j < pc - 1; j++)
                {
                    Vector3 mid = (pos[i, j] + pos[i + 1, j + 1]) * 0.5f;
                    Quad(s, idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1], mid - cRing);
                }
            }
        }

        // ------------------------------------------------------------------ extruded polygon
        /// <summary>Star-shaped 2D polygon in the (u, v) plane at origin, extruded along w by thickness (centered).</summary>
        public void Extrude(int s, IList<Vector2> poly, Vector3 origin, Vector3 u, Vector3 v, Vector3 w, float thickness, int sideSub = -1)
        {
            if (sideSub < 0) sideSub = s;
            int n = poly.Count;
            Vector2 c2 = Vector2.zero;
            foreach (var p in poly) c2 += p;
            c2 /= n;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 off = w * (thickness * 0.5f * side);
                int c = V(origin + u * c2.x + v * c2.y + off, c2);
                var ids = new int[n];
                for (int i = 0; i < n; i++) ids[i] = V(origin + u * poly[i].x + v * poly[i].y + off, poly[i]);
                for (int i = 0; i < n; i++) Tri(s, c, ids[i], ids[(i + 1) % n], w * side);
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector3 pa = origin + u * a.x + v * a.y, pb = origin + u * b.x + v * b.y;
                Vector3 h = w * (thickness * 0.5f);
                int i0 = V(pa - h, new Vector2(0, 0)), i1 = V(pb - h, new Vector2(1, 0)), i2 = V(pb + h, new Vector2(1, 1)), i3 = V(pa + h, new Vector2(0, 1));
                Vector2 mid = (a + b) * 0.5f;
                Vector3 outDir = u * (mid.x - c2.x) + v * (mid.y - c2.y);
                Quad(sideSub, i0, i1, i2, i3, outDir);
            }
        }

        public void Box(int s, Vector3 center, Vector3 size, Quaternion rot)
        {
            var poly = new[] { new Vector2(-size.x / 2, -size.y / 2), new Vector2(size.x / 2, -size.y / 2), new Vector2(size.x / 2, size.y / 2), new Vector2(-size.x / 2, size.y / 2) };
            Extrude(s, poly, center, rot * Vector3.right, rot * Vector3.up, rot * Vector3.forward, size.z);
        }

        /// <summary>Rounded-rectangle outline helper.</summary>
        public static List<Vector2> RoundRect(float w, float h, float r, int cornerSeg = 4)
        {
            var o = new List<Vector2>();
            Vector2[] centers = { new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= cornerSeg; i++)
                {
                    float a = (c * 90f + i * 90f / cornerSeg) * Mathf.Deg2Rad;
                    o.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            return o;
        }
    }
}
