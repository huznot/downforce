using System.IO;
using UnityEditor.Android;

namespace OWR.EditorTools
{
    /// <summary>
    /// Adjusts the generated Gradle project: skips Android's release "lint vital" pass, which resolves and writes extra
    /// tooling into the user Gradle cache on every build (it failed when the system drive was full) and adds nothing
    /// to the shipped bundle.
    /// </summary>
    public class AndroidGradleTweaks : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string root = Path.GetDirectoryName(unityLibraryPath.TrimEnd('/', '\\'));
            foreach (var module in new[] { "launcher", "unityLibrary" })
            {
                string f = Path.Combine(root, module, "build.gradle");
                if (!File.Exists(f)) continue;
                string s = File.ReadAllText(f);
                if (s.Contains("OWR: no lint")) continue;
                s += "\n// OWR: no lint\nandroid {\n    lint {\n        checkReleaseBuilds false\n        abortOnError false\n    }\n}\n";
                File.WriteAllText(f, s);
            }
        }
    }
}
