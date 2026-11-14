using UnityEngine;
using UnityEngine.SceneManagement;

namespace OWR.UI
{
    /// <summary>Lives in the CelebrationPreview scene: flags the preview and loads the race scene straight into the win celebration.</summary>
    public class CelebrationBootstrap : MonoBehaviour
    {
        public string raceScene = "Race";
        void Start()
        {
            Race.GameOptions.CelebrationPreview = true;
            SceneManager.LoadScene(raceScene);
        }
    }
}
