using System.Collections.Generic;
using System.IO;
using OWR.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OWR.EditorTools
{
    /// <summary>
    /// OWR > Build Circuit: Interlagos. The real 4.3 km layout and elevation (Tools/make_track_interlagos.py ->
    /// Tracks/Interlagos.json) built through the shared scene pipeline (TestSceneBuilder.BuildScene), with its own
    /// ground (terrain shaped from the real heights) and trackside: pit complex, grandstands where the real ones are,
    /// advertising bridges, the Sao Paulo skyline, the lake, palms and painted run-off logos.
    /// </summary>
    public static class CircuitBuilder
    {
        const string Root = "Assets/_Game";
        public const string ScenePath = Root + "/Scenes/Interlagos.unity";
        const float PitWall = 8.4f;          // pit wall offset on the left of the main straight
        const float PitLaneOuter = 21.5f;    // garages start here
        const int PitFrom = -374, PitTo = 60; // pit lane: Juncao exit -> after the start line (indices relative to the line)
        const int GarageFrom = -150, GarageTo = 30;

        [System.Serializable]
        class TrackJson
        {
            public string name; public float roadWidth; public int startIndex;
            public float[] px, py, pz, wallLeft, wallRight;
            public string[] cornerNames; public int[] cornerIndex;
        }

        static TrackJson data;
        static List<Vector3> pts;
        static float[] wl, wr;
        static int N => pts.Count;
        static Vector2 lakeCentre; static float lakeRadius, lakeLevel;

        [MenuItem("OWR/Build Circuit: Interlagos")]
        public static void Build()
        {
            data = JsonUtility.FromJson<TrackJson>(File.ReadAllText(Root + "/Tracks/Interlagos.json"));
            pts = new List<Vector3>(data.px.Length);
            for (int i = 0; i < data.px.Length; i++) pts.Add(new Vector3(data.px[i], data.py[i], data.pz[i]));
            wl = (float[])data.wallLeft.Clone(); wr = (float[])data.wallRight.Clone();
            // pit wall along the main straight (left side), blended in/out over 40 m
            for (int k = PitFrom - 20; k <= PitTo + 20; k++)
            {
                int i = Idx(k);
                float t = Mathf.Clamp01(Mathf.Min(k - (PitFrom - 20), (PitTo + 20) - k) / 20f);
                wl[i] = Mathf.Lerp(wl[i], PitWall, t);
            }
            FindLake();
            var spec = new TestSceneBuilder.TrackSpec
            {
                name = "Interlagos", scenePath = ScenePath, pts = pts, wallLeft = wl, wallRight = wr,
                roadWidth = data.roadWidth, startIndex = data.startIndex,
                buildGround = BuildGround, buildTrackside = BuildTrackside,
            };
            TestSceneBuilder.BuildScene(spec);
        }

        static int Idx(int k) => ((k % N) + N) % N;
        static float WallAt(int i, int side) => side < 0 ? wl[i] : wr[i];
        static Vector3 Right(int i) => TestSceneBuilder.Right(pts, i);
        static Vector3 Tan(int i) => Vector3.Cross(Right(i), Vector3.up);

        // ================================================================== terrain height
        static Dictionary<(int, int), List<int>> grid;
        const float Cell = 40f;

        static void BuildGrid()
        {
            grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < N; i += 2)
            {
                var key = ((int)Mathf.Floor(pts[i].x / Cell), (int)Mathf.Floor(pts[i].z / Cell));
                if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                l.Add(i);
            }
        }

        /// <summary>Nearest centreline sample within the search radius (cells), or -1.</summary>
        static int Nearest(Vector3 p, int rad, out float dist)
        {
            int cx = (int)Mathf.Floor(p.x / Cell), cz = (int)Mathf.Floor(p.z / Cell);
            int best = -1; float bd = float.MaxValue;
            for (int dx = -rad; dx <= rad; dx++)
                for (int dz = -rad; dz <= rad; dz++)
                    if (grid.TryGetValue((cx + dx, cz + dz), out var l))
                        foreach (int i in l)
                        {
                            float d = (pts[i].x - p.x) * (pts[i].x - p.x) + (pts[i].z - p.z) * (pts[i].z - p.z);
                            if (d < bd) { bd = d; best = i; }
                        }
            dist = Mathf.Sqrt(bd);
            return best;
        }

        static float avgHeight = -1f;

        /// <summary>
        /// Ground height: just under the track inside the barriers, blending out to the surrounding land, which follows
        /// the circuit's own elevation (inverse-distance) plus rolling hills further out - Sao Paulo sits on hills.
        /// </summary>
        public static float GroundHeight(float x, float z)
        {
            if (grid == null) BuildGrid();
            if (avgHeight < 0f) { float s = 0f; foreach (var p in pts) s += p.y; avgHeight = s / N; }
            var q = new Vector3(x, 0f, z);
            int ni = Nearest(q, 6, out float d);
            // inverse-distance blend of the circuit heights within ~240 m
            float wsum = 0f, hsum = 0f;
            int cx = (int)Mathf.Floor(x / Cell), cz = (int)Mathf.Floor(z / Cell);
            for (int dx = -6; dx <= 6; dx++)
                for (int dz = -6; dz <= 6; dz++)
                    if (grid.TryGetValue((cx + dx, cz + dz), out var l))
                        foreach (int i in l)
                        {
                            float dd = (pts[i].x - x) * (pts[i].x - x) + (pts[i].z - z) * (pts[i].z - z);
                            float w = 1f / (dd + 400f); wsum += w; hsum += w * pts[i].y;
                        }
            float far = wsum > 0f ? hsum / wsum : avgHeight;
            float dTrack = ni >= 0 ? d : 999f;
            float hills = (Mathf.PerlinNoise(x * 0.0021f + 3.1f, z * 0.0021f + 7.7f) - 0.45f) * 22f * Smooth(120f, 600f, dTrack)
                        + (Mathf.PerlinNoise(x * 0.0007f + 1.3f, z * 0.0007f + 2.9f) - 0.3f) * 70f * Smooth(500f, 1600f, dTrack);
            float h = far + hills;
            if (ni >= 0)
            {
                float flat = Mathf.Max(wl[ni], wr[ni]);
                if (InPit(ni)) flat = Mathf.Max(flat, 46f);
                float near = pts[ni].y - 0.45f;
                h = Mathf.Lerp(near, h, Smooth(flat + 2f, flat + 70f, d));
            }
            // the lake basin
            float ld = Vector2.Distance(new Vector2(x, z), lakeCentre);
            if (lakeRadius > 0f) h = Mathf.Lerp(lakeLevel - 2.5f, h, Smooth(lakeRadius * 0.85f, lakeRadius + 25f, ld));
            return h;
        }

        static float Smooth(float a, float b, float v) { float t = Mathf.Clamp01((v - a) / (b - a)); return t * t * (3f - 2f * t); }
        static bool InPit(int i) { int k = i > N / 2 ? i - N : i; return k >= PitFrom && k <= PitTo; }

        /// <summary>The lake sits in the biggest open space of the infield, just above the circuit's lowest point.</summary>
        static void FindLake()
        {
            if (grid == null) BuildGrid();
            Vector3 c = Vector3.zero; foreach (var p in pts) c += p; c /= N;
            float best = 0f; Vector2 bp = Vector2.zero;
            float minY = float.MaxValue; foreach (var p in pts) minY = Mathf.Min(minY, p.y);
            for (float x = c.x - 500f; x <= c.x + 500f; x += 10f)
                for (float z = c.z - 600f; z <= c.z + 600f; z += 10f)
                {
                    if (!InsideLoop(x, z)) continue;
                    Nearest(new Vector3(x, 0f, z), 8, out float d);
                    if (d > best) { best = d; bp = new Vector2(x, z); }
                }
            lakeCentre = bp; lakeRadius = Mathf.Max(0f, best - 45f); lakeLevel = minY - 1.2f;
        }

        static bool InsideLoop(float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = N - 1; i < N; j = i++)
                if ((pts[i].z > z) != (pts[j].z > z) && x < (pts[j].x - pts[i].x) * (z - pts[i].z) / (pts[j].z - pts[i].z) + pts[i].x) inside = !inside;
            return inside;
        }

        // ================================================================== ground
        static Material WorldMat(string name, Material src, float smooth)
        {
            var m = EnvironmentBuilder.GetMat($"{Root}/Materials/{name}.mat", "Universal Render Pipeline/Lit");
            if (src)
            {
                m.CopyPropertiesFromMaterial(src);
                m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildGround(TestSceneBuilder.TrackSpec spec, Transform trackRoot, Material grassSrc)
        {
            grid = null; avgHeight = -1f; BuildGrid();
            var grass = WorldMat("Grass_World", grassSrc, 0.38f);
            var root = new GameObject("Ground").transform; root.SetParent(trackRoot, false);

            // verges: flat strips from the road edge out under the barriers, following the circuit (world-space UVs)
            for (int side = -1; side <= 1; side += 2)
            {
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                for (int k = 0; k <= N; k++)
                {
                    int i = k % N; Vector3 r = Right(i);
                    Vector3 a = pts[i] + r * side * (spec.roadWidth * 0.5f - 0.2f) + Vector3.up * 0.0f;
                    Vector3 b = pts[i] + r * side * (WallAt(i, side) + 0.6f) + Vector3.up * -0.06f;
                    verts.Add(a); uvs.Add(new Vector2(a.x, a.z) / 6f);
                    verts.Add(b); uvs.Add(new Vector2(b.x, b.z) / 6f);
                }
                for (int k = 0; k < N; k++)
                {
                    int v = k * 2;
                    if (side > 0) tris.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                    else tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                }
                var m = new Mesh { name = "Verge", indexFormat = IndexFormat.UInt32 };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
                var g = TestSceneBuilder.MeshObject(side < 0 ? "Verge_L" : "Verge_R", root, m, grass, true);
            }

            // terrain: 10 m grid in 300 m chunks around the circuit, colliders only near the track
            Vector3 mn = pts[0], mx = pts[0];
            foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            const float pad = 1300f, step = 10f; const int chunkCells = 30;
            float x0 = Mathf.Floor((mn.x - pad) / step) * step, z0 = Mathf.Floor((mn.z - pad) / step) * step;
            int cellsX = Mathf.CeilToInt((mx.x + pad - x0) / step), cellsZ = Mathf.CeilToInt((mx.z + pad - z0) / step);
            var hcache = new float[cellsX + 1, cellsZ + 1];
            for (int ix = 0; ix <= cellsX; ix++)
                for (int iz = 0; iz <= cellsZ; iz++)
                    hcache[ix, iz] = GroundHeight(x0 + ix * step, z0 + iz * step);
            int chunks = 0;
            for (int cx = 0; cx < cellsX; cx += chunkCells)
                for (int cz = 0; cz < cellsZ; cz += chunkCells)
                {
                    int w = Mathf.Min(chunkCells, cellsX - cx), h = Mathf.Min(chunkCells, cellsZ - cz);
                    var verts = new Vector3[(w + 1) * (h + 1)]; var uvs = new Vector2[verts.Length];
                    for (int a = 0; a <= w; a++)
                        for (int b = 0; b <= h; b++)
                        {
                            var p = new Vector3(x0 + (cx + a) * step, hcache[cx + a, cz + b], z0 + (cz + b) * step);
                            verts[a * (h + 1) + b] = p; uvs[a * (h + 1) + b] = new Vector2(p.x, p.z) / 6f;
                        }
                    var tris = new List<int>();
                    for (int a = 0; a < w; a++)
                        for (int b = 0; b < h; b++)
                        {
                            int v = a * (h + 1) + b;
                            tris.AddRange(new[] { v, v + 1, v + h + 1, v + 1, v + h + 2, v + h + 1 });
                        }
                    var m = new Mesh { name = "Terrain_" + chunks, indexFormat = IndexFormat.UInt32 };
                    m.vertices = verts; m.uv = uvs; m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
                    Vector3 centre = m.bounds.center;
                    Nearest(centre, 8, out float dc);
                    var g = TestSceneBuilder.MeshObject("Terrain_" + chunks, root, m, grass, dc < 420f);
                    g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    chunks++;
                }

            // lake surface
            if (lakeRadius > 10f)
            {
                var water = EnvironmentBuilder.GetMat($"{Root}/Materials/Lake_Water.mat", "Universal Render Pipeline/Lit");
                water.SetColor("_BaseColor", new Color(0.12f, 0.16f, 0.17f)); water.SetFloat("_Smoothness", 0.96f); water.SetFloat("_Metallic", 0.1f);
                EditorUtility.SetDirty(water);
                var lake = new GameObject("Lake");
                lake.transform.SetParent(root, false);
                var mesh = new Mesh { name = "Lake" };
                var lv = new List<Vector3> { new Vector3(lakeCentre.x, lakeLevel, lakeCentre.y) }; var lt = new List<int>();
                int seg = 64;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    float rr = lakeRadius * (0.9f + 0.12f * Mathf.PerlinNoise(Mathf.Cos(a) * 2f + 5f, Mathf.Sin(a) * 2f + 5f));
                    lv.Add(new Vector3(lakeCentre.x + Mathf.Cos(a) * rr * 1.25f, lakeLevel, lakeCentre.y + Mathf.Sin(a) * rr));
                    if (s > 0) lt.AddRange(new[] { 0, s + 1, s });
                }
                mesh.SetVertices(lv); mesh.SetTriangles(lt, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                lake.AddComponent<MeshFilter>().sharedMesh = mesh;
                lake.AddComponent<MeshRenderer>().sharedMaterial = water;
                lake.isStatic = true;
            }
            Debug.Log($"[OWR] Interlagos ground: {chunks} terrain chunks, lake r={lakeRadius:0} m");
        }

        // ================================================================== trackside
        static void BuildTrackside(TestSceneBuilder.TrackSpec spec, Transform parent, TestSceneBuilder.TracksideMats m)
        {
            BuildCatchFence(parent, m.fence, m.post);
            EnvironmentBuilder.BuildSponsorBoards(pts, WallAt, parent);
            var keepOut = new List<Vector4>();
            BuildPitComplex(parent, m, keepOut);
            BuildStands(parent, m, keepOut);
            BuildBridge(parent, 470, "Bridge_RetaOposta");
            BuildBridge(parent, Idx(-60), "Bridge_PitStraight");
            BuildBridge(parent, 1500, "Bridge_Mergulho");
            BuildRunoffPaint(parent, spec);
            EnvironmentBuilder.BuildTyreWalls(pts, WallAt, parent);
            EnvironmentBuilder.BuildMarshalPosts(pts, WallAt, parent, m.crowd, -0.45f);
            EnvironmentBuilder.BuildPoles(pts, WallAt, parent, -0.8f);
            BuildSkyline(parent);
            BuildVegetation(parent, m, keepOut);
        }

        static void BuildCatchFence(Transform parent, Material fenceMat, Material postMat)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                float dist = 0f;
                for (int k = 0; k <= N; k++)
                {
                    int i = k % N;
                    if (k > 0) dist += Vector3.Distance(pts[i], pts[(k - 1) % N]);
                    Vector3 p = pts[i] + Right(i) * (side * (WallAt(i, side) + 0.1f));
                    verts.Add(p + Vector3.up * 1.0f); uvs.Add(new Vector2(dist / 1.2f, 0f));
                    verts.Add(p + Vector3.up * 4.4f); uvs.Add(new Vector2(dist / 1.2f, 3.4f / 1.2f));
                }
                for (int k = 0; k < N; k++) { int v = k * 2; tris.AddRange(new[] { v, v + 1, v + 3, v, v + 3, v + 2 }); }
                var mesh = new Mesh { name = "Fence", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var f = EnvironmentBuilder.Obj("CatchFence_" + (side < 0 ? "L" : "R"), parent, mesh, fenceMat);
                f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                var pk = new MeshKit(1);
                float acc = 0f;
                for (int i = 1; i < N; i++)
                {
                    acc += Vector3.Distance(pts[i], pts[i - 1]);
                    if (acc < 6f) continue;
                    acc = 0f;
                    Vector3 r = Right(i);
                    Vector3 basePos = pts[i] + r * (side * (WallAt(i, side) + 0.18f)) + Vector3.up * 1.0f;
                    pk.Tube(0, new List<Vector3> { basePos, basePos + Vector3.up * 3.5f - r * side * 0.5f }, 0.045f, 6, 1f, false);
                }
                EnvironmentBuilder.Obj("FencePosts_" + (side < 0 ? "L" : "R"), parent, pk.Build("FencePosts"), postMat);
            }
        }

        // ------------------------------------------------------------------ pit lane, garages, control tower
        static void BuildPitComplex(Transform parent, TestSceneBuilder.TracksideMats m, List<Vector4> keepOut)
        {
            var root = new GameObject("PitComplex").transform; root.SetParent(parent, false);
            var asphalt = m.road;
            // pit lane surface + fast-lane line, in two pieces either side of the start line (index wrap)
            foreach (var (a, b) in new[] { (Idx(PitFrom), N - 1), (0, PitTo) })
            {
                var lane = TestSceneBuilder.BuildRibbon(pts, a, b, false, -PitLaneOuter, -(PitWall + 0.2f), 0.012f, 0.012f);
                TestSceneBuilder.MeshObject("PitLane", root, lane, asphalt, true);
                var line = TestSceneBuilder.BuildRibbon(pts, a, b, false, -(PitWall + 3.3f), -(PitWall + 3.1f), 0.02f, 0.02f);
                TestSceneBuilder.MeshObject("PitLaneLine", root, line, EnvironmentBuilder.GetMat($"{Root}/Materials/TrackLine.mat", "Universal Render Pipeline/Lit"), false);
            }

            var concrete = m.concrete;
            var glass = EnvironmentBuilder.GetMat($"{Root}/Materials/Pit_Glass.mat", "Universal Render Pipeline/Lit");
            glass.SetColor("_BaseColor", new Color(0.12f, 0.16f, 0.2f)); glass.SetFloat("_Smoothness", 0.95f); glass.SetFloat("_Metallic", 0.6f);
            var white = EnvironmentBuilder.GetMat($"{Root}/Materials/Pit_White.mat", "Universal Render Pipeline/Lit");
            white.SetColor("_BaseColor", new Color(0.86f, 0.87f, 0.88f)); white.SetFloat("_Smoothness", 0.45f);
            var dark = EnvironmentBuilder.GetMat($"{Root}/Materials/Pit_GarageDark.mat", "Universal Render Pipeline/Lit");
            dark.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.055f)); dark.SetFloat("_Smoothness", 0.3f);
            Color[] team = { new Color(1f, 0.38f, 0.06f), new Color(0.05f, 0.2f, 0.65f), new Color(0.55f, 0.03f, 0.05f), new Color(0.35f, 0.7f, 0.1f),
                             new Color(0.02f, 0.35f, 0.18f), new Color(0.85f, 0.15f, 0.45f), new Color(0.65f, 0.67f, 0.7f), new Color(0.03f, 0.03f, 0.035f),
                             new Color(0.95f, 0.75f, 0.05f), new Color(0.3f, 0.08f, 0.5f), new Color(0.25f, 0.55f, 0.85f), new Color(0.85f, 0.85f, 0.87f) };
            var teamMats = new Material[team.Length];
            for (int t = 0; t < team.Length; t++)
            {
                teamMats[t] = EnvironmentBuilder.GetMat($"{Root}/Materials/Pit_Team{t}.mat", "Universal Render Pipeline/Lit");
                teamMats[t].SetColor("_BaseColor", team[t]); teamMats[t].SetFloat("_Smoothness", 0.6f);
            }
            var mats = new List<Material> { concrete, glass, white, dark }; mats.AddRange(teamMats);
            var k = new MeshKit(mats.Count);
            var logos = new BrandKit();
            float acc = 0f; int garage = 0;
            float depth = 16f, gH = 6.5f, upH = 6f;
            for (int kk = GarageFrom; kk <= GarageTo; kk++)
            {
                int i = Idx(kk), j = Idx(kk + 1);
                acc += Vector3.Distance(pts[i], pts[j]);
                if (acc < 14f) continue;
                acc = 0f;
                // one 14 m garage bay, oriented along the local straight; local +x faces the track
                Vector3 r = Right(i), tan = Tan(i);
                Vector3 front = pts[i] - r * PitLaneOuter;
                var rot = Quaternion.LookRotation(tan, Vector3.up);
                Vector3 inward = -r;    // away from the track
                Vector3 C(float back, float y, float along) => front + inward * back + Vector3.up * y + tan * along;
                // ground floor: structure, dark open garage, team colour fascia
                k.Box(0, C(depth * 0.5f, gH * 0.5f, 0f), new Vector3(depth, gH, 14.05f), rot);
                k.Box(3, C(-0.02f, gH * 0.42f, 0f), new Vector3(0.1f, gH * 0.8f, 11.5f), rot);
                k.Box(4 + garage % team.Length, C(-0.06f, gH * 0.9f, 0f), new Vector3(0.12f, gH * 0.16f, 13.8f), rot);
                // hospitality floor: glass band, white floor slabs, roof
                k.Box(2, C(depth * 0.5f - 1.2f, gH + 0.2f, 0f), new Vector3(depth + 2.4f, 0.4f, 14.05f), rot);
                k.Box(1, C(depth * 0.5f, gH + 0.4f + upH * 0.5f, 0f), new Vector3(depth - 0.6f, upH, 14.05f), rot);
                k.Box(2, C(depth * 0.5f - 1.5f, gH + upH + 0.6f, 0f), new Vector3(depth + 3f, 0.5f, 14.05f), rot);
                // brand board on the front of the roof edge
                logos.Quad(C(-2.9f, gH + upH + 1.6f, 0f), tan, Vector3.up, 12f, 1.5f, garage * 5 + 2, false);
                k.Box(3, C(-2.75f, gH + upH + 1.6f, 0f), new Vector3(0.12f, 1.8f, 12.4f), rot);
                garage++;
                keepOut.Add(new Vector4(front.x + inward.x * 10f, front.z + inward.z * 10f, 24f, 0f));
            }
            // race control tower behind the start line
            {
                int i = Idx(-8); Vector3 r = Right(i), tan = Tan(i);
                Vector3 b = pts[i] - r * (PitLaneOuter + depth + 12f);
                var rot = Quaternion.LookRotation(tan, Vector3.up);
                k.Box(0, b + Vector3.up * 12f, new Vector3(9f, 24f, 9f), rot);
                k.Box(1, b + Vector3.up * 26.5f - r * 0.5f, new Vector3(13f, 5f, 11f), rot);
                k.Box(2, b + Vector3.up * 29.4f - r * 0.5f, new Vector3(14f, 0.8f, 12f), rot);
                logos.Quad(b + Vector3.up * 31.2f + r * 6.05f, tan, Vector3.up, 13f, 1.6f, 3, false);
                k.Box(3, b + Vector3.up * 31.2f + r * 5.95f, new Vector3(0.15f, 2f, 13.4f), rot);
                keepOut.Add(new Vector4(b.x, b.z, 20f, 0f));
            }
            var g = EnvironmentBuilder.Obj("PitBuilding", root, k.Build("PitBuilding"), null);
            g.GetComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            g.AddComponent<MeshCollider>();
            logos.Build("PitLogos", root, BrandKit.BoardMat());
        }

        // ------------------------------------------------------------------ grandstands where the real ones are
        static void BuildStands(Transform parent, TestSceneBuilder.TracksideMats m, List<Vector4> keepOut)
        {
            // (from index, to index, side: +1 = right of the racing direction, rows, roof)
            var runs = new (int a, int b, int side, int rows, bool roof, string name)[]
            {
                (Idx(-230), 70, 1, 16, true, "Main"),            // main straight, opposite the pits
                (95, 215, 1, 20, false, "SetorA"),               // the huge stand on the outside of the Senna S
                (270, 360, 1, 11, false, "CurvaDoSol"),
                (630, 720, 1, 11, true, "Lago"),                 // Descida do Lago
                (1330, 1380, -1, 9, false, "BicoDePato"),
                (1620, 1720, 1, 14, true, "Juncao"),
                (1760, 1900, 1, 12, false, "Subida"),
            };
            int count = 0;
            foreach (var run in runs)
            {
                int a = run.a, b = run.b; if (b < a) b += N;
                float acc = 999f;
                for (int kk = a; kk <= b; kk++)
                {
                    int i = Idx(kk);
                    acc += Vector3.Distance(pts[i], pts[Idx(kk + 1)]);
                    if (acc < 52f) continue;
                    acc = 0f;
                    Vector3 r = Right(i), tan = Tan(i);
                    Vector3 pos = pts[i] + r * run.side * (WallAt(i, run.side) + 4.5f);
                    pos.y = pts[i].y - 0.45f;
                    var rot = Quaternion.LookRotation(run.side > 0 ? -tan : tan, Vector3.up);
                    if (!EnvironmentBuilder.ClearOfTrack(pts, pos, rot, 50f, run.rows, 13.5f)) continue;
                    EnvironmentBuilder.BuildGrandstand(parent, $"Grandstand_{run.name}_{count}", pos, rot, 50f, run.rows, run.roof,
                        m.concrete, m.roof, m.seats, m.crowd, m.skin);
                    keepOut.Add(new Vector4(pos.x - r.x * run.side * 10f, pos.z - r.z * run.side * 10f, 42f, 0f));
                    count++;
                }
            }
            Debug.Log($"[OWR] Interlagos: {count} grandstand sections");
        }

        // ------------------------------------------------------------------ advertising bridges over the track
        static void BuildBridge(Transform parent, int i, string name)
        {
            var steel = EnvironmentBuilder.GetMat($"{Root}/Materials/Pole_Steel.mat", "Universal Render Pipeline/Lit");
            var frame = BrandKit.FrameMat();
            Vector3 r = Right(i), tan = Tan(i), p = pts[i];
            float L = wl[i] + 2f, R = wr[i] + 2f;
            var rot = Quaternion.LookRotation(tan, Vector3.up);
            var k = new MeshKit(2);
            foreach (float s in new[] { -L, R })
                k.Box(0, p + r * s + Vector3.up * 4.5f, new Vector3(1.4f, 10f, 2.2f), rot);
            Vector3 mid = p + r * ((R - L) * 0.5f);
            float span = L + R + 1.4f;
            k.Box(1, mid + Vector3.up * 8.1f, new Vector3(span, 2.6f, 2.0f), rot);       // deck / banner box
            k.Box(0, mid + Vector3.up * 9.6f, new Vector3(span, 0.4f, 2.4f), rot);       // walkway + rail
            var g = EnvironmentBuilder.Obj(name, parent, k.Build(name), null);
            g.GetComponent<MeshRenderer>().sharedMaterials = new[] { steel, frame };
            var logos = new BrandKit();
            int brand = i % BrandKit.Count;
            for (int f = -1; f <= 1; f += 2)   // both faces: approaching cars and the other way
            {
                Vector3 n = -tan * f;
                Vector3 right = Vector3.Cross(n, Vector3.up).normalized;
                float w = Mathf.Min(span - 1f, 2.4f * 8f);
                logos.Quad(mid + Vector3.up * 8.1f + n * 1.01f, right, Vector3.up, w, w / 8f, brand + (f > 0 ? 0 : 5), false);
            }
            logos.Build(name + "_Logos", parent, BrandKit.BoardMat());
        }

        // ------------------------------------------------------------------ asphalt run-off with painted logos (Senna S, Juncao)
        static void BuildRunoffPaint(Transform parent, TestSceneBuilder.TrackSpec spec)
        {
            var paint = new BrandKit();
            var road = EnvironmentBuilder.GetMat($"{Root}/Materials/Runoff_Asphalt.mat", "Universal Render Pipeline/Lit");
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/Road_Wet.mat");
            if (src) road.CopyPropertiesFromMaterial(src);
            road.SetColor("_BaseColor", new Color(0.3f, 0.3f, 0.31f));
            foreach (var (a, b, side) in new[] { (110, 240, 1), (180, 260, -1), (1630, 1720, 1), (640, 740, 1) })
            {
                float inner = spec.roadWidth * 0.5f + 1.6f;
                float outer = float.MaxValue; for (int i = a; i <= b; i++) outer = Mathf.Min(outer, WallAt(i, side) - 1.4f);
                if (outer - inner < 4f) continue;
                var mesh = side > 0 ? TestSceneBuilder.BuildRibbon(pts, a, b, false, inner, outer, 0.016f, 0.016f)
                                    : TestSceneBuilder.BuildRibbon(pts, a, b, false, -outer, -inner, 0.016f, 0.016f);
                TestSceneBuilder.MeshObject("Runoff_" + a, parent, mesh, road, true);
                float mid = (inner + outer) * 0.5f, len = Mathf.Min(24f, (outer - inner) * 6f);
                for (int i = a + 8; i < b - 8; i += 16)
                {
                    Vector3 r = Right(i), tan = Tan(i);
                    paint.Quad(pts[i] + r * side * mid + Vector3.up * 0.03f, tan * side, -r * side, len, len / 8f, i / 16, true);
                }
            }
            paint.Build("RunoffLogos", parent, BrandKit.PaintMat());
        }

        // ------------------------------------------------------------------ Sao Paulo skyline
        static void BuildSkyline(Transform parent)
        {
            var tex = WindowTexture();
            var mat = EnvironmentBuilder.GetMat($"{Root}/Materials/Skyline_Building.mat", "Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Smoothness", 0.55f);
            mat.EnableKeyword("_EMISSION"); mat.SetTexture("_EmissionMap", tex); mat.SetColor("_EmissionColor", new Color(0.35f, 0.33f, 0.3f));
            EditorUtility.SetDirty(mat);
            Vector3 c = Vector3.zero; foreach (var p in pts) c += p; c /= N;
            var rnd = new System.Random(19);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int placed = 0;
            for (int attempt = 0; attempt < 6000 && placed < 420; attempt++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float dist = 650f + (float)rnd.NextDouble() * 1100f;
                var pos = new Vector3(c.x + Mathf.Cos(ang) * dist, 0f, c.z + Mathf.Sin(ang) * dist * 1.1f);
                Nearest(pos, 10, out float d);
                if (d < 300f) continue;
                if (lakeRadius > 0f && Vector2.Distance(new Vector2(pos.x, pos.z), lakeCentre) < lakeRadius + 40f) continue;
                float far = Mathf.Clamp01((d - 300f) / 900f);
                float h = Mathf.Lerp(18f, 120f, Mathf.Pow((float)rnd.NextDouble(), 1.6f)) * Mathf.Lerp(0.6f, 1.15f, far);
                float w = 16f + (float)rnd.NextDouble() * 22f, dp = 16f + (float)rnd.NextDouble() * 22f;
                pos.y = GroundHeight(pos.x, pos.z) - 1f;
                AddBlock(verts, uvs, tris, pos, w, dp, h, (float)rnd.NextDouble() * 90f, rnd.Next(4));
                placed++;
            }
            var mesh = new Mesh { name = "Skyline", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var g = EnvironmentBuilder.Obj("Skyline", parent, mesh, mat);
            g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log($"[OWR] Interlagos skyline: {placed} buildings");
        }

        /// <summary>Box building whose UVs tile the window texture once per floor (3.4 m) and bay (4 m); variant picks a facade style column.</summary>
        static void AddBlock(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 basePos, float w, float d, float h, float yaw, int variant)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 X = q * Vector3.right * (w * 0.5f), Z = q * Vector3.forward * (d * 0.5f), Y = Vector3.up * h;
            Vector3[] corners = { -X - Z, X - Z, X + Z, -X + Z };
            float u0 = variant * 0.25f;
            for (int f = 0; f < 4; f++)
            {
                Vector3 a = basePos + corners[f], b = basePos + corners[(f + 1) % 4];
                float len = Vector3.Distance(a, b);
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(b + Y); v.Add(a + Y);
                float bays = len / 4f, floors = h / 3.4f;
                // tile within the variant's quarter of the texture using the fractional repeat trick: u spans bays/8 of a 0.25 column
                uv.Add(new Vector2(u0, 0f)); uv.Add(new Vector2(u0 + bays * 0.25f / 8f, 0f));
                uv.Add(new Vector2(u0 + bays * 0.25f / 8f, floors / 32f)); uv.Add(new Vector2(u0, floors / 32f));
                t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            int r0 = v.Count;   // roof
            foreach (var cn in corners) { v.Add(basePos + cn + Y); uv.Add(new Vector2(0.99f, 0.99f)); }
            t.AddRange(new[] { r0, r0 + 3, r0 + 2, r0, r0 + 2, r0 + 1 });
        }

        /// <summary>Facade atlas: 4 styles side by side (concrete, glass, brick-ish, white), 8 bays x 32 floors each, some windows lit.</summary>
        static Texture2D WindowTexture()
        {
            string path = $"{Root}/Textures/Skyline_Windows.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            const int W = 512, H = 1024;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var rnd = new System.Random(5);
            Color[] wall = { new Color(0.46f, 0.46f, 0.45f), new Color(0.2f, 0.25f, 0.3f), new Color(0.52f, 0.42f, 0.36f), new Color(0.74f, 0.74f, 0.72f) };
            Color[] glass = { new Color(0.12f, 0.14f, 0.16f), new Color(0.16f, 0.22f, 0.28f), new Color(0.13f, 0.12f, 0.12f), new Color(0.18f, 0.2f, 0.22f) };
            var px = new Color[W * H];
            for (int s = 0; s < 4; s++)
                for (int bx = 0; bx < 8; bx++)
                    for (int fy = 0; fy < 32; fy++)
                    {
                        bool lit = rnd.NextDouble() < 0.16;
                        Color win = lit ? new Color(1f, 0.86f, 0.6f) * (0.8f + (float)rnd.NextDouble() * 0.4f) : glass[s] * (0.85f + (float)rnd.NextDouble() * 0.3f);
                        for (int x = 0; x < 16; x++)
                            for (int y = 0; y < 32; y++)
                            {
                                int gx = s * 128 + bx * 16 + x, gy = fy * 32 + y;
                                bool isWin = s == 1 ? (y > 3 && x > 0) : (x > 3 && x < 13 && y > 8 && y < 27);
                                px[gy * W + gx] = isWin ? win : wall[s] * (0.95f + 0.05f * Mathf.PerlinNoise(gx * 0.1f, gy * 0.1f));
                            }
                    }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 4; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ trees and palms, merged per area
        static void BuildVegetation(Transform parent, TestSceneBuilder.TracksideMats m, List<Vector4> keepOut)
        {
            var palmLeaves = EnvironmentBuilder.GetMat($"{Root}/Materials/Palm_Leaves.mat", "Universal Render Pipeline/Lit");
            palmLeaves.SetColor("_BaseColor", new Color(0.13f, 0.24f, 0.1f)); palmLeaves.SetFloat("_Smoothness", 0.35f); palmLeaves.SetFloat("_Cull", 0f);
            palmLeaves.doubleSidedGI = true;
            var palm = PalmMesh();
            var trees = new[] { EnvironmentBuilder.TreeMesh(0), EnvironmentBuilder.TreeMesh(1), EnvironmentBuilder.TreeMesh(2), palm };
            var mats = new[] { new[] { m.bark, m.leaves }, new[] { m.bark, m.leaves }, new[] { m.bark, m.leaves }, new[] { m.bark, palmLeaves } };
            Vector3 mn = pts[0], mx = pts[0];
            foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            var rnd = new System.Random(11);
            // per-chunk combine lists: [chunk][submesh]
            var chunks = new Dictionary<(int, int, int), (List<CombineInstance> a, List<CombineInstance> b)>();
            int placed = 0;
            for (int attempt = 0; attempt < 30000 && placed < 1400; attempt++)
            {
                var p = new Vector3(Mathf.Lerp(mn.x - 380f, mx.x + 380f, (float)rnd.NextDouble()), 0f, Mathf.Lerp(mn.z - 380f, mx.z + 380f, (float)rnd.NextDouble()));
                int ni = Nearest(p, 8, out float d);
                float clear = ni >= 0 ? Mathf.Max(wl[ni], wr[ni]) + 10f : 0f;
                if (ni >= 0 && InPit(ni)) clear = 60f;
                if (d < clear) continue;
                if (lakeRadius > 0f && Vector2.Distance(new Vector2(p.x, p.z), lakeCentre) < lakeRadius + 6f) continue;
                bool blocked = false;
                foreach (var ko in keepOut) if (new Vector2(p.x - ko.x, p.z - ko.y).magnitude < ko.z) { blocked = true; break; }
                if (blocked) continue;
                // clumps: more trees in the infield and the hills, fewer in the open
                if (Mathf.PerlinNoise(p.x * 0.006f, p.z * 0.006f) < 0.38f) continue;
                int v = rnd.NextDouble() < 0.32 ? 3 : rnd.Next(3);
                p.y = GroundHeight(p.x, p.z) - 0.2f;
                float sc = (0.85f + (float)rnd.NextDouble() * 0.6f) * (v == 3 ? 1.1f : 1f);
                var mtx = Matrix4x4.TRS(p, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), new Vector3(sc, sc * (0.85f + (float)rnd.NextDouble() * 0.4f), sc));
                var key = (Mathf.FloorToInt(p.x / 250f), Mathf.FloorToInt(p.z / 250f), v == 3 ? 1 : 0);
                if (!chunks.TryGetValue(key, out var lists)) chunks[key] = lists = (new List<CombineInstance>(), new List<CombineInstance>());
                lists.a.Add(new CombineInstance { mesh = trees[v], subMeshIndex = 0, transform = mtx });
                lists.b.Add(new CombineInstance { mesh = trees[v], subMeshIndex = 1, transform = mtx });
                placed++;
            }
            var root = new GameObject("Vegetation").transform; root.SetParent(parent, false);
            int idx = 0;
            foreach (var kv in chunks)
            {
                var a = new Mesh { indexFormat = IndexFormat.UInt32 }; a.CombineMeshes(kv.Value.a.ToArray(), true, true);
                var b = new Mesh { indexFormat = IndexFormat.UInt32 }; b.CombineMeshes(kv.Value.b.ToArray(), true, true);
                var mesh = new Mesh { name = "Trees_" + idx, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(new[] { new CombineInstance { mesh = a }, new CombineInstance { mesh = b } }, false, false);
                var g = EnvironmentBuilder.Obj("Trees_" + idx++, root, mesh, null);
                g.GetComponent<MeshRenderer>().sharedMaterials = kv.Key.Item3 == 1 ? mats[3] : mats[0];
            }
            Debug.Log($"[OWR] Interlagos vegetation: {placed} trees in {idx} chunks");
        }

        /// <summary>Low-poly palm: curved, tapering trunk and a crown of drooping fronds (double-sided quads).</summary>
        static Mesh PalmMesh()
        {
            string path = $"{Root}/Meshes/Palm.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            var k = new MeshKit(2);
            float h = 11f;
            var trunk = new List<Vector3>();
            for (int s = 0; s <= 6; s++) { float t = s / 6f; trunk.Add(new Vector3(Mathf.Sin(t * 1.4f) * 1.1f, t * h, 0f)); }
            k.Tube(0, trunk, 0.22f, 7, 1f, false, t => Mathf.Lerp(1f, 0.6f, t));
            Vector3 top = trunk[trunk.Count - 1];
            for (int f = 0; f < 9; f++)
            {
                float a = f / 9f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.55f;
                Vector3 mid = top + dir * 2.4f + Vector3.up * 0.6f, tip = top + dir * 4.6f + Vector3.down * 1.3f;
                int q0 = k.V(top + side * 0.3f, new Vector2(0f, 0f)), q1 = k.V(top - side * 0.3f, new Vector2(1f, 0f));
                int q2 = k.V(mid - side, new Vector2(1f, 0.5f)), q3 = k.V(mid + side, new Vector2(0f, 0.5f));
                int q4 = k.V(tip - side * 0.1f, new Vector2(1f, 1f)), q5 = k.V(tip + side * 0.1f, new Vector2(0f, 1f));
                k.Quad(1, q0, q1, q2, q3, Vector3.up);
                k.Quad(1, q3, q2, q4, q5, Vector3.up);
            }
            var mesh = k.Build("Palm");
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
