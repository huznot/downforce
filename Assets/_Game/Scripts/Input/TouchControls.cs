using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OWR.Input
{
    /// <summary>
    /// On-screen driving controls for phones/tablets (created automatically on touch devices).
    /// Steering: tilt the device like a wheel (default) or ◀ ▶ buttons. Big THROTTLE pad bottom-right, BRAKE pad
    /// bottom-left (next to the arrows in button mode). Top-right: pause, camera, reset.
    /// Reads raw touches every frame (true multi-touch) and hit-tests the pads; DriverInput merges the values.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static float Throttle, Brake, Steer;
        static bool pause, reset, cam;
        public static bool ConsumePause() { bool v = pause; pause = false; return v; }
        public static bool ConsumeReset() { bool v = reset; reset = false; return v; }
        public static bool ConsumeCamera() { bool v = cam; cam = false; return v; }

        /// <summary>True on phones/tablets (and touch-only browsers) – also used to adapt menus and hints.</summary>
        public static bool IsTouchDevice => Application.isMobilePlatform || (Touchscreen.current != null && Keyboard.current == null);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!IsTouchDevice) return;
            if (FindFirstObjectByType<TouchControls>()) return;
            new GameObject("TouchControls").AddComponent<TouchControls>();
        }

        RectTransform root, throttlePad, brakePad, leftPad, rightPad, pauseBtn, camBtn, resetBtn;
        Canvas canvas;
        CanvasGroup group;
        float touchSteer;
        bool tiltMode;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Active = true;
            if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
            if (GravitySensor.current != null) InputSystem.EnableDevice(GravitySensor.current);
            Build();
        }

        void OnDestroy() { Active = false; Throttle = Brake = Steer = 0f; }

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
            group = gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            root = (RectTransform)transform;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            throttlePad = Pad("THROTTLE", new Vector2(1, 0), new Vector2(-60, 60), new Vector2(330, 420), new Color(0.2f, 0.85f, 0.35f), font);
            brakePad = Pad("BRAKE", new Vector2(0, 0), new Vector2(60, 60), new Vector2(330, 300), new Color(0.95f, 0.25f, 0.2f), font);
            leftPad = Pad("◀", new Vector2(0, 0), new Vector2(60, 60), new Vector2(230, 300), new Color(1f, 1f, 1f), font);
            rightPad = Pad("▶", new Vector2(0, 0), new Vector2(310, 60), new Vector2(230, 300), new Color(1f, 1f, 1f), font);
            pauseBtn = Pad("II", new Vector2(1, 1), new Vector2(-40, -40), new Vector2(130, 130), Color.white, font);
            camBtn = Pad("CAM", new Vector2(1, 1), new Vector2(-190, -40), new Vector2(130, 130), Color.white, font);
            resetBtn = Pad("RESET", new Vector2(1, 1), new Vector2(-340, -40), new Vector2(130, 130), new Color(1f, 0.75f, 0.2f), font);
        }

        RectTransform Pad(string label, Vector2 anchor, Vector2 offset, Vector2 size, Color c, Font font)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = offset; rt.sizeDelta = size;
            var img = go.GetComponent<Image>(); img.color = new Color(c.r, c.g, c.b, 0.16f); img.raycastTarget = false;
            var outline = go.AddComponent<Outline>(); outline.effectColor = new Color(c.r, c.g, c.b, 0.55f); outline.effectDistance = new Vector2(3, -3);
            var t = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var trt = (RectTransform)t.transform; trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tx = t.GetComponent<Text>(); tx.text = label; tx.font = font; tx.fontSize = label.Length > 2 ? 34 : 60;
            tx.alignment = TextAnchor.MiddleCenter; tx.color = new Color(1f, 1f, 1f, 0.8f); tx.raycastTarget = false;
            return rt;
        }

        static void Light(RectTransform r, bool on)
        {
            var img = r.GetComponent<Image>(); var c = img.color; c.a = on ? 0.42f : 0.16f; img.color = c;
        }

        bool Hit(RectTransform r, Vector2 screen) => r.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(r, screen, null);

        void Update()
        {
            var rm = Race.RaceManager.Instance;
            bool driving = rm && !rm.waitForStart && !rm.CelebrationPreviewMode && Time.timeScale > 0.01f;
            bool show = driving && rm.State != Race.RaceManager.RaceState.Finished;
            canvas.enabled = show;
            if (!show) { Throttle = Brake = Steer = 0f; return; }

            tiltMode = Race.GameOptions.TiltSteering;
            leftPad.gameObject.SetActive(!tiltMode); rightPad.gameObject.SetActive(!tiltMode);
            brakePad.anchoredPosition = tiltMode ? new Vector2(60, 60) : new Vector2(560, 60);
            resetBtn.gameObject.SetActive(rm.Practice || rm.OffTrackTime > 1f || rm.State == Race.RaceManager.RaceState.Racing);

            bool thr = false, brk = false, left = false, right = false, pauseHit = false, camHit = false, resetHit = false;
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    Vector2 p = t.position.ReadValue();
                    bool began = t.press.wasPressedThisFrame;
                    if (Hit(throttlePad, p)) thr = true;
                    else if (Hit(brakePad, p)) brk = true;
                    else if (Hit(leftPad, p)) left = true;
                    else if (Hit(rightPad, p)) right = true;
                    else if (began && Hit(pauseBtn, p)) pauseHit = true;
                    else if (began && Hit(camBtn, p)) camHit = true;
                    else if (began && Hit(resetBtn, p)) resetHit = true;
                }
            if (pauseHit) pause = true;
            if (camHit) cam = true;
            if (resetHit) reset = true;

            float dt = Time.unscaledDeltaTime;
            Throttle = Mathf.MoveTowards(Throttle, thr ? 1f : 0f, dt * 7f);
            Brake = Mathf.MoveTowards(Brake, brk ? 1f : 0f, dt * 10f);
            if (tiltMode) Steer = TiltSteer();
            else
            {
                int dir = (right ? 1 : 0) - (left ? 1 : 0);
                touchSteer = Mathf.MoveTowards(touchSteer, dir, dt * (dir == 0 ? 6f : 3.2f));
                Steer = touchSteer;
            }
            Light(throttlePad, thr); Light(brakePad, brk); Light(leftPad, left); Light(rightPad, right);
        }

        /// <summary>Device roll relative to the landscape screen: ±24° = full lock, small deadzone, smoothed.</summary>
        float TiltSteer()
        {
            Vector3 g = Vector3.zero;
            if (GravitySensor.current != null && GravitySensor.current.enabled) g = GravitySensor.current.gravity.ReadValue();
            else if (Accelerometer.current != null) g = Accelerometer.current.acceleration.ReadValue();
            if (g.sqrMagnitude < 0.01f) return 0f;
            // device axes are portrait-natural: map them onto the current landscape screen
            Vector2 right, up;
            if (Screen.orientation == ScreenOrientation.LandscapeRight) { right = new Vector2(0f, 1f); up = new Vector2(-1f, 0f); }
            else { right = new Vector2(0f, -1f); up = new Vector2(1f, 0f); }
            float gr = g.x * right.x + g.y * right.y, gu = g.x * up.x + g.y * up.y;
            float angle = Mathf.Atan2(gr, -gu) * Mathf.Rad2Deg;
            float a = Mathf.Abs(angle);
            a = a < 2f ? 0f : (a - 2f) / 22f;
            float target = Mathf.Sign(angle) * Mathf.Clamp01(a);
            target = Mathf.Sign(target) * Mathf.Pow(Mathf.Abs(target), 1.3f);
            return Mathf.Lerp(Steer, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
        }
    }
}
