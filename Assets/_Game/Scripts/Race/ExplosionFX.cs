using System.Collections;
using OWR.Vehicle;
using UnityEngine;

namespace OWR.Race
{
    /// <summary>
    /// Layered, two-stage car explosion:
    ///  flash + flickering firelight -> turbulent fireball -> secondary fuel burst (0.25 s) -> shockwave ring on the
    ///  ground + radial wet-track spray -> glowing embers that fall and bounce -> thick smoke lit orange from below,
    ///  flying wheels/carbon shards, a scorch mark on the tarmac, and a wreck that keeps burning.
    /// Deep procedural boom with a second thump and debris clatter; camera shake by proximity.
    /// Materials are asset references so they ship in builds (incl. WebGL).
    /// </summary>
    public class ExplosionFX : MonoBehaviour
    {
        public static ExplosionFX Instance { get; private set; }
        public Material fireMat;     // additive soft particle
        public Material smokeMat;    // alpha-blended soft particle
        public Material sparkMat;    // additive (stretched)
        public Material debrisMat;   // carbon
        public Material scorchedMat; // wreck
        public Material scorchDecalMat; // decal on the track
        [Header("Low-poly look")]
        public Mesh puffMesh;           // faceted icosphere (per-face shade baked in vertex colours)
        public Material fireMeshMat;    // unlit, glowing (HDR base colour x particle colour)
        public Material smokeMeshMat;   // lit, opaque, flat-shaded
        /// <summary>Camera shake impulse read by the cockpit/chase cameras (decays).</summary>
        public static float Shake;

        AudioClip boom;

        void Awake() { Instance = this; boom = MakeBoom(); }
        void Update() { Shake = Mathf.MoveTowards(Shake, 0f, Time.deltaTime * 1.2f); }

