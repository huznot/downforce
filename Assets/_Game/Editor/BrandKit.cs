using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OWR.EditorTools
{
    /// <summary>
    /// Sponsor logos from the brand atlas (Textures/BrandAtlas.png, drawn by Tools/make_brands.py): 16 invented brands,
    /// column 0 = full-colour board, column 1 = white painted version with alpha. Quads are merged into one mesh per
    /// group so hundreds of boards cost a handful of draw calls.
    /// </summary>
    public class BrandKit
    {
        public const int Count = 16;
        const string Root = "Assets/_Game";
        const string AtlasPath = Root + "/Textures/BrandAtlas.png";

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public static Texture2D Atlas()
        {
            var imp = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (imp && (imp.mipmapEnabled == false || imp.anisoLevel < 8 || !imp.alphaIsTransparency || imp.maxTextureSize < 2048))
            {
                imp.mipmapEnabled = true; imp.anisoLevel = 8; imp.alphaIsTransparency = true; imp.maxTextureSize = 2048;
                imp.wrapMode = TextureWrapMode.Clamp; imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        }

        /// <summary>
        /// LED board material: the logo atlas behind a glossy clear front, a fine LED pixel grid up close (detail albedo +
        /// detail normal, fades out with distance through mipmaps) and a soft backlit glow like real trackside screens.
        /// </summary>
        public static Material BoardMat()
        {
            var m = EnvironmentBuilder.GetMat(Root + "/Materials/Brand_Boards.mat", "Universal Render Pipeline/Lit");
            var t = Atlas();
            m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.92f));
            m.SetFloat("_Smoothness", 0.82f); m.SetFloat("_Metallic", 0.05f);
            m.SetFloat("_SpecularHighlights", 1f); m.SetFloat("_EnvironmentReflections", 1f);
            m.EnableKeyword("_EMISSION"); m.SetTexture("_EmissionMap", t); m.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.55f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var (alb, nrm) = LedDetail();
            m.SetTexture("_DetailAlbedoMap", alb); m.SetTexture("_DetailNormalMap", nrm);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(900f, 1000f));
            m.SetFloat("_DetailAlbedoMapScale", 1f); m.SetFloat("_DetailNormalMapScale", 0.6f);
            m.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Brushed aluminium frame for boards, fascias and bridge banners.</summary>
        public static Material FrameMat()
        {
            var m = EnvironmentBuilder.GetMat(Root + "/Materials/Board_Frame.mat", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", new Color(0.42f, 0.43f, 0.45f)); m.SetFloat("_Metallic", 0.85f); m.SetFloat("_Smoothness", 0.58f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>One LED cell per 16x16 texels: a domed pixel (normal) with dark gaps between pixels (albedo, x2 detail).</summary>
        static (Texture2D albedo, Texture2D normal) LedDetail()
        {
            string ap = Root + "/Textures/LED_DetailAlbedo.png", np = Root + "/Textures/LED_DetailNormal.png";
            var a0 = AssetDatabase.LoadAssetAtPath<Texture2D>(ap); var n0 = AssetDatabase.LoadAssetAtPath<Texture2D>(np);
            if (a0 && n0) return (a0, n0);
            const int S = 16;
            var alb = new Texture2D(S, S, TextureFormat.RGBA32, true, true);
            var nor = new Texture2D(S, S, TextureFormat.RGBA32, true, true);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    bool gap = Mathf.Abs(u) > 0.78f || Mathf.Abs(v) > 0.78f;
                    float g = gap ? 0.3f : Mathf.Lerp(0.62f, 0.5f, r);   // 0.5 = unchanged under x2 detail
                    alb.SetPixel(x, y, new Color(g, g, g, 1f));
                    // dome normal (pixels bulge slightly), flat in the gaps
                    Vector3 n = gap ? Vector3.forward : new Vector3(u * 0.45f, v * 0.45f, 1f).normalized;
                    nor.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
                }
            alb.Apply(); nor.Apply();
            System.IO.File.WriteAllBytes(ap, alb.EncodeToPNG()); System.IO.File.WriteAllBytes(np, nor.EncodeToPNG());
            AssetDatabase.ImportAsset(ap); AssetDatabase.ImportAsset(np);
            var ai = (TextureImporter)AssetImporter.GetAtPath(ap); ai.sRGBTexture = false; ai.wrapMode = TextureWrapMode.Repeat; ai.filterMode = FilterMode.Trilinear; ai.SaveAndReimport();
            var ni = (TextureImporter)AssetImporter.GetAtPath(np); ni.textureType = TextureImporterType.NormalMap; ni.wrapMode = TextureWrapMode.Repeat; ni.filterMode = FilterMode.Trilinear; ni.SaveAndReimport();
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(ap), AssetDatabase.LoadAssetAtPath<Texture2D>(np));
        }

        /// <summary>Painted logos (white, alpha-clipped) for run-off tarmac, bridges and wall tops.</summary>
        public static Material PaintMat()
        {
            var m = EnvironmentBuilder.GetMat(Root + "/Materials/Brand_Paint.mat", "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", Atlas()); m.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.9f));
            m.SetFloat("_Smoothness", 0.7f);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Adds one logo quad (single-sided, facing +normal = Cross(right, up)).</summary>
        public void Quad(Vector3 centre, Vector3 right, Vector3 up, float width, float height, int brand, bool painted)
        {
            brand = ((brand % Count) + Count) % Count;
            float u0 = painted ? 0.5f : 0f, u1 = u0 + 0.5f;
            float v1 = 1f - brand / (float)Count, v0 = v1 - 1f / Count;
            float inset = 0.5f / 2048f; u0 += inset; u1 -= inset; v0 += inset; v1 -= inset;
            Vector3 r = right.normalized * width * 0.5f, u = up.normalized * height * 0.5f;
            Vector3 n = Vector3.Cross(up, right).normalized;   // Unity is left-handed: this points toward the viewer
            int b = verts.Count;
            verts.Add(centre - r - u); uvs.Add(new Vector2(u0, v0));
            verts.Add(centre + r - u); uvs.Add(new Vector2(u1, v0));
            verts.Add(centre + r + u); uvs.Add(new Vector2(u1, v1));
            verts.Add(centre - r + u); uvs.Add(new Vector2(u0, v1));
            for (int i = 0; i < 4; i++) norms.Add(n);
            tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        public int QuadCount => verts.Count / 4;

        public GameObject Build(string name, Transform parent, Material mat)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            g.isStatic = true;
            return g;
        }
    }
}
