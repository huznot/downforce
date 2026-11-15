using System.IO;
using UnityEditor;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>
    /// OWR > Build Website (WebGL): full PC quality tier, custom full-window template, gzip with decompression
    /// fallback (works on any static host), output in Builds/Website ready to upload.
    /// </summary>
    public static class WebBuild
    {
        const string RacePath = "Assets/_Game/Scenes/Race.unity";
        const string Out = "Builds/Website";

        [MenuItem("OWR/Build Website (WebGL)")]
        public static void Build()
        {
            if (!AssetDatabase.LoadAssetAtPath<OWR.AudioFX.AudioBank>("Assets/_Game/Resources/AudioBank.asset")) AudioBankBaker.Bake();
            SetPlatformQuality("WebGL", "PC");

            PlayerSettings.productName = "Downforce";
            PlayerSettings.companyName = "OWR";
            PlayerSettings.WebGL.template = "PROJECT:Downforce";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.maximumMemorySize = 4096;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            if (Directory.Exists(Out))   // clear the old build; the folder itself may be held open by Explorer, so keep it
                foreach (var e in Directory.GetFileSystemEntries(Out))
                    if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = BuildScenes(),
                locationPathName = Out,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            Debug.Log($"[OWR] WebGL build: {report.summary.result}  size {report.summary.totalSize / (1024 * 1024)} MB  errors {report.summary.totalErrors}");
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) WriteReadme();
        }

        /// <summary>Points a platform's default quality level at the named tier (e.g. WebGL -> PC).</summary>
        public static void SetPlatformQuality(string platform, string level)
        {
            int idx = System.Array.IndexOf(QualitySettings.names, level);
            if (idx < 0) return;
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(qs);
            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < map.arraySize; i++)
            {
                var e = map.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == platform) e.FindPropertyRelative("second").intValue = idx;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WriteReadme()
        {
            File.WriteAllText(Path.Combine(Out, "README.txt"),
@"DOWNFORCE - website build
=========================

Upload EVERYTHING in this folder (index.html, Build/, TemplateData if present) as-is.

itch.io
  1. Zip the CONTENTS of this folder (index.html must be at the top of the zip).
  2. New project > Kind: HTML > upload the zip > tick 'This file will be played in the browser'.
  3. Embed options: 'Click to launch in fullscreen' or set the viewport to 1280x720 and tick 'Fullscreen button'.
  4. Tick 'SharedArrayBuffer support' OFF (not needed).

GitHub Pages / Netlify / Cloudflare Pages / any static host
  Just upload the folder. The .gz files work even if the host doesn't send 'Content-Encoding: gzip'
  (decompression fallback is built in). If your host CAN send that header for *.gz, loading is faster.

Test locally
  Double-clicking index.html will NOT work (browsers block it). From this folder run:
      python -m http.server 8000
  then open http://localhost:8000

Controls: keyboard, mouse steering (Settings) or any gamepad. Click once to go fullscreen.
Best in Chrome or Edge. The game scales its resolution automatically to stay smooth.

Laptop running slowly? Most gaming laptops run the browser on the weak built-in graphics chip.
Windows Settings > System > Display > Graphics > add Chrome (or Edge) > Options > High performance,
then restart the browser. Add ?fps to the address to see the frame rate.

Privacy policy for the Google Play listing: privacy.html (in this folder) -> https://<your-site>/privacy.html
");
        }
    
        /// <summary>Interlagos first (the default circuit), then the classic track.</summary>
        public static string[] BuildScenes()
        {
            const string interlagos = "Assets/_Game/Scenes/Interlagos.unity";
            return System.IO.File.Exists(interlagos) ? new[] { interlagos, "Assets/_Game/Scenes/Race.unity" } : new[] { "Assets/_Game/Scenes/Race.unity" };
        }
    }
}
