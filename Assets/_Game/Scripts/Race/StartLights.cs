using UnityEngine;

namespace OWR.Race
{
    /// <summary>Real start lights: five columns of lamps on the gantry, each with an emissive lens and a red point light.</summary>
    public class StartLights : MonoBehaviour
    {
        public Renderer[] columns1;   // upper lamp per column
        public Renderer[] columns2;   // lower lamp per column
        public Light[] columnLights;
        public float lightIntensity = 6f;
        MaterialPropertyBlock mpb;
        static readonly int Em = Shader.PropertyToID("_EmissionColor");
        static readonly int Base = Shader.PropertyToID("_BaseColor");

        void Awake() { mpb = new MaterialPropertyBlock(); SetLit(0); }

        public void SetLit(int count)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            for (int i = 0; i < 5; i++)
            {
                bool on = i < count;
                Color e = on ? new Color(1f, 0.04f, 0.02f) * 14f : Color.black;
                Color b = on ? new Color(1f, 0.1f, 0.05f) : new Color(0.12f, 0.02f, 0.02f);
                foreach (var arr in new[] { columns1, columns2 })
                {
                    if (arr == null || i >= arr.Length || !arr[i]) continue;
                    mpb.SetColor(Em, e); mpb.SetColor(Base, b);
                    arr[i].SetPropertyBlock(mpb);
                }
                if (columnLights != null && i < columnLights.Length && columnLights[i]) columnLights[i].intensity = on ? lightIntensity : 0f;
            }
        }
    }
}
