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
    }
}
