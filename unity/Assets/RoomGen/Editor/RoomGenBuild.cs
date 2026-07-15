using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoomGen.Editor
{
    public static class RoomGenBuild
    {
        const string ParticipantScene = "Assets/RoomGen/Scenes/ParticipantRunner.unity";

        [MenuItem("RoomGen/Build Windows Application")]
        public static void BuildWindows() =>
            Build("Room Studio", "Assets/RoomGen/Scenes/RoomStudio.unity",
                "Builds/Windows/RoomStudio.exe", BuildTarget.StandaloneWindows64);

        /// <summary>
        /// macOS build for the desktop arm — the scene is light enough for an M-series MacBook (VR
        /// stays Windows/lab-PC). Requires the "Mac Build Support (Mono)" module in Unity Hub when
        /// cross-building from Windows. First launch on a Mac: right-click the .app > Open (unsigned).
        /// </summary>
        [MenuItem("RoomGen/Build macOS Application")]
        public static void BuildMacOS() =>
            Build("Room Studio (macOS)", "Assets/RoomGen/Scenes/RoomStudio.unity",
                "Builds/macOS/RoomStudio.app", BuildTarget.StandaloneOSX);

        // ---- Participant study app (A2) — what teammates run for the team session. Boots straight
        // into ParticipantRunner (no operator tools); a teammate double-clicks it, does one session,
        // and the perf sidecar + CSV land locally. ----

        [MenuItem("RoomGen/Build Participant App (Windows)")]
        public static void BuildParticipantWindows() =>
            Build("Participant app", ParticipantScene,
                "Builds/Windows-Participant/KnowledgeAtlasStudy.exe", BuildTarget.StandaloneWindows64);

        [MenuItem("RoomGen/Build Participant App (macOS)")]
        public static void BuildParticipantMacOS() =>
            Build("Participant app (macOS)", ParticipantScene,
                "Builds/macOS-Participant/KnowledgeAtlasStudy.app", BuildTarget.StandaloneOSX);

        static void Build(string label, string scene, string relativeOutput, BuildTarget target)
        {
            if (target == BuildTarget.StandaloneOSX &&
                !BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                throw new BuildFailedException(
                    "macOS build support is not installed. Unity Hub > Installs > 6000.3.16f1 > Add modules > Mac Build Support (Mono).");

            RoomGenProjectBootstrap.EnsureProject();
            var output = Path.GetFullPath(relativeOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);

            // OpenXR's build preprocessor throws "OpenXR Settings found in project but not yet loaded.
            // Please build again." on the FIRST build after a platform switch — the settings asset is
            // created on that pass and loads on the next. Interactively you just click Build twice;
            // headless, each -executeMethod is a fresh session, so retry once IN-SESSION here. The
            // participant app has no VR — this is purely OpenXR's build hook being fussy in batchmode.
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogWarning($"{label}: first build attempt failed ({report.summary.totalErrors} errors) — " +
                                 "retrying once in-session (OpenXR settings load on the 2nd pass).");
                report = BuildPipeline.BuildPlayer(options);
            }

            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{label} build failed with {report.summary.totalErrors} errors.");
            Debug.Log($"{label} built at {output}");
        }
    }
}
