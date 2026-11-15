using UnityEditor;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>
    /// OWR > Build Android (Google Play AAB): landscape, IL2CPP ARM64, Android App Bundle for the Play Console.
    /// Signing uses the keystore configured in Player Settings > Publishing Settings (create it there with the
    /// Keystore Manager and type the passwords yourself). Without one, a debug-signed test build is produced.
    /// </summary>
    public static class AndroidBuild
    {
        public const string PackageId = "com.owr.downforce";
        const string RacePath = "Assets/_Game/Scenes/Race.unity";

        static void Configure()
        {
            if (!AssetDatabase.LoadAssetAtPath<OWR.AudioFX.AudioBank>("Assets/_Game/Resources/AudioBank.asset")) AudioBankBaker.Bake();
            WebBuild.SetPlatformQuality("Android", "PC");   // same look as desktop; AdaptiveQuality keeps it smooth
            PlayerSettings.productName = "Downforce";
            PlayerSettings.companyName = "OWR";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, PackageId);
            PlayerSettings.bundleVersion = "1.0.1";
            if (PlayerSettings.Android.bundleVersionCode < 2) PlayerSettings.Android.bundleVersionCode = 2;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;   // highest installed (Play needs a current one)
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.Android.startInFullscreen = true;
            EditorUserBuildSettings.buildAppBundle = true;
            PlayerSettings.Android.splitApplicationBinary = false;
            // use Unity's prebuilt engine library: re-linking a stripped libunity needs several GB of RAM and
            // ran out of memory on this machine; the unstripped one only adds a few MB to the download
            PlayerSettings.stripEngineCode = false;
        }

        [MenuItem("OWR/Build Android (Google Play AAB)")]
        public static void BuildAab() => Build(true);

        [MenuItem("OWR/Build Android (test APK)")]
        public static void BuildApk() => Build(false);

        static void Build(bool aab)
        {
            Configure();
            EditorUserBuildSettings.buildAppBundle = aab;
            bool signed = PlayerSettings.Android.useCustomKeystore && !string.IsNullOrEmpty(PlayerSettings.Android.keystorePass);
            if (aab && !signed)
                Debug.LogWarning("[OWR] No upload keystore set (Player Settings > Publishing Settings > Keystore Manager). " +
                                 "Building a debug-signed AAB: fine for testing, Google Play will reject it.");
            string outPath = aab ? "Builds/Android/Downforce.aab" : "Builds/Android/Downforce-test.apk";
            System.IO.Directory.CreateDirectory("Builds/Android");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { RacePath },
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log($"[OWR] Android build ({(aab ? "AAB" : "APK")}, {(signed ? "upload-key signed" : "debug signed")}): {report.summary.result}  size {report.summary.totalSize / (1024 * 1024)} MB  errors {report.summary.totalErrors}");
        }
    }
}
