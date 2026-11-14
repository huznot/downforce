using OWR.CameraRig;
using OWR.Race;
using UnityEngine;

namespace OWR.UI
{
    /// <summary>
    /// The main menu lives inside the race scene: while it is up, a drone camera cuts between cinematic shots of the
    /// real circuit and the field waiting on the grid. RACE hands over to the cockpit and starts the race sequence.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class MenuDirector : MonoBehaviour
    {
        public RaceManager race;
        public MainMenu menu;
        public Camera droneCam;
        public CameraDirector cameras;
        public TrackPath path;
        public float shotLength = 7f;

        int shot;
        float shotTime;
        bool active;
        Vector3 gridCentre;
        AudioFX.EngineAudio playerEngine; float playerEngineVol;

        void OnDestroy() { AudioFX.EngineAudio.MenuMute = false; }

        void Awake()
        {
            bool skip = GameOptions.SkipMenuOnce || GameOptions.CelebrationPreview;
            GameOptions.SkipMenuOnce = false;
            if (skip || race == null)
            {
                if (menu) menu.gameObject.SetActive(false);
                if (droneCam) droneCam.gameObject.SetActive(false);
                return;
            }
            race.waitForStart = true;
            active = true;
        }

        void Start()
        {
            if (!active) return;
            if (cameras) { cameras.cockpitCamera.enabled = false; cameras.chaseCamera.enabled = false; SetListener(cameras.cockpitCamera, false); SetListener(cameras.chaseCamera, false); }
            droneCam.gameObject.SetActive(true);
            SetListener(droneCam, true);
            menu.onStartRace = Begin;
            AudioFX.EngineAudio.MenuMute = true;
            if (race.Player != null) { playerEngine = race.Player.car.GetComponent<AudioFX.EngineAudio>(); if (playerEngine) { playerEngineVol = playerEngine.volume; playerEngine.volume = 0f; } }
            foreach (var e in race.entries) gridCentre += e.car.transform.position;
            gridCentre /= Mathf.Max(1, race.entries.Count);
        }

        static void SetListener(Camera c, bool on) { var l = c ? c.GetComponent<AudioListener>() : null; if (l) l.enabled = on; }

        void Begin()
        {
            active = false;
            droneCam.gameObject.SetActive(false);
            if (cameras) cameras.SetMode(GameOptions.CameraMode);
            race.waitForStart = false;
            AudioFX.EngineAudio.MenuMute = false;
            if (playerEngine) playerEngine.volume = playerEngineVol;
        }

        void LateUpdate()
        {
            if (!active) return;
            shotTime += Time.deltaTime;
            if (shotTime > shotLength) { shotTime = 0f; shot++; }
            float t = shotTime;
            var cars = race.entries;
            Transform cam = droneCam.transform;
            Vector3 fwd = path ? path.TangentAt(0f) : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            switch (shot % 6)
            {
                case 0: // slow orbit around the grid
                {
                    float a = 40f + t * 6f;
                    Vector3 o = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 22f;
                    cam.position = gridCentre + o + Vector3.up * (5f + Mathf.Sin(t * 0.3f));
                    cam.LookAt(gridCentre + Vector3.up * 0.6f);
                    droneCam.fieldOfView = 45f;
                    break;
                }
                case 1: // low dolly along the line of cars
                {
                    Vector3 back = cars[cars.Count - 1].car.transform.position;
                    cam.position = back + right * 6.5f + fwd * (t * 9f - 6f) + Vector3.up * 0.75f;
                    cam.LookAt(cam.position + fwd * 12f - right * 5f + Vector3.down * 0.6f);
                    droneCam.fieldOfView = 50f;
                    break;
                }
                case 2: // front-wing close-up of one car
                {
                    var c = cars[(shot / 6) % cars.Count].car.transform;
                    cam.position = c.TransformPoint(new Vector3(1.8f - t * 0.25f, 0.45f, 4.2f));
                    cam.LookAt(c.TransformPoint(new Vector3(0f, 0.45f, 1.2f)));
                    droneCam.fieldOfView = 38f;
                    break;
                }
                case 3: // crane over the main grandstand, looking down on the grid
                {
                    cam.position = gridCentre - right * 34f + Vector3.up * (12f + t * 1.6f) - fwd * 18f;
                    cam.LookAt(gridCentre + fwd * 10f);
                    droneCam.fieldOfView = 42f;
                    break;
                }
                case 4: // high aerial circling the circuit
                {
                    Vector3 centre = path ? Centroid() : gridCentre;
                    Vector3 o = Quaternion.Euler(0f, 200f + t * 3f, 0f) * Vector3.forward * 260f;
                    cam.position = centre + o + Vector3.up * 120f;
                    cam.LookAt(centre);
                    droneCam.fieldOfView = 55f;
                    break;
                }
                default: // under the start lights, looking back down the grid
                {
                    Vector3 p = path.PointAt(6f, 4.5f) + Vector3.up * (1.2f + t * 0.15f);
                    cam.position = p;
                    cam.LookAt(gridCentre + Vector3.up * 0.5f);
                    droneCam.fieldOfView = 40f;
                    break;
                }
            }
        }

        Vector3 centroidCache; bool centroidDone;
        Vector3 Centroid()
        {
            if (centroidDone) return centroidCache;
            foreach (var p in path.points) centroidCache += p;
            centroidCache /= path.points.Length; centroidDone = true;
            return centroidCache;
        }
    }
}
