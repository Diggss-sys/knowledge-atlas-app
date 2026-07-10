using System.IO;
using RoomGen.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.Editor
{
    /// <summary>
    /// Creates the additive A2 scene: an empty scene with one "Participant Runner" object carrying a
    /// <see cref="ParticipantFlow"/> + a UIDocument wired to ParticipantScreens.uxml and a
    /// screen-rendered PanelSettings (neutral clear colour — no brand). Play it and the flow boots
    /// itself (Start). Ships beside RoomStudio.unity and OperatorStudio.unity; build settings untouched.
    /// </summary>
    public static class ParticipantRunnerSceneSetup
    {
        const string ScenesDir = "Assets/RoomGen/Scenes";
        const string ScenePath = ScenesDir + "/ParticipantRunner.unity";
        const string PanelSettingsPath = "Assets/RoomGen/UI/Runner/ParticipantPanelSettings.asset";
        const string UxmlPath = "Assets/RoomGen/UI/Runner/ParticipantScreens.uxml";
        const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        [MenuItem("RoomGen/Setup Participant Runner Scene")]
        public static void CreateAndSaveScene()
        {
            if (!Directory.Exists(ScenesDir))
            {
                Directory.CreateDirectory(ScenesDir);
                AssetDatabase.Refresh();
            }

            var panelSettings = EnsurePanelSettings();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                Debug.LogError("ParticipantRunnerSceneSetup: ParticipantScreens.uxml not found at " + UxmlPath);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var runnerObject = new GameObject("Participant Runner");
            runnerObject.AddComponent<ParticipantFlow>();
            var doc = runnerObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;   // runner.uss is pulled in by the uxml's own <Style src=.../>

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("ParticipantRunnerSceneSetup: saved additive scene to " + ScenePath
                      + " — open it and press Play.");
            Selection.activeGameObject = runnerObject;
        }

        static PanelSettings EnsurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null) return ps;

            ps = ScriptableObject.CreateInstance<PanelSettings>();
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null) ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            // clearColor must stay FALSE on a screen-space panel: clearing wipes the walk camera's
            // view during the participant's room exploration (the panel clears even with its root
            // hidden). The .runner-root is opaque + full-bleed, so no clear is needed. Guarded by
            // ScenePanelSettingsTests.
            ps.clearColor = false;
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return ps;
        }
    }
}
