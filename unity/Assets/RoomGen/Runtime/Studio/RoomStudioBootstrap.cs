using UnityEngine;

namespace RoomGen.Studio
{
    public static class RoomStudioBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureStudio()
        {
            if (Object.FindFirstObjectByType<RoomStudioController>() != null) return;
            var root = new GameObject("Room Studio");
            root.AddComponent<RoomStudioController>();
        }
    }
}
