using UnityEditor;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>
    /// Command-line release builds (editor closed):
    ///   Unity -batchmode -quit -projectPath . -buildTarget Win64 -executeMethod OWR.EditorTools.ReleaseBuild.Windows
    ///   Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod OWR.EditorTools.ReleaseBuild.Web
    /// </summary>
    public static class ReleaseBuild
    {
        public static void Windows()
        {
            CircuitBuilder.Build();                 // regenerate Interlagos (the scene is not stored in git)
            AssetDatabase.SaveAssets();
            MenuSceneBuilder.BuildPlayer();
            Debug.Log("[OWR] ReleaseBuild.Windows done");
        }

        public static void Web()
        {
            if (!System.IO.File.Exists(CircuitBuilder.ScenePath)) CircuitBuilder.Build();
            WebBuild.Build();
            Debug.Log("[OWR] ReleaseBuild.Web done");
        }
    }
}
