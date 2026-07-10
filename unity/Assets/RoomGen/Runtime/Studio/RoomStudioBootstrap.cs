using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomGen.Studio
{
    public static class RoomStudioBootstrap
    {
        // The additive interactive studio (A1) lives in its own scene and composes the UI Toolkit
        // panel itself; auto-spawning the legacy IMGUI controller there would draw a second studio
        // over it and fight for input. Skip that one scene by name — every other scene (the built
        // RoomStudio demo, a bare test scene) behaves exactly as before.
        const string OperatorStudioScene = "OperatorStudio";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureStudio()
        {
            if (SceneManager.GetActiveScene().name == OperatorStudioScene) return;
            if (Object.FindFirstObjectByType<RoomStudioController>() != null) return;
            var root = new GameObject("Room Studio");
            root.AddComponent<RoomStudioController>();
        }
    }
}
