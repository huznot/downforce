using UnityEngine;

namespace OWR.Track
{
    /// <summary>
    /// Drives the low-poly marshal figure: loops an idle clip on the body, then swings his real arm to follow the
    /// flag pivot that <see cref="ChequeredFlag"/> animates, so the hand stays on the flag pole while he waves.
    /// Runs after ChequeredFlag (execution order) so it reads this frame's pivot.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class MarshalRig : MonoBehaviour
    {
        public AnimationClip idle;
        public Transform body;          // root the clip is sampled on
        public Transform upperArm, lowerArm, wrist;
        public Transform target;        // where the hand should be (the pole grip)
        float t;

        void LateUpdate()
        {
            if (idle && body)
            {
                t += Time.deltaTime;
                idle.SampleAnimation(body.gameObject, idle.length > 0f ? t % idle.length : 0f);
            }
            if (!upperArm || !wrist || !target) return;
            // aim the whole arm at the grip (two passes converge)
            for (int i = 0; i < 2; i++)
            {
                Vector3 cur = wrist.position - upperArm.position, want = target.position - upperArm.position;
                if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) break;
                upperArm.rotation = Quaternion.FromToRotation(cur, want) * upperArm.rotation;
            }
        }
    }
}
