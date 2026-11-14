using System.Collections.Generic;
using OWR.Race;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OWR.UI
{
    /// <summary>
    /// Broadcast-style race HUD built at runtime: position tower, lap counter, timing, speed/gear/RPM/ERS cluster,
    /// start-light overlay, event banners, commentary subtitles, pause menu and results screen.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        public Font titleFont;   // Bebas Neue
        public Font uiFont;      // Barlow Condensed SemiBold
        public Font numFont;     // Barlow Condensed Bold Italic
        public Commentary commentary;
        public string menuScene = "Menu";
        public string gameTitle = "DOWNFORCE";
        public string titleSub = "WET  RACE   ·   8  LAPS";

        static readonly Color Panel = new Color(0.04f, 0.05f, 0.08f, 0.82f);
        static readonly Color PanelLight = new Color(0.1f, 0.12f, 0.17f, 0.85f);
        static readonly Color Accent = new Color(1f, 0.38f, 0.06f);
        static readonly Color Cream = new Color(0.98f, 0.87f, 0.74f);
        static readonly Color Purple = new Color(0.67f, 0.35f, 1f);

        RaceManager rm;
        RectTransform root;
        Text lapText, timeText, lastText, bestText, fastText, speedText, gearText, ersText, assistText, bannerText, subtitleText, subtitleTag;
        CanvasGroup bannerGroup, subtitleGroup, lightsGroup, pauseGroup, resultsGroup, titleGroup, overGroup;
        RectTransform barTop, barBottom, titleRect;
        GameObject overFirst;
        bool overShown;
        readonly List<(Image bg, Text pos, Image bar, Text code, Text gap)> rows = new List<(Image, Text, Image, Text, Text)>();
        Image[] rpmBlocks; Image ersFill; Image[] lightDots;
        RectTransform resultsTable;
        float bannerTimer;
        CanvasGroup noticeGroup; Text noticeText, noticeTag, resetHint, titleSubText; RectTransform noticeRect; float noticeTimer;
        float crashTime;
        float stoppedAt = -1f;
        float resultsTimer; bool autoMenuSelected, autoMenuClicked;
        bool resultsShown;
        GameObject pauseFirst, resultsFirst;

        void Start()
        {
            rm = RaceManager.Instance;
            Build();
            if (rm)
            {
                rm.OnEvent += OnEvent;
                rm.OnLightsChanged += OnLights;
            }
        }

        void OnDestroy()
        {
            if (rm) { rm.OnEvent -= OnEvent; rm.OnLightsChanged -= OnLights; }
        }

        // ================================================================= build
        void Build()
        {
            var canvasGo = new GameObject("RaceHUD_Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)canvasGo.transform;

            if (!FindFirstObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // ---- tower ----
            var tower = Rect(root, "Tower", new Vector2(0, 1), new Vector2(40, -40), new Vector2(330, 60));
            Img(tower, "LapBg", Vector2.zero, new Vector2(330, 60), Panel, new Vector2(0, 1));
            Img(tower, "LapAccent", Vector2.zero, new Vector2(8, 60), Accent, new Vector2(0, 1));
            Label(tower, "LapLbl", "LAP", uiFont, 22, new Vector2(24, -8), new Vector2(80, 44), TextAnchor.MiddleLeft, new Color(0.7f, 0.72f, 0.78f), new Vector2(0, 1));
            lapText = Label(tower, "Lap", "1 / 8", titleFont, 46, new Vector2(80, -6), new Vector2(240, 50), TextAnchor.MiddleLeft, Color.white, new Vector2(0, 1));
            int n = rm ? rm.entries.Count : 8;
            for (int i = 0; i < n; i++)
            {
                float y = -66 - i * 40;
                var bg = Img(tower, "RowBg" + i, new Vector2(0, y), new Vector2(330, 38), Panel, new Vector2(0, 1));
                var posBg = Img(tower, "PosBg" + i, new Vector2(0, y), new Vector2(42, 38), PanelLight, new Vector2(0, 1));
                var pos = Label(tower, "Pos" + i, (i + 1).ToString(), titleFont, 30, new Vector2(0, y), new Vector2(42, 38), TextAnchor.MiddleCenter, Color.white, new Vector2(0, 1));
                var bar = Img(tower, "Team" + i, new Vector2(48, y - 7), new Vector2(5, 24), Color.white, new Vector2(0, 1));
                var code = Label(tower, "Code" + i, "---", uiFont, 26, new Vector2(62, y), new Vector2(120, 38), TextAnchor.MiddleLeft, Color.white, new Vector2(0, 1));
                var gap = Label(tower, "Gap" + i, "", numFont, 22, new Vector2(180, y), new Vector2(140, 38), TextAnchor.MiddleRight, new Color(0.78f, 0.8f, 0.86f), new Vector2(0, 1));
                rows.Add((bg, pos, bar, code, gap));
            }

            // ---- timing (top right) ----
            var timing = Rect(root, "Timing", new Vector2(1, 1), new Vector2(-40, -40), new Vector2(360, 130));
            Img(timing, "Bg", Vector2.zero, new Vector2(360, 130), Panel, new Vector2(1, 1));
            Img(timing, "Accent", Vector2.zero, new Vector2(360, 5), Accent, new Vector2(1, 1));
            Label(timing, "RaceLbl", "RACE", uiFont, 20, new Vector2(-250, -12), new Vector2(100, 40), TextAnchor.MiddleLeft, new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 1));
            timeText = Label(timing, "Time", "0:00.000", numFont, 40, new Vector2(-16, -10), new Vector2(240, 46), TextAnchor.MiddleRight, Color.white, new Vector2(1, 1));
            lastText = Label(timing, "Last", "LAST  --:--.---", uiFont, 22, new Vector2(-16, -56), new Vector2(330, 30), TextAnchor.MiddleRight, new Color(0.85f, 0.87f, 0.92f), new Vector2(1, 1));
            bestText = Label(timing, "Best", "BEST  --:--.---", uiFont, 22, new Vector2(-16, -82), new Vector2(330, 30), TextAnchor.MiddleRight, new Color(0.85f, 0.87f, 0.92f), new Vector2(1, 1));
            fastText = Label(timing, "Fast", "", uiFont, 20, new Vector2(-16, -106), new Vector2(330, 26), TextAnchor.MiddleRight, Purple, new Vector2(1, 1));

            // ---- cluster (bottom right) ----
            var cl = Rect(root, "Cluster", new Vector2(1, 0), new Vector2(-40, 40), new Vector2(420, 190));
            Img(cl, "Bg", Vector2.zero, new Vector2(420, 190), Panel, new Vector2(1, 0));
            Img(cl, "Accent", new Vector2(0, 185), new Vector2(420, 5), Accent, new Vector2(1, 0));
            gearText = Label(cl, "Gear", "N", titleFont, 150, new Vector2(-300, 10), new Vector2(120, 170), TextAnchor.MiddleCenter, Color.white, new Vector2(1, 0));
            speedText = Label(cl, "Speed", "0", titleFont, 92, new Vector2(-60, 60), new Vector2(220, 100), TextAnchor.MiddleRight, Color.white, new Vector2(1, 0));
            Label(cl, "Kph", "KM/H", uiFont, 22, new Vector2(-16, 66), new Vector2(60, 30), TextAnchor.MiddleRight, new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 0));
            rpmBlocks = new Image[16];
            for (int i = 0; i < 16; i++)
                rpmBlocks[i] = Img(cl, "Rpm" + i, new Vector2(-280 + i * 16.5f, 150), new Vector2(13, 18), new Color(1, 1, 1, 0.12f), new Vector2(1, 0));
            Label(cl, "ErsLbl", "ERS", uiFont, 20, new Vector2(-226, 26), new Vector2(50, 26), TextAnchor.MiddleLeft, new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 0));
            Img(cl, "ErsBg", new Vector2(-16, 30), new Vector2(170, 12), new Color(1, 1, 1, 0.1f), new Vector2(1, 0));
            ersFill = Img(cl, "ErsFill", new Vector2(-16, 30), new Vector2(170, 12), new Color(1f, 0.82f, 0.1f), new Vector2(1, 0));
            ersFill.type = Image.Type.Filled; ersFill.fillMethod = Image.FillMethod.Horizontal; ersFill.fillOrigin = 0;
            ersFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            ersText = Label(cl, "ErsMode", "RACE", uiFont, 20, new Vector2(-190, 24), new Vector2(110, 26), TextAnchor.MiddleRight, new Color(1f, 0.82f, 0.1f), new Vector2(1, 0));
            assistText = Label(cl, "Assist", "", uiFont, 18, new Vector2(-16, 4), new Vector2(380, 24), TextAnchor.MiddleRight, new Color(0.6f, 0.62f, 0.68f), new Vector2(1, 0));

            // ---- start lights overlay ----
            var lg = Rect(root, "Lights", new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(560, 120));
            lightsGroup = lg.gameObject.AddComponent<CanvasGroup>();
            Img(lg, "Bg", Vector2.zero, new Vector2(560, 120), Panel, new Vector2(0.5f, 1));
            lightDots = new Image[5];
            var knob = CircleSprite();
            for (int i = 0; i < 5; i++)
            {
                lightDots[i] = Img(lg, "L" + i, new Vector2(-220 + i * 110, -20), new Vector2(80, 80), new Color(0.15f, 0.03f, 0.03f), new Vector2(0.5f, 1));
                lightDots[i].sprite = knob;
            }
            lightsGroup.alpha = 0f;

            // ---- banner ----
            var bn = Rect(root, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(1200, 110));
            bannerGroup = bn.gameObject.AddComponent<CanvasGroup>();
            Img(bn, "Bg", Vector2.zero, new Vector2(900, 100), Panel, new Vector2(0.5f, 0.5f));
            Img(bn, "Acc", new Vector2(0, -48), new Vector2(900, 6), Accent, new Vector2(0.5f, 0.5f));
            bannerText = Label(bn, "Text", "", titleFont, 78, Vector2.zero, new Vector2(1200, 100), TextAnchor.MiddleCenter, Cream, new Vector2(0.5f, 0.5f));
            bannerGroup.alpha = 0f;

            // ---- subtitles ----
            var sb = Rect(root, "Subs", new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(1100, 70));
            subtitleGroup = sb.gameObject.AddComponent<CanvasGroup>();
            Img(sb, "Bg", Vector2.zero, new Vector2(1100, 70), new Color(0, 0, 0, 0.55f), new Vector2(0.5f, 0.5f));
            subtitleTag = Label(sb, "Tag", "COMMENTARY", uiFont, 18, new Vector2(-470, 0), new Vector2(140, 70), TextAnchor.MiddleLeft, Accent, new Vector2(0.5f, 0.5f));
            subtitleText = Label(sb, "Text", "", uiFont, 28, new Vector2(70, 0), new Vector2(940, 70), TextAnchor.MiddleLeft, Color.white, new Vector2(0.5f, 0.5f));
            subtitleGroup.alpha = 0f;

            // ---- corner notification (under the timing panel) ----
            noticeRect = Rect(root, "Notice", new Vector2(1, 1), new Vector2(-40, -186), new Vector2(360, 64));
            noticeGroup = noticeRect.gameObject.AddComponent<CanvasGroup>();
            Img(noticeRect, "Bg", Vector2.zero, new Vector2(360, 64), new Color(0.12f, 0.02f, 0.02f, 0.88f), new Vector2(1, 1));
            Img(noticeRect, "Bar", Vector2.zero, new Vector2(8, 64), new Color(1f, 0.82f, 0.1f), new Vector2(1, 1));
            noticeTag = Label(noticeRect, "Tag", "YELLOW FLAG", uiFont, 18, new Vector2(-24, -6), new Vector2(320, 24), TextAnchor.MiddleRight, new Color(1f, 0.82f, 0.1f), new Vector2(1, 1));
            noticeText = Label(noticeRect, "Text", "", titleFont, 34, new Vector2(-24, -26), new Vector2(320, 36), TextAnchor.MiddleRight, Color.white, new Vector2(1, 1));
            noticeGroup.alpha = 0f;
            resetHint = Label(root, "ResetHint", "PRESS  R / VIEW  TO RESET   ·   HOLD TO RESTART LAP", uiFont, 34, new Vector2(0, 120), new Vector2(1200, 60), TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), new Vector2(0.5f, 0.5f));
            resetHint.gameObject.AddComponent<Shadow>();
            resetHint.enabled = false;

            BuildPause();
            BuildResults();
            BuildTitleDrop();
            BuildRaceOver();
            if (rm && (rm.waitForStart || Race.GameOptions.CelebrationPreview)) root.GetComponent<Canvas>().enabled = false;   // hidden behind the menu / preview
            else StartCoroutine(TitleDrop());
        }

        // ---- cinematic title drop at the start of the race ----
        void BuildTitleDrop()
        {
            barTop = Rect(root, "BarTop", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000, 0));
            barTop.gameObject.AddComponent<Image>().color = Color.black;
            barBottom = Rect(root, "BarBottom", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(4000, 0));
            barBottom.gameObject.AddComponent<Image>().color = Color.black;
            titleRect = Rect(root, "TitleDrop", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1800, 420));
            titleGroup = titleRect.gameObject.AddComponent<CanvasGroup>();
            var t = Label(titleRect, "Name", gameTitle, titleFont, 300, new Vector2(0, 40), new Vector2(1800, 320), TextAnchor.MiddleCenter, Cream, new Vector2(0.5f, 0.5f));
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0.36f, 0.2f, 0.17f, 0.85f); sh.effectDistance = new Vector2(8, -8);
            Img(titleRect, "Rule", new Vector2(0, -118), new Vector2(620, 5), Accent, new Vector2(0.5f, 0.5f));
            titleSubText = Label(titleRect, "Sub", titleSub, uiFont, 46, new Vector2(0, -160), new Vector2(1400, 70), TextAnchor.MiddleCenter, Cream, new Vector2(0.5f, 0.5f));
            titleGroup.alpha = 0f;
        }

        System.Collections.IEnumerator TitleDrop()
        {
            float barH = 140f;
            for (float t = 0; t < 7.2f; t += Time.deltaTime)
            {
                float bars = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.8f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 6.2f) / 1f)));
                barTop.sizeDelta = new Vector2(4000, barH * bars);
                barBottom.sizeDelta = new Vector2(4000, barH * bars);
                float a = Mathf.Clamp01((t - 0.9f) / 0.9f) * (1f - Mathf.Clamp01((t - 5.4f) / 0.8f));
                titleGroup.alpha = a;
                float sc = Mathf.Lerp(1.12f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.9f) / 2.5f)));
                titleRect.localScale = Vector3.one * sc;
                yield return null;
            }
            titleGroup.alpha = 0f; barTop.sizeDelta = barBottom.sizeDelta = new Vector2(4000, 0);
        }

        // ---- race over (player crashed) ----
        void BuildRaceOver()
        {
            var p = Rect(root, "RaceOver", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            p.anchorMin = Vector2.zero; p.anchorMax = Vector2.one;
            overGroup = p.gameObject.AddComponent<CanvasGroup>();
            var dim = Img(p, "Dim", Vector2.zero, Vector2.zero, new Color(0.05f, 0.01f, 0.01f, 0.72f), new Vector2(0.5f, 0.5f));
            dim.rectTransform.anchorMin = Vector2.zero; dim.rectTransform.anchorMax = Vector2.one; dim.rectTransform.sizeDelta = Vector2.zero;
            var t = Label(p, "Title", "RACE OVER", titleFont, 240, new Vector2(0, 170), new Vector2(1600, 260), TextAnchor.MiddleCenter, new Color(1f, 0.42f, 0.18f), new Vector2(0.5f, 0.5f));
            t.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(7, -7);
            Label(p, "Sub", "YOU CRASHED OUT", uiFont, 54, new Vector2(0, 20), new Vector2(1200, 80), TextAnchor.MiddleCenter, Cream, new Vector2(0.5f, 0.5f));
            overFirst = Button(p, "RESTART", new Vector2(-230, -170), () => Reload(true)).gameObject;
            Button(p, "MAIN MENU", new Vector2(230, -170), () => Reload(false));
            SetGroup(overGroup, false);
        }

        void BuildPause()
        {
            var p = Rect(root, "Pause", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            p.anchorMin = Vector2.zero; p.anchorMax = Vector2.one; p.sizeDelta = Vector2.zero;
            pauseGroup = p.gameObject.AddComponent<CanvasGroup>();
            var dim = Img(p, "Dim", Vector2.zero, Vector2.zero, new Color(0.02f, 0.03f, 0.05f, 0.78f), new Vector2(0.5f, 0.5f));
            dim.rectTransform.anchorMin = Vector2.zero; dim.rectTransform.anchorMax = Vector2.one; dim.rectTransform.sizeDelta = Vector2.zero;
            Label(p, "Title", "PAUSED", titleFont, 170, new Vector2(-520, 230), new Vector2(800, 200), TextAnchor.MiddleLeft, Cream, new Vector2(0.5f, 0.5f)).gameObject.AddComponent<Shadow>().effectDistance = new Vector2(5, -5);
            pauseFirst = Button(p, "RESUME", new Vector2(-520, 60), () => TogglePause()).gameObject;
            Button(p, "RESTART RACE", new Vector2(-520, -30), () => Reload(true));
            Button(p, "MAIN MENU", new Vector2(-520, -120), () => Reload(false));
            Label(p, "Help", "RT  THROTTLE     LT  BRAKE     L-STICK  STEER     RB/LB  SHIFT     R-STICK  LOOK\n" +
                               "D-PAD  BRAKE BIAS / TC / ERS MODE     B  OVERTAKE     X  PIT LIMITER     Y  CAMERA     VIEW  RESET",
                uiFont, 24, new Vector2(-520, -300), new Vector2(1400, 90), TextAnchor.UpperLeft, new Color(0.75f, 0.78f, 0.85f), new Vector2(0.5f, 0.5f));
            SetGroup(pauseGroup, false);
        }

        void BuildResults()
        {
            var p = Rect(root, "Results", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            p.anchorMin = Vector2.zero; p.anchorMax = Vector2.one; p.sizeDelta = Vector2.zero;
            resultsGroup = p.gameObject.AddComponent<CanvasGroup>();
            var dim = Img(p, "Dim", Vector2.zero, Vector2.zero, new Color(0.02f, 0.03f, 0.05f, 0.85f), new Vector2(0.5f, 0.5f));
            dim.rectTransform.anchorMin = Vector2.zero; dim.rectTransform.anchorMax = Vector2.one; dim.rectTransform.sizeDelta = Vector2.zero;
            Label(p, "Title", "CLASSIFICATION", titleFont, 120, new Vector2(0, 400), new Vector2(1400, 140), TextAnchor.MiddleCenter, Cream, new Vector2(0.5f, 0.5f)).gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4, -4);
            resultsTable = Rect(p, "Table", new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(1100, 480));
            resultsFirst = Button(p, "RACE AGAIN", new Vector2(-230, -440), () => Reload(true)).gameObject;
            Button(p, "MAIN MENU", new Vector2(230, -440), () => Reload(false));
            SetGroup(resultsGroup, false);
        }

        void FillResults()
        {
            for (int i = resultsTable.childCount - 1; i >= 0; i--) Destroy(resultsTable.GetChild(i).gameObject);
            var leader = rm.Order[0];
            for (int i = 0; i < rm.Order.Count; i++)
            {
                var e = rm.Order[i];
                float y = -i * 56;
                y = -i * 50;
                Img(resultsTable, "Bg", new Vector2(0, y), new Vector2(1100, 46), e.isPlayer ? new Color(1f, 0.38f, 0.06f, 0.35f) : Panel, new Vector2(0.5f, 1));
                Label(resultsTable, "P", (i + 1).ToString(), titleFont, 40, new Vector2(-500, y), new Vector2(60, 50), TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 1));
                Img(resultsTable, "T", new Vector2(-455, y - 10), new Vector2(6, 30), e.teamColor, new Vector2(0.5f, 1));
                Label(resultsTable, "N", e.driver.ToUpper(), uiFont, 30, new Vector2(-260, y), new Vector2(360, 50), TextAnchor.MiddleLeft, Color.white, new Vector2(0.5f, 1));
                Label(resultsTable, "Tm", e.team.ToUpper(), uiFont, 24, new Vector2(20, y), new Vector2(260, 50), TextAnchor.MiddleLeft, new Color(0.7f, 0.72f, 0.78f), new Vector2(0.5f, 1));
                string time = i == 0 ? RaceManager.FormatTime(e.finished ? e.FinalTime : rm.RaceTime)
                                     : e.finished && leader.finished ? "+" + (e.FinalTime - leader.FinalTime).ToString("0.000") : "+" + Mathf.Max(1, rm.totalLaps - e.lap + 1) + " LAP" + (rm.totalLaps - e.lap + 1 > 1 ? "S" : "");
                if (e.penalty > 0f) time += $"  <size=20><color=#ffcc33>(+{e.penalty:0}s)</color></size>";
                if (i > 0 && !e.finished) time = e.dnf ? "DNF" : "+" + e.gapToLeader.ToString("0.0") + "s";
                Label(resultsTable, "Time", time, numFont, 30, new Vector2(330, y), new Vector2(220, 50), TextAnchor.MiddleRight, Color.white, new Vector2(0.5f, 1));
                bool fl = e.code == rm.FastestLapCode;
                Label(resultsTable, "Best", RaceManager.FormatTime(e.bestLap), numFont, 24, new Vector2(500, y), new Vector2(160, 50), TextAnchor.MiddleRight, fl ? Purple : new Color(0.7f, 0.72f, 0.78f), new Vector2(0.5f, 1));
            }
        }

        // ================================================================= update
        void Update()
        {
            if (!rm) return;
            var kb = Keyboard.current; var gp = Gamepad.current;
            bool pausePressed = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame) || Input.TouchControls.ConsumePause();
            if (rm.CelebrationPreviewMode) { if (pausePressed) Reload(false); return; }
            if (pausePressed && rm.State != RaceManager.RaceState.Finished && rm.State != RaceManager.RaceState.Crashed) TogglePause();

            // tower
            for (int i = rm.Order.Count; i < rows.Count; i++)
                if (rows[i].bg.gameObject.activeSelf)
                {
                    rows[i].bg.gameObject.SetActive(false); rows[i].pos.gameObject.SetActive(false); rows[i].bar.gameObject.SetActive(false);
                    rows[i].code.gameObject.SetActive(false); rows[i].gap.gameObject.SetActive(false);
                    var pb = rows[i].pos.transform.parent.Find("PosBg" + i); if (pb) pb.gameObject.SetActive(false);
                }
            for (int i = 0; i < rows.Count && i < rm.Order.Count; i++)
            {
                var e = rm.Order[i]; var r = rows[i];
                r.pos.text = (i + 1).ToString();
                r.code.text = e.code;
                r.bar.color = e.teamColor;
                r.bg.color = e.isPlayer ? new Color(1f, 0.38f, 0.06f, 0.55f) : e.dnf ? new Color(0.25f, 0.05f, 0.05f, 0.75f) : Panel;
                if (e.dnf) { r.gap.text = "DNF"; continue; }
                string pen = e.penalty > 0f ? $"<color=#ffcc33>+{e.penalty:0}s </color>" : "";
                r.gap.supportRichText = true;
                r.gap.text = pen + (i == 0 ? (rm.State == RaceManager.RaceState.Racing ? "LEADER" : "") : (e.finished && rm.Order[0].finished ? "+" + (e.finishTime - rm.Order[0].finishTime).ToString("0.000") : rm.State == RaceManager.RaceState.Racing ? "+" + e.gapToLeader.ToString("0.0") : ""));
                if (rm.Practice) r.gap.text = "PRACTICE";
            }
            var pl = rm.Player;
            if (pl != null)
            {
                float curLap = rm.State == RaceManager.RaceState.Racing && pl.lap >= 1 && !pl.finished ? rm.RaceTime - pl.lapStart : 0f;
                lapText.text = rm.Practice ? $"{Mathf.Max(1, pl.lap)}" : $"{Mathf.Clamp(pl.lap, 1, rm.totalLaps)} / {rm.totalLaps}";
                timeText.text = RaceManager.FormatTime(rm.Practice ? Mathf.Max(curLap, 0.0001f) : (rm.RaceTime > 0 ? rm.RaceTime : 0.0001f));
                float frac = Mathf.Clamp01(pl.d / Mathf.Max(1f, rm.path.Length));
                float liveDelta = pl.bestLap < float.MaxValue && curLap > 0f ? curLap - pl.bestLap * frac : 0f;
                resetHint.enabled = rm.OffTrackTime > 1.5f;
                lastText.text = "LAST  " + RaceManager.FormatTime(pl.lastLap);
                bestText.text = "BEST  " + RaceManager.FormatTime(pl.bestLap);
                fastText.text = rm.FastestLap < float.MaxValue ? $"FASTEST  {rm.FastestLapCode}  {RaceManager.FormatTime(rm.FastestLap)}" : "";

                var t = pl.car.telemetry;
                speedText.text = Mathf.RoundToInt(Mathf.Abs(t.SpeedKmh)).ToString();
                gearText.text = t.gear == -1 ? "R" : t.gear == 0 || pl.car.Drivetrain.ForceNeutral ? "N" : t.gear.ToString();
                float f = Mathf.InverseLerp(5000f, 12200f, t.rpm);
                for (int i = 0; i < rpmBlocks.Length; i++)
                {
                    bool on = i < Mathf.CeilToInt(f * rpmBlocks.Length);
                    Color c = i < 10 ? new Color(0.25f, 1f, 0.4f) : i < 14 ? new Color(1f, 0.15f, 0.1f) : new Color(0.3f, 0.5f, 1f);
                    rpmBlocks[i].color = on ? c : new Color(1, 1, 1, 0.12f);
                }
                ersFill.fillAmount = t.batteryFraction;
                var wc = pl.car.GetComponent<Cockpit.WheelControls>();
                ersText.text = wc ? Cockpit.WheelControls.ErsNames[wc.ErsMode] + (wc.OvertakeTimer > 0 ? "  OVT" : "") : "";
                var s = pl.car.setup;
                assistText.text = $"BB {s.brakeBias * 100f:0.0}   TC {(s.tractionControl ? "ON" : "OFF")}   STAB {(s.stabilityAssist ? "ON" : "OFF")}";

                // feed the steering-wheel display
                var rig = pl.car.GetComponentInChildren<Cockpit.SteeringWheelRig>();
                if (rig)
                {
                    rig.raceLap = Mathf.Clamp(pl.lap, 1, rm.totalLaps); rig.raceLaps = rm.totalLaps; rig.racePos = pl.pos; rig.raceCars = rm.entries.Count;
                    rig.raceLapTime = curLap; rig.raceDelta = liveDelta; rig.practice = rm.Practice;
                }
            }

            // corner notice: slide in, hold, fade
            if (noticeTimer > 0f)
            {
                noticeTimer -= Time.unscaledDeltaTime;
                noticeGroup.alpha = Mathf.Clamp01(Mathf.Min((5f - noticeTimer) * 4f, noticeTimer * 1.5f));
                noticeRect.anchoredPosition = new Vector2(-40 + (1f - Mathf.Clamp01((5f - noticeTimer) * 4f)) * 380f, -186);
            }
            else noticeGroup.alpha = 0f;
            // banner fade
            if (bannerTimer > 0f) { bannerTimer -= Time.unscaledDeltaTime; bannerGroup.alpha = Mathf.Clamp01(bannerTimer * 2f); }
            // subtitles
            string sub = commentary ? commentary.Subtitle : "";
            subtitleText.text = sub;
            subtitleGroup.alpha = Mathf.MoveTowards(subtitleGroup.alpha, string.IsNullOrEmpty(sub) ? 0f : 1f, Time.unscaledDeltaTime * 4f);

            if (rm.State == RaceManager.RaceState.Crashed && !overShown && rm.RaceTime - crashTime > 3.5f)
            {
                overShown = true;
                SetGroup(overGroup, true);
                EventSystem.current?.SetSelectedGameObject(overFirst);
            }
            if (rm.State != RaceManager.RaceState.Crashed) crashTime = rm.RaceTime;
            bool stopped = rm.Player != null && Mathf.Abs(rm.Player.car.telemetry.speedMs) < 0.5f;
            bool won = rm.Player != null && rm.Player.finished && rm.Player.pos == 1;
            if (rm.State == RaceManager.RaceState.Finished && stopped && stoppedAt < 0f) stoppedAt = rm.RaceTime;
            // let a winner's celebration play before the classification covers it
            if (won && (stoppedAt < 0f || rm.RaceTime - stoppedAt < 7f)) stopped = false;

            if (rm.State == RaceManager.RaceState.Finished && !resultsShown && rm.RaceTime - (rm.Player != null ? rm.Player.finishTime : 0f) > (Race.GameOptions.AutoDrive ? (stopped ? 1f : 20f) : (won ? (stopped ? 1f : 20f) : 4f)))
            {
                resultsShown = true;
                FillResults();
                SetGroup(resultsGroup, true);
                EventSystem.current?.SetSelectedGameObject(resultsFirst);
            }
            if (resultsShown && Time.frameCount % 30 == 0) FillResults();
            // showcase: after a look at the classification, select and press MAIN MENU
            if (resultsShown && Race.GameOptions.AutoDrive)
            {
                resultsTimer += Time.unscaledDeltaTime;
                var mm = resultsGroup.transform.Find("Btn_MAIN MENU");
                if (resultsTimer > 7f && !autoMenuSelected && mm) { autoMenuSelected = true; EventSystem.current?.SetSelectedGameObject(mm.gameObject); }
                if (resultsTimer > 8.5f && !autoMenuClicked && mm) { autoMenuClicked = true; mm.GetComponent<Button>().onClick.Invoke(); }
            }
        }

        void OnEvent(string ev)
        {
            switch (ev)
            {
                case "practice": ShowBanner("FREE PRACTICE  ·  GO"); break;
                case "pb": ShowBanner("PERSONAL BEST"); break;
                case "practice_restart": ShowNotice("RESTART LAP", "PRACTICE"); break;
                case "penalty": ShowNotice($"{rm.LastPenaltySeconds:0} SEC PENALTY  ·  {rm.LastPenaltyCode}", "CONTACT"); break;
                case "penalty_you": ShowNotice($"{rm.LastPenaltySeconds:0} SEC PENALTY  ·  YOU", "CONTACT"); break;
                case "begin":
                    titleSubText.text = rm.Practice ? "FREE  PRACTICE" : $"WET  RACE   ·   {rm.totalLaps}  LAPS";
                    root.GetComponent<Canvas>().enabled = true;
                    StartCoroutine(TitleDrop());
                    break;
                case "go": ShowBanner("LIGHTS OUT"); break;
                case "finallap": ShowBanner("FINAL LAP"); break;
                case "fastest": ShowBanner("FASTEST LAP"); break;
                case "lead": ShowBanner("P1  RACE LEADER"); break;
                case "win": ShowBanner("VICTORY"); break;
                case "podium": ShowBanner("P" + rm.Player.pos + "  PODIUM"); break;
                case "finish": ShowBanner("FINISHED  P" + rm.Player.pos); break;
                case "aicrash": ShowNotice("ACCIDENT  ·  " + rm.LastAccidentCode + " OUT", "YELLOW FLAG"); break;
            }
        }

        void OnLights(int lit)
        {
            if (lit < 0) { lightsGroup.alpha = 0f; return; }
            lightsGroup.alpha = 1f;
            for (int i = 0; i < 5; i++) lightDots[i].color = i < lit ? new Color(1f, 0.08f, 0.05f) : new Color(0.15f, 0.03f, 0.03f);
        }

        void ShowNotice(string text, string tag = "RACE CONTROL") { noticeText.text = text; noticeTag.text = tag; noticeTimer = 5f; }

        void ShowBanner(string text) { bannerText.text = text; bannerTimer = 2.8f; bannerGroup.alpha = 1f; }

        void TogglePause()
        {
            bool p = !rm.Paused;
            rm.SetPaused(p);
            SetGroup(pauseGroup, p);
            if (p) EventSystem.current?.SetSelectedGameObject(pauseFirst);
        }

        static Sprite CircleSprite()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(S / 2f - 1f - d)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Reload the race scene: straight onto the grid (restart) or back to the main menu.</summary>
        static void Reload(bool skipMenu)
        {
            Time.timeScale = 1f; AudioListener.pause = false;
            Race.GameOptions.SkipMenuOnce = skipMenu;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        static void SetGroup(CanvasGroup g, bool on) { g.alpha = on ? 1f : 0f; g.interactable = on; g.blocksRaycasts = on; }

        // ================================================================= ui helpers
        static RectTransform Rect(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform;
            r.anchorMin = r.anchorMax = anchor; r.pivot = anchor;
            r.anchoredPosition = pos; r.sizeDelta = size;
            return r;
        }

        static Image Img(RectTransform parent, string name, Vector2 pos, Vector2 size, Color c, Vector2 anchor)
        {
            var r = Rect(parent, name, anchor, pos, size);
            var i = r.gameObject.AddComponent<Image>();
            i.color = c;
            return i;
        }

        static Text Label(RectTransform parent, string name, string text, Font font, int size, Vector2 pos, Vector2 dim, TextAnchor align, Color c, Vector2 anchor)
        {
            var r = Rect(parent, name, anchor, pos, dim);
            var t = r.gameObject.AddComponent<Text>();
            t.font = font ? font : UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text; t.fontSize = size; t.alignment = align; t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        Button Button(RectTransform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var bg = Img(parent, "Btn_" + label, pos, new Vector2(420, 72), new Color(1, 1, 1, 0.06f), new Vector2(0.5f, 0.5f));
            bg.raycastTarget = true;
            var b = bg.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = new Color(1, 1, 1, 0.06f);
            cb.highlightedColor = new Color(1f, 0.38f, 0.06f, 0.9f);
            cb.selectedColor = new Color(1f, 0.38f, 0.06f, 0.9f);
            cb.pressedColor = new Color(1f, 0.6f, 0.3f, 1f);
            cb.colorMultiplier = 1f;
            b.colors = cb;
            b.onClick.AddListener(onClick);
            Label((RectTransform)bg.transform, "Label", label, titleFont, 48, new Vector2(28, 0), new Vector2(380, 72), TextAnchor.MiddleLeft, Color.white, new Vector2(0, 0.5f));
            return b;
        }
    }
}
