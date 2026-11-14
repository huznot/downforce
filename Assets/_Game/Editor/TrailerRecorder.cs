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
    /// OWR > Record Trailer: two automatic passes recorded to Recordings/ (1080p60 mp4 with game audio) plus store
    /// screenshots in Recordings/StoreShots. Pass 1: an auto-driven race shot by <see cref="TrailerDirector"/>.
    /// Pass 2: the chequered flag and the win celebration. Tools/make_trailer.py then cuts the final trailer.
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerRecorder
    {
        const string Key = "OWR_TrailerPass";
        static RecorderController controller;

        static TrailerRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Tick;
        }

        [MenuItem("OWR/Record Trailer")]
        public static void Record()
        {
            if (System.IO.Directory.Exists("Recordings/StoreShots")) System.IO.Directory.Delete("Recordings/StoreShots", true);
            SessionState.SetInt(Key, 1);
            StartPass();
        }

        static void StartPass()
        {
            int pass = SessionState.GetInt(Key, 0);
            PlayerPrefs.SetInt("OWR_AutoDrive", pass == 1 ? 1 : 0);
            GameOptions.CelebrationPreview = pass == 2;
            PlayerPrefs.Save();
            EditorSceneManager.OpenScene(MenuSceneBuilder.RacePath);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            int pass = SessionState.GetInt(Key, 0);
            if (pass == 0) return;
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                GameOptions.ForcedDifficulty = 1;
                var go = new GameObject("TrailerDirector");
                go.AddComponent<TrailerDirector>().celebrationMode = pass == 2;
                var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "Trailer";
                movie.Enabled = true;
                movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
                };
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
                movie.CaptureAudio = true;
                movie.OutputFile = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Recordings", pass == 1 ? "trailer_race" : "trailer_celebration");
                settings.AddRecorderSettings(movie);
                settings.SetRecordModeToManual();
                settings.FrameRate = 60f;
                settings.CapFrameRate = true;
                controller = new RecorderController(settings);
                controller.PrepareRecording();
                controller.StartRecording();
                Debug.Log("[OWR] Trailer pass " + pass + " recording");
            }
            else if (s == PlayModeStateChange.ExitingPlayMode)
            {
                if (controller != null && controller.IsRecording()) controller.StopRecording();
                controller = null;
                GameOptions.ForcedDifficulty = -1;
                PlayerPrefs.SetInt("OWR_AutoDrive", 0); PlayerPrefs.Save();
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                if (pass == 1) { SessionState.SetInt(Key, 2); EditorApplication.delayCall += StartPass; }
                else { SessionState.SetInt(Key, 0); GameOptions.CelebrationPreview = false; Debug.Log("[OWR] Trailer recorded"); }
            }
        }

        static void Tick()
        {
            if (SessionState.GetInt(Key, 0) == 0 || !EditorApplication.isPlaying) return;
            if (TrailerDirector.Done) { TrailerDirector.Done = false; EditorApplication.isPlaying = false; }
        }
    }
}
