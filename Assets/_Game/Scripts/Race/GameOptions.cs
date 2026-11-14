using UnityEngine;

namespace OWR.Race
{
    public enum Difficulty { Easy = 0, Normal = 1, Hard = 2 }
    public enum GameMode { Race = 0, Practice = 1 }

    /// <summary>Player options (persisted) + showcase auto-drive. Easy + automatic gears are the defaults.</summary>
    public static class GameOptions
    {
        /// <summary>Set by Restart / Race Again so the reloaded scene skips the menu and goes straight to the grid.</summary>
        public static bool SkipMenuOnce;

        public static Difficulty Difficulty
        {
            get => ForcedDifficulty >= 0 ? (Difficulty)ForcedDifficulty : (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt("OWR_Difficulty", 0), 0, 2);
            set { PlayerPrefs.SetInt("OWR_Difficulty", (int)value); PlayerPrefs.Save(); }
        }

        public static bool AutoGears
        {
            get => PlayerPrefs.GetInt("OWR_AutoGears", 1) == 1;
            set { PlayerPrefs.SetInt("OWR_AutoGears", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Chosen from the main menu each session (RACE or FREE PRACTICE); kept across restarts.</summary>
        public static GameMode Mode = GameMode.Race;
        public static bool Practice => Mode == GameMode.Practice;

        public static readonly int[] LapChoices = { 3, 5, 8, 12, 20 };
        public static int Laps
        {
            get => PlayerPrefs.GetInt("OWR_Laps", 5);
            set { PlayerPrefs.SetInt("OWR_Laps", value); PlayerPrefs.Save(); }
        }

        /// <summary>Starting view: 0 cockpit, 1 T-cam, 2 chase.</summary>
        public static int CameraMode
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("OWR_CamMode", 0), 0, 2);
            set { PlayerPrefs.SetInt("OWR_CamMode", value); PlayerPrefs.Save(); }
        }
        public static bool ChaseCamera => CameraMode == 2;

        /// <summary>One-shot flag: the next race-scene load skips straight to the win celebration (preview).</summary>
        public static bool CelebrationPreview
        {
            get => PlayerPrefs.GetInt("OWR_CelebPreview", 0) == 1;
            set { PlayerPrefs.SetInt("OWR_CelebPreview", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Touch devices: steer by tilting the phone (true) or with on-screen arrows.</summary>
        public static bool TiltSteering
        {
            get => PlayerPrefs.GetInt("OWR_Tilt", 1) == 1;
            set { PlayerPrefs.SetInt("OWR_Tilt", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool MouseSteering
        {
            get => PlayerPrefs.GetInt("OWR_MouseSteer", 0) == 1;
            set { PlayerPrefs.SetInt("OWR_MouseSteer", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Recording only: forces a difficulty without touching the player's saved choice (-1 = none).</summary>
        public static int ForcedDifficulty = -1;

        public static bool AutoDrive
        {
            get
            {
                foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-autodrive") return true;
                return PlayerPrefs.GetInt("OWR_AutoDrive", 0) == 1;
            }
        }

        // ---- difficulty tables ----
        public static int Rivals => Difficulty == Difficulty.Easy ? 7 : Difficulty == Difficulty.Normal ? 9 : 11;
        public static float RivalPace => Difficulty == Difficulty.Easy ? 0.86f : Difficulty == Difficulty.Normal ? 0.93f : 0.98f;
        public static int MaxRivalAccidents => Difficulty == Difficulty.Easy ? 1 : Difficulty == Difficulty.Normal ? 2 : 3;
        public static float MistakeChance => Difficulty == Difficulty.Easy ? 0.04f : Difficulty == Difficulty.Normal ? 0.08f : 0.12f;
    }
}
