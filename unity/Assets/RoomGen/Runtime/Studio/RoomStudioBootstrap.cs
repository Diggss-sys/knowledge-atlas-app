using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomGen.Studio
{
    public static class RoomStudioBootstrap
    {
        // The ONE scene the legacy IMGUI studio still owns. This used to be an opt-OUT list (spawn
        // everywhere except a few named scenes), which meant pressing Play on any empty/Untitled
        // scene silently produced the OLD panel — you had to know to open a specific scene to see the
        // new one. Opt-IN inverts that: the legacy studio appears only where it is explicitly wanted,
        // and RoomStudioUiBootstrap (in RoomGen.UI) supplies the UI Toolkit studio everywhere else.
        const string LegacySceneName = "RoomStudio";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureStudio()
        {
            if (SceneManager.GetActiveScene().name != LegacySceneName) return;
            if (Object.FindFirstObjectByType<RoomStudioController>() != null) return;
            if (HasUiToolkitStudio()) return;
            var root = new GameObject("Room Studio");
            root.AddComponent<RoomStudioController>();
        }

        /// <summary>
        /// True when the scene already carries the UI Toolkit studio. The name list above is the
        /// thing that broke: RoomStudioUI was added and nobody remembered to list it, so the legacy
        /// IMGUI panel drew its full-screen GUI.Box over the new one. This check cannot go stale.
        ///
        /// Matched by type NAME on purpose — RoomGen.Runtime must not reference RoomGen.UI (that
        /// would invert the dependency: every UI producer drives the engine, never the reverse).
        /// </summary>
        static bool HasUiToolkitStudio()
        {
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (behaviour != null && behaviour.GetType().Name == "RoomStudioPanelController")
                    return true;
            return false;
        }
    }
}
