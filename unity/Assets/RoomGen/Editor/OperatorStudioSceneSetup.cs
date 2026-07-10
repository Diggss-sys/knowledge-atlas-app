using System.IO;
using RoomGen.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.Editor
{
    /// <summary>
    /// Creates the additive A1 scene: an empty scene with one "Operator Studio" object carrying an
    /// <see cref="OperatorStudio"/> + a UIDocument wired to OperatorPanel.uxml and a screen-rendered
    /// PanelSettings. Play the scene and the studio boots itself (Start). This ships BESIDE
    /// RoomStudio.unity — it never replaces it, and build settings are left untouched.
    /// </summary>
    public static class OperatorStudioSceneSetup
    {
        const string ScenesDir = "Assets/RoomGen/Scenes";
        const string ScenePath = ScenesDir + "/OperatorStudio.unity";
        const string PanelSettingsPath = "Assets/RoomGen/UI/Operator/OperatorPanelSettings.asset";
        const string UxmlPath = "Assets/RoomGen/UI/Operator/OperatorPanel.uxml";
        const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        [MenuItem("RoomGen/Setup Operator Studio Scene")]
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
                Debug.LogError("OperatorStudioSceneSetup: OperatorPanel.uxml not found at " + UxmlPath);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var studioObject = new GameObject("Operator Studio");
            studioObject.AddComponent<OperatorStudio>();
            var doc = studioObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;   // base.uss is pulled in by the uxml's own <Style src=.../>

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("OperatorStudioSceneSetup: saved additive scene to " + ScenePath
                      + " — open it and press Play (RoomStudio.unity is untouched).");
            Selection.activeGameObject = studioObject;
        }

        static PanelSettings EnsurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null) return ps;

            ps = ScriptableObject.CreateInstance<PanelSettings>();
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null) ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            ps.clearColor = true;
            ps.colorClearValue = new Color(0.968f, 0.956f, 0.937f, 1f); // --ka-cream
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return ps;
        }
    }
}
