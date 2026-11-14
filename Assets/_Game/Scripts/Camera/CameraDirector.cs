using OWR.Vehicle;
using UnityEngine;

namespace OWR.CameraRig
{
    public enum CamMode { Cockpit = 0, TCam = 1, Chase = 2 }

    /// <summary>
    /// Cycles the player's view (C key / gamepad Y / CAM button): cockpit -> T-cam (roll-hoop camera with the
    /// driver's helmet in view) -> chase. The helmet is shadow-only in the cockpit view.
    /// </summary>
    public class CameraDirector : MonoBehaviour
    {
        public CarController car;
        public Camera cockpitCamera;
        public Camera tCamera;
        public Camera chaseCamera;
        public Renderer[] hideInCockpit;
        [Tooltip("Global volume that adds onboard-camera exposure; enabled only in cockpit view.")]
        public UnityEngine.Rendering.Volume cockpitExposure;
        public bool startInCockpit = true;
        CamMode mode;
        public CamMode Mode => mode;

        void Start() => Apply((CamMode)OWR.Race.GameOptions.CameraMode);

        void Update()
        {
            if (car && car.input && car.input.ConsumeCameraToggle())
            {
                if (OWR.Race.RaceManager.Instance && OWR.Race.RaceManager.Instance.waitForStart) return; // menu is up
                var next = (CamMode)(((int)mode + 1) % 3);
                if (next == CamMode.TCam && !tCamera) next = CamMode.Chase;
                Apply(next);
            }
        }

        public void SetCockpit(bool on) => Apply(on ? CamMode.Cockpit : CamMode.Chase);
        public void SetMode(int m) => Apply((CamMode)Mathf.Clamp(m, 0, 2));

        static void Enable(Camera c, bool on)
        {
            if (!c) return;
            c.enabled = on;
            var l = c.GetComponent<AudioListener>();
            if (l) l.enabled = on;
        }

        void Apply(CamMode m)
        {
            if (m == CamMode.TCam && !tCamera) m = CamMode.Chase;
            mode = m;
            bool cockpit = m == CamMode.Cockpit;
            if (cockpitExposure) cockpitExposure.weight = cockpit ? 1f : 0f;
            Enable(cockpitCamera, cockpit);
            Enable(tCamera, m == CamMode.TCam);
            Enable(chaseCamera, m == CamMode.Chase);
            if (hideInCockpit != null)
                foreach (var r in hideInCockpit)
                    if (r) r.shadowCastingMode = cockpit ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
