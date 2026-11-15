using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OWR.UI
{
    /// <summary>
    /// Title screen + main menu, all centred: chunky cream condensed title, "PRESS ANY BUTTON", then
    /// RACE / HOW TO PLAY / QUIT and a tutorial page. Runs over the live race scene (drone shots behind it);
    /// RACE invokes onStartRace (or loads raceScene when used standalone). Gamepad / keyboard / mouse.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public Font titleFont, uiFont;
        public string raceScene = "";
        public string gameTitle = "DOWNFORCE";
        public string subtitle = "WET  RACE";
        public Transform cameraRig;
        public AudioSource music;
        public System.Action onStartRace;

        static readonly Color Cream = new Color(0.98f, 0.84f, 0.72f);
        static readonly Color CreamShadow = new Color(0.25f, 0.14f, 0.12f, 0.9f);
        static readonly Color Accent = new Color(1f, 0.42f, 0.12f);

        enum Page { Title, Menu, Settings, HowTo, Loading }
        Page page = Page.Title;
        RectTransform root;
        CanvasGroup titleGroup, pressGroup, menuGroup, howGroup, fadeGroup, settingsGroup;
        GameObject firstSettingsButton;
        GameObject firstMenuButton, howBack, shade;

        void Start()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Build();
            StartCoroutine(FadeIn());
            if (music) { music.volume = 0f; music.loop = true; music.Play(); StartCoroutine(FadeMusic(0.75f, 3f)); }
            if (Race.GameOptions.AutoDrive) StartCoroutine(AutoFlow());
            if (Race.GameOptions.ReturnToMenuOnce) { Race.GameOptions.ReturnToMenuOnce = false; Show(Page.Menu); }
        }

        IEnumerator FadeMusic(float to, float seconds)
        {
            float from = music.volume;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) { music.volume = Mathf.Lerp(from, to, t / seconds); yield return null; }
            music.volume = to;
            if (to <= 0f) music.Stop();
        }

        IEnumerator AutoFlow()
        {
            yield return new WaitForSeconds(10f);
            Show(Page.Menu);
            yield return new WaitForSeconds(3f);
            StartCoroutine(StartRace());
        }

        IEnumerator FadeIn()
        {
            fadeGroup.alpha = 1f;
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { fadeGroup.alpha = 1f - t / 1.5f; yield return null; }
            fadeGroup.alpha = 0f; fadeGroup.blocksRaycasts = false;
        }

        void Build()
        {
            var cg = new GameObject("MenuCanvas", typeof(RectTransform));
            cg.transform.SetParent(transform, false);
            var canvas = cg.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var sc = cg.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            cg.AddComponent<GraphicRaycaster>();
            root = (RectTransform)cg.transform;
            if (!FindFirstObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            // gentle darkening so the text reads over any drone shot
            shade = Img(root, "Shade", Vector2.zero, new Vector2(4000, 1400), new Color(0f, 0f, 0f, 0.25f)).gameObject;

            var tb = Rect(root, "Title", new Vector2(0, 170), new Vector2(1600, 420));
            titleRect = tb;
            titleGroup = tb.gameObject.AddComponent<CanvasGroup>();
            var title = Label(tb, "Name", gameTitle, titleFont, 260, new Vector2(0, 40), new Vector2(1600, 280), Cream);
            var sh = title.gameObject.AddComponent<Shadow>(); sh.effectColor = CreamShadow; sh.effectDistance = new Vector2(8, -8);
            Img(tb, "Rule", new Vector2(0, -104), new Vector2(560, 5), Accent);
            var sub = Label(tb, "Sub", subtitle, titleFont, 76, new Vector2(0, -168), new Vector2(1000, 90), Cream);
            sub.gameObject.AddComponent<Shadow>().effectColor = CreamShadow;

            var pg = Rect(root, "Press", new Vector2(0, -170), new Vector2(1200, 80));
            pressGroup = pg.gameObject.AddComponent<CanvasGroup>();
            var press = Label(pg, "Text", Input.TouchControls.IsTouchDevice ? "T A P   T O   S T A R T" : "P R E S S   A N Y   B U T T O N", uiFont, 40, Vector2.zero, new Vector2(1200, 80), Cream);
            press.gameObject.AddComponent<Shadow>().effectColor = CreamShadow;

            // ---- main page: modes first ----
            var mg = Rect(root, "Menu", new Vector2(-560, -90), new Vector2(800, 520));
            menuGroup = mg.gameObject.AddComponent<CanvasGroup>();
            var trackBtn = Option(mg, new Vector2(0, 255), TrackText, () =>
            {
                Race.GameOptions.TrackIndex = (Race.GameOptions.TrackIndex + 1) % Race.GameOptions.TrackScenes.Length;
                string scene = Race.GameOptions.TrackScenes[Race.GameOptions.TrackIndex];
                if (Application.CanStreamedLevelBeLoaded(scene)) { Race.GameOptions.ReturnToMenuOnce = true; StartCoroutine(SwitchTrack(scene)); }
            });
            ((RectTransform)trackBtn.transform).sizeDelta = new Vector2(620, 60);
            trackBtn.GetComponentInChildren<Text>().fontSize = 40;
            firstMenuButton = MenuButton(mg, "RACE", new Vector2(0, 160), () => { Race.GameOptions.Mode = Race.GameMode.Race; StartCoroutine(StartRace()); }).gameObject;
            MenuButton(mg, "FREE PRACTICE", new Vector2(0, 75), () => { Race.GameOptions.Mode = Race.GameMode.Practice; StartCoroutine(StartRace()); });
            MenuButton(mg, "SETTINGS", new Vector2(0, -10), () => Show(Page.Settings));
            MenuButton(mg, "HOW TO PLAY", new Vector2(0, -95), () => Show(Page.HowTo));
#if !UNITY_WEBGL
            MenuButton(mg, "QUIT", new Vector2(0, -180), () => Application.Quit());
#endif
            SetGroup(menuGroup, false);

            // ---- settings page (each row cycles on press) ----
            var sg = Rect(root, "Settings", new Vector2(-480, -90), new Vector2(900, 560));
            settingsGroup = sg.gameObject.AddComponent<CanvasGroup>();
            firstSettingsButton = Option(sg, new Vector2(0, 200), LapsText, () =>
            {
                var c = Race.GameOptions.LapChoices;
                int i = System.Array.IndexOf(c, Race.GameOptions.Laps);
                Race.GameOptions.Laps = c[(i + 1 + c.Length) % c.Length];
            }).gameObject;
            Option(sg, new Vector2(0, 130), DiffText, () => Race.GameOptions.Difficulty = (Race.Difficulty)(((int)Race.GameOptions.Difficulty + 1) % 3));
            Option(sg, new Vector2(0, 60), GearText, () => Race.GameOptions.AutoGears = !Race.GameOptions.AutoGears);
            if (Input.TouchControls.IsTouchDevice)
                Option(sg, new Vector2(0, -10), TiltText, () => Race.GameOptions.TiltSteering = !Race.GameOptions.TiltSteering);
            else
                Option(sg, new Vector2(0, -10), MouseText, () => Race.GameOptions.MouseSteering = !Race.GameOptions.MouseSteering);
            Option(sg, new Vector2(0, -80), CameraText, () => Race.GameOptions.CameraMode = (Race.GameOptions.CameraMode + 1) % 3);
            MenuButton(sg, "BACK", new Vector2(0, -175), () => Show(Page.Menu));
            SetGroup(settingsGroup, false);

            var hg = Rect(root, "HowTo", Vector2.zero, new Vector2(1500, 880));
            howGroup = hg.gameObject.AddComponent<CanvasGroup>();
            Img(hg, "Bg", Vector2.zero, new Vector2(1500, 880), new Color(0.04f, 0.05f, 0.08f, 0.9f));
            Img(hg, "Acc", new Vector2(0, 437), new Vector2(1500, 6), Accent);
            Label(hg, "Title", "HOW TO PLAY", titleFont, 110, new Vector2(0, 360), new Vector2(1400, 120), Cream);
            bool touch = Input.TouchControls.IsTouchDevice;
            Label(hg, "Pad", touch ? "TOUCH" : "GAMEPAD", titleFont, 54, new Vector2(-370, 260), new Vector2(600, 60), Accent);
            Label(hg, "PadTxt", touch
                ? "Tilt your phone like a wheel to steer\n(or switch to arrow buttons in Settings)\nRight pad  Throttle\nLeft pad  Brake\nGears shift automatically\n" +
                  "CAM  Cockpit / T-cam / chase\nRESET  Back on track\nII  Pause"
                : "RT  Throttle      LT  Brake\nLeft stick  Steer\nRB / LB  Shift up / down\nRight stick  Look around\nY  Cockpit / T-cam / chase\n" +
                  "D-pad L / R  Brake bias\nD-pad down  Traction control\nD-pad up  ERS mode\nB  Overtake boost      X  Pit limiter\nView  Reset car      Menu  Pause",
                uiFont, 32, new Vector2(-370, 40), new Vector2(680, 400), Color.white);
            Label(hg, "Kb", touch ? "CONTROLLER" : "KEYBOARD", titleFont, 54, new Vector2(370, 260), new Vector2(600, 60), Accent);
            Label(hg, "KbTxt", touch
                ? "Bluetooth gamepads work too:\nRT  Throttle      LT  Brake\nLeft stick  Steer\nY  Camera\nView  Reset car\nMenu  Pause"
                : "W / S  Throttle / brake\nA / D  Steer\nE / Q  Shift up / down\nRight mouse  Look around\nC  Camera (cockpit / T-cam / chase)\n[ ]  Brake bias      T  Traction control\nM  ERS mode\nO / P  Overtake / pit limiter\nR  Reset car      Esc  Pause\nF11  Fullscreen",
                uiFont, 32, new Vector2(370, 40), new Vector2(680, 400), Color.white);
            var tips = Label(hg, "Tips",
                "Wet race, your choice of laps and rivals in Settings. Wait for five red lights - when they go out, GO.\n" +
                "Brake in a straight line and squeeze the throttle out of slow corners. Hit someone and the stewards hand you a penalty.",
                uiFont, 28, new Vector2(0, -250), new Vector2(1350, 110), new Color(0.82f, 0.85f, 0.92f));
            tips.horizontalOverflow = HorizontalWrapMode.Wrap;
            howBack = MenuButton(hg, "BACK", new Vector2(0, -360), () => Show(Page.Menu)).gameObject;
            SetGroup(howGroup, false);

            var fg = Rect(root, "Fade", Vector2.zero, Vector2.zero);
            fg.anchorMin = Vector2.zero; fg.anchorMax = Vector2.one;
            fadeGroup = fg.gameObject.AddComponent<CanvasGroup>();
            var fimg = fg.gameObject.AddComponent<Image>(); fimg.color = Color.black; fimg.raycastTarget = false;
        }

        static string TrackText() => "TRACK:  " + Race.GameOptions.TrackLabels[Race.GameOptions.TrackIndex];

        IEnumerator SwitchTrack(string scene)
        {
            for (float t = 0; t < 0.35f; t += Time.unscaledDeltaTime) { fadeGroup.alpha = t / 0.35f; yield return null; }
            fadeGroup.alpha = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        }

        static string LapsText() => "RACE LAPS:  " + Race.GameOptions.Laps;
        static string CameraText() => "CAMERA:  " + (new[] { "COCKPIT", "T-CAM", "CHASE" })[Race.GameOptions.CameraMode];

        /// <summary>Settings row: shows a live value and cycles it when pressed.</summary>
        Button Option(RectTransform parent, Vector2 pos, System.Func<string> text, System.Action cycle)
        {
            Text label = null;
            var b = MenuButton(parent, "Opt" + pos.y, pos, () => { cycle(); label.text = text(); });
            label = b.GetComponentInChildren<Text>(); label.fontSize = 46; label.text = text();
            ((RectTransform)b.transform).sizeDelta = new Vector2(820, 70); ((RectTransform)label.transform).sizeDelta = new Vector2(820, 70);
            return b;
        }

        static string DiffText() => "DIFFICULTY:  " + Race.GameOptions.Difficulty.ToString().ToUpper();
        static string MouseText() => "MOUSE STEERING:  " + (Race.GameOptions.MouseSteering ? "ON" : "OFF");
        static string TiltText() => "STEERING:  " + (Race.GameOptions.TiltSteering ? "TILT" : "BUTTONS");
        static string GearText() => "GEARS:  " + (Race.GameOptions.AutoGears ? "AUTOMATIC" : "MANUAL");

        RectTransform titleRect;

        void LateUpdate()
        {
            if (!titleRect) return;
            bool compact = page == Page.Menu || page == Page.Settings;
            Vector2 pos = compact ? new Vector2(-560, 330) : new Vector2(0, 170);
            float scale = compact ? 0.5f : 1f;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f);
            titleRect.anchoredPosition = Vector2.Lerp(titleRect.anchoredPosition, pos, k);
            titleRect.localScale = Vector3.one * Mathf.Lerp(titleRect.localScale.x, scale, k);
        }

        void Update()
        {
            if (page == Page.Title)
            {
                pressGroup.alpha = 0.55f + 0.45f * Mathf.Sin(Time.time * 2.4f);
                if (AnyPressed()) Show(Page.Menu);
            }
            else if (page == Page.HowTo || page == Page.Settings)
            {
                var gp = Gamepad.current; var kb = Keyboard.current;
                if ((gp != null && gp.buttonEast.wasPressedThisFrame) || (kb != null && kb.escapeKey.wasPressedThisFrame)) Show(Page.Menu);
            }
        }

        static bool AnyPressed()
        {
            var kb = Keyboard.current; var gp = Gamepad.current; var m = Mouse.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            if (m != null && m.leftButton.wasPressedThisFrame) return true;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) return true;
            if (gp != null)
                foreach (var c in gp.allControls)
                    if (c is UnityEngine.InputSystem.Controls.ButtonControl b && b.wasPressedThisFrame) return true;
            return false;
        }

        void Show(Page p)
        {
            page = p;
            pressGroup.alpha = p == Page.Title ? 1f : 0f;
            SetGroup(menuGroup, p == Page.Menu);
            SetGroup(howGroup, p == Page.HowTo);
            SetGroup(settingsGroup, p == Page.Settings);
            if (p == Page.Settings) EventSystem.current?.SetSelectedGameObject(firstSettingsButton);
            titleGroup.alpha = p == Page.HowTo ? 0f : 1f;
            if (p == Page.Menu) EventSystem.current?.SetSelectedGameObject(firstMenuButton);
            if (p == Page.HowTo) EventSystem.current?.SetSelectedGameObject(howBack);
        }

        IEnumerator StartRace()
        {
            if (page == Page.Loading) yield break;
            page = Page.Loading;
            SetGroup(menuGroup, false);
            if (music) StartCoroutine(FadeMusic(0f, 1.5f));
            fadeGroup.blocksRaycasts = true;
            for (float t = 0; t < 0.8f; t += Time.deltaTime) { fadeGroup.alpha = t / 0.8f; yield return null; }
            if (onStartRace != null)
            {
                titleGroup.alpha = 0f; pressGroup.alpha = 0f; shade.SetActive(false);
                onStartRace();
                for (float t = 0; t < 0.8f; t += Time.deltaTime) { fadeGroup.alpha = 1f - t / 0.8f; yield return null; }
                Destroy(root.gameObject);
            }
            else if (!string.IsNullOrEmpty(raceScene)) SceneManager.LoadScene(raceScene);
        }

        // ================================================================= ui helpers (everything centred)
        Button MenuButton(RectTransform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction a)
        {
            var r = Rect(parent, "Btn_" + label, pos, new Vector2(620, 92));
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            var b = r.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = new Color(0, 0, 0, 0);
            cb.highlightedColor = new Color(0f, 0f, 0f, 0.35f);
            cb.selectedColor = new Color(0f, 0f, 0f, 0.35f);
            cb.pressedColor = new Color(0f, 0f, 0f, 0.6f);
            b.colors = cb;
            b.onClick.AddListener(a);
            var t = Label(r, "Label", label, titleFont, 64, Vector2.zero, new Vector2(620, 92), Cream);
            t.gameObject.AddComponent<Shadow>().effectColor = CreamShadow;
            var marker = Img(r, "Marker", new Vector2(0, -44), new Vector2(0, 5), Accent);
            var sel = r.gameObject.AddComponent<SelectionMarker>(); sel.marker = marker; sel.label = t;
            return b;
        }

        static RectTransform Rect(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos; r.sizeDelta = size;
            return r;
        }

        static Image Img(RectTransform parent, string name, Vector2 pos, Vector2 size, Color c)
        {
            var r = Rect(parent, name, pos, size);
            var i = r.gameObject.AddComponent<Image>(); i.color = c; i.raycastTarget = false;
            return i;
        }

        static Text Label(RectTransform parent, string name, string text, Font f, int size, Vector2 pos, Vector2 dim, Color c)
        {
            var r = Rect(parent, name, pos, dim);
            var t = r.gameObject.AddComponent<Text>();
            t.font = f ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter; t.color = c; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.05f;
            return t;
        }

        static void SetGroup(CanvasGroup g, bool on) { g.alpha = on ? 1f : 0f; g.interactable = on; g.blocksRaycasts = on; }
    }

    /// <summary>Shows the accent underline and brightens the label when its button is selected.</summary>
    public class SelectionMarker : MonoBehaviour
    {
        public Image marker; public Text label;
        void Update()
        {
            bool sel = EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject;
            if (marker) marker.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(marker.rectTransform.sizeDelta.x, sel ? 260f : 0f, Time.unscaledDeltaTime * 12f), 5f);
            if (label) label.color = sel ? Color.white : new Color(0.98f, 0.84f, 0.72f);
        }
    }
}
