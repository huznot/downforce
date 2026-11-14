using UnityEngine;

namespace OWR.Vehicle
{
    /// <summary>Snapshot of the car state, written each physics step and read by camera, audio, HUD and hands.</summary>
    [System.Serializable]
    public struct CarTelemetry
    {
        public float speedMs;
        public float SpeedKmh => speedMs * 3.6f;
        public float rpm;
        public int gear;            // -1 = R, 0 = N, 1..8
        public bool shifting;
        public float throttle, brake, steer;   // inputs after processing (steer -1..1)
        public float steerAngleDeg;
        public Vector3 localAccelG;  // x = lateral, y = vertical, z = longitudinal (in g)
        public float downforceN;
        public float batteryFraction;
        public bool ersDeploying;
        public bool revLimiter;
        public int groundedWheels;
    }
}
