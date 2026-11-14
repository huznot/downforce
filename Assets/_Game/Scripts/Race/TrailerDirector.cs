using UnityEngine;

using OWR.Vehicle;

namespace OWR.Race
{
    /// <summary>
    /// Cinematic camera for the store trailer (added only by OWR > Record Trailer). Cuts through a fixed shot list
    /// during an auto-driven race: grid push-in, lights from the cockpit, T-cam, rival chase in the spray, side
    /// tracking, helicopter, a fiery crash, onboard action and a rear-facing shot; or, in celebration mode, the
    /// chequered flag and the fist pumps. Saves 1920x1080 store screenshots at the best moments.
    /// </summary>
    public class TrailerDirector : MonoBehaviour
    {
        public static bool Done;
        public bool celebrationMode;
        public string shotFolder = "Recordings/StoreShots";
        Camera cam;
        CameraRig.CameraDirector dir;
        Canvas[] hud;
        float startT, goT = -1f;
        int shot = -1;
        Vector3 anchor; Quaternion anchorRot;
        CarController target;
        bool exploded;
        int shotIndex;
        readonly System.Collections.Generic.HashSet<string> taken = new System.Collections.Generic.HashSet<string>();

        void Start()
        {
            Done = false;
            cam = new GameObject("TrailerCam", typeof(Camera)).GetComponent<Camera>();
            cam.depth = 100; cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
            var src = Camera.main;
            if (src)
            {
                var data = src.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                var mine = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (data) { mine.renderPostProcessing = true; mine.antialiasing = data.antialiasing; }
            }
            dir = FindFirstObjectByType<CameraRig.CameraDirector>();
            startT = Time.time;
            System.IO.Directory.CreateDirectory(shotFolder);
        }

        void Hud(bool on)
        {
            var h = FindFirstObjectByType<UI.RaceHUD>();
            if (!h) return;
            foreach (var c in h.GetComponentsInChildren<Canvas>(true)) c.enabled = on;
        }

