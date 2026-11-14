using System.Collections.Generic;
using OWR.Cockpit;
using OWR.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OWR.EditorTools
{
    /// <summary>
    /// Builds the semi-poly open-wheel car: lofted monocoque with open cockpit, sidepods, airbox, halo,
    /// airfoil wings, floor + diffuser, lathed wheels with covers, moving suspension, mirrors, helmet,
    /// and a steering wheel with shift lights and a live display. Car local space: x right, y up, z forward, origin on the ground.
    /// </summary>
    public static class CarModelBuilder
    {
        public class Mats
        {
            public Material paint, carbon, accent, interior, metal, white, gloss, rearLight, rubber, grip, shiftLight, suit, glove, glovePalm;
        }

        public class Result
        {
            public Renderer helmet;
            public Renderer shoulders;
            public SteeringWheelRig wheelRig;
        }

        // Body submesh slots
        const int P = 0, CF = 1, AC = 2, IN = 3, MT = 4, WH = 5, GB = 6, RL = 7, SU = 8;
        const string MeshDir = "Assets/_Game/Meshes";
        /// <summary>Layer for the driver's own head/shoulders: drawn by the chase cam, culled by the cockpit cam.</summary>
        public const int DriverHiddenLayer = 6;
        static Mesh linkMeshCache;

        public static Result Build(GameObject root, CarController ctrl, CarSetup s, Mats m)
        {
            int layer = root.layer;
            var res = new Result();

            // ================================================================= BODY
            segCache.Clear();
            var k = new MeshKit(9);
            BuildMonocoque(k);
            BuildSidepods(k);
            BuildAirboxAndFin(k);
            BuildFloor(k);
            BuildFrontWing(k);
            BuildRearWing(k);
            BuildHalo(k);
            BuildMirrors(k);
            BuildCockpitDetails(k);
            var bodyMesh = Save(k.Build("CarBody"), "CarBody");
            var body = Child(root.transform, "Body", layer);
            body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.paint, m.carbon, m.accent, m.interior, m.metal, m.white, m.gloss, m.rearLight, m.suit };

            // ================================================================= HELMET (hidden in cockpit view)
            var hk = new MeshKit(5);
            var bk = new MeshKit(5);
            BuildHelmet(hk, bk);
            // neck pivot so the head can turn/lean (HelmetSway); mesh is authored in car space, so offset it back
            var neck = new Vector3(0f, 0.70f, -0.06f);
            var pivot = Child(root.transform, "HelmetPivot", DriverHiddenLayer);
            pivot.transform.localPosition = neck;
            var helmet = Child(pivot.transform, "DriverHelmet", DriverHiddenLayer);
            helmet.transform.localPosition = -neck;
            helmet.AddComponent<MeshFilter>().sharedMesh = Save(hk.Build("Helmet"), "Helmet");
            var hr = helmet.AddComponent<MeshRenderer>();
            hr.sharedMaterials = new[] { m.white, m.gloss, m.accent, m.paint, m.interior };
            res.helmet = hr;
            var shouldersGo = Child(root.transform, "DriverShoulders", DriverHiddenLayer);
            shouldersGo.AddComponent<MeshFilter>().sharedMesh = Save(bk.Build("DriverShoulders"), "DriverShoulders");
            var sr = shouldersGo.AddComponent<MeshRenderer>();
            sr.sharedMaterials = new[] { m.white, m.gloss, m.accent, m.paint, m.interior };
            res.shoulders = sr;
            var sway = root.AddComponent<OWR.Cockpit.HelmetSway>();
            sway.car = ctrl; sway.pivot = pivot.transform;

            // ================================================================= WHEELS
            var frontMesh = Save(BuildWheelMesh(0.30f, s.frontRadius), "Wheel_Front");
            var rearMesh = Save(BuildWheelMesh(0.40f, s.rearRadius), "Wheel_Rear");
            var frontStatic = Save(BuildFrontWheelStatic(0.30f, s.frontRadius), "Wheel_FrontStatic");
            var wheelMats = new[] { m.rubber, m.metal, m.carbon, m.accent, m.gloss };
            var linkMesh = Save(BuildLinkMesh(), "SuspensionLink");
            linkMeshCache = linkMesh;

            foreach (var w in ctrl.wheels)
            {
                // clear placeholder visuals
                for (int i = w.spinVisual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(w.spinVisual.GetChild(i).gameObject);
                var model = Child(w.spinVisual, "Model", layer);
                model.transform.localScale = new Vector3(w.isLeft ? -1f : 1f, 1f, 1f);
                model.AddComponent<MeshFilter>().sharedMesh = w.isFront ? frontMesh : rearMesh;
                model.AddComponent<MeshRenderer>().sharedMaterials = wheelMats;

                if (w.isFront)
                {
                    var st = Child(w.steerVisual, "Upright_Static", layer);
                    st.transform.localScale = new Vector3(w.isLeft ? -1f : 1f, 1f, 1f);
                    st.AddComponent<MeshFilter>().sharedMesh = frontStatic;
                    st.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.carbon, m.gloss };
                }
                BuildSuspension(root.transform, w, linkMesh, m, layer);
            }

            // ================================================================= STEERING WHEEL
            res.wheelRig = BuildSteeringWheel(root, ctrl, m, layer);

            // ================================================================= REFLECTION PROBE (paint reflects the world)
            var probeGo = Child(root.transform, "ReflectionProbe", layer);
            probeGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.EveryFrame;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 64;
            probe.size = new Vector3(30f, 20f, 30f);
            probe.importance = 2;
            probe.cullingMask = ~(1 << layer);
            probe.hdr = true;
            return res;
        }

        // ================================================================= monocoque
        static void BuildMonocoque(MeshKit k)
        {
            var front = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(3.12f, 0, 0.215f, 0.075f, 0.045f, 0.045f, 2.2f),
                new MeshKit.Sec(2.95f, 0, 0.225f, 0.11f, 0.06f, 0.06f, 2.4f),
                new MeshKit.Sec(2.60f, 0, 0.255f, 0.155f, 0.085f, 0.075f, 2.6f),
                new MeshKit.Sec(2.10f, 0, 0.32f, 0.20f, 0.11f, 0.12f, 3.0f),
                new MeshKit.Sec(1.60f, 0, 0.40f, 0.25f, 0.15f, 0.19f, 3.4f),
                new MeshKit.Sec(1.15f, 0, 0.44f, 0.32f, 0.19f, 0.29f, 3.6f),
                new MeshKit.Sec(0.78f, 0, 0.44f, 0.37f, 0.23f, 0.34f, 3.6f),
            };
            k.Loft(P, front, 44, 6, capStartSub: AC, capEndSub: IN);

            // Open cockpit: shell without the top arc, plus an inner liner and padded rims.
            var cockpit = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(0.78f, 0, 0.44f, 0.37f, 0.23f, 0.34f, 3.4f),
                new MeshKit.Sec(0.40f, 0, 0.44f, 0.40f, 0.25f, 0.36f, 3.6f),
                new MeshKit.Sec(0.00f, 0, 0.44f, 0.41f, 0.26f, 0.36f, 3.6f),
                new MeshKit.Sec(-0.42f, 0, 0.45f, 0.40f, 0.27f, 0.36f, 3.6f),
            };
            float phi0 = 116f * Mathf.Deg2Rad, phi1 = (64f + 360f) * Mathf.Deg2Rad;
            k.Loft(P, cockpit, 40, 6, phi0, phi1);
            var liner = new List<MeshKit.Sec>();
            foreach (var c in cockpit) liner.Add(new MeshKit.Sec(c.z, c.cx, c.cy, c.hw - 0.025f, c.hTop - 0.02f, c.hBot - 0.03f, c.n));
            k.Loft(IN, liner, 40, 6, phi0, phi1, inward: true);
            // padded rims along both edges
            var dense = MeshKit.Densify(cockpit, 6);
            foreach (float phi in new[] { phi0, phi1 })
            {
                var path = new List<Vector3>();
                foreach (var sec in dense) path.Add(sec.Point(phi) + new Vector3(0, 0.004f, 0));
                k.Tube(IN, path, 0.024f, 10);
            }
            // seat back & floor of the cockpit (dark)
            k.Box(IN, new Vector3(0, 0.36f, -0.35f), new Vector3(0.62f, 0.5f, 0.06f), Quaternion.Euler(-12f, 0, 0));
            k.Box(IN, new Vector3(0, 0.15f, 0.2f), new Vector3(0.66f, 0.03f, 1.1f), Quaternion.identity);

            var rear = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(-0.42f, 0, 0.45f, 0.40f, 0.27f, 0.36f, 3.6f),
                new MeshKit.Sec(-0.75f, 0, 0.47f, 0.36f, 0.30f, 0.36f, 3.2f),
                new MeshKit.Sec(-1.20f, 0, 0.46f, 0.27f, 0.30f, 0.32f, 3.0f),
                new MeshKit.Sec(-1.70f, 0, 0.42f, 0.17f, 0.25f, 0.26f, 2.8f),
                new MeshKit.Sec(-2.10f, 0, 0.37f, 0.11f, 0.16f, 0.18f, 2.6f),
                new MeshKit.Sec(-2.34f, 0, 0.34f, 0.08f, 0.10f, 0.10f, 2.4f),
            };
            k.Loft(P, rear, 44, 6, capStartSub: IN, capEndSub: GB);

            // livery stripe: white band along the nose top
            var stripe = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(2.90f, 0, 0.226f, 0.035f, 0.062f, 0.01f, 2.2f),
                new MeshKit.Sec(2.20f, 0, 0.302f, 0.05f, 0.112f, 0.01f, 2.4f),
                new MeshKit.Sec(1.30f, 0, 0.405f, 0.07f, 0.186f, 0.01f, 2.6f),
                new MeshKit.Sec(0.85f, 0, 0.44f, 0.08f, 0.226f, 0.01f, 2.6f),
            };
            k.Loft(WH, stripe, 16, 5, 20f * Mathf.Deg2Rad, 160f * Mathf.Deg2Rad);

            // rain light
            k.Box(RL, new Vector3(0, 0.34f, -2.345f), new Vector3(0.11f, 0.045f, 0.02f), Quaternion.identity);
        }

        static void BuildSidepods(MeshKit k)
        {
            var pod = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(0.50f, 0.60f, 0.40f, 0.17f, 0.13f, 0.15f, 3.0f),
                new MeshKit.Sec(0.30f, 0.62f, 0.41f, 0.21f, 0.16f, 0.19f, 3.4f),
                new MeshKit.Sec(-0.20f, 0.61f, 0.40f, 0.21f, 0.17f, 0.21f, 3.4f),
                new MeshKit.Sec(-0.75f, 0.53f, 0.37f, 0.17f, 0.15f, 0.20f, 3.0f),
                new MeshKit.Sec(-1.25f, 0.40f, 0.32f, 0.10f, 0.10f, 0.15f, 2.6f),
                new MeshKit.Sec(-1.55f, 0.28f, 0.28f, 0.04f, 0.05f, 0.08f, 2.2f),
            };
            for (int side = -1; side <= 1; side += 2)
            {
                k.M = Matrix4x4.Scale(new Vector3(side, 1, 1));
                k.Loft(P, pod, 36, 6, capStartSub: GB);
                // inlet lip (accent)
                var lip = new List<Vector3>();
                var sec = pod[0];
                for (int i = 0; i <= 24; i++) lip.Add(sec.Point(i / 24f * Mathf.PI * 2f) + new Vector3(0, 0, 0.005f));
                k.Tube(AC, lip, 0.012f, 8, 1f, false);
                // floor-to-pod vertical fences (bargeboard style)
                k.Box(CF, new Vector3(0.62f, 0.17f, 0.75f), new Vector3(0.012f, 0.16f, 0.45f), Quaternion.Euler(0, 6f, 0));
                k.Box(CF, new Vector3(0.70f, 0.15f, 0.62f), new Vector3(0.012f, 0.12f, 0.30f), Quaternion.Euler(0, 9f, 0));
            }
            k.M = Matrix4x4.identity;
        }

        static void BuildAirboxAndFin(MeshKit k)
        {
            var air = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(-0.36f, 0, 0.87f, 0.08f, 0.10f, 0.12f, 2.4f),
                new MeshKit.Sec(-0.50f, 0, 0.86f, 0.12f, 0.12f, 0.15f, 2.6f),
                new MeshKit.Sec(-0.80f, 0, 0.81f, 0.13f, 0.10f, 0.16f, 2.8f),
                new MeshKit.Sec(-1.20f, 0, 0.75f, 0.09f, 0.07f, 0.12f, 2.6f),
                new MeshKit.Sec(-1.60f, 0, 0.67f, 0.035f, 0.04f, 0.07f, 2.2f),
            };
            k.Loft(P, air, 32, 6, capStartSub: GB);
            // roll-hoop side intakes
            for (int side = -1; side <= 1; side += 2)
                k.Box(GB, new Vector3(side * 0.115f, 0.83f, -0.43f), new Vector3(0.03f, 0.08f, 0.05f), Quaternion.Euler(0, side * 15f, 0));

            var fin = new List<Vector2> { new Vector2(-0.95f, 0.90f), new Vector2(-1.4f, 0.90f), new Vector2(-2.08f, 0.96f), new Vector2(-2.22f, 0.86f), new Vector2(-1.7f, 0.74f), new Vector2(-1.1f, 0.82f) };
            k.Extrude(P, fin, Vector3.zero, Vector3.forward, Vector3.up, Vector3.right, 0.008f);
            // accent number panel on the fin
            var num = new List<Vector2> { new Vector2(-1.55f, 0.84f), new Vector2(-1.95f, 0.86f), new Vector2(-1.95f, 0.93f), new Vector2(-1.55f, 0.91f) };
            k.Extrude(WH, num, Vector3.zero, Vector3.forward, Vector3.up, Vector3.right, 0.0095f);
        }

        static void BuildFloor(MeshKit k)
        {
            var outline = new List<Vector2>
            {
                new Vector2(0.35f, 1.20f), new Vector2(0.62f, 0.85f), new Vector2(0.84f, 0.60f), new Vector2(0.87f, -0.30f),
                new Vector2(0.86f, -1.10f), new Vector2(0.78f, -1.45f), new Vector2(0.55f, -1.58f), new Vector2(0.50f, -1.63f),
                new Vector2(-0.50f, -1.63f), new Vector2(-0.55f, -1.58f), new Vector2(-0.78f, -1.45f), new Vector2(-0.86f, -1.10f),
                new Vector2(-0.87f, -0.30f), new Vector2(-0.84f, 0.60f), new Vector2(-0.62f, 0.85f), new Vector2(-0.35f, 1.20f),
            };
            k.Extrude(CF, outline, new Vector3(0, 0.085f, 0), Vector3.right, Vector3.forward, Vector3.up, 0.02f);
            // floor edge curl
            for (int side = -1; side <= 1; side += 2)
            {
                var edge = new List<Vector3> { new Vector3(side * 0.84f, 0.1f, 0.6f), new Vector3(side * 0.87f, 0.11f, -0.3f), new Vector3(side * 0.86f, 0.11f, -1.1f), new Vector3(side * 0.78f, 0.1f, -1.45f) };
                k.Tube(CF, edge, 0.018f, 8, 0.6f);
            }
            // diffuser ramp + fences
            k.Box(CF, new Vector3(0, 0.19f, -1.94f), new Vector3(1.0f, 0.015f, 0.68f), Quaternion.Euler(19f, 0, 0));
            foreach (float x in new[] { -0.5f, -0.33f, -0.14f, 0.14f, 0.33f, 0.5f })
                k.Box(CF, new Vector3(x, 0.15f, -1.95f), new Vector3(0.01f, 0.13f, 0.62f), Quaternion.Euler(19f, 0, 0));
            // plank
            k.Box(MT, new Vector3(0, 0.07f, -0.2f), new Vector3(0.3f, 0.012f, 2.4f), Quaternion.identity);
        }

        static void BuildFrontWing(MeshKit k)
        {
            k.Wing(CF, -0.95f, 0.95f, 3.20f, 0.085f, 0.30f, 0.09f, -0.05f, 6f, 14, 16,
                f => 0.025f * (1f - Mathf.Abs(2f * f - 1f)));
            for (int side = -1; side <= 1; side += 2)
            {
                float a = side > 0 ? 0.22f : -0.95f, b = side > 0 ? 0.95f : -0.22f;
                System.Func<float, float> outward = side > 0 ? (System.Func<float, float>)(f => f) : (f => 1f - f);
                k.Wing(CF, a, b, 3.03f, 0.125f, 0.17f, 0.09f, -0.06f, 18f, 12, 10, f => 0.03f * outward(f));
                k.Wing(P, a, b, 2.89f, 0.17f, 0.13f, 0.09f, -0.06f, 30f, 12, 10, f => 0.05f * outward(f), f => 1f - 0.25f * (1f - outward(f)), AC);
                var plate = new List<Vector2> { new Vector2(3.22f, 0.05f), new Vector2(2.70f, 0.05f), new Vector2(2.68f, 0.22f), new Vector2(2.86f, 0.27f), new Vector2(3.12f, 0.20f) };
                k.Extrude(P, plate, new Vector3(side * 0.958f, 0, 0), Vector3.forward, Vector3.up, Vector3.right, 0.012f);
                var edge = new List<Vector2> { new Vector2(3.23f, 0.05f), new Vector2(3.17f, 0.05f), new Vector2(3.08f, 0.205f), new Vector2(3.13f, 0.205f) };
                k.Extrude(AC, edge, new Vector3(side * 0.958f, 0, 0), Vector3.forward, Vector3.up, Vector3.right, 0.014f);
                // nose pylons
                k.Box(CF, new Vector3(side * 0.06f, 0.15f, 2.86f), new Vector3(0.012f, 0.13f, 0.20f), Quaternion.identity);
            }
        }

        static void BuildRearWing(MeshKit k)
        {
            k.Wing(CF, -0.50f, 0.50f, -2.20f, 0.83f, 0.30f, 0.10f, -0.06f, 10f, 14, 10);
            k.Wing(P, -0.50f, 0.50f, -2.47f, 0.93f, 0.18f, 0.09f, -0.06f, 30f, 12, 10, null, null, AC);
            k.Wing(CF, -0.40f, 0.40f, -2.20f, 0.45f, 0.16f, 0.10f, -0.05f, 8f, 12, 8);
            for (int side = -1; side <= 1; side += 2)
            {
                var plate = new List<Vector2> { new Vector2(-2.12f, 0.66f), new Vector2(-2.17f, 1.00f), new Vector2(-2.66f, 0.99f), new Vector2(-2.64f, 0.62f), new Vector2(-2.40f, 0.56f) };
                k.Extrude(P, plate, new Vector3(side * 0.51f, 0, 0), Vector3.forward, Vector3.up, Vector3.right, 0.014f);
                var stripe = new List<Vector2> { new Vector2(-2.17f, 0.93f), new Vector2(-2.17f, 1.00f), new Vector2(-2.66f, 0.99f), new Vector2(-2.66f, 0.92f) };
                k.Extrude(AC, stripe, new Vector3(side * 0.51f, 0, 0), Vector3.forward, Vector3.up, Vector3.right, 0.016f);
            }
            k.Box(CF, new Vector3(0, 0.60f, -2.30f), new Vector3(0.02f, 0.46f, 0.18f), Quaternion.identity);
        }

        static void BuildHalo(MeshKit k)
        {
            var pillar = new List<Vector3> { new Vector3(0, 0.66f, 0.80f), new Vector3(0, 0.76f, 0.73f), new Vector3(0, 0.88f, 0.66f), new Vector3(0, 0.975f, 0.60f) };
            k.Tube(P, pillar, 0.024f, 12, 1.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                var path = new List<Vector3>();
                for (int a = 0; a <= 125; a += 5)
                {
                    float t = a * Mathf.Deg2Rad;
                    path.Add(new Vector3(side * 0.40f * Mathf.Sin(t), 0.975f - 0.035f * (a / 125f), 0.10f + 0.50f * Mathf.Cos(t)));
                }
                path.Add(new Vector3(side * 0.39f, 0.88f, -0.26f));
                path.Add(new Vector3(side * 0.40f, 0.78f, -0.32f));
                path.Add(new Vector3(side * 0.41f, 0.68f, -0.35f));
                k.Tube(P, path, 0.022f, 12);
            }
        }

        static void BuildMirrors(MeshKit k)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                k.M = Matrix4x4.Scale(new Vector3(side, 1, 1));
                k.Tube(CF, new List<Vector3> { new Vector3(0.46f, 0.52f, 0.58f), new Vector3(0.55f, 0.56f, 0.57f), new Vector3(0.62f, 0.59f, 0.56f) }, 0.009f, 8);
                var housing = new List<MeshKit.Sec>
                {
                    new MeshKit.Sec(0.66f, 0.64f, 0.605f, 0.02f, 0.012f, 0.012f, 2.2f),
                    new MeshKit.Sec(0.62f, 0.64f, 0.605f, 0.085f, 0.033f, 0.033f, 3.2f),
                    new MeshKit.Sec(0.53f, 0.64f, 0.605f, 0.088f, 0.035f, 0.035f, 3.4f),
                    new MeshKit.Sec(0.515f, 0.64f, 0.605f, 0.08f, 0.03f, 0.03f, 3.4f),
                };
                k.Loft(P, housing, 24, 4, capEndSub: MT);
            }
            k.M = Matrix4x4.identity;
        }

        static void BuildCockpitDetails(MeshKit k)
        {
            // steering column & dash
            k.Tube(CF, new List<Vector3> { new Vector3(0, 0.54f, 0.76f), new Vector3(0, 0.565f, 0.52f), new Vector3(0, 0.585f, 0.33f) }, 0.022f, 10);
            // ---------------- cockpit interior detail ----------------
            // padded suede side walls (inside the cockpit opening)
            for (int side = -1; side <= 1; side += 2)
            {
                k.Box(IN, new Vector3(side * 0.335f, 0.585f, 0.30f), new Vector3(0.05f, 0.14f, 0.42f), Quaternion.Euler(0, 0, side * 8f));
                k.Box(IN, new Vector3(side * 0.34f, 0.585f, -0.12f), new Vector3(0.05f, 0.15f, 0.36f), Quaternion.Euler(0, 0, side * 8f));
                // diamond-quilted pad faces with white stitching
                for (float z = -0.28f; z <= 0.50f; z += 0.045f)
                    for (float y = 0.53f; y <= 0.64f; y += 0.045f)
                    {
                        float off = (Mathf.RoundToInt((z + 0.28f) / 0.045f) % 2 == 0) ? 0f : 0.0225f;
                        if (y + off > 0.645f) continue;
                        k.Box(IN, new Vector3(side * 0.306f, y + off, z), new Vector3(0.008f, 0.034f, 0.034f), Quaternion.Euler(45f, 0f, 0f));
                    }
                k.Tube(WH, new List<Vector3> { new Vector3(side * 0.302f, 0.662f, -0.30f), new Vector3(side * 0.302f, 0.662f, 0.52f) }, 0.0016f, 4, 1f, false);
                k.Tube(WH, new List<Vector3> { new Vector3(side * 0.302f, 0.515f, -0.30f), new Vector3(side * 0.302f, 0.515f, 0.52f) }, 0.0016f, 4, 1f, false);
                // cockpit rim insert: orange edge strip along the top of the opening
                k.Tube(AC, new List<Vector3> { new Vector3(side * 0.29f, 0.70f, -0.38f), new Vector3(side * 0.285f, 0.70f, 0.1f), new Vector3(side * 0.27f, 0.695f, 0.70f) }, 0.006f, 6, 1f, false);
            }
            // dash bulkhead panel in front of the knees, with a switch cluster
            k.Box(CF, new Vector3(0f, 0.50f, 0.66f), new Vector3(0.50f, 0.16f, 0.03f), Quaternion.Euler(-18f, 0, 0));
            for (int i = 0; i < 6; i++)
            {
                float x = -0.13f + i * 0.052f;
                k.Box(GB, new Vector3(x, 0.515f, 0.638f), new Vector3(0.026f, 0.018f, 0.01f), Quaternion.Euler(-18f, 0, 0));    // switch guard
                k.Box(i % 3 == 0 ? AC : MT, new Vector3(x, 0.522f, 0.63f), new Vector3(0.006f, 0.006f, 0.022f), Quaternion.Euler(-40f, 0, 0)); // toggle
            }
            // fire extinguisher button: red cap in a yellow ring
            k.M = Matrix4x4.TRS(new Vector3(0.19f, 0.47f, 0.645f), Quaternion.Euler(-18f, 90f, 0f), Vector3.one);
            k.Lathe(AC, new List<Vector2> { new Vector2(0f, 0.001f), new Vector2(0f, 0.022f), new Vector2(0.006f, 0.022f), new Vector2(0.006f, 0.001f), new Vector2(0f, 0.001f) }, 18);
            k.Lathe(RL, new List<Vector2> { new Vector2(0.004f, 0.001f), new Vector2(0.004f, 0.014f), new Vector2(0.014f, 0.012f), new Vector2(0.016f, 0.001f), new Vector2(0.004f, 0.001f) }, 18);
            k.M = Matrix4x4.identity;
            // status LED bar on top of the chassis, just ahead of the wheel
            k.Box(GB, new Vector3(0f, 0.672f, 0.86f), new Vector3(0.16f, 0.012f, 0.03f), Quaternion.Euler(-6f, 0, 0));
            for (int i = 0; i < 5; i++) k.Box(RL, new Vector3(-0.05f + i * 0.025f, 0.68f, 0.855f), new Vector3(0.012f, 0.006f, 0.012f), Quaternion.identity);

            // six-point harness: shoulder straps over the shoulders, lap belts, crotch strap, metal buckle
            for (int side = -1; side <= 1; side += 2)
            {
                k.Tube(AC, new List<Vector3> { new Vector3(side * 0.10f, 0.70f, -0.36f), new Vector3(side * 0.11f, 0.66f, -0.14f), new Vector3(side * 0.10f, 0.56f, 0.0f), new Vector3(side * 0.06f, 0.43f, 0.10f), new Vector3(side * 0.025f, 0.40f, 0.13f) }, 0.026f, 8, 0.2f);
                k.Tube(AC, new List<Vector3> { new Vector3(side * 0.27f, 0.30f, -0.06f), new Vector3(side * 0.18f, 0.37f, 0.06f), new Vector3(side * 0.05f, 0.395f, 0.12f) }, 0.026f, 8, 0.2f);
                // HANS tethers (short loops from helmet line to the shoulders)
                k.Tube(AC, new List<Vector3> { new Vector3(side * 0.13f, 0.71f, -0.10f), new Vector3(side * 0.16f, 0.67f, -0.06f), new Vector3(side * 0.15f, 0.63f, -0.12f) }, 0.006f, 6);
            }
            k.Tube(AC, new List<Vector3> { new Vector3(0f, 0.26f, 0.0f), new Vector3(0f, 0.33f, 0.08f), new Vector3(0f, 0.39f, 0.13f) }, 0.024f, 8, 0.2f);
            k.M = Matrix4x4.TRS(new Vector3(0f, 0.40f, 0.135f), Quaternion.Euler(-60f, 90f, 0f), Vector3.one);
            k.Lathe(MT, new List<Vector2> { new Vector2(0f, 0.002f), new Vector2(0f, 0.04f), new Vector2(0.014f, 0.038f), new Vector2(0.016f, 0.002f), new Vector2(0f, 0.002f) }, 20);
            k.M = Matrix4x4.identity;

            // driver's legs: race-suit tubes from the hips, knees up under the wheel, feet to the pedals
            for (int side = -1; side <= 1; side += 2)
                k.Tube(SU, new List<Vector3> { new Vector3(side * 0.10f, 0.26f, -0.05f), new Vector3(side * 0.12f, 0.36f, 0.30f), new Vector3(side * 0.12f, 0.45f, 0.52f), new Vector3(side * 0.11f, 0.38f, 0.85f), new Vector3(side * 0.09f, 0.30f, 1.25f) },
                    0.068f, 12, 1f, true, t => Mathf.Lerp(1.1f, 0.75f, t));

            // TV camera pod + antenna on the nose (seen ahead over the wheel)
            k.Box(GB, new Vector3(0f, 0.585f, 1.42f), new Vector3(0.05f, 0.05f, 0.12f), Quaternion.identity);
            k.Box(GB, new Vector3(0f, 0.56f, 1.42f), new Vector3(0.012f, 0.03f, 0.05f), Quaternion.identity);
            k.Tube(MT, new List<Vector3> { new Vector3(0.12f, 0.58f, 1.15f), new Vector3(0.12f, 0.70f, 1.13f) }, 0.003f, 6);
            k.Tube(MT, new List<Vector3> { new Vector3(0f, 0.20f, 3.1f), new Vector3(0f, 0.21f, 3.35f) }, 0.004f, 6); // pitot


        }

        // ================================================================= helmet
        static void BuildHelmet(MeshKit k, MeshKit body)
        {
            Vector3 c = new Vector3(0, 0.765f, -0.04f);
            var shell = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(c.z + 0.135f, 0, c.y - 0.01f, 0.03f, 0.03f, 0.03f, 2f),
                new MeshKit.Sec(c.z + 0.11f, 0, c.y, 0.10f, 0.10f, 0.11f, 2.1f),
                new MeshKit.Sec(c.z + 0.05f, 0, c.y + 0.005f, 0.135f, 0.135f, 0.14f, 2.2f),
                new MeshKit.Sec(c.z - 0.02f, 0, c.y + 0.005f, 0.14f, 0.14f, 0.15f, 2.2f),
                new MeshKit.Sec(c.z - 0.09f, 0, c.y, 0.125f, 0.13f, 0.14f, 2.2f),
                new MeshKit.Sec(c.z - 0.135f, 0, c.y - 0.01f, 0.06f, 0.07f, 0.08f, 2f),
            };
            k.Loft(0, shell, 32, 4, capStartSub: 0, capEndSub: 0);
            // visor band (lathe around a vertical axis through the head centre)
            k.M = Matrix4x4.TRS(c + new Vector3(0, 0.0f, 0.0f), Quaternion.Euler(0, 0, 90f), Vector3.one);
            var band = new List<Vector2> { new Vector2(-0.015f, 0.139f), new Vector2(0.045f, 0.139f), new Vector2(0.045f, 0.147f), new Vector2(-0.015f, 0.147f), new Vector2(-0.015f, 0.139f) };
            k.Lathe(1, band, 24, 25f * Mathf.Deg2Rad, 155f * Mathf.Deg2Rad);
            var stripe = new List<Vector2> { new Vector2(0.075f, 0.128f), new Vector2(0.095f, 0.124f), new Vector2(0.095f, 0.132f), new Vector2(0.075f, 0.136f), new Vector2(0.075f, 0.128f) };
            k.Lathe(2, stripe, 40);
            k.M = Matrix4x4.identity;
            // headrest pads + driver shoulders: hidden in cockpit view together with the helmet
            for (int side = -1; side <= 1; side += 2)
                body.Box(4, new Vector3(side * 0.27f, 0.70f, -0.08f), new Vector3(0.11f, 0.11f, 0.30f), Quaternion.identity);
            body.Box(4, new Vector3(0, 0.72f, -0.24f), new Vector3(0.44f, 0.13f, 0.07f), Quaternion.identity);
            var shoulders = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(0.12f, 0, 0.56f, 0.12f, 0.04f, 0.05f, 2.2f),
                new MeshKit.Sec(0.0f, 0, 0.58f, 0.21f, 0.07f, 0.07f, 2.6f),
                new MeshKit.Sec(-0.20f, 0, 0.58f, 0.21f, 0.07f, 0.07f, 2.6f),
            };
            body.Loft(3, shoulders, 20, 4, capStartSub: 3, capEndSub: 3);
        }

        // ================================================================= wheels (outward = +x)
        static Mesh BuildWheelMesh(float W, float R)
        {
            const int TY = 0, ME = 1, CB = 2, ACC = 3, GLB = 4;
            var k = new MeshKit(5);
            float h = W * 0.5f, rim = 0.229f;
            var tyre = new List<Vector2>
            {
                new Vector2(h - 0.005f, rim), new Vector2(h + 0.010f, rim + 0.03f), new Vector2(h + 0.012f, rim + 0.07f),
                new Vector2(h - 0.002f, R - 0.016f), new Vector2(h - 0.025f, R - 0.003f), new Vector2(h - 0.05f, R),
                new Vector2(-h + 0.05f, R), new Vector2(-h + 0.025f, R - 0.003f), new Vector2(-h + 0.002f, R - 0.016f),
                new Vector2(-h - 0.012f, rim + 0.07f), new Vector2(-h - 0.010f, rim + 0.03f), new Vector2(-h + 0.005f, rim),
                new Vector2(h - 0.005f, rim),
            };
            k.Lathe(TY, tyre, 56);
            // compound band on both sidewalls
            foreach (float sgn in new[] { 1f, -1f })
            {
                float x = sgn * (h + 0.0125f);
                var band = new List<Vector2> { new Vector2(x, rim + 0.05f), new Vector2(x + sgn * 0.002f, rim + 0.05f), new Vector2(x + sgn * 0.002f, rim + 0.062f), new Vector2(x, rim + 0.062f), new Vector2(x, rim + 0.05f) };
                k.Lathe(ACC, band, 56);
            }
            // rim lip
            var lip = new List<Vector2> { new Vector2(h - 0.02f, rim - 0.012f), new Vector2(h + 0.004f, rim - 0.006f), new Vector2(h + 0.004f, rim + 0.004f), new Vector2(h - 0.02f, rim), new Vector2(h - 0.02f, rim - 0.012f) };
            k.Lathe(ME, lip, 56);
            // wheel cover (slightly dished) + centre nut
            var cover = new List<Vector2> { new Vector2(h - 0.03f, 0.05f), new Vector2(h - 0.004f, rim - 0.006f), new Vector2(h - 0.012f, rim - 0.006f), new Vector2(h - 0.038f, 0.05f), new Vector2(h - 0.03f, 0.05f) };
            k.Lathe(CB, cover, 56);
            var nut = new List<Vector2> { new Vector2(h - 0.035f, 0.003f), new Vector2(h - 0.035f, 0.055f), new Vector2(h + 0.01f, 0.05f), new Vector2(h + 0.02f, 0.003f), new Vector2(h - 0.035f, 0.003f) };
            k.Lathe(ACC, nut, 6);
            // inner face (dark)
            var inner = new List<Vector2> { new Vector2(-h + 0.01f, 0.02f), new Vector2(-h + 0.01f, rim - 0.004f), new Vector2(-h + 0.02f, rim - 0.004f), new Vector2(-h + 0.02f, 0.02f), new Vector2(-h + 0.01f, 0.02f) };
            k.Lathe(GLB, inner, 40);
            return k.Build(W > 0.35f ? "Wheel_Rear" : "Wheel_Front");
        }

        static Mesh BuildFrontWheelStatic(float W, float R)
        {
            var k = new MeshKit(2);
            float h = W * 0.5f;
            // brake duct drum inboard of the wheel
            var drum = new List<Vector2> { new Vector2(-h - 0.12f, 0.02f), new Vector2(-h - 0.12f, 0.15f), new Vector2(-h - 0.01f, 0.17f), new Vector2(-h - 0.01f, 0.02f), new Vector2(-h - 0.12f, 0.02f) };
            k.Lathe(1, drum, 24);
            // wheel-arch winglet over the front of the tyre
            var arc = new List<Vector3>();
            for (int a = 35; a <= 115; a += 8)
            {
                float t = a * Mathf.Deg2Rad;
                arc.Add(new Vector3(-h * 0.2f, Mathf.Sin(t) * (R + 0.035f), Mathf.Cos(t) * (R + 0.035f)));
            }
            k.Tube(0, arc, 0.05f, 10, 0.12f);
            k.Box(0, new Vector3(-h * 0.2f - 0.05f, R * 0.55f, R * 0.55f), new Vector3(0.1f, 0.01f, 0.04f), Quaternion.Euler(45, 0, 0));
            return k.Build("Wheel_FrontStatic");
        }

        static Mesh BuildLinkMesh()
        {
            var k = new MeshKit(1);
            // unit streamlined tube along +z, length 1, unit cross-section (scaled by SuspensionLink)
            var path = new List<Vector3> { Vector3.zero, new Vector3(0, 0, 0.5f), new Vector3(0, 0, 1f) };
            k.Tube(0, path, 0.5f, 10, 1f);
            return k.Build("SuspensionLink");
        }

        static void BuildSuspension(Transform root, RaycastWheel w, Mesh link, Mats m, int layer)
        {
            float sx = w.isLeft ? -1f : 1f;
            float inward = -sx * 0.11f;
            Transform hubUp = Anchor(w.steerVisual, "Hub_Upper", new Vector3(inward, 0.12f, 0f), layer);
            Transform hubLo = Anchor(w.steerVisual, "Hub_Lower", new Vector3(inward, -0.12f, 0f), layer);
            Transform hubSteer = Anchor(w.steerVisual, "Hub_Steer", new Vector3(inward, -0.02f, w.isFront ? 0.12f : -0.12f), layer);
            Transform hubCentre = Anchor(w.steerVisual, "Hub_Centre", new Vector3(inward, 0f, 0f), layer);
            float wz = w.transform.localPosition.z;

            if (w.isFront)
            {
                Link(root, Anchor(root, "Ch_FUF", new Vector3(sx * 0.20f, 0.50f, wz + 0.22f), layer), hubUp, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_FUR", new Vector3(sx * 0.25f, 0.50f, wz - 0.32f), layer), hubUp, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_FLF", new Vector3(sx * 0.17f, 0.26f, wz + 0.26f), layer), hubLo, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_FLR", new Vector3(sx * 0.25f, 0.26f, wz - 0.36f), layer), hubLo, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_FTR", new Vector3(sx * 0.19f, 0.34f, wz + 0.12f), layer), hubSteer, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_FPR", new Vector3(sx * 0.20f, 0.56f, wz - 0.12f), layer), hubLo, link, m.carbon, layer, 0.025f, 0.025f);
            }
            else
            {
                Link(root, Anchor(root, "Ch_RUF", new Vector3(sx * 0.16f, 0.46f, wz + 0.30f), layer), hubUp, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_RUR", new Vector3(sx * 0.15f, 0.46f, wz - 0.22f), layer), hubUp, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_RLF", new Vector3(sx * 0.14f, 0.22f, wz + 0.35f), layer), hubLo, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_RLR", new Vector3(sx * 0.13f, 0.22f, wz - 0.25f), layer), hubLo, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_RTL", new Vector3(sx * 0.14f, 0.30f, wz - 0.28f), layer), hubSteer, link, m.carbon, layer);
                Link(root, Anchor(root, "Ch_RDS", new Vector3(sx * 0.10f, 0.33f, wz), layer), hubCentre, link, m.metal, layer, 0.045f, 0.045f);
            }
        }

        static Transform Anchor(Transform parent, string name, Vector3 localPos, int layer)
        {
            var g = Child(parent, name, layer);
            g.transform.localPosition = localPos;
            return g.transform;
        }

        static void Link(Transform root, Transform a, Transform b, Mesh mesh, Material mat, int layer, float width = 0.035f, float thick = 0.013f)
        {
            var g = Child(root, "Link_" + a.name, layer);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var l = g.AddComponent<SuspensionLink>();
            l.from = a; l.to = b; l.width = width; l.thickness = thick;
        }

        // ================================================================= steering wheel
        static SteeringWheelRig BuildSteeringWheel(GameObject root, CarController ctrl, Mats m, int layer)
        {
            const int CBN = 0, GRP = 1, GLS = 2, ACC = 3, WHT = 4, MET = 5;
            var k = new MeshKit(6);
            var body = new List<Vector2>
            {
                new Vector2(-0.100f, 0.080f), new Vector2(0.100f, 0.080f), new Vector2(0.120f, 0.060f), new Vector2(0.120f, -0.050f),
                new Vector2(0.094f, -0.080f), new Vector2(-0.094f, -0.080f), new Vector2(-0.120f, -0.050f), new Vector2(-0.120f, 0.060f),
            };
            k.Extrude(CBN, body, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, 0.03f);
            // raised carbon bezel around the face + metal edge trim
            var rim = new List<Vector3>();
            foreach (var p in body) rim.Add(new Vector3(p.x * 0.985f, p.y * 0.985f, -0.0155f));
            rim.Add(rim[0]);
            k.Tube(MET, rim, 0.0022f, 6, 1f, false);
            // grips (loft along local y), rubber with a raised outer ridge
            for (int side = -1; side <= 1; side += 2)
            {
                k.M = Matrix4x4.TRS(new Vector3(side * 0.138f, 0f, 0.004f), Quaternion.Euler(-90f, 0, 0), Vector3.one);
                var grip = new List<MeshKit.Sec>
                {
                    new MeshKit.Sec(-0.088f, 0, 0, 0.018f, 0.022f, 0.022f, 2.4f),
                    new MeshKit.Sec(-0.062f, 0, 0, 0.027f, 0.032f, 0.032f, 2.6f),
                    new MeshKit.Sec(0.0f, 0, 0, 0.029f, 0.035f, 0.035f, 2.6f),
                    new MeshKit.Sec(0.062f, 0, 0, 0.027f, 0.032f, 0.032f, 2.6f),
                    new MeshKit.Sec(0.088f, 0, 0, 0.018f, 0.022f, 0.022f, 2.4f),
                };
                k.Loft(GRP, grip, 20, 4, capStartSub: GRP, capEndSub: GRP);
                k.M = Matrix4x4.identity;
                // thumb rests / grip top caps
                k.Box(ACC, new Vector3(side * 0.112f, 0.066f, -0.016f), new Vector3(0.018f, 0.01f, 0.006f), Quaternion.Euler(0, 0, side * -20f));
            }
            // display bezel + status LED strip housing
            k.Box(GLS, new Vector3(0, 0.022f, -0.0165f), new Vector3(0.126f, 0.07f, 0.004f), Quaternion.identity);
            k.Box(GLS, new Vector3(0, 0.067f, -0.0165f), new Vector3(0.12f, 0.012f, 0.004f), Quaternion.identity);
            // clutch paddles (fixed, lower)
            for (int side = -1; side <= 1; side += 2)
                k.Box(MET, new Vector3(side * 0.07f, -0.062f, 0.024f), new Vector3(0.05f, 0.03f, 0.003f), Quaternion.identity);

            var pivot = Child(root.transform, "SteeringPivot", layer);
            pivot.transform.localPosition = new Vector3(0f, 0.60f, 0.30f);
            pivot.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
            var wheel = Child(pivot.transform, "SteeringWheel", layer);
            wheel.AddComponent<MeshFilter>().sharedMesh = Save(k.Build("SteeringWheel"), "SteeringWheel");
            wheel.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.carbon, m.grip, m.gloss, m.accent, m.white, m.metal };

            var rig = root.AddComponent<SteeringWheelRig>();
            rig.car = ctrl;
            rig.wheel = wheel.transform;

            // ---------------- shift / status LEDs ----------------
            int n = 12;
            rig.shiftLights = new Renderer[n];
            for (int i = 0; i < n; i++)
            {
                var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(l.GetComponent<Collider>());
                l.name = "ShiftLight_" + i;
                l.layer = layer;
                l.transform.SetParent(wheel.transform, false);
                l.transform.localPosition = new Vector3(Mathf.Lerp(-0.052f, 0.052f, i / (float)(n - 1)), 0.067f, -0.0185f);
                l.transform.localScale = new Vector3(0.0062f, 0.0062f, 0.003f);
                var r = l.GetComponent<Renderer>();
                r.sharedMaterial = m.shiftLight;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rig.shiftLights[i] = r;
            }

            // ---------------- controls (separate moving parts) ----------------
            var controls = root.AddComponent<WheelControls>();
            controls.car = ctrl;
            var list = new List<WheelControls.Control>();
            var buttonMesh = Save(BuildButtonMesh(), "WheelButton");
            var rotaryMesh = Save(BuildRotaryMesh(), "WheelRotary");
            // id, x, y, kind, cap colour
            (string id, float x, float y, WheelControls.Kind kind, Color cap)[] defs =
            {
                ("N",   -0.087f,  0.046f, WheelControls.Kind.Press,  new Color(0.15f, 0.8f, 0.25f)),
                ("DV",  -0.087f,  0.020f, WheelControls.Kind.Press,  new Color(0.85f, 0.15f, 0.1f)),
                ("TK",  -0.087f, -0.006f, WheelControls.Kind.Press,  new Color(0.9f, 0.9f, 0.9f)),
                ("OVT",  0.087f,  0.046f, WheelControls.Kind.Press,  new Color(0.15f, 0.45f, 1f)),
                ("PIT",  0.087f,  0.020f, WheelControls.Kind.Press,  new Color(1f, 0.8f, 0.1f)),
                ("RAD",  0.087f, -0.006f, WheelControls.Kind.Press,  new Color(0.9f, 0.9f, 0.9f)),
                ("ERS", -0.078f, -0.042f, WheelControls.Kind.Rotary, new Color(1f, 0.78f, 0.05f)),
                ("BB",  -0.034f, -0.060f, WheelControls.Kind.Rotary, new Color(0.9f, 0.12f, 0.1f)),
                ("TC",   0.034f, -0.060f, WheelControls.Kind.Rotary, new Color(0.2f, 0.85f, 0.3f)),
                ("DIFF", 0.078f, -0.042f, WheelControls.Kind.Rotary, new Color(1f, 0.5f, 0.1f)),
                ("SL",   0.000f, -0.066f, WheelControls.Kind.Press,  new Color(0.6f, 0.6f, 0.62f)),
            };
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var labelCanvas = new GameObject("Labels", typeof(RectTransform));
            labelCanvas.layer = layer;
            labelCanvas.transform.SetParent(wheel.transform, false);
            labelCanvas.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            labelCanvas.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 8f;
            var lrt = (RectTransform)labelCanvas.transform;
            lrt.sizeDelta = new Vector2(480f, 320f);
            lrt.localScale = Vector3.one * 0.0005f;
            lrt.localPosition = new Vector3(0f, 0f, -0.0157f);

            foreach (var d in defs)
            {
                var capMat = CapMaterial(d.id, d.cap);
                var g = Child(wheel.transform, "Ctrl_" + d.id, layer);
                g.transform.localPosition = new Vector3(d.x, d.y, -0.017f);
                g.AddComponent<MeshFilter>().sharedMesh = d.kind == WheelControls.Kind.Press ? buttonMesh : rotaryMesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.gloss, capMat, m.white };
                list.Add(new WheelControls.Control { id = d.id, target = g.transform, kind = d.kind, rightHand = d.x > 0.001f });
                // printed label next to the control
                float ly = d.kind == WheelControls.Kind.Rotary ? d.y - 0.016f : d.y + 0.011f;
                Label(lrt, "L_" + d.id, font, 13, new Vector2(d.x / 0.0005f, ly / 0.0005f), new Vector2(60f, 16f), TextAnchor.MiddleCenter, new Color(0.85f, 0.87f, 0.9f)).text = d.id;
            }
            controls.controls = list.ToArray();
            rig.controls = controls;
            // brand mark under the display
            Label(lrt, "Brand", font, 15, new Vector2(0f, -0.030f / 0.0005f), new Vector2(200f, 20f), TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.95f)).text = "VANTA  SIM";
            foreach (var t in labelCanvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            // ---------------- shift paddles (separate, pivot at top edge) ----------------
            var paddles = new Transform[2];
            for (int side = -1; side <= 1; side += 2)
            {
                var pv = Child(wheel.transform, side < 0 ? "Paddle_L" : "Paddle_R", layer);
                pv.transform.localPosition = new Vector3(side * 0.095f, 0.04f, 0.026f);
                pv.transform.localRotation = Quaternion.Euler(0f, side * -10f, 0f);
                var pk = new MeshKit(2);
                var shape = new List<Vector2> { new Vector2(-0.035f, 0f), new Vector2(0.035f, 0f), new Vector2(0.038f, -0.05f), new Vector2(0.02f, -0.064f), new Vector2(-0.03f, -0.058f) };
                pk.Extrude(0, shape, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, 0.004f);
                pk.Box(1, new Vector3(side * 0.03f, -0.03f, -0.0025f), new Vector3(0.006f, 0.05f, 0.001f), Quaternion.identity);
                pv.AddComponent<MeshFilter>().sharedMesh = Save(pk.Build("Paddle"), side < 0 ? "Paddle_L" : "Paddle_R");
                pv.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.carbon, m.accent };
                paddles[side < 0 ? 0 : 1] = pv.transform;
            }

            // ---------------- display (world-space UI) ----------------
            var canvasGo = new GameObject("Display", typeof(RectTransform));
            canvasGo.layer = layer;
            canvasGo.transform.SetParent(wheel.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 8f;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(236f, 128f);
            rt.localScale = Vector3.one * 0.0005f;
            rt.localPosition = new Vector3(0f, 0.022f, -0.0188f);

            var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Panel(rt, "Bg", new Vector2(0, 0), new Vector2(236, 128), new Color(0.01f, 0.015f, 0.03f));
            Panel(rt, "SpeedTag", new Vector2(-106, 42), new Vector2(12, 12), new Color(0.15f, 0.85f, 0.25f));
            rig.speedText = Label(rt, "Speed", font, 28, new Vector2(-68, 42), new Vector2(76, 32), TextAnchor.MiddleCenter, Color.white);
            Label(rt, "SpeedUnit", font, 9, new Vector2(-68, 25), new Vector2(70, 12), TextAnchor.MiddleCenter, new Color(0.6f, 0.6f, 0.65f)).text = "KPH";
            rig.gearText = Label(rt, "Gear", font, 76, new Vector2(0, 16), new Vector2(70, 84), TextAnchor.MiddleCenter, Color.white);
            rig.deltaText = Label(rt, "Delta", font, 22, new Vector2(70, 46), new Vector2(96, 26), TextAnchor.MiddleCenter, new Color(0.3f, 1f, 0.4f));
            rig.lapTimeText = Label(rt, "LapTime", font, 14, new Vector2(70, 27), new Vector2(96, 16), TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.9f));
            var stats = Label(rt, "Stats", font, 12, new Vector2(-70, -14), new Vector2(84, 48), TextAnchor.UpperLeft, new Color(0.8f, 0.85f, 0.95f));
            rig.statsText = stats;
            stats.text = "LAP  1\nPOS  1\nTYR  INT";
            Panel(rt, "OvtBox", new Vector2(4, -22), new Vector2(40, 14), new Color(0.05f, 0.25f, 0.85f));
            Label(rt, "Ovt", font, 10, new Vector2(4, -22), new Vector2(40, 14), TextAnchor.MiddleCenter, Color.white).text = "OVT 1";
            rig.infoText = Label(rt, "Info", font, 11, new Vector2(0, -50), new Vector2(230, 22), TextAnchor.MiddleCenter, new Color(0.75f, 0.95f, 1f));
            rig.infoText.supportRichText = true;
            Label(rt, "ErsLabel", font, 9, new Vector2(72, -30), new Vector2(64, 12), TextAnchor.MiddleCenter, new Color(0.6f, 0.6f, 0.65f)).text = "BATTERY";
            var barBg = Panel(rt, "BatteryBg", new Vector2(72, -10), new Vector2(60, 22), new Color(0.03f, 0.2f, 0.07f));
            var bar = new GameObject("Battery", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(barBg.transform, false);
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var img = bar.GetComponent<Image>();
            img.color = new Color(0.35f, 1f, 0.45f);
            img.sprite = uiSprite;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            rig.batteryBar = img;
            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            // ---------------- chunky segmented gloved hands on the grips (no arms) ----------------
            var arms = root.AddComponent<DriverArms>();
            arms.car = ctrl;
            controls.arms = arms;
            var handSrc = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/External/WebXRHands/right.glb");
            for (int side = -1; side <= 1; side += 2)
            {
                var anchor = Child(wheel.transform, side < 0 ? "Hand_L" : "Hand_R", layer);
                anchor.transform.localPosition = new Vector3(side * 0.138f, 0f, 0.004f);
                anchor.transform.localScale = new Vector3(side, 1f, 1f); // mirror the right hand for the left grip

                var hand = (GameObject)Object.Instantiate(handSrc, anchor.transform);
                hand.name = "Hand";
                // natural 3/9 o'clock hold: palm on the outer side of the grip, fingers wrapping round the back,
                // knuckle line running down the grip, wrist low and toward the driver
                var Y = new Vector3(-0.6f, -0.55f, -0.58f).normalized; // back of the glove faces the driver, fingers wrap round the outside
                var Z = Vector3.down; Z = (Z - Vector3.Dot(Z, Y) * Y).normalized;
                var q = Quaternion.LookRotation(Z, Y);
                hand.transform.localRotation = q;
                hand.transform.localPosition = -(q * new Vector3(0.022f, -0.045f, 0.02f));
                foreach (var smr in hand.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = false; // skeleton only
                SegmentHand(hand.transform, m);
                foreach (var t in hand.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                var thumbTarget = Anchor(anchor.transform, "ThumbTarget", new Vector3(-0.006f, 0.032f, -0.021f), layer);
                var grip = hand.AddComponent<HandGrip>();
                grip.mcp = 52f; grip.pip = 62f; grip.dip = 35f;
                grip.thumbTarget = thumbTarget;
                grip.gripAxis = anchor.transform;   // grip centre + axis: the solver wraps each finger onto it

                var arm = side < 0 ? arms.left : arms.right;
                arm.hand = anchor.transform;
                arm.grip = grip;
                arm.paddle = paddles[side < 0 ? 0 : 1];
            }
            var cel = root.AddComponent<Celebration>();
            cel.handL = wheel.transform.Find("Hand_L"); cel.handR = wheel.transform.Find("Hand_R");
            cel.gripL = cel.handL.GetComponentInChildren<HandGrip>(); cel.gripR = cel.handR.GetComponentInChildren<HandGrip>();
            return rig;
        }

        /// <summary>Push button: dark collar + coloured cap (+z points into the wheel).</summary>
        static Mesh BuildButtonMesh()
        {
            var k = new MeshKit(3);
            k.M = Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f)); // lathe axis (x) -> local -z (toward the driver)
            k.Lathe(0, new List<Vector2> { new Vector2(0.0f, 0.001f), new Vector2(0.0f, 0.0085f), new Vector2(0.002f, 0.0085f), new Vector2(0.002f, 0.001f), new Vector2(0.0f, 0.001f) }, 20);
            k.Lathe(1, new List<Vector2> { new Vector2(0.001f, 0.0005f), new Vector2(0.001f, 0.0062f), new Vector2(0.0045f, 0.0058f), new Vector2(0.0052f, 0.0005f), new Vector2(0.001f, 0.0005f) }, 20);
            k.M = Matrix4x4.identity;
            return k.Build("WheelButton");
        }

        /// <summary>Rotary: knurled dark body, coloured cap and a white pointer notch.</summary>
        static Mesh BuildRotaryMesh()
        {
            var k = new MeshKit(3);
            k.M = Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f));
            k.Lathe(0, new List<Vector2> { new Vector2(0.0f, 0.001f), new Vector2(0.0f, 0.0125f), new Vector2(0.008f, 0.0122f), new Vector2(0.009f, 0.001f), new Vector2(0.0f, 0.001f) }, 16);
            k.Lathe(1, new List<Vector2> { new Vector2(0.009f, 0.0005f), new Vector2(0.009f, 0.0085f), new Vector2(0.011f, 0.0075f), new Vector2(0.011f, 0.0005f), new Vector2(0.009f, 0.0005f) }, 24);
            k.M = Matrix4x4.identity;
            k.Box(2, new Vector3(0f, 0.0068f, -0.0112f), new Vector3(0.0018f, 0.0045f, 0.0012f), Quaternion.identity);
            return k.Build("WheelRotary");
        }

        static Material CapMaterial(string id, Color c)
        {
            string path = $"Assets/_Game/Materials/WheelCap_{id}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", 0.75f);
            mat.SetColor("_EmissionColor", c * 0.25f);
            mat.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ================================================================= chunky segmented hands
        static readonly Dictionary<string, Mesh> segCache = new Dictionary<string, Mesh>();

        /// <summary>Rounded block from z=0 to z=len (low-poly knuckle segment).</summary>
        static Mesh Segment(float len, float w, float th)
        {
            string key = $"{len:0.000}_{w:0.000}_{th:0.000}";
            if (segCache.TryGetValue(key, out var cached)) return cached;
            var k = new MeshKit(1);
            var secs = new List<MeshKit.Sec>
            {
                new MeshKit.Sec(0f, 0, 0, w * 0.40f, th * 0.40f, th * 0.40f, 4f),
                new MeshKit.Sec(len * 0.14f, 0, 0, w * 0.5f, th * 0.5f, th * 0.5f, 4.5f),
                new MeshKit.Sec(len * 0.86f, 0, 0, w * 0.48f, th * 0.48f, th * 0.48f, 4.5f),
                new MeshKit.Sec(len, 0, 0, w * 0.36f, th * 0.36f, th * 0.36f, 4f),
            };
            k.Loft(0, secs, 16, 1, capStartSub: 0, capEndSub: 0);
            var mesh = k.Build("Seg_" + key);
            mesh = Save(mesh, "HandSeg_" + key.Replace('.', 'p'));
            segCache[key] = mesh;
            return mesh;
        }

        static void SegmentPart(Transform bone, Vector3 toWorld, float lenScale, float w, float th, Material mat, string name, float startOffset = 0f)
        {
            float len = toWorld.magnitude * lenScale;
            var g = new GameObject(name);
            g.transform.SetParent(bone, false);
            Vector3 dirLocal = bone.InverseTransformDirection(toWorld).normalized;
            g.transform.localRotation = Quaternion.LookRotation(dirLocal, Vector3.up);
            g.transform.localPosition = dirLocal * startOffset;
            g.AddComponent<MeshFilter>().sharedMesh = Segment(len, w, th);
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
        }

        /// <summary>Builds the blocky gloved hand: palm block, three rounded segments per finger, thumb, cuff.</summary>
        static void SegmentHand(Transform hand, Mats m)
        {
            var all = hand.GetComponentsInChildren<Transform>(true);
            Transform B(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
            string[] fingers = { "index-finger", "middle-finger", "ring-finger", "pinky-finger" };
            float[] widths = { 0.0215f, 0.022f, 0.0205f, 0.0185f };
            var wrist = B("wrist");
            Vector3 knuckles = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                var p = B(fingers[i] + "-phalanx-proximal");
                var mid = B(fingers[i] + "-phalanx-intermediate");
                var d = B(fingers[i] + "-phalanx-distal");
                var tip = B(fingers[i] + "-tip");
                knuckles += p.position;
                float w = widths[i];
                SegmentPart(p, mid.position - p.position, 1.12f, w, w * 0.95f, m.glove, "Seg_P", -0.004f);
                SegmentPart(mid, d.position - mid.position, 1.14f, w * 0.95f, w * 0.9f, m.glove, "Seg_I", -0.003f);
                SegmentPart(d, tip.position - d.position, 1.3f, w * 0.9f, w * 0.85f, m.glovePalm, "Seg_D", -0.002f);
                // orange knuckle pad on the back of each proximal segment
                var pad = new GameObject("KnucklePad");
                pad.transform.SetParent(p, false);
                Vector3 dl = p.InverseTransformDirection(mid.position - p.position).normalized;
                pad.transform.localRotation = Quaternion.LookRotation(dl, Vector3.up);
                pad.transform.localPosition = dl * 0.006f + Vector3.up * (w * 0.47f);
                pad.AddComponent<MeshFilter>().sharedMesh = Segment((mid.position - p.position).magnitude * 0.55f, w * 0.75f, w * 0.25f);
                pad.AddComponent<MeshRenderer>().sharedMaterial = m.accent;
            }
            knuckles /= 4f;

            // thumb
            var t0 = B("thumb-metacarpal"); var t1 = B("thumb-phalanx-proximal"); var t2 = B("thumb-phalanx-distal"); var tt = B("thumb-tip");
            SegmentPart(t0, t1.position - t0.position, 1.0f, 0.028f, 0.026f, m.glove, "Seg_T0", -0.004f);
            SegmentPart(t1, t2.position - t1.position, 1.12f, 0.0225f, 0.021f, m.glove, "Seg_T1", -0.003f);
            SegmentPart(t2, tt.position - t2.position, 1.3f, 0.021f, 0.019f, m.glovePalm, "Seg_T2", -0.002f);

            // palm: thick block from the wrist to the knuckle line
            Vector3 toKnuckles = knuckles - wrist.position;
            var palm = new GameObject("Palm");
            palm.transform.SetParent(wrist, false);
            Vector3 dir = wrist.InverseTransformDirection(toKnuckles).normalized;
            palm.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            palm.transform.localPosition = dir * 0.004f;
            float palmLen = toKnuckles.magnitude * 1.02f;
            palm.AddComponent<MeshFilter>().sharedMesh = Segment(palmLen + 0.012f, 0.09f, 0.036f);
            palm.AddComponent<MeshRenderer>().sharedMaterial = m.glove;
            // livery panel across the back of the hand
            var panel = new GameObject("BackPanel");
            panel.transform.SetParent(palm.transform, false);
            panel.transform.localPosition = new Vector3(0f, 0.015f, palmLen * 0.18f);
            panel.AddComponent<MeshFilter>().sharedMesh = Segment(palmLen * 0.5f, 0.07f, 0.01f);
            panel.AddComponent<MeshRenderer>().sharedMaterial = m.accent;

            // cuff (gauntlet) running back toward the driver, white with an orange band
            var cuff = new GameObject("Cuff");
            cuff.transform.SetParent(wrist, false);
            cuff.transform.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
            cuff.transform.localPosition = dir * 0.012f;
            cuff.AddComponent<MeshFilter>().sharedMesh = Segment(0.04f, 0.066f, 0.048f);
            cuff.AddComponent<MeshRenderer>().sharedMaterial = m.glove;
            var band = new GameObject("CuffBand");
            band.transform.SetParent(cuff.transform, false);
            band.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            band.AddComponent<MeshFilter>().sharedMesh = Segment(0.014f, 0.074f, 0.056f);
            band.AddComponent<MeshRenderer>().sharedMaterial = m.accent;
        }

        static GameObject Panel(RectTransform parent, string name, Vector2 pos, Vector2 size, Color c)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Image));
            g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            g.GetComponent<Image>().color = c;
            return g;
        }

        /// <summary>
        /// Right-hand glove in grip space: origin on the grip axis (local Y), +x = outward, -z = toward the driver.
        /// indexOnly builds just the index finger (a separate object so it can pull the paddle).
        /// </summary>
        static Mesh BuildGlove(bool indexOnly)
        {
            const int FAB = 0, PALM = 1, PIPE = 2;
            var k = new MeshKit(3);
            float[] ys = { 0.034f, 0.012f, -0.010f, -0.032f };
            float[] wraps = { 190f, 205f, 198f, 180f };
            float[] rads = { 0.0095f, 0.0102f, 0.0098f, 0.0088f };
            for (int f = 0; f < 4; f++)
            {
                if (indexOnly != (f == 0)) continue;
                var path = new List<Vector3>();
                for (float a = -25f; a <= wraps[f]; a += 15f)
                {
                    float t = a * Mathf.Deg2Rad;
                    float r = 0.046f - Mathf.Max(0f, a - 120f) * 0.00006f;
                    path.Add(new Vector3(r * Mathf.Cos(t), ys[f] - (a / 400f) * 0.004f, r * Mathf.Sin(t)));
                }
                k.Tube(FAB, path, rads[f], 10, 1f, true, tt => Mathf.Lerp(1.05f, 0.82f, tt));
                // grey grip patches on the finger pads (inside of the wrap)
                var pad = new List<Vector3>();
                for (float a = 30f; a <= wraps[f] - 20f; a += 15f)
                {
                    float t = a * Mathf.Deg2Rad;
                    pad.Add(new Vector3(0.0395f * Mathf.Cos(t), ys[f], 0.0395f * Mathf.Sin(t)));
                }
                k.Tube(PALM, pad, rads[f] * 0.55f, 8, 0.6f);
            }
            if (!indexOnly)
            {
                var back = new List<MeshKit.Sec>
                {
                    new MeshKit.Sec(0.030f, 0.056f, 0.000f, 0.016f, 0.044f, 0.044f, 2.6f),
                    new MeshKit.Sec(-0.010f, 0.064f, 0.000f, 0.023f, 0.048f, 0.046f, 3.0f),
                    new MeshKit.Sec(-0.055f, 0.066f, -0.003f, 0.023f, 0.044f, 0.042f, 3.0f),
                    new MeshKit.Sec(-0.090f, 0.060f, -0.005f, 0.021f, 0.036f, 0.034f, 2.6f),
                    new MeshKit.Sec(-0.130f, 0.056f, -0.005f, 0.028f, 0.040f, 0.040f, 2.4f),
                };
                k.Loft(FAB, back, 24, 4, capStartSub: FAB, capEndSub: FAB);
                // knuckle ridge + cuff band
                k.Tube(PALM, new List<Vector3> { new Vector3(0.074f, 0.04f, 0.012f), new Vector3(0.080f, 0.0f, 0.012f), new Vector3(0.074f, -0.04f, 0.012f) }, 0.007f, 8);
                var cuff = new List<Vector3>();
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f * Mathf.PI * 2f;
                    cuff.Add(new Vector3(0.056f + Mathf.Cos(t) * 0.029f, -0.005f + Mathf.Sin(t) * 0.041f, -0.112f));
                }
                k.Tube(PIPE, cuff, 0.005f, 6, 1f, false);
                // white piping seams over the back of the hand
                k.Tube(PIPE, new List<Vector3> { new Vector3(0.084f, 0.030f, -0.105f), new Vector3(0.0875f, 0.032f, -0.05f), new Vector3(0.086f, 0.030f, -0.005f), new Vector3(0.076f, 0.034f, 0.026f) }, 0.0019f, 6);
                k.Tube(PIPE, new List<Vector3> { new Vector3(0.084f, -0.026f, -0.105f), new Vector3(0.0875f, -0.026f, -0.05f), new Vector3(0.086f, -0.024f, -0.005f), new Vector3(0.076f, -0.03f, 0.026f) }, 0.0019f, 6);
                // thumb resting on the wheel face
                k.Tube(FAB, new List<Vector3> { new Vector3(0.055f, 0.028f, -0.050f), new Vector3(0.036f, 0.044f, -0.052f), new Vector3(0.010f, 0.050f, -0.050f), new Vector3(-0.012f, 0.046f, -0.044f) },
                    0.0115f, 10, 1f, true, tt => Mathf.Lerp(1.1f, 0.85f, tt));
            }
            return k.Build(indexOnly ? "Glove_Index" : "Glove");
        }

        static Text Label(RectTransform parent, string name, Font font, int size, Vector2 pos, Vector2 dim, TextAnchor anchor, Color color)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform;
            r.anchoredPosition = pos;
            r.sizeDelta = dim;
            var t = g.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // ================================================================= helpers
        static GameObject Child(Transform parent, string name, int layer)
        {
            var g = new GameObject(name);
            g.layer = layer;
            g.transform.SetParent(parent, false);
            return g;
        }

        static Mesh Save(Mesh mesh, string name)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/_Game", "Meshes");
            string path = $"{MeshDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