        public void Explode(CarController car)
        {
            Vector3 p = car.transform.position + Vector3.up * 0.6f;
            Vector3 ground = car.transform.position;
            var root = new GameObject("Explosion");
            root.transform.position = p;

            // ---- light: white-hot flash, then a flickering orange firelight
            var lg = new GameObject("Flash"); lg.transform.SetParent(root.transform, false);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 45f; l.color = new Color(1f, 0.85f, 0.6f); l.intensity = 90f;
            StartCoroutine(FlashThenFlicker(l, 6f));

            // ---- stage 1: fireball (turbulent, white -> yellow -> orange -> deep red -> soot)
            var fire = Burst(root.transform, "Fireball", fireMat, 110, new Vector2(0.5f, 1.4f), new Vector2(1.6f, 4.2f), new Vector2(3f, 11f), -0.15f,
                new[] { new Color(1f, 0.98f, 0.9f), new Color(1f, 0.75f, 0.25f), new Color(1f, 0.35f, 0.05f), new Color(0.5f, 0.08f, 0.02f) },
                new[] { 1f, 1f, 0.8f, 0f }, 3.4f, false, 1.2f);
            AddNoise(fire, 2.2f, 0.9f);
            Puff(fire, true, 2.4f, 0.55f, 0.8f, 0f);

            // ---- shockwave ring + radial spray off the wet tarmac
            Ring(ground + Vector3.up * 0.15f, root.transform);
            var spray = Burst(root.transform, "Spray", smokeMat, 90, new Vector2(0.6f, 1.4f), new Vector2(0.6f, 1.6f), new Vector2(9f, 18f), 0.6f,
                new[] { new Color(0.85f, 0.88f, 0.92f, 0.65f), new Color(0.8f, 0.82f, 0.86f, 0.4f), new Color(0.8f, 0.82f, 0.86f, 0f) },
                new[] { 0.65f, 0.4f, 0f }, 2.6f, false, 0.6f);
            var ssh = spray.shape; ssh.shapeType = ParticleSystemShapeType.Circle; ssh.radius = 0.8f; ssh.radiusThickness = 0f; ssh.rotation = new Vector3(90f, 0f, 0f);
            spray.transform.position = ground + Vector3.up * 0.2f;

            // ---- sparks and glowing embers that fall and bounce
            Burst(root.transform, "Sparks", sparkMat, 160, new Vector2(0.3f, 1.2f), new Vector2(0.04f, 0.1f), new Vector2(14f, 36f), 1.3f,
                new[] { new Color(1f, 0.95f, 0.7f), new Color(1f, 0.55f, 0.15f), new Color(0.6f, 0.1f, 0f) }, new[] { 1f, 1f, 0f }, 1f, true, 1f);
            var embers = Burst(root.transform, "Embers", fireMat, 70, new Vector2(2f, 4f), new Vector2(0.06f, 0.16f), new Vector2(5f, 14f), 0.7f,
                new[] { new Color(1f, 0.8f, 0.4f), new Color(1f, 0.4f, 0.08f), new Color(0.4f, 0.05f, 0f) }, new[] { 1f, 1f, 0f }, 0.6f, false, 0.6f);
            var col = embers.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
            col.bounce = 0.35f; col.dampen = 0.4f; col.lifetimeLoss = 0.1f; col.quality = ParticleSystemCollisionQuality.Low;

            // ---- thick rolling smoke, lit orange from below at first
            var smoke = Burst(root.transform, "SmokeBurst", smokeMat, 45, new Vector2(3f, 6f), new Vector2(2.5f, 5f), new Vector2(2f, 6f), -0.08f,
                new[] { new Color(0.55f, 0.28f, 0.12f, 0.9f), new Color(0.16f, 0.15f, 0.14f, 0.85f), new Color(0.22f, 0.22f, 0.22f, 0f) },
                new[] { 0.9f, 0.8f, 0f }, 3.2f, false, 1.5f);
            AddNoise(smoke, 0.8f, 0.4f);
            Puff(smoke, false, 1.8f, 0.6f, 0.24f, 0.7f);

            // ---- stage 2: secondary fuel burst
            StartCoroutine(Secondary(root.transform, p + Random.insideUnitSphere * 0.8f, 0.25f));

            // ---- the wreck keeps burning: fire, smoke column, flickering light
            var burn = new GameObject("Burning"); burn.transform.SetParent(car.transform, false); burn.transform.localPosition = new Vector3(0f, 0.6f, -0.6f);
            var bf = Stream(burn.transform, "Fire", fireMat, 55f, new Vector2(0.4f, 0.9f), new Vector2(0.7f, 1.7f), 2.6f,
                new Color(1f, 0.65f, 0.2f), new Color(0.6f, 0.12f, 0.02f), 14f);
            AddNoise(bf, 1.5f, 1.2f);
            Puff(bf, true, 1.5f, 0.4f, 0.7f, 0f);
            var bs = Stream(burn.transform, "Smoke", smokeMat, 22f, new Vector2(4f, 7f), new Vector2(2f, 4f), 3.2f,
                new Color(0.12f, 0.11f, 0.11f, 0.8f), new Color(0.32f, 0.32f, 0.32f, 0f), 20f, 4.5f);
            AddNoise(bs, 0.6f, 0.3f);
            Puff(bs, false, 2.0f, 0.5f, 0.28f, 0f);
            var fl = new GameObject("Firelight"); fl.transform.SetParent(burn.transform, false);
            var flight = fl.AddComponent<Light>(); flight.type = LightType.Point; flight.color = new Color(1f, 0.5f, 0.15f); flight.range = 14f;
            StartCoroutine(Flicker(flight, 14f, 4f));

            // ---- scorch mark on the tarmac
            if (scorchDecalMat)
            {
                var sd = new GameObject("Scorch");
                sd.transform.position = ground + Vector3.up * 1f;
                sd.transform.rotation = Quaternion.LookRotation(Vector3.down, Random.onUnitSphere);
                var dp = sd.AddComponent<UnityEngine.Rendering.Universal.DecalProjector>();
                dp.material = scorchDecalMat;
                dp.size = new Vector3(6f, 6f, 2.5f);
                dp.pivot = new Vector3(0f, 0f, 1.25f);
                Destroy(sd, 90f);
            }

            // ---- debris: wheels fly off, carbon shards scatter
            var rb = car.Body;
            foreach (var w in car.wheels)
            {
                var model = w.spinVisual ? w.spinVisual.Find("Model") : null;
                if (!model) continue;
                var piece = Instantiate(model.gameObject, model.position, model.rotation);
                piece.name = "Debris_Wheel"; piece.layer = 0;
                piece.AddComponent<SphereCollider>().radius = 0.34f;
                var prb = piece.AddComponent<Rigidbody>(); prb.mass = 12f;
                prb.linearVelocity = rb.linearVelocity * 0.6f + (piece.transform.position - p).normalized * Random.Range(8f, 16f) + Vector3.up * Random.Range(5f, 12f);
                prb.angularVelocity = Random.insideUnitSphere * 22f;
                model.gameObject.SetActive(false);
                Destroy(piece, 30f);
            }
            for (int i = 0; i < 22; i++)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "Debris";
                shard.transform.position = p + Random.insideUnitSphere * 0.8f;
                shard.transform.localScale = new Vector3(Random.Range(0.05f, 0.5f), Random.Range(0.015f, 0.05f), Random.Range(0.08f, 0.6f));
                shard.GetComponent<Renderer>().sharedMaterial = debrisMat;
                var srb = shard.AddComponent<Rigidbody>(); srb.mass = 1.2f;
                srb.linearVelocity = rb.linearVelocity * 0.5f + Random.onUnitSphere * Random.Range(6f, 20f) + Vector3.up * 7f;
                srb.angularVelocity = Random.insideUnitSphere * 30f;
                Destroy(shard, 30f);
            }