        void Shoot(string name)
        {
            if (taken.Contains(name)) return;
            taken.Add(name);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotFolder, $"{++shotIndex:00}_{name}.png"));
        }

        void Use(bool trailerCam, int onboardMode = -1)
        {
            cam.enabled = trailerCam;
            if (!trailerCam && dir && onboardMode >= 0) dir.SetMode(onboardMode);
        }

        CarController Rival(int offset)
        {
            var rm = RaceManager.Instance;
            int pi = rm.Order.IndexOf(rm.Player);
            int i = Mathf.Clamp(pi + offset, 0, rm.Order.Count - 1);
            if (rm.Order[i].isPlayer) i = Mathf.Clamp(i + (offset < 0 ? -1 : 1), 0, rm.Order.Count - 1);
            return rm.Order[i].car;
        }

        void LateUpdate()
        {
            var rm = RaceManager.Instance;
            if (!rm || rm.Player == null) return;
            var player = rm.Player.car;
            if (celebrationMode) { Celebration(rm, player); return; }

            if (goT < 0f && rm.State == RaceManager.RaceState.Racing) goT = Time.time;
            float pre = Time.time - startT;
            float t = goT < 0f ? -1f : Time.time - goT;

            int s = goT < 0f ? (rm.State == RaceManager.RaceState.Lights ? 1 : 0)
                  : t < 6f ? 2 : t < 12f ? 3 : t < 18f ? 4 : t < 24f ? 5 : t < 31f ? 6 : t < 38f ? 7 : t < 44f ? 8 : 9;
            bool cut = s != shot;
            shot = s;
            var path = rm.path;
            switch (s)
            {
                case 0: // grid: low camera ahead of the field, slow push toward the cars
                {
                    Use(true); Hud(false);
                    float d = rm.Player.d;
                    if (cut) { anchor = path.PointAt(d + 18f, -3.5f) + Vector3.up * 0.45f; }
                    Vector3 look = player.transform.position + Vector3.up * 0.6f;
                    Vector3 pos = Vector3.Lerp(anchor, look, Mathf.Clamp01(pre / 40f) * 0.35f);
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                    cam.fieldOfView = 38f;
                    if (pre > 3f) Shoot("grid");
                    break;
                }
                case 1: Use(false, 0); Hud(true); if (cut) { } break;                      // lights from the cockpit
                case 2: Use(false, 1); Hud(false); if (t > 3f) Shoot("tcam"); break;     // T-cam off the line
                case 3: // low chase behind the car ahead, in its spray
                {
                    Use(true); Hud(false);
                    if (cut) target = Rival(-1);
                    var tr = target.transform;
                    Vector3 pos = tr.position - tr.forward * 5.2f + Vector3.up * 0.75f + tr.right * 0.6f;
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tr.position + tr.forward * 6f + Vector3.up * 0.3f - pos));
                    cam.fieldOfView = 55f;
                    if (t > 9f) Shoot("spray");
                    break;
                }
                case 4: // side tracking shot of the player, low
                {
                    Use(true); Hud(false);
                    var tr = player.transform;
                    Vector3 pos = tr.position + tr.right * 4.2f + tr.forward * 1.2f + Vector3.up * 0.55f;
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tr.position + Vector3.up * 0.45f - pos));
                    cam.fieldOfView = 42f;
                    if (t > 15f) Shoot("tracking");
                    break;
                }
                case 5: // helicopter: high, behind and to the side of the pack
                {
                    Use(true); Hud(false);
                    var tr = player.transform;
                    Vector3 pos = tr.position - tr.forward * 28f + Vector3.up * 16f - tr.right * 10f;
                    cam.transform.position = Vector3.Lerp(cam.transform.position, pos, cut ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 2f));
                    cam.transform.rotation = Quaternion.LookRotation(tr.position + tr.forward * 25f - cam.transform.position);
                    cam.fieldOfView = 45f;
                    if (t > 21f) Shoot("helicopter");
                    break;
                }
                case 6: // the crash: a rival behind goes up in flames, trackside camera
                {
                    Use(true); Hud(false);
                    if (cut) { target = Rival(1); exploded = false; anchor = target.transform.position + target.transform.right * 11f + target.transform.forward * 14f + Vector3.up * 1.6f; }
                    if (!exploded && t > 25.2f && ExplosionFX.Instance) { ExplosionFX.Instance.Explode(target); exploded = true; }
                    cam.transform.SetPositionAndRotation(anchor, Quaternion.LookRotation(target.transform.position + Vector3.up * 1.2f - anchor));
                    cam.fieldOfView = 48f;
                    if (t > 26.3f) Shoot("explosion");
                    break;
                }
                case 7: Use(false, 0); Hud(true); if (t > 34f) Shoot("cockpit"); break;  // gameplay onboard
                case 8: // rear-facing on the player's car: chasers in the spray
                {
                    Use(true); Hud(false);
                    var tr = player.transform;
                    Vector3 pos = tr.position + tr.forward * 0.4f + Vector3.up * 1.25f;
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-tr.forward * 10f - Vector3.up * 1.1f));
                    cam.fieldOfView = 60f;
                    if (t > 41f) Shoot("rearview");
                    break;
                }
                default: Done = true; break;
            }
        }

        void Celebration(RaceManager rm, CarController player)
        {
            float t = Time.time - startT;
            Hud(false);
            var flag = FindFirstObjectByType<Track.ChequeredFlag>();
            if (t < 5.5f && flag)
            {
                Use(true);
                var ft = flag.transform;
                Vector3 pos = ft.position + ft.forward * 7f - ft.right * 3.5f + Vector3.up * 2.4f;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(ft.position + Vector3.up * 3.8f - pos));
                cam.fieldOfView = 40f;
                if (t > 3.5f) Shoot("flag");
            }
            else if (t < 12f)
            {
                Use(false, 0);
                if (t > 9f) Shoot("celebration");
            }
            else Done = true;
        }
    }
}
