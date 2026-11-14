using System.Collections.Generic;
using OWR.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace OWR.EditorTools
{
    /// <summary>
    /// Fictional team livery: renders sponsor wordmarks/shapes to transparent textures (UI rendered on black and
    /// on white to recover exact alpha), then places URP DecalProjectors on the car (nose, sidepods, engine cover,
    /// wings, fin, tyre sidewalls). Team "VANTA", title sponsor "KAIRO". All brands invented.
    /// </summary>
    public static class LiveryBuilder
    {
        const string Dir = "Assets/_Game/Textures/Livery";
        const string MatDir = "Assets/_Game/Materials/Livery";
        const int TempLayer = 31;

        static readonly Color Orange = new Color(1f, 0.36f, 0.04f);
        static readonly Color Red = new Color(0.9f, 0.08f, 0.1f);
        static readonly Color Cyan = new Color(0.1f, 0.85f, 0.95f);

        public static void Apply(GameObject car, CarController ctrl)
        {
            EnsureFolder(Dir); EnsureFolder(MatDir);
            InstallDecalFeature();

            var kairo = Logo("Kairo", 1024, 256, rt =>
            {
                Bar(rt, new Vector2(30, -78), new Vector2(760, 26), Red, -8f);
                Text(rt, "KAIRO", 200, Color.white, FontStyle.BoldAndItalic, Vector2.zero, new Vector2(1000, 230));
            });
            var veltro = Logo("Veltro", 1024, 256, rt =>
            {
                Bar(rt, new Vector2(0, 0), new Vector2(930, 190), Orange, 0f, -14f);
                Text(rt, "VELTRO", 150, Color.white, FontStyle.BoldAndItalic, Vector2.zero, new Vector2(1000, 220));
            });
            var meridian = Logo("Meridian", 1024, 256, rt =>
            {
                Text(rt, "MERIDIAN", 150, Color.white, FontStyle.Bold, new Vector2(0, 10), new Vector2(1000, 200));
                Bar(rt, new Vector2(0, -88), new Vector2(620, 10), Cyan, 0f);
            });
            var quanta = Logo("Quanta", 1024, 256, rt =>
            {
                Text(rt, "QUANTA", 170, Color.white, FontStyle.BoldAndItalic, new Vector2(-40, 0), new Vector2(900, 220));
                Dot(rt, new Vector2(400, 52), 46, Cyan);
            });
            var vanta = Logo("Vanta", 1024, 256, rt =>
            {
                Bar(rt, new Vector2(-380, 0), new Vector2(150, 34), Orange, 0f, 0f, 40f);
                Bar(rt, new Vector2(-380, -60), new Vector2(150, 34), Orange, 0f, 0f, -40f);
                Text(rt, "VANTA", 170, Color.white, FontStyle.Bold, new Vector2(80, 0), new Vector2(800, 220));
            });
            var number = Logo("Number17", 512, 512, rt =>
            {
                var t = Text(rt, "17", 380, Color.white, FontStyle.BoldAndItalic, Vector2.zero, new Vector2(500, 480));
                var o = t.gameObject.AddComponent<Outline>(); o.effectColor = Orange; o.effectDistance = new Vector2(10, -10);
            });
            var swoosh = Logo("Swoosh", 1024, 512, rt =>
            {
                Bar(rt, new Vector2(-60, 40), new Vector2(1500, 150), Orange, 0f, 0f, 14f);
                Bar(rt, new Vector2(-60, -70), new Vector2(1500, 34), Color.white, 0f, 0f, 14f);
            });
            var rescue = Logo("Rescue", 512, 256, rt =>
            {
                Bar(rt, Vector2.zero, new Vector2(500, 240), Red, 0f);
                Text(rt, "RESCUE", 110, Color.white, FontStyle.Bold, new Vector2(0, 30), new Vector2(500, 130));
                Text(rt, "PULL  >", 60, Color.white, FontStyle.Bold, new Vector2(0, -70), new Vector2(500, 80));
            });
            var hv = Logo("HighVoltage", 256, 256, rt =>
            {
                Bar(rt, Vector2.zero, new Vector2(240, 240), new Color(1f, 0.82f, 0.05f), 0f);
                Text(rt, "!", 200, new Color(0.05f, 0.05f, 0.05f), FontStyle.Bold, new Vector2(0, 0), new Vector2(240, 240));
                Text(rt, "HV", 46, new Color(0.05f, 0.05f, 0.05f), FontStyle.Bold, new Vector2(0, -92), new Vector2(240, 60));
            });
            var tyre = Logo("TyreBrand", 1024, 128, rt =>
            {
                Text(rt, "ORBITA   INTERMEDIATE", 92, new Color(0.95f, 0.95f, 0.95f), FontStyle.Bold, Vector2.zero, new Vector2(1020, 120));
            });

            var root = new GameObject("Livery").transform;
            root.SetParent(car.transform, false);
            root.gameObject.layer = car.layer;

            // ---- nose (read from the cockpit, like a title sponsor on a real car) ----
            Down(root, kairo, "Nose_Kairo", new Vector3(0f, 0.85f, 1.12f), new Vector2(0.46f, 0.115f), 0.42f, 1);
            Down(root, vanta, "Nose_Vanta", new Vector3(0f, 0.65f, 2.42f), new Vector2(0.24f, 0.06f), 0.4f, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                Side(root, number, "Nose_Number", s, new Vector3(0.5f, 0.33f, 2.05f), new Vector2(0.14f, 0.14f), 0.42f, 1);
                Side(root, swoosh, "Sidepod_Swoosh", s, new Vector3(1.05f, 0.40f, -0.30f), new Vector2(1.5f, 0.42f), 0.62f, 0);
                Side(root, veltro, "Sidepod_Veltro", s, new Vector3(1.05f, 0.47f, -0.05f), new Vector2(0.72f, 0.18f), 0.62f, 1);
                Side(root, meridian, "Cover_Meridian", s, new Vector3(0.62f, 0.72f, -1.15f), new Vector2(0.62f, 0.15f), 0.5f, 1);
                Side(root, number, "Fin_Number", s, new Vector3(0.40f, 0.85f, -1.75f), new Vector2(0.15f, 0.15f), 0.398f, 1);
                Side(root, meridian, "Endplate_Meridian", s, new Vector3(0.80f, 0.80f, -2.40f), new Vector2(0.44f, 0.11f), 0.33f, 1);
                Side(root, quanta, "FrontEndplate_Quanta", s, new Vector3(1.20f, 0.15f, 2.96f), new Vector2(0.36f, 0.09f), 0.3f, 1);
                Side(root, kairo, "Halo_Kairo", s, new Vector3(0.62f, 0.86f, 0.20f), new Vector2(0.26f, 0.065f), 0.32f, 1);
            }
            // inside the cockpit: rescue + high-voltage stickers on the side walls (projected outward from the centre)
            Side(root, rescue, "Cockpit_Rescue", 1, new Vector3(-0.05f, 0.60f, 0.38f), new Vector2(0.09f, 0.045f), 0.3f, 2);
            Side(root, hv, "Cockpit_HV", -1, new Vector3(-0.05f, 0.60f, 0.40f), new Vector2(0.05f, 0.05f), 0.3f, 2);
            // tub shoulders either side of the opening (read from the seat)
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                var g2 = new GameObject("Shoulder_Veltro") { layer = root.gameObject.layer };
                g2.transform.SetParent(root, false);
                g2.transform.localPosition = new Vector3(s2 * 0.40f, 0.95f, 0.62f);
                g2.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                var dp2 = g2.AddComponent<DecalProjector>();
                dp2.material = DecalMat(veltro, veltro.name, 1);
                dp2.size = new Vector3(0.22f, 0.055f, 0.45f);
                dp2.pivot = new Vector3(0f, 0f, 0.225f);
                dp2.drawDistance = 50f;
            }
            Down(root, quanta, "RearWing_Quanta", new Vector3(0f, 1.25f, -2.33f), new Vector2(0.86f, 0.2f), 0.45f, 1);
            Down(root, kairo, "Cover_Kairo", new Vector3(0f, 1.2f, -0.95f), new Vector2(0.2f, 0.62f), 0.5f, 1, rotateText: true);

            // ---- tyre sidewalls (rotate with the wheel) ----
            foreach (var w in ctrl.wheels)
            {
                float R = w.isFront ? ctrl.setup.frontRadius : ctrl.setup.rearRadius;
                float half = (w.isFront ? 0.30f : 0.40f) * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 2; k++)
                    {
                        var g = new GameObject("TyreDecal");
                        g.layer = car.layer;
                        g.transform.SetParent(w.spinVisual, false);
                        float ang = k * 180f;
                        Quaternion around = Quaternion.Euler(ang, 0f, 0f);
                        g.transform.localPosition = around * new Vector3(side * (half + 0.12f), R - 0.05f, 0f);
                        g.transform.localRotation = around * Quaternion.LookRotation(new Vector3(-side, 0f, 0f), Vector3.up);
                        var dp = g.AddComponent<DecalProjector>();
                        dp.material = DecalMat(tyre, "TyreBrand");
                        dp.size = new Vector3(0.30f, 0.04f, 0.14f);
                        dp.pivot = new Vector3(0f, 0f, 0.07f);
                        dp.drawDistance = 60f;
                    }
            }
        }

        /// <summary>Race-number decal (white italic digits with a coloured outline).</summary>
        public static Material NumberMat(int number, Color outline)
        {
            EnsureFolder(Dir); EnsureFolder(MatDir);
            var tex = Logo("Number" + number, 512, 512, rt =>
            {
                var t = Text(rt, number.ToString(), 380, Color.white, FontStyle.BoldAndItalic, Vector2.zero, new Vector2(500, 480));
                var o = t.gameObject.AddComponent<Outline>(); o.effectColor = outline; o.effectDistance = new Vector2(10, -10);
            });
            return DecalMat(tex, "Number" + number, 1);
        }

        /// <summary>Title-sponsor wordmark in white with an underline in the car's accent colour.</summary>
        public static Material BrandMat(string brand, Color accent)
        {
            EnsureFolder(Dir); EnsureFolder(MatDir);
            string key = "Brand_" + brand;
            var tex = Logo(key, 1024, 256, rt =>
            {
                Bar(rt, new Vector2(20, -82), new Vector2(720, 22), accent, -6f);
                Text(rt, brand.ToUpper(), 180, Color.white, FontStyle.BoldAndItalic, Vector2.zero, new Vector2(1000, 230));
            });
            return DecalMat(tex, key, 1);
        }

        // ------------------------------------------------------------------ projector helpers
        static void Down(Transform root, Texture2D tex, string name, Vector3 pos, Vector2 size, float depth, int order, bool rotateText = false)
        {
            var g = new GameObject(name) { layer = root.gameObject.layer };
            g.transform.SetParent(root, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.LookRotation(Vector3.down, rotateText ? Vector3.left : Vector3.forward);
            var dp = g.AddComponent<DecalProjector>();
            dp.material = DecalMat(tex, tex.name, order);
            dp.size = new Vector3(size.x, size.y, depth);
            dp.pivot = new Vector3(0f, 0f, depth * 0.5f);
            dp.drawDistance = 250f;
        }

        static void Side(Transform root, Texture2D tex, string name, int side, Vector3 posRight, Vector2 size, float depth, int order)
        {
            var g = new GameObject(name + (side < 0 ? "_L" : "_R")) { layer = root.gameObject.layer };
            g.transform.SetParent(root, false);
            g.transform.localPosition = new Vector3(posRight.x * side, posRight.y, posRight.z);
            g.transform.localRotation = Quaternion.LookRotation(new Vector3(-side, 0f, 0f), Vector3.up);
            var dp = g.AddComponent<DecalProjector>();
            dp.material = DecalMat(tex, tex.name, order);
            dp.size = new Vector3(size.x, size.y, depth);
            dp.pivot = new Vector3(0f, 0f, depth * 0.5f);
            dp.drawDistance = 250f;
        }

        internal static Material DecalMat(Texture2D tex, string name, int order = 1)
        {
            string path = $"{MatDir}/Decal_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Shader Graphs/Decal"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("Base_Map", tex);
            m.SetFloat("_DrawOrder", order);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void InstallDecalFeature()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = listField?.GetValue(urp) as ScriptableRendererData[];
            if (list == null || list.Length == 0) return;
            var data = list[0];
            foreach (var f in data.rendererFeatures)
                if (f is DecalRendererFeature) return;
            var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
            feature.name = "Decals";
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Insert(0, feature);
            var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (mapField?.GetValue(data) is List<long> map && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId))
                map.Insert(0, localId);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ logo rendering
        internal static Texture2D Logo(string name, int w, int h, System.Action<RectTransform> build)
        {
            string path = $"{Dir}/{name}.png";
            var camGo = new GameObject("LogoCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.cullingMask = 1 << TempLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            camGo.transform.position = new Vector3(0, -5000, 0);
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            var canvasGo = new GameObject("LogoCanvas", typeof(RectTransform));
            canvasGo.layer = TempLayer;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var content = new GameObject("Content", typeof(RectTransform));
            content.layer = TempLayer;
            content.transform.SetParent(canvasGo.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(w, h);
            build(crt);
            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = TempLayer;

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            Color[] onBlack = RenderOn(cam, rt, Color.black, w, h);
            Color[] onWhite = RenderOn(cam, rt, Color.white, w, h);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                float a = Mathf.Clamp01(1f - (onWhite[i].grayscale - onBlack[i].grayscale));
                Color c = a > 0.001f ? onBlack[i] / a : Color.clear;
                c.a = a;
                px[i] = c;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(canvasGo);
            Object.DestroyImmediate(camGo);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return result;
        }

        static Color[] RenderOn(Camera cam, RenderTexture rt, Color bg, int w, int h)
        {
            cam.backgroundColor = bg;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            var px = t.GetPixels();
            Object.DestroyImmediate(t);
            return px;
        }

        static Text Text(RectTransform parent, string s, int size, Color c, FontStyle style, Vector2 pos, Vector2 dim)
        {
            var g = new GameObject("T", typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform;
            r.anchoredPosition = pos; r.sizeDelta = dim;
            var t = g.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = s; t.fontSize = size; t.fontStyle = style; t.color = c;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Solid bar; shearX skews it into a parallelogram (via rotation of a child), rotZ rotates it.</summary>
        static void Bar(RectTransform parent, Vector2 pos, Vector2 size, Color c, float rotZ = 0f, float shearDeg = 0f, float rot = 0f)
        {
            var g = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform;
            r.anchoredPosition = pos; r.sizeDelta = size;
            r.localRotation = Quaternion.Euler(0, 0, rotZ + rot);
            if (Mathf.Abs(shearDeg) > 0.01f)
            {
                // fake a shear with a skewed scale: rotate, squash, rotate back
                r.localRotation = Quaternion.Euler(0, 0, shearDeg);
                r.sizeDelta = new Vector2(size.x * 0.97f, size.y);
            }
            g.GetComponent<Image>().color = c;
        }

        static void Dot(RectTransform parent, Vector2 pos, float d, Color c)
        {
            var g = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform;
            r.anchoredPosition = pos; r.sizeDelta = new Vector2(d, d);
            var img = g.GetComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            img.color = c;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
