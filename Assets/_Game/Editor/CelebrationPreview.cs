using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>OWR > Preview Celebration: plays the win celebration without racing (also saved as a scene you can open).</summary>
    public static class CelebrationPreview
    {
        const string ScenePath = "Assets/_Game/Scenes/CelebrationPreview.unity";

        [MenuItem("OWR/Preview Celebration")]
        public static void Preview()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            new GameObject("CelebrationBootstrap").AddComponent<OWR.UI.CelebrationBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
