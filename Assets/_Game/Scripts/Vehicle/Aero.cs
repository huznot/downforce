using UnityEngine;

namespace OWR.Vehicle
{
    /// <summary>Downforce growing with v^2, split between the axles, plus aerodynamic drag.</summary>
    public static class Aero
    {
        public static float Apply(Rigidbody rb, Transform car, CarSetup s, Vector3 frontAxle, Vector3 rearAxle)
        {
            Vector3 vel = rb.linearVelocity;
            float vFwd = Vector3.Dot(vel, car.forward);
            float q = 0.5f * s.airDensity * vFwd * vFwd;

            float downforce = q * s.liftCoefficientArea;
            Vector3 down = -car.up;
            rb.AddForceAtPosition(down * downforce * s.aeroBalanceFront, frontAxle);
            rb.AddForceAtPosition(down * downforce * (1f - s.aeroBalanceFront), rearAxle);

            float speed = vel.magnitude;
            if (speed > 0.1f)
                rb.AddForce(-vel / speed * (0.5f * s.airDensity * speed * speed * s.dragCoefficientArea));

            return downforce;
        }
    }
}
