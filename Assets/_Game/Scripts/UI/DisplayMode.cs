using UnityEngine;
using UnityEngine.InputSystem;

namespace OWR.UI
{
    /// <summary>
    /// Desktop display handling: starts in borderless fullscreen at the monitor's native resolution (ignoring any
    /// stale windowed setting a previous build saved), and F11 / Alt+Enter toggles fullscreen <-> window anytime.
    /// </summary>
    public class DisplayMode : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer) return;
            if (FindFirstObjectByType<DisplayMode>()) return;
            var go = new GameObject("DisplayMode"); DontDestroyOnLoad(go); go.AddComponent<DisplayMode>();
            if (PlayerPrefs.GetInt("OWR_Windowed", 0) == 0) SetFullscreen(true);
        }

        static void SetFullscreen(bool on)
        {
            if (on)
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else
            {
                int w = Mathf.RoundToInt(Screen.currentResolution.width * 0.75f), h = Mathf.RoundToInt(Screen.currentResolution.height * 0.75f);
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
            PlayerPrefs.SetInt("OWR_Windowed", on ? 0 : 1); PlayerPrefs.Save();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool toggle = kb.f11Key.wasPressedThisFrame || (kb.enterKey.wasPressedThisFrame && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed));
            if (toggle) SetFullscreen(Screen.fullScreenMode == FullScreenMode.Windowed);
        }
    }
}
