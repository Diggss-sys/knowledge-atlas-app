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
    }
}
