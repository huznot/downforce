using UnityEditor;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>Generates tiling albedo + normal textures (carbon twill, asphalt, grass, kerb) and saves them as assets.</summary>
    public static class ProcTextures
    {
        const string Dir = "Assets/_Game/Textures";

        static (Texture2D, Texture2D) Cached(string a, string n)
        {
            var ta = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{a}.asset");
            var tn = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{n}.asset");
            return ta && tn ? (ta, tn) : (null, null);
        }

        public static (Texture2D albedo, Texture2D normal) Carbon()
        {
            var c0 = Cached("Carbon_Albedo", "Carbon_Normal"); if (c0.Item1) return c0;
            const int S = 256, C = 16;
            var h = new float[S, S];
            var col = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = x / C, j = y / C;
                    float u = (x % C + 0.5f) / C, v = (y % C + 0.5f) / C;
                    bool horiz = ((i + j) & 3) < 2;         // 2x2 twill
                    float bulge = horiz ? Mathf.Sin(Mathf.PI * v) : Mathf.Sin(Mathf.PI * u);
                    float fibre = Mathf.PerlinNoise(x * (horiz ? 0.05f : 0.9f), y * (horiz ? 0.9f : 0.05f)) * 0.15f;
                    h[x, y] = bulge * 0.8f + fibre;
                    float b = (horiz ? 0.075f : 0.115f) + fibre * 0.12f + bulge * 0.02f;
                    col[y * S + x] = new Color(b, b, b * 1.05f, 1f);
                }
            return (Save("Carbon_Albedo", col, S, false), Save("Carbon_Normal", NormalFromHeight(h, S, 2.5f), S, true));
        }

        public static (Texture2D albedo, Texture2D normal) Asphalt()
        {
            var c0 = Cached("Asphalt_Albedo", "Asphalt_Normal"); if (c0.Item1) return c0;
            const int S = 512;
            var h = new float[S, S];
            var col = new Color[S * S];
            var rnd = new System.Random(7);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float n = Fbm(x, y, S, 0.02f, 5);
                    float grain = (float)rnd.NextDouble();
                    float stone = grain > 0.93f ? 0.35f : grain > 0.7f ? 0.12f : 0f;
                    h[x, y] = n * 0.5f + stone + grain * 0.15f;
                    float g = 0.13f + n * 0.05f + stone * 0.25f + grain * 0.03f;
                    col[y * S + x] = new Color(g, g, g * 1.02f, 1f);
                }
            return (Save("Asphalt_Albedo", col, S, false), Save("Asphalt_Normal", NormalFromHeight(h, S, 3f), S, true));
        }

        public static (Texture2D albedo, Texture2D normal) Grass()
        {
            var c0 = Cached("Grass_Albedo", "Grass_Normal"); if (c0.Item1) return c0;
            const int S = 512;
            var h = new float[S, S];
            var col = new Color[S * S];
            var rnd = new System.Random(3);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float big = Fbm(x, y, S, 0.008f, 4);
                    float blade = (float)rnd.NextDouble();
                    h[x, y] = blade * 0.6f + big * 0.4f;
                    var c = Color.Lerp(new Color(0.16f, 0.26f, 0.08f), new Color(0.30f, 0.38f, 0.14f), big);
                    c *= 0.8f + blade * 0.35f;
                    c.a = 1f;
                    col[y * S + x] = c;
                }
            return (Save("Grass_Albedo", col, S, false), Save("Grass_Normal", NormalFromHeight(h, S, 1.5f), S, true));
        }

        /// <summary>Kerb: red/white blocks along v, with a raised ridge profile across u for the normal map.</summary>
        public static (Texture2D albedo, Texture2D normal) Kerb()
        {
            var c0 = Cached("Kerb_Albedo", "Kerb_Normal"); if (c0.Item1) return c0;
            const int S = 256;
            var h = new float[S, S];
            var col = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool red = y < S / 2;
                    float wear = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
                    Color c = red ? new Color(0.62f, 0.05f, 0.04f) : new Color(0.82f, 0.82f, 0.8f);
                    c = Color.Lerp(c, new Color(0.15f, 0.15f, 0.15f), Mathf.Clamp01(wear - 0.72f) * 2.5f);
                    c.a = 1f;
                    col[y * S + x] = c;
                    float u = x / (float)S;
                    h[x, y] = Mathf.Sin(u * Mathf.PI * 6f) * 0.5f + wear * 0.2f; // ridges across the kerb
                }
            return (Save("Kerb_Albedo", col, S, false), Save("Kerb_Normal", NormalFromHeight(h, S, 1.2f), S, true));
        }

        // ------------------------------------------------------------------ helpers
        static float Fbm(int x, int y, int size, float freq, int oct)
        {
            float sum = 0f, amp = 0.5f, f = freq, norm = 0f;
            for (int o = 0; o < oct; o++)
            {
                // Tile by sampling on a torus-ish blend of the four wrapped positions.
                float a = Mathf.PerlinNoise(x * f, y * f);
                float b = Mathf.PerlinNoise((x - size) * f, y * f);
                float c = Mathf.PerlinNoise(x * f, (y - size) * f);
                float d = Mathf.PerlinNoise((x - size) * f, (y - size) * f);
                float tx = x / (float)size, ty = y / (float)size;
                float v = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                sum += v * amp; norm += amp;
                amp *= 0.5f; f *= 2f;
            }
            return sum / norm;
        }

        static Color[] NormalFromHeight(float[,] h, int S, float strength)
        {
            var n = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = h[(x + 1) % S, y] - h[(x - 1 + S) % S, y];
                    float dy = h[x, (y + 1) % S] - h[x, (y - 1 + S) % S];
                    var v = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    n[y * S + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1f);
                }
            return n;
        }

        static Texture2D Save(string name, Color[] px, int S, bool linear)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game", "Textures");
            string path = $"{Dir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels(px);
            t.Apply(true);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }
    }
}
