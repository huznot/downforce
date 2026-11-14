using OWR.Race;
using UnityEngine;

namespace OWR.Cockpit
{
    /// <summary>
    /// Victory celebration: both hands let go of the wheel, close into fists, rise up in front of the visor and pump
    /// (alternating, with a bit of shake), the head bobs, the driver yells on the radio and the crowd erupts.
    /// Runs before HandGrip so the fist pose is applied on top of the raised hands.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class Celebration : MonoBehaviour
    {
        public Transform handL, handR;          // the Hand_L / Hand_R anchors on the steering wheel
        public HandGrip gripL, gripR;
        public Transform head;                  // cockpit camera (bobs a little)
        public bool active;
        float t;
        Vector3 restL, restR;
        Quaternion restRotL, restRotR;
        bool cached;

        bool oneHand;
        /// <summary>Crossing the line: the right hand punches the air while the left keeps steering.</summary>
        public void BeginOneHand()
        {
            if (active) return;
            Cache();
            active = true; oneHand = true; t = 0f;
            var com = FindFirstObjectByType<Commentary>();
            if (com) com.Say("yes");
            Track.CrowdHype.Boost(1f, 20f);
        }

        /// <summary>Stopped (or preview): both hands off the wheel.</summary>
        public void Begin()
        {
            if (active && !oneHand) return;
            bool wasOneHand = active && oneHand;
            Cache();
            oneHand = false;
            if (!wasOneHand) t = 0f;
            leftT = 0f;
            active = true;
            var com = FindFirstObjectByType<Commentary>();
            if (com) com.Say("yes");
            Track.CrowdHype.Boost(1f, 20f);
        }
        float leftT;

        void Cache()
        {
            if (cached || !handL || !handR) return;
            restL = handL.localPosition; restR = handR.localPosition;
            restRotL = handL.localRotation; restRotR = handR.localRotation;
            cached = true;
        }

        void LateUpdate()
        {
            if (!active || !handL || !handR) return;
            t += Time.deltaTime;
            if (!oneHand) leftT += Time.deltaTime;
            float liftR = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.5f));
            float liftL = oneHand ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(leftT / 0.5f));
            if (gripR) { gripR.fist = liftR; if (liftR > 0.3f) gripR.gripAxis = null; }
            if (gripL && !oneHand) { gripL.fist = liftL; if (liftL > 0.3f) gripL.gripAxis = null; }

            float pumpL = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 13.5f)), 0.6f);
            float pumpR = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 13.5f + Mathf.PI)), 0.6f);
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t * 6f, 0f) - 0.5f, Mathf.PerlinNoise(0f, t * 6f) - 0.5f, 0f) * 0.006f;
            // the anchors are children of the turning wheel: counter-rotate so a raised fist stays steady
            Quaternion inv = handR.parent ? Quaternion.Inverse(handR.parent.localRotation) : Quaternion.identity;
            // wheel space: +y up, -z toward the driver. Anchors are mirrored on the left (scale.x = -1).
            Vector3 upR = oneHand
                ? new Vector3(0.10f, 0.16f + 0.07f * pumpR, -0.03f - 0.03f * pumpR) - shake     // one fist up, out to the side
                : new Vector3(0.05f, 0.12f + 0.07f * pumpR, -0.02f - 0.03f * pumpR) - shake;
            handR.localPosition = Vector3.Lerp(restR, inv * (restR + upR), liftR);
            handR.localRotation = Quaternion.Slerp(restRotR, inv * restRotR * Quaternion.Euler(-55f - 15f * pumpR, 0f, -25f), liftR);
            if (!oneHand)
            {
                Vector3 upL = new Vector3(-0.05f, 0.12f + 0.07f * pumpL, -0.02f - 0.03f * pumpL) + shake;
                handL.localPosition = Vector3.Lerp(restL, inv * (restL + upL), liftL);
                handL.localRotation = Quaternion.Slerp(restRotL, inv * restRotL * Quaternion.Euler(-55f - 15f * pumpL, 0f, 25f), liftL);
            }

            // gentle nod in time with the pumps (kept small: the view must stay steady)
            if (head) head.localRotation *= Quaternion.Euler(1.2f * Mathf.Sin(t * 6.75f) * Mathf.Max(liftL, liftR * 0.6f), 0f, 0f);
        }
    }
}