            // ---- the car: thrown, scorched, out of the race
            rb.AddForce(Vector3.up * rb.mass * 7f + Random.insideUnitSphere * rb.mass * 3f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * rb.mass * 4f, ForceMode.Impulse);
            car.parkingBrake = true;
            car.input.inputBlocked = true;
            if (car.input.aiControlled) car.input.SetAI(0f, 1f, 0f);
            if (scorchedMat)
                foreach (var r in car.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.GetComponentInParent<Canvas>()) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = scorchedMat;
                    r.sharedMaterials = mats;
                }
            foreach (var d in car.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>()) d.enabled = false;
            var eng = car.GetComponent<AudioFX.EngineAudio>();
            if (eng) eng.volume = 0f;

            // ---- sound + shake
            var a = root.AddComponent<AudioSource>();
            a.clip = boom; a.spatialBlend = 0.85f; a.minDistance = 18f; a.maxDistance = 700f; a.volume = 1f; a.dopplerLevel = 0f;
            a.Play();
            var cam = Camera.main;
            float dist = cam ? Vector3.Distance(cam.transform.position, p) : 100f;
            Shake = Mathf.Max(Shake, Mathf.Clamp01(1.3f - dist / 120f));
            Destroy(root, 25f);
        }

        IEnumerator Secondary(Transform parent, Vector3 at, float delay)
        {
            yield return new WaitForSeconds(delay);
            var g = new GameObject("FuelBurst"); g.transform.SetParent(parent, false); g.transform.position = at;
            var ps = Burst(g.transform, "Fuel", fireMat, 60, new Vector2(0.4f, 1.0f), new Vector2(1.2f, 3f), new Vector2(4f, 9f), -0.3f,
                new[] { new Color(1f, 0.9f, 0.6f), new Color(1f, 0.45f, 0.08f), new Color(0.35f, 0.06f, 0.02f) }, new[] { 1f, 0.8f, 0f }, 2.8f, false, 1f);
            AddNoise(ps, 2f, 1f);
            Puff(ps, true, 2.2f, 0.5f, 0.75f, 0f);
            Shake = Mathf.Max(Shake, 0.35f);
        }

        void Ring(Vector3 at, Transform parent)
        {
            var go = new GameObject("Shockwave"); go.transform.SetParent(parent, false); go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = false; m.duration = 0.1f; m.playOnAwake = false; m.startLifetime = 0.45f; m.startSpeed = 0f;
            m.startSize = 1f; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startColor = new Color(1f, 0.9f, 0.75f, 0.55f);
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var sh = ps.shape; sh.enabled = false;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(22f, AnimationCurve.EaseInOut(0f, 0.05f, 1f, 1f));
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = smokeMat; r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ps.Play();
        }

        IEnumerator FlashThenFlicker(Light l, float total)
        {
            float i0 = l.intensity;
            for (float k = 0; k < 0.35f && l; k += Time.deltaTime) { l.intensity = Mathf.Lerp(i0, 18f, k / 0.35f); yield return null; }
            if (!l) yield break;
            l.color = new Color(1f, 0.55f, 0.2f);
            for (float k = 0; k < total && l; k += Time.deltaTime)
            {
                l.intensity = (18f * (1f - k / total)) * (0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9f, 0.3f));
                yield return null;
            }
            if (l) l.intensity = 0f;
        }

        IEnumerator Flicker(Light l, float seconds, float strength)
        {
            for (float k = 0; k < seconds && l; k += Time.deltaTime)
            {
                float fade = 1f - Mathf.Clamp01((k - seconds * 0.7f) / (seconds * 0.3f));
                l.intensity = strength * fade * (0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 12f, 1.7f));
                yield return null;
            }
            if (l) l.intensity = 0f;
        }

        /// <summary>
        /// Turns a soft billboard burst into chunky low-poly puffs: faceted mesh particles that tumble, swell and
        /// then shrink away (no alpha fade), fire cooling from yellow to charcoal. Fewer, bigger, readable shapes.
        /// </summary>
        void Puff(ParticleSystem ps, bool fire, float peak, float countScale, float sizeMul, float delay)
        {
            if (!puffMesh || !(fire ? fireMeshMat : smokeMeshMat)) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = puffMesh;
            r.sharedMaterial = fire ? fireMeshMat : smokeMeshMat;
            r.alignment = ParticleSystemRenderSpace.World;
            r.shadowCastingMode = fire ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            var m = ps.main;
            m.startRotation3D = true;
            m.startRotationX = m.startRotationY = m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startSizeMultiplier *= sizeMul;
            m.startDelay = new ParticleSystem.MinMaxCurve(delay, delay + (delay > 0f ? 0.5f : 0f));
            var rol = ps.rotationOverLifetime; rol.enabled = true; rol.separateAxes = true;
            rol.x = rol.y = rol.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(peak, new AnimationCurve(
                new Keyframe(0f, 0.25f), new Keyframe(0.18f, 0.9f), new Keyframe(0.6f, 1f), new Keyframe(1f, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            if (fire)
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.12f), 0.25f),
                                  new GradientColorKey(new Color(0.85f, 0.22f, 0.05f), 0.55f), new GradientColorKey(new Color(0.12f, 0.1f, 0.1f), 0.85f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(new Color(0.5f, 0.46f, 0.43f), 0f), new GradientColorKey(new Color(0.42f, 0.42f, 0.44f), 0.35f),
                                  new GradientColorKey(new Color(0.72f, 0.72f, 0.75f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            col.color = g;
            var em = ps.emission;
            if (em.burstCount > 0)
            {
                var b = em.GetBurst(0); b.count = new ParticleSystem.MinMaxCurve(Mathf.Max(6f, b.count.constant * countScale)); em.SetBurst(0, b);
            }
            else em.rateOverTimeMultiplier *= countScale;
            ps.Play();
        }

        static void AddNoise(ParticleSystem ps, float strength, float frequency)
        {
            var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = frequency; n.scrollSpeed = 0.6f; n.quality = ParticleSystemNoiseQuality.Medium;
        }

        static ParticleSystem Burst(Transform parent, string name, Material mat, int count, Vector2 life, Vector2 size, Vector2 speed, float gravity,
            Color[] colors, float[] alphas, float growth, bool stretch, float drag)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = false; m.duration = 0.2f; m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.gravityModifier = gravity;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = count * 2;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var ck = new GradientColorKey[colors.Length]; var ak = new GradientAlphaKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                float t = colors.Length == 1 ? 0f : i / (float)(colors.Length - 1);
                ck[i] = new GradientColorKey(colors[i], t); ak[i] = new GradientAlphaKey(alphas[i], t);
            }
            var g = new Gradient(); g.SetKeys(ck, ak); col.color = g;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, growth));
            var rol = ps.rotationOverLifetime; rol.enabled = !stretch; rol.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = drag; lim.multiplyDragByParticleSize = !stretch;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.045f; r.lengthScale = 1f; }
            ps.Play();
            return ps;
        }

        static ParticleSystem Stream(Transform parent, string name, Material mat, float rate, Vector2 life, Vector2 size, float upSpeed, Color c0, Color c1, float duration, float growth = 2.2f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = false; m.duration = duration; m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 800;
            var e = ps.emission; e.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f); v.y = new ParticleSystem.MinMaxCurve(upSpeed * 0.7f, upSpeed); v.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(c0.a, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, growth));
            var rol = ps.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            ps.Play();
            return ps;
        }

        /// <summary>Procedural explosion: sharp transient, deep rumble, a second thump and debris clatter (~4 s).</summary>
        static AudioClip MakeBoom()
        {
            int sr = 44100, n = sr * 4;
            var data = new float[n];
            var rnd = new System.Random(5);
            float lp = 0f, lp2 = 0f, lp3 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * Mathf.Lerp(0.6f, 0.025f, Mathf.Clamp01(t * 2f));
                lp2 += (lp - lp2) * 0.06f;
                lp3 += (lp2 - lp3) * 0.03f;
                float crack = noise * Mathf.Exp(-t * 40f) * 1.2f;
                float body = lp * Mathf.Exp(-t * 2.4f) * 1.5f;
                float rumble = lp3 * Mathf.Exp(-t * 0.7f) * 6f;
                float thump = Mathf.Sin(2f * Mathf.PI * (48f - t * 18f) * t) * Mathf.Exp(-t * 3.5f) * 1.0f;
                float t2 = t - 0.27f;
                float thump2 = t2 > 0f ? Mathf.Sin(2f * Mathf.PI * 62f * t2) * Mathf.Exp(-t2 * 6f) * 0.7f + lp * Mathf.Exp(-t2 * 5f) * 0.6f : 0f;
                float clatter = (t > 0.6f && rnd.NextDouble() < 0.0016 * Mathf.Exp(-(t - 0.6f) * 0.9f)) ? noise * 0.7f : 0f;
                data[i] = Mathf.Clamp((float)System.Math.Tanh((crack + body + rumble + thump + thump2 + clatter) * 1.25f), -1f, 1f);
            }
            var clip = AudioClip.Create("Boom", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
