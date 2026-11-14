using OWR.Race;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OWR.EditorTools
{
    /// <summary>
    /// OWR > Record Full Race: plays from the title screen through a full auto-driven race to the classification
    /// screen and records 1080p60 MP4 with game audio to Recordings/. Stops itself ~12 s after the results appear.
    /// </summary>
    [InitializeOnLoad]
    public static class RaceRecorder
    {
        const string Key = "OWR_Recording";
        static RecorderController controller;
        static double finishSeenAt = -1;
        static double menuAfterRaceAt = -1;
        const int ShowcaseDifficulty = 1; // Normal: 10 cars, starting P3 for a fight at the front

        static RaceRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Tick;
        }

        [MenuItem("OWR/Record Full Race")]
        public static void Record()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            PlayerPrefs.SetInt("OWR_AutoDrive", 1);
            PlayerPrefs.Save();
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene(MenuSceneBuilder.RacePath);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "Downforce Race";
                movie.Enabled = true;
                movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
                };
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
                movie.CaptureAudio = true;
                movie.OutputFile = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Recordings", "Downforce_FullRace_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm"));
                settings.AddRecorderSettings(movie);
                settings.SetRecordModeToManual();
                settings.FrameRate = 60f;
                settings.CapFrameRate = true;
                GameOptions.ForcedDifficulty = ShowcaseDifficulty;
                SessionState.SetBool("OWR_SawFinish", false);
                controller = new RecorderController(settings);
                controller.PrepareRecording();
                controller.StartRecording();
                finishSeenAt = -1;
                Debug.Log("[OWR] Recording started -> " + movie.OutputFile);
            }
            else if (s == PlayModeStateChange.ExitingPlayMode)
            {
                Stop();
            }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || controller == null) return;
            var rm = RaceManager.Instance;
            if (rm && (rm.State == RaceManager.RaceState.Finished || rm.State == RaceManager.RaceState.Crashed))
                SessionState.SetBool("OWR_SawFinish", true);
            // after the race the showcase clicks MAIN MENU; let the drone shots run a few seconds, then stop
            if (SessionState.GetBool("OWR_SawFinish", false) && rm && rm.waitForStart)
            {
                if (menuAfterRaceAt < 0) menuAfterRaceAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - menuAfterRaceAt > 7.0) EditorApplication.isPlaying = false;
            }
            if (rm && rm.State == RaceManager.RaceState.Crashed)
            {
                if (finishSeenAt < 0) finishSeenAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - finishSeenAt > 20.0) EditorApplication.isPlaying = false;
            }
        }

        static void Stop()
        {
            if (controller != null && controller.IsRecording()) controller.StopRecording();
            controller = null;
            menuAfterRaceAt = -1; finishSeenAt = -1;
            GameOptions.ForcedDifficulty = -1;
            SessionState.SetBool(Key, false);
            SessionState.SetBool("OWR_SawFinish", false);
            PlayerPrefs.SetInt("OWR_AutoDrive", 0);
            PlayerPrefs.Save();
            Debug.Log("[OWR] Recording stopped");
        }
    }
}
