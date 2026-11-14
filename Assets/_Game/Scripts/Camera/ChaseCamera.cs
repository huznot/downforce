using OWR.Vehicle;
using UnityEngine;

namespace OWR.CameraRig
{
    /// <summary>Simple smoothed third-person chase camera. Replaced/joined by the cockpit camera in Milestone 2.</summary>
    public class ChaseCamera : MonoBehaviour
    {
        public CarController target;
        public float distance = 6.5f;
        public float height = 1.9f;
        public float lookAhead = 3f;
        public float lookHeight = 0.7f;
        public float positionSmoothing = 0.08f;
        public float yawFollow = 6f;
        public float baseFov = 60f;
        public float fovAtSpeed = 72f;

        Vector3 vel;
        Vector3 smoothedOffset;
        Vector3 flatFwd;
        Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (target) flatFwd = Flat(target.transform.forward);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }

        void LateUpdate()
        {
            if (!target) return;
            Transform t = target.transform;
            flatFwd = Vector3.Slerp(flatFwd, Flat(t.forward), 1f - Mathf.Exp(-yawFollow * Time.deltaTime));

            // Rigidly attached in distance (no lag at speed); only the yaw is smoothed for a natural swing in corners.
            Vector3 desired = t.position - flatFwd * distance + Vector3.up * height;
            Vector3 local = desired - t.position;
            smoothedOffset = Vector3.SmoothDamp(smoothedOffset == Vector3.zero ? local : smoothedOffset, local, ref vel, positionSmoothing);
            transform.position = t.position + smoothedOffset + Random.insideUnitSphere * (OWR.Race.ExplosionFX.Shake * 0.25f);
            transform.rotation = Quaternion.LookRotation(t.position + flatFwd * lookAhead + Vector3.up * lookHeight - transform.position);

            if (cam)
            {
                float k = Mathf.Clamp01(Mathf.Abs(target.telemetry.speedMs) / 90f);
                cam.fieldOfView = Mathf.Lerp(baseFov, fovAtSpeed, k);
            }
        }
    }
}
