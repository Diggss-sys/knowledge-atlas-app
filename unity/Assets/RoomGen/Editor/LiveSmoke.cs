using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoomGen.Editor
{
    /// <summary>
    /// Live-display smoke entry points: launch the FULL editor (no -batchmode) with
    /// <c>-executeMethod RoomGen.Editor.LiveSmoke.PlayOperatorStudio</c> (or PlayParticipantRunner)
    /// and it opens the scene and enters play mode on a real display. Exists because headless suites
    /// structurally cannot see real-display bugs — the F1/F5 class (panel clearing the walk view,
    /// a detached panel root) only ever shows up here. Also reachable from the RoomGen menu.
    /// </summary>
    public static class LiveSmoke
    {
        [MenuItem("RoomGen/Live Smoke/Play Operator Studio")]
        public static void PlayOperatorStudio() => Play("Assets/RoomGen/Scenes/OperatorStudio.unity");

        [MenuItem("RoomGen/Live Smoke/Play Participant Runner")]
        public static void PlayParticipantRunner() => Play("Assets/RoomGen/Scenes/ParticipantRunner.unity");

        static void Play(string scenePath)
        {
            // Delay until the editor is fully initialized — -executeMethod runs early, and entering
            // play mode before the first editor update is ignored on some versions.
            EditorApplication.delayCall += () =>
            {
                EditorSceneManager.OpenScene(scenePath);
                Debug.Log("LiveSmoke: opened " + scenePath + " — entering play mode.");
                EditorApplication.EnterPlaymode();
            };
        }
    }
}
