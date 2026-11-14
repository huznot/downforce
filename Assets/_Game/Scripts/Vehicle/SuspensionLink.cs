using UnityEngine;

namespace OWR.Vehicle
{
    /// <summary>Stretches a unit-length streamlined tube (along +z, length 1) between two anchors every frame.</summary>
    [ExecuteAlways]
    public class SuspensionLink : MonoBehaviour
    {
        public Transform from;
        public Transform to;
        public float width = 0.035f;
        public float thickness = 0.014f;

        void LateUpdate()
        {
            if (!from || !to) return;
            Vector3 a = from.position, b = to.position;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Transform car = transform.parent;
            Vector3 up = car ? car.up : Vector3.up;
            transform.SetPositionAndRotation(a, Quaternion.LookRotation(d / len, up));
            Vector3 ps = car ? car.lossyScale : Vector3.one;
            transform.localScale = new Vector3(width / ps.x, thickness / ps.y, len / ps.z);
        }
    }
}
