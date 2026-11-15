using System.Collections.Generic;
using OWR.Race;
using OWR.UI;
using OWR.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OWR.EditorTools
{
    /// <summary>Turns the test scene into a race: path, painted start/finish + grid, start-light gantry, AI field, managers, HUD.</summary>
    public static class RaceBuilder
    {
        /// <summary>Name of the track being built (set by TestSceneBuilder.BuildScene); keeps world-space mesh assets per track.</summary>
        public static string TrackTag = "Classic";

        const string Root = "Assets/_Game";
        public const int StartIndex = 60;   // centreline sample used as the start/finish line
        const int CarLayer = 2;

        struct Driver { public string name, code, team; public Color paint, accent; public int number; public float skill; }

        // 11 rivals, every car its own colours, number and title sponsor (all names and brands invented)
        static readonly Driver[] Field =
        {
            new Driver { name = "Ilya Varga",     code = "VAR", team = "Vanta",    paint = new Color(0.05f, 0.2f, 0.65f),  accent = new Color(0.95f, 0.95f, 0.95f), number = 4,  skill = 0.96f },
            new Driver { name = "Marco Bellini",  code = "BEL", team = "Kairo",    paint = new Color(0.55f, 0.03f, 0.05f), accent = new Color(0.95f, 0.95f, 0.95f), number = 7,  skill = 0.99f },
            new Driver { name = "Theo Lund",      code = "LUN", team = "Solenne",  paint = new Color(0.35f, 0.7f, 0.1f),   accent = new Color(0.05f, 0.05f, 0.05f), number = 11, skill = 0.94f },
            new Driver { name = "Rafael Okoye",   code = "OKO", team = "Nordax",   paint = new Color(0.02f, 0.35f, 0.18f), accent = new Color(0.95f, 0.75f, 0.2f),  number = 22, skill = 0.97f },
            new Driver { name = "Kenji Arata",    code = "ARA", team = "Halcyon",  paint = new Color(0.85f, 0.15f, 0.45f), accent = new Color(0.95f, 0.95f, 0.95f), number = 9,  skill = 0.95f },
            new Driver { name = "Nils Kester",    code = "KES", team = "Meridian", paint = new Color(0.65f, 0.67f, 0.7f),  accent = new Color(0.1f, 0.8f, 0.9f),    number = 3,  skill = 0.96f },
            new Driver { name = "Dario Morel",    code = "MOR", team = "Braven",   paint = new Color(0.03f, 0.03f, 0.035f), accent = new Color(0.95f, 0.75f, 0.2f), number = 33, skill = 0.93f },
            new Driver { name = "Lukas Severin",  code = "SEV", team = "Orbita",   paint = new Color(0.95f, 0.75f, 0.05f), accent = new Color(0.05f, 0.05f, 0.05f), number = 44, skill = 0.95f },
            new Driver { name = "Amara Dufresne", code = "DUF", team = "Quanta",   paint = new Color(0.3f, 0.08f, 0.5f),   accent = new Color(1f, 0.45f, 0.1f),     number = 8,  skill = 0.97f },
            new Driver { name = "Pablo Rana",     code = "RAN", team = "Altus",    paint = new Color(0.25f, 0.55f, 0.85f), accent = new Color(1f, 0.4f, 0.65f),    number = 12, skill = 0.94f },
            new Driver { name = "Mira Kowal",     code = "KOW", team = "Veltro",   paint = new Color(0.85f, 0.85f, 0.87f), accent = new Color(0.85f, 0.08f, 0.1f),  number = 27, skill = 0.95f },
        };
        const int PlayerSlot = 7; // starts P8 of 12

        public static void Setup(GameObject player, CarController ctrl, List<Vector3> pts, Transform trackRoot, float roadWidth, Transform spawn, int startIndex = StartIndex)
        {
            // ---------------- path (index 0 = start line) ----------------
            int n = pts.Count;
            var rotated = new Vector3[n];
            for (int i = 0; i < n; i++) rotated[i] = pts[(i + startIndex) % n];
            var path = trackRoot.gameObject.AddComponent<TrackPath>();
            path.points = rotated;
            path.roadWidth = roadWidth;
            path.Build();
            float L = path.Length;

            PaintStartFinish(path, trackRoot, roadWidth);
            var lights = BuildStartLights(path, trackRoot, roadWidth);
            BuildFlagStand(path, trackRoot, roadWidth);

            // ---------------- grid poses ----------------
            var poses = new List<(Vector3, Quaternion)>();
            for (int k = 0; k < Field.Length + 1; k++)
            {
                float d = L - 9f - k * 8.5f;
                float lat = (k % 2 == 0) ? -2.6f : 2.6f;
                Vector3 p = path.PointAt(d, lat) + Vector3.up * 0.12f;
                poses.Add((p, Quaternion.LookRotation(path.TangentAt(d), Vector3.up)));
                PaintGridBox(path, trackRoot, d, lat);
            }

            // ---------------- race manager ----------------
            var rmGo = new GameObject("RaceManager");
            var rm = rmGo.AddComponent<RaceManager>();
            rm.path = path;
            rm.totalLaps = 8;
            rm.lights = lights;

            player.transform.SetPositionAndRotation(poses[PlayerSlot].Item1, poses[PlayerSlot].Item2);
            spawn.SetPositionAndRotation(poses[PlayerSlot].Item1, poses[PlayerSlot].Item2);
            var entries = new List<RaceManager.Entry>();
            var playerEntry = new RaceManager.Entry { car = ctrl, driver = "You", code = "YOU", team = "Vanta", teamColor = new Color(1f, 0.38f, 0.06f), isPlayer = true };

            // ---------------- AI field (clones of the player car) ----------------
            int slot = 0;
            for (int i = 0; i < Field.Length; i++)
            {
                if (slot == PlayerSlot) { entries.Add(playerEntry); slot++; }
                var dr = Field[i];
                var ai = Object.Instantiate(player);
                ai.name = "AI_" + dr.code;
                ai.transform.SetPositionAndRotation(poses[slot].Item1, poses[slot].Item2);
                StripPlayerOnly(ai);
                ApplyLivery(ai, dr);
                var actrl = ai.GetComponent<CarController>();
                actrl.spawnPoint = null;
                var aid = ai.AddComponent<AIDriver>();
                aid.car = actrl; aid.path = path; aid.skill = dr.skill; aid.laneOffset = (slot % 2 == 0) ? -1.2f : 1.2f;
                Color tower = dr.paint.maxColorComponent < 0.1f ? dr.accent : dr.paint;
                entries.Add(new RaceManager.Entry { car = actrl, driver = dr.name, code = dr.code, team = dr.team, teamColor = tower, ai = aid });
                slot++;
            }
            if (!entries.Contains(playerEntry)) entries.Add(playerEntry);
            rm.entries = entries;

            // ---------------- commentary ----------------
            var comGo = new GameObject("Commentary");
            var com = comGo.AddComponent<Commentary>();
            var src = comGo.AddComponent<AudioSource>();
            src.spatialBlend = 0f; src.volume = 1f; src.priority = 0; src.playOnAwake = false;
            com.source = src;
            (string key, string text)[] lines =
            {
                ("intro", "Welcome to a wet and dramatic afternoon here at the circuit. The grip is low, the spray is heavy, and the crowd are on their feet."),
                ("lights", "Five red lights. Here we go."),
                ("go", "And it's lights out, and away we go!"),
                ("gain", "What a move! That's a place gained!"),
                ("gain2", "Brave overtake in these conditions. Up another spot!"),
                ("lose", "Oh, and a position lost there."),
                ("lead", "And now, he leads the race!"),
                ("fastest", "Fastest lap of the race! Purple sectors all round."),
                ("halfway", "We're at the halfway point, and the rain just keeps on falling."),
                ("finallap", "Final lap! One more time around, in these treacherous conditions."),
                ("spin", "Oh, he's around! A big moment in the wet!"),
                ("contact", "Contact! That's going to hurt."),
                ("win", "And it's victory! A brilliant drive in the rain!"),
                ("podium", "It's a podium finish! A great result today."),
                ("finish", "Across the line, and the chequered flag is out."),
                ("crash", "Oh no! Huge accident! That's the end of his race!"),
                ("aicrash", "Big crash out there! That car is done, and the debris is everywhere!"),
            };
            // neural-voice lines (Tools/gen_commentary.py): every mp3 + its subtitle from lines.json
            var list = new List<Commentary.Line>();
            string manifest = System.IO.File.ReadAllText(Root + "/Audio/Commentary/lines.json");
            foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(manifest, @"""([a-z_0-9]+)"":\s*""((?:[^""\\]|\\.)*)"""))
            {
                string key = mt.Groups[1].Value;
                list.Add(new Commentary.Line { key = key, text = System.Text.RegularExpressions.Regex.Unescape(mt.Groups[2].Value), clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/Commentary/{key}.mp3") });
            }
            if (lines.Length < 0) Debug.Log("legacy line table unused");
            com.lines = list.ToArray();

            // ---------------- explosions ----------------
            var fx = new GameObject("ExplosionFX").AddComponent<ExplosionFX>();
            fx.fireMat = ParticleMat("FX_Fire", true, new Color(1f, 0.7f, 0.35f, 1f));
            fx.sparkMat = ParticleMat("FX_Sparks", true, new Color(1f, 0.8f, 0.5f, 1f));
            fx.smokeMat = ParticleMat("FX_Smoke", false, new Color(0.25f, 0.24f, 0.23f, 0.8f));
            fx.debrisMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Car_Carbon.mat");
            var scorched = EnvironmentBuilder.GetMat(Root + "/Materials/FX_Scorched.mat", "Universal Render Pipeline/Lit");
            scorched.SetColor("_BaseColor", new Color(0.035f, 0.03f, 0.028f)); scorched.SetFloat("_Smoothness", 0.25f);
            fx.scorchedMat = scorched;
            fx.scorchDecalMat = LiveryBuilder.DecalMat(ScorchTexture(), "Scorch", 0);
            fx.puffMesh = PuffMesh();
            var fireMesh = EnvironmentBuilder.GetMat(Root + "/Materials/FX_FirePuff.mat", "Universal Render Pipeline/Particles/Unlit");
            fireMesh.SetColor("_BaseColor", new Color(3.2f, 2.6f, 2.2f, 1f));   // HDR: blooms like flame
            fireMesh.SetFloat("_Surface", 0f); EditorUtility.SetDirty(fireMesh);
            var smokeMesh = EnvironmentBuilder.GetMat(Root + "/Materials/FX_SmokePuff.mat", "Universal Render Pipeline/Particles/Lit");
            smokeMesh.SetColor("_BaseColor", Color.white); smokeMesh.SetFloat("_Surface", 0f); smokeMesh.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(smokeMesh);
            fx.fireMeshMat = fireMesh; fx.smokeMeshMat = smokeMesh;

            // ---------------- HUD ----------------
            var hudGo = new GameObject("RaceHUD");
            var hud = hudGo.AddComponent<RaceHUD>();
            hud.titleFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/BebasNeue-Regular.ttf");
            hud.uiFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/BarlowCondensed-SemiBold.ttf");
            hud.numFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/BarlowCondensed-BoldItalic.ttf");
            hud.commentary = com;

            // ---------------- main menu over the live scene: drone camera + centred title UI + theme ----------------
            var droneGo = new GameObject("DroneCamera", typeof(Camera), typeof(AudioListener));
            var drone = droneGo.GetComponent<Camera>();
            drone.nearClipPlane = 0.1f; drone.farClipPlane = 4000f;
            var dd = droneGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            dd.renderPostProcessing = true;
            dd.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            droneGo.SetActive(false);
            var menuGo = new GameObject("MainMenu");
            var menu = menuGo.AddComponent<MainMenu>();
            menu.titleFont = hud.titleFont;
            menu.uiFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/BarlowCondensed-Medium.ttf");
            if (!AssetDatabase.LoadAssetAtPath<AudioClip>(MusicComposer.OutPath)) MusicComposer.Compose();
            var mus = menuGo.AddComponent<AudioSource>();
            mus.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicComposer.OutPath);
            mus.loop = true; mus.playOnAwake = false; mus.spatialBlend = 0f; mus.volume = 0f; mus.priority = 0;
            menu.music = mus;
            var md = menuGo.AddComponent<MenuDirector>();
            md.race = rm; md.menu = menu; md.droneCam = drone; md.path = path;
            md.cameras = Object.FindFirstObjectByType<OWR.CameraRig.CameraDirector>();
        }

        /// <summary>Unique colours, number and title sponsor for one rival car.</summary>
        static void ApplyLivery(GameObject ai, Driver dr)
        {
            var basePaint = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Car_Paint.mat");
            var baseAccent = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Car_Accent.mat");
            var paint = new Material(basePaint); paint.SetColor("_BaseColor", dr.paint);
            var accent = new Material(baseAccent); accent.SetColor("_BaseColor", dr.accent);
            string pp = $"{Root}/Materials/Livery/Paint_{dr.code}.mat", ap = $"{Root}/Materials/Livery/Accent_{dr.code}.mat";
            AssetDatabase.DeleteAsset(pp); AssetDatabase.CreateAsset(paint, pp);
            AssetDatabase.DeleteAsset(ap); AssetDatabase.CreateAsset(accent, ap);
            foreach (var r in ai.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == basePaint) { mats[i] = paint; changed = true; }
                    else if (mats[i] == baseAccent) { mats[i] = accent; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            var number = LiveryBuilder.NumberMat(dr.number, dr.accent);
            var brand = LiveryBuilder.BrandMat(dr.team, dr.accent);
            var livery = ai.transform.Find("Livery");
            if (!livery) return;
            foreach (var dp in livery.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true))
            {
                if (dp.name.Contains("Number")) dp.material = number;
                else if (dp.name.Contains("Kairo")) dp.material = brand;
            }
        }

        /// <summary>Faceted, slightly lumpy icosphere (unshared vertices, per-face normals + per-face shade in vertex colour).</summary>
        static Mesh PuffMesh()
        {
            string p = Root + "/Meshes/FX_Puff.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (existing) return existing;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                        3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            // one subdivision -> 80 faces: chunky but round enough
            var faces = new List<int>(); var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                v.Add((v[a] + v[b]) * 0.5f); mid[key] = v.Count - 1; return v.Count - 1;
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                faces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            var rnd = new System.Random(7);
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized * (0.5f * (0.86f + 0.28f * (float)rnd.NextDouble()));
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
            for (int i = 0; i < faces.Count; i += 3)
            {
                Vector3 a = v[faces[i]], b = v[faces[i + 1]], c = v[faces[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, a + b + c) < 0f) { var tmp = b; b = c; c = tmp; n = -n; }
                // fake key light from above for the unlit fire: faces pointing up are brighter
                float shade = 0.72f + 0.28f * Mathf.Clamp01(n.y * 0.5f + 0.5f) + 0.06f * (float)rnd.NextDouble();
                var sc = new Color(shade, shade, shade, 1f);
                int s0 = pos.Count;
                pos.Add(a); pos.Add(b); pos.Add(c);
                nrm.Add(n); nrm.Add(n); nrm.Add(n);
                col.Add(sc); col.Add(sc); col.Add(sc);
                tri.Add(s0); tri.Add(s0 + 1); tri.Add(s0 + 2);
            }
            var mesh = new Mesh { name = "FX_Puff" };
            mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetColors(col); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, p);
            return mesh;
        }

        /// <summary>Burnt, sooty blotch with ragged edges for the track decal.</summary>
        static Texture2D ScorchTexture()
        {
            string p = Root + "/Textures/Scorch.asset";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t) return t;
            const int S = 256;
            t = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "Scorch", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S - 0.5f, v = y / (float)S - 0.5f;
                    float r = Mathf.Sqrt(u * u + v * v) * 2f;
                    float ang = Mathf.Atan2(v, u);
                    float edge = 0.62f + 0.22f * Mathf.PerlinNoise(Mathf.Cos(ang) * 2f + 5f, Mathf.Sin(ang) * 2f + 5f) + 0.1f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                    float a = Mathf.Clamp01((edge - r) * 4f) * (0.75f + 0.25f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f));
                    float soot = 0.03f + 0.05f * Mathf.PerlinNoise(x * 0.2f + 9f, y * 0.2f);
                    t.SetPixel(x, y, new Color(soot, soot * 0.9f, soot * 0.8f, a * 0.92f));
                }
            t.Apply(true);
            AssetDatabase.CreateAsset(t, p);
            return t;
        }

        static Material ParticleMat(string name, bool additive, Color c)
        {
            var m = EnvironmentBuilder.GetMat($"{Root}/Materials/{name}.mat", "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", EnvironmentBuilder.SoftDot());
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.SrcAlpha : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.DisableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Removes the player-only parts (cameras, cockpit audio, reflection probe) and makes the engine a 3D source.</summary>
        static void StripPlayerOnly(GameObject ai)
        {
            foreach (var n in new[] { "CockpitCamera", "TCamCamera", "ReflectionProbe", "Audio_Wind", "Audio_Tyres", "Audio_Rain" })
            {
                var t = ai.transform.Find(n);
                if (t) Object.DestroyImmediate(t.gameObject);
            }
            // rivals' helmet + shoulders must be visible from our cockpit (the player's own are hidden in cockpit view)
            foreach (var n in new[] { "HelmetPivot", "DriverShoulders" })
            {
                var t = ai.transform.Find(n);
                if (t) foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = CarLayer;
            }
            // cockpit-only machinery nobody can see from outside: hand/finger IK, wheel buttons, the wheel's live
            // display (a canvas rebuilt every frame) and the celebration. Saves several ms per frame with a full grid.
            foreach (var c in ai.GetComponentsInChildren<OWR.Cockpit.DriverArms>(true)) Object.DestroyImmediate(c);
            foreach (var c in ai.GetComponentsInChildren<OWR.Cockpit.WheelControls>(true)) Object.DestroyImmediate(c);
            foreach (var c in ai.GetComponentsInChildren<OWR.Cockpit.Celebration>(true)) Object.DestroyImmediate(c);
            foreach (var c in ai.GetComponentsInChildren<OWR.Cockpit.HandGrip>(true)) Object.DestroyImmediate(c);
            foreach (var c in ai.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);
            foreach (var c in ai.GetComponentsInChildren<OWR.AudioFX.ShiftBeep>(true)) Object.DestroyImmediate(c);
            var src = ai.GetComponent<AudioSource>();
            if (src)
            {
                src.spatialBlend = 1f; src.minDistance = 6f; src.maxDistance = 240f; src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0.6f; src.priority = 64;
            }
            var eng = ai.GetComponent<OWR.AudioFX.EngineAudio>();
            if (eng) { eng.volume = 0.7f; eng.harmonics = 4; eng.cullDistance = 160f; }
        }

        // ================================================================= painted start/finish and grid
        static void PaintStartFinish(TrackPath path, Transform parent, float width)
        {
            var tex = Checker();
            var mat = EnvironmentBuilder.GetMat(Root + "/Materials/StartFinish.mat", "Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
            mat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f));
            mat.SetFloat("_Smoothness", 0.82f);
            var m = Strip(path, 0f, 2.4f, width, 0.031f, 14f, 2f);
            var g = new GameObject("StartFinishLine");
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void PaintGridBox(TrackPath path, Transform parent, float d, float lat)
        {
            var mat = EnvironmentBuilder.GetMat(Root + "/Materials/GridPaint.mat", "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.83f));
            mat.SetFloat("_Smoothness", 0.8f);
            // front bar + two short side legs, like a real grid slot
            var root = new GameObject("GridSlot");
            root.transform.SetParent(parent, false);
            AddPaint(root.transform, path, d + 0.9f, lat, 2.6f, 0.25f, mat);
            AddPaint(root.transform, path, d + 0.2f, lat - 1.2f, 0.2f, 1.2f, mat);
            AddPaint(root.transform, path, d + 0.2f, lat + 1.2f, 0.2f, 1.2f, mat);
        }

        static void AddPaint(Transform parent, TrackPath path, float d, float lat, float w, float len, Material mat)
        {
            Vector3 c = path.PointAt(d, lat) + Vector3.up * 0.031f;
            Vector3 f = path.TangentAt(d), r = Vector3.Cross(Vector3.up, f);
            var m = new Mesh { name = "Paint" };
            m.vertices = new[] { c - r * w / 2 - f * len / 2, c + r * w / 2 - f * len / 2, c + r * w / 2 + f * len / 2, c - r * w / 2 + f * len / 2 };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            m.RecalculateNormals(); m.RecalculateBounds();
            var g = new GameObject("Paint");
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Flat strip across the road at distance d (length along the track), UV tiled by tilesU x tilesV.</summary>
        static Mesh Strip(TrackPath path, float d, float len, float width, float y, float tilesU, float tilesV)
        {
            Vector3 c = path.PointAt(d) + Vector3.up * y;
            Vector3 f = path.TangentAt(d), r = Vector3.Cross(Vector3.up, f);
            var m = new Mesh { name = "Strip" };
            m.vertices = new[] { c - r * width / 2 - f * len / 2, c + r * width / 2 - f * len / 2, c + r * width / 2 + f * len / 2, c - r * width / 2 + f * len / 2 };
            m.uv = new[] { new Vector2(0, 0), new Vector2(tilesU, 0), new Vector2(tilesU, tilesV), new Vector2(0, tilesV) };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            string p = Root + "/Meshes/StartFinishStrip" + (TrackTag == "Classic" ? "" : "_" + TrackTag) + ".asset"; AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(m, p);   // per track: the strip is in world space
            return m;
        }

        static Texture2D Checker()
        {
            string p = Root + "/Textures/Checker.asset";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t) return t;
            t = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = "Checker", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool w = ((x / 32) + (y / 32)) % 2 == 0;
                    t.SetPixel(x, y, w ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.06f, 0.06f, 0.07f));
                }
            t.Apply(true);
            AssetDatabase.CreateAsset(t, p);
            return t;
        }

        // ================================================================= start-light gantry with real lights
        static StartLights BuildStartLights(TrackPath path, Transform parent, float width)
        {
            var steel = EnvironmentBuilder.GetMat(Root + "/Materials/Pole_Steel.mat", "Universal Render Pipeline/Lit");
            var housing = EnvironmentBuilder.GetMat(Root + "/Materials/Gantry_Housing.mat", "Universal Render Pipeline/Lit");
            housing.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f)); housing.SetFloat("_Smoothness", 0.7f);
            var lamp = EnvironmentBuilder.GetMat(Root + "/Materials/Gantry_Lamp.mat", "Universal Render Pipeline/Lit");
            lamp.SetColor("_BaseColor", new Color(0.12f, 0.02f, 0.02f)); lamp.SetFloat("_Smoothness", 0.9f);
            lamp.EnableKeyword("_EMISSION"); lamp.SetColor("_EmissionColor", Color.black);
            lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            var g = new GameObject("StartLightGantry");
            g.transform.SetParent(parent, false);
            g.transform.position = path.PointAt(-1.5f);
            g.transform.rotation = Quaternion.LookRotation(-path.TangentAt(0f), Vector3.up); // lamps face the grid
            float half = width / 2f + 3f;
            for (int s = -1; s <= 1; s += 2) Cube(g.transform, steel, new Vector3(s * half, 3.6f, 0f), new Vector3(0.45f, 7.2f, 0.45f));
            Cube(g.transform, steel, new Vector3(0f, 6.9f, 0f), new Vector3(half * 2f + 0.45f, 0.5f, 0.5f));
            var sl = g.AddComponent<StartLights>();
            sl.columns1 = new Renderer[5]; sl.columns2 = new Renderer[5]; sl.columnLights = new Light[5];
            for (int i = 0; i < 5; i++)
            {
                float x = -2.4f + i * 1.2f;
                Cube(g.transform, housing, new Vector3(x, 6.0f, 0.05f), new Vector3(0.75f, 1.25f, 0.35f));
                sl.columns1[i] = Lens(g.transform, lamp, new Vector3(x, 6.28f, 0.24f));
                sl.columns2[i] = Lens(g.transform, lamp, new Vector3(x, 5.78f, 0.24f));
                var lg = new GameObject("Light" + i);
                lg.transform.SetParent(g.transform, false);
                lg.transform.localPosition = new Vector3(x, 6.0f, 0.9f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.08f, 0.04f); l.range = 14f; l.intensity = 0f; l.shadows = LightShadows.None;
                sl.columnLights[i] = l;
            }
            return sl;
        }

        /// <summary>Raised flag-marshal platform just past the line (left side) with a marshal who waves the chequered flag.</summary>
        static void BuildFlagStand(TrackPath path, Transform parent, float width)
        {
            var steel = EnvironmentBuilder.GetMat(Root + "/Materials/Pole_Steel.mat", "Universal Render Pipeline/Lit");
            var orange = EnvironmentBuilder.GetMat(Root + "/Materials/Marshal_Orange.mat", "Universal Render Pipeline/Lit");
            orange.SetColor("_BaseColor", new Color(1f, 0.42f, 0.05f)); orange.SetFloat("_Smoothness", 0.5f);
            var white = EnvironmentBuilder.GetMat(Root + "/Materials/Marshal_Helmet.mat", "Universal Render Pipeline/Lit");
            white.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.9f)); white.SetFloat("_Smoothness", 0.8f);
            var dark = EnvironmentBuilder.GetMat(Root + "/Materials/Marshal_Dark.mat", "Universal Render Pipeline/Lit");
            dark.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f)); dark.SetFloat("_Smoothness", 0.3f);
            var flagMat = EnvironmentBuilder.GetMat(Root + "/Materials/Flag_Chequered.mat", "Universal Render Pipeline/Lit");
            flagMat.SetTexture("_BaseMap", CheckerTexture()); flagMat.SetColor("_BaseColor", Color.white);
            flagMat.SetFloat("_Cull", 0f); flagMat.SetFloat("_Smoothness", 0.35f);
            flagMat.doubleSidedGI = true;
            EditorUtility.SetDirty(flagMat);

            // ~22 m before the line on the left, angled toward the oncoming cars so the driver sees him on the run in
            float d = path.Length - 22f, lat = -(width / 2f + 1.7f);
            Vector3 tan = path.TangentAt(d);
            Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
            var g = new GameObject("FlagStand");
            g.transform.SetParent(parent, false);
            g.transform.position = path.PointAt(d, lat);
            g.transform.rotation = Quaternion.LookRotation(Vector3.Slerp(right, -tan, 0.35f).normalized, Vector3.up);
            g.transform.localScale = Vector3.one * 1.35f;
            const float deck = 2.3f;
            Cube(g.transform, steel, new Vector3(0f, deck * 0.5f, -0.5f), new Vector3(0.25f, deck, 0.25f));
            Cube(g.transform, steel, new Vector3(0f, deck, 0f), new Vector3(1.6f, 0.12f, 1.6f));
            Cube(g.transform, steel, new Vector3(0f, deck + 0.55f, -0.78f), new Vector3(1.6f, 0.05f, 0.05f));   // rail
            for (int s = -1; s <= 1; s += 2) Cube(g.transform, steel, new Vector3(s * 0.78f, deck + 0.28f, -0.78f), new Vector3(0.05f, 0.55f, 0.05f));

            var marshal = new GameObject("FlagMarshal").transform;
            marshal.SetParent(g.transform, false); marshal.localPosition = new Vector3(0f, deck + 0.06f, 0.1f);
            // the marshal: a rigged low-poly figure (Quaternius Worker, CC0) in orange overalls; his left arm follows the flag pivot
            var fig = EnvironmentBuilder.MarshalFigure(marshal, orange, out var idleClip);
            Transform Bone(string n) { foreach (var t in fig.GetComponentsInChildren<Transform>()) if (t.name == n) return t; return null; }
            Transform upper = Bone("UpperArm.L"), lower = Bone("LowerArm.L"), wrist = Bone("Wrist.L");
            var shoulder = new GameObject("Shoulder").transform;
            shoulder.SetParent(marshal, false); shoulder.position = upper.position;
            float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, wrist.position);
            reach /= marshal.lossyScale.y;
            var pole = new GameObject("Pole").transform;
            pole.SetParent(shoulder, false); pole.localPosition = new Vector3(0f, -reach, 0f);
            Cube(pole, dark, new Vector3(0f, -0.55f, 0f), new Vector3(0.035f, 1.3f, 0.035f));
            var furled = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(furled.GetComponent<Collider>());
            furled.transform.SetParent(pole, false); furled.transform.localPosition = new Vector3(0f, -0.85f, 0f);
            furled.transform.localScale = new Vector3(0.09f, 0.4f, 0.09f);
            furled.GetComponent<Renderer>().sharedMaterial = flagMat;
            var clothGo = new GameObject("Cloth");
            clothGo.transform.SetParent(pole, false);
            clothGo.transform.localPosition = new Vector3(0f, -1.2f, 0f);
            clothGo.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);   // hangs from the tip back toward the hand
            var mf = clothGo.AddComponent<MeshFilter>();
            var mr = clothGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = flagMat;

            var flag = g.AddComponent<OWR.Track.ChequeredFlag>();
            flag.shoulder = shoulder; flag.pole = pole; flag.cloth = mf; flag.furled = furled;
            var rig = fig.AddComponent<OWR.Track.MarshalRig>();
            rig.idle = idleClip; rig.body = fig.transform; rig.upperArm = upper; rig.lowerArm = lower; rig.wrist = wrist; rig.target = pole;
            foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        static Texture2D CheckerTexture()
        {
            string p = Root + "/Textures/Chequered.asset";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (t) return t;
            const int W = 128, H = 96, Sq = 16;
            t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Chequered", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    t.SetPixel(x, y, ((x / Sq + y / Sq) & 1) == 0 ? new Color(0.92f, 0.92f, 0.9f) : new Color(0.03f, 0.03f, 0.035f));
            t.Apply(true);
            AssetDatabase.CreateAsset(t, p);
            return t;
        }

        static void Cube(Transform parent, Material m, Vector3 pos, Vector3 size)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.transform.SetParent(parent, false);
            c.transform.localPosition = pos; c.transform.localScale = size;
            c.GetComponent<Renderer>().sharedMaterial = m;
        }

        static Renderer Lens(Transform parent, Material m, Vector3 pos)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(s.GetComponent<Collider>());
            s.transform.SetParent(parent, false);
            s.transform.localPosition = pos; s.transform.localScale = new Vector3(0.42f, 0.42f, 0.12f);
            var r = s.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        // ================================================================= mountains
        public static void BuildMountains(Vector3 centre, Transform parent)
        {
            (float radius, float hMin, float hMax, Color top, Color bottom, int seed)[] layers =
            {
                (3200f, 260f, 560f, new Color(0.56f, 0.6f, 0.65f), new Color(0.66f, 0.69f, 0.72f), 1),
                (2500f, 170f, 400f, new Color(0.45f, 0.5f, 0.55f), new Color(0.63f, 0.66f, 0.69f), 2),
                (1800f, 90f, 260f, new Color(0.33f, 0.38f, 0.42f), new Color(0.6f, 0.63f, 0.66f), 3),
            };
            var root = new GameObject("Mountains").transform;
            root.SetParent(parent, false);
            foreach (var l in layers)
            {
                var mat = EnvironmentBuilder.GetMat($"{Root}/Materials/Mountains_{l.seed}.mat", "OWR/Skyline");
                mat.SetColor("_ColorTop", l.top); mat.SetColor("_ColorBottom", l.bottom);
                mat.SetFloat("_HeightMin", 0f); mat.SetFloat("_HeightMax", l.hMax);
                var mesh = Ridge(l.radius, l.hMin, l.hMax, l.seed);
                string p = $"{Root}/Meshes/Mountains_{l.seed}.asset"; AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(mesh, p);
                var g = new GameObject("Ridge_" + l.seed);
                g.transform.SetParent(root, false);
                g.transform.position = centre;
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        /// <summary>Closed ring of jagged ridgeline (fractal peaks), base well below the horizon.</summary>
        public static Mesh Ridge(float radius, float hMin, float hMax, int seed, float arc = 360f, float arcStart = 0f)
        {
            int seg = Mathf.Max(64, Mathf.RoundToInt(arc * 1.5f));
            var v = new List<Vector3>(); var t = new List<int>();
            float o = seed * 37.7f;
            for (int i = 0; i <= seg; i++)
            {
                float a = (arcStart + arc * i / seg) * Mathf.Deg2Rad;
                float u = i / (float)seg * 12f;
                float h = Mathf.PerlinNoise(u + o, o) * 0.6f + Mathf.PerlinNoise(u * 3.1f + o, o * 2f) * 0.28f + Mathf.PerlinNoise(u * 9.3f + o, o * 3f) * 0.12f;
                h = Mathf.Lerp(hMin, hMax, Mathf.Pow(h, 1.4f));
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(dir * radius + Vector3.down * 80f);
                v.Add(dir * radius + Vector3.up * h);
            }
            for (int i = 0; i < seg; i++) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            var m = new Mesh { name = "Ridge_" + seed, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
