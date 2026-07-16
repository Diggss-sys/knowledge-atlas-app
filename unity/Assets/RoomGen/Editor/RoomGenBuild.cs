using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoomGen.Editor
{
    public static class RoomGenBuild
    {
        [MenuItem("RoomGen/Build Windows Application")]
        public static void BuildWindows()
        {
            RoomGenProjectBootstrap.EnsureProject();
            var output = Path.GetFullPath("Builds/Windows/RoomStudio.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/RoomGen/Scenes/RoomStudio.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"Room Studio build failed with {report.summary.totalErrors} errors.");
            Debug.Log("Room Studio built at " + output);
        }

        /// <summary>
        /// macOS (Apple Silicon + Intel) build for the desktop arm — the scene is light enough for an
        /// M-series MacBook (see docs/CLOUD_RENDERING_RESEARCH.md; VR stays Windows/lab-PC). Requires
        /// the "Mac Build Support (Mono)" module in Unity Hub when cross-building from Windows, or run
        /// this on the Mac itself. First launch on a Mac: right-click the .app > Open (unsigned).
        /// </summary>
        [MenuItem("RoomGen/Build macOS Application")]
        public static void BuildMacOS()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                throw new BuildFailedException(
                    "macOS build support is not installed. Unity Hub > Installs > 6000.3.16f1 > Add modules > Mac Build Support (Mono).");

            RoomGenProjectBootstrap.EnsureProject();
            var output = Path.GetFullPath("Builds/macOS/RoomStudio.app");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/RoomGen/Scenes/RoomStudio.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"Room Studio macOS build failed with {report.summary.totalErrors} errors.");
            Debug.Log("Room Studio (macOS) built at " + output);
        }

        // ---- Participant study app (A2) — what teammates actually run for the team session ----
        // Boots straight into the ParticipantRunner scene (no operator tools), so a non-technical
        // teammate double-clicks it, does one session, and the perf sidecar + CSV land locally.

        const string ParticipantScene = "Assets/RoomGen/Scenes/ParticipantRunner.unity";

        [MenuItem("RoomGen/Build Participant App (Windows)")]
        public static void BuildParticipantWindows()
        {
            RoomGenProjectBootstrap.EnsureProject();
            var output = Path.GetFullPath("Builds/Windows-Participant/KnowledgeAtlasStudy.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ParticipantScene },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"Participant app build failed with {report.summary.totalErrors} errors.");
            Debug.Log("Participant app built at " + output);
        }

        [MenuItem("RoomGen/Build Participant App (macOS)")]
        public static void BuildParticipantMacOS()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                throw new BuildFailedException(
                    "macOS build support is not installed. Unity Hub > Installs > 6000.3.16f1 > Add modules > Mac Build Support (Mono).");

            RoomGenProjectBootstrap.EnsureProject();
            var output = Path.GetFullPath("Builds/macOS-Participant/KnowledgeAtlasStudy.app");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ParticipantScene },
                locationPathName = output,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"Participant app (macOS) build failed with {report.summary.totalErrors} errors.");
            Debug.Log("Participant app (macOS) built at " + output);
        }
    }
}
