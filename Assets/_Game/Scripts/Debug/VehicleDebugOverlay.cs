using OWR.Vehicle;
using UnityEngine;

namespace OWR.DebugTools
{
    /// <summary>On-screen telemetry. F1 toggles the full panel, F2 toggles the auto gearbox.</summary>
    public class VehicleDebugOverlay : MonoBehaviour
    {
        public CarController car;
        public bool showFull = true;
        GUIStyle style, big;

        void Update()
        {
            if (car && car.input && car.input.ConsumeDebugToggle()) showFull = !showFull;
        }

        void OnGUI()
        {
            if (!car) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                style.normal.textColor = Color.white;
                big = new GUIStyle(style) { fontSize = 34, fontStyle = FontStyle.Bold };
            }

            if (!showFull) return; // the race HUD replaces this outside of debug
            var t = car.telemetry;
            string gear = t.gear == -1 ? "R" : t.gear == 0 ? "N" : t.gear.ToString();
            GUI.Box(new Rect(Screen.width - 230, Screen.height - 120, 220, 110), GUIContent.none);
            GUI.Label(new Rect(Screen.width - 220, Screen.height - 118, 210, 44), $"{Mathf.Abs(t.SpeedKmh):0} km/h", big);
            GUI.Label(new Rect(Screen.width - 220, Screen.height - 76, 210, 24),
                $"Gear <b>{gear}</b>   {t.rpm:0} rpm{(t.revLimiter ? " <color=red>LIM</color>" : "")}", style);
            GUI.Label(new Rect(Screen.width - 220, Screen.height - 52, 210, 24),
                $"ERS {t.batteryFraction * 100f:0}%{(t.ersDeploying ? " <color=yellow>DEPLOY</color>" : "")}   {(car.setup.autoShift ? "AUTO" : "MAN")}  TC {(car.setup.tractionControl ? (car.TractionControlActive ? "<color=orange>ON</color>" : "on") : "off")}", style);

            if (!showFull)
            {
                GUI.Label(new Rect(10, 10, 400, 24), "F1: telemetry", style);
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 430, 420), GUI.skin.box);
            GUILayout.Label("<b>VEHICLE DEBUG</b>  (F1 hide · F2 auto gears · F3 TC)", style);
            GUILayout.Label($"Throttle {t.throttle:0.00}  Brake {t.brake:0.00}  Steer {t.steer:+0.00;-0.00}  ({t.steerAngleDeg:0.0}°)", style);
            GUILayout.Label($"G  lat {t.localAccelG.x:+0.00;-0.00}  long {t.localAccelG.z:+0.00;-0.00}  vert {t.localAccelG.y:+0.00;-0.00}", style);
            GUILayout.Label($"Downforce {t.downforceN:0} N  ({t.downforceN / (car.setup.mass * 9.81f):0.00} x weight)", style);
            GUILayout.Space(6);
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < car.wheels.Length; i++)
            {
                var w = car.wheels[i];
                string slipColor = w.combinedSlip > 1.15f ? "red" : w.combinedSlip > 0.85f ? "yellow" : "lime";
                GUILayout.Label(
                    $"{names[i]}  load {w.load,6:0} N  comp {w.compression * 1000f,4:0} mm  " +
                    $"κ {w.slipRatio:+0.00;-0.00}  α {w.slipAngleDeg:+0.0;-0.0}°  <color={slipColor}>slip {w.combinedSlip:0.00}</color>", style);
            }
            GUILayout.Space(6);
            GUILayout.Label("Pad: RT gas · LT brake · L-stick steer · RB/LB shift · View reset", style);
            GUILayout.Label("Keys: W/S gas/brake · A/D steer · E/Q shift · R reset", style);
            GUILayout.EndArea();
        }
    }
}
