using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RoomGen.UI.Studio
{
    /// <summary>
    /// Makes the UI Toolkit Room Studio the DEFAULT thing you get when you press Play, in any scene
    /// that does not already compose its own UI.
    ///
    /// Before this, `RoomStudioBootstrap` auto-spawned the LEGACY IMGUI panel into every scene except
    /// a hardcoded skip list, so pressing Play on an empty/Untitled scene produced the old UI and the
    /// new studio was only reachable by knowing to open one specific scene file. That is backwards —
    /// the current studio should be what you get by default, and the legacy one should be the thing
    /// you go looking for. `RoomStudioBootstrap` is now opt-in to the "RoomStudio" scene only, and
    /// this supplies the new panel everywhere else.
    ///
    /// Editor-only by design: it loads the UXML + PanelSettings through AssetDatabase, which does not
    /// exist in a player. A BUILD does not need it — `Scenes/RoomStudioUI.unity` is first in Build
    /// Settings and already carries a fully wired UIDocument.
    /// </summary>
    public static class RoomStudioUiBootstrap
    {
#if UNITY_EDITOR
        const string UxmlPath = "Assets/RoomGen/UI/Studio/RoomStudioPanel.uxml";
        const string PanelSettingsPath = "Assets/RoomGen/UI/Operator/OperatorPanelSettings.asset";

        // Scenes that build their own UI and must be left alone. "RoomStudio" is the legacy IMGUI
        // studio's own scene; the other two are Paco's operator/participant scenes.
        static readonly string[] SelfComposingScenes =
            { "RoomStudio", "OperatorStudio", "ParticipantRunner" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureStudio()
        {
            var active = SceneManager.GetActiveScene().name;
            if (System.Array.IndexOf(SelfComposingScenes, active) >= 0) return;
            // PlayMode test scenes ("InitTestScene<guid>") must stay empty: spawning the studio there
            // builds rooms on layers 8/9 at the origin, on top of whatever the test itself builds.
            if (active.StartsWith("InitTestScene")) return;
            // RoomStudioUI.unity already carries the controller; don't add a second one.
            if (Object.FindFirstObjectByType<RoomStudioPanelController>() != null) return;

            var uxml = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (uxml == null || panelSettings == null)
            {
                Debug.LogWarning("RoomStudioUiBootstrap: panel assets missing — open "
                                 + "Scenes/RoomStudioUI.unity directly.");
                return;
            }

            var root = new GameObject("Room Studio (UI Toolkit)");
            // The controller's [RequireComponent(typeof(UIDocument))] creates the document; configure
            // it BEFORE adding the controller so its Start() finds a bound root on the first frame.
            var doc = root.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;
            root.AddComponent<RoomStudioPanelController>();
        }
#endif
    }
}
