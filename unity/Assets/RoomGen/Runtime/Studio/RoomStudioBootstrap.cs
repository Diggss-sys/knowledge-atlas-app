using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomGen.Studio
{
    public static class RoomStudioBootstrap
    {
        // The additive scenes (A1 operator studio, A2 participant runner) compose their own UI and
        // rooms; auto-spawning the legacy IMGUI controller there would draw a second studio over them
        // and fight for input. Skip those scenes by name — every other scene (the built RoomStudio
        // demo, a bare test scene) behaves exactly as before.
        static readonly string[] AdditiveScenes = { "OperatorStudio", "ParticipantRunner" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureStudio()
        {
            var active = SceneManager.GetActiveScene().name;
            if (System.Array.IndexOf(AdditiveScenes, active) >= 0) return;
            // PlayMode test scenes ("InitTestScene<guid>") must stay empty too: auto-spawning the full
            // IMGUI studio there builds its bundled pair on layers 8/9 at the origin — superimposed on
            // whatever rooms the test itself builds on those layers (Fable review, 2026-07-10).
            if (active.StartsWith("InitTestScene")) return;
            if (Object.FindFirstObjectByType<RoomStudioController>() != null) return;
            var root = new GameObject("Room Studio");
            root.AddComponent<RoomStudioController>();
        }
    }
}
