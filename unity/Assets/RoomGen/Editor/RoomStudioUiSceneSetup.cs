using System.IO;
using RoomGen.UI.Studio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.Editor
{
    /// <summary>
    /// Creates the scene for the UI Toolkit Room Studio: one object carrying
    /// <see cref="RoomStudioPanelController"/> + a UIDocument wired to RoomStudioPanel.uxml. Open it
    /// and press Play; the panel boots itself in Start.
    ///
    /// Ships BESIDE RoomStudio.unity (the legacy IMGUI studio) — that scene is untouched and keeps
    /// working until parity is confirmed. Build settings are left alone.
    /// </summary>
    public static class RoomStudioUiSceneSetup
    {
        const string ScenesDir = "Assets/RoomGen/Scenes";
        const string ScenePath = ScenesDir + "/RoomStudioUI.unity";
        const string UxmlPath = "Assets/RoomGen/UI/Studio/RoomStudioPanel.uxml";
        // Reuse the operator's PanelSettings rather than authoring a second one: it already carries
        // the hard-won configuration (notably clearColor = false, without which the panel wipes the
        // walk camera's view the moment the operator enters a room).
        const string PanelSettingsPath = "Assets/RoomGen/UI/Operator/OperatorPanelSettings.asset";

        [MenuItem("RoomGen/Setup Room Studio (UI Toolkit) Scene")]
        public static void CreateAndSaveScene()
        {
            if (!Directory.Exists(ScenesDir))
            {
                Directory.CreateDirectory(ScenesDir);
                AssetDatabase.Refresh();
            }

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                Debug.LogError("RoomStudioUiSceneSetup: RoomStudioPanel.uxml not found at " + UxmlPath);
                return;
            }

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                Debug.LogError("RoomStudioUiSceneSetup: PanelSettings not found at " + PanelSettingsPath
                               + " — run 'RoomGen/Setup Operator Studio Scene' once to create it.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var studioObject = new GameObject("Room Studio (UI Toolkit)");
            var doc = studioObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;   // base.uss comes in via the uxml's own <Style src=.../>
            studioObject.AddComponent<RoomStudioPanelController>();

            // In batchmode the panelSettings SETTER does not always reach the serialized stream (the
            // saved scene then carries m_PanelSettings {fileID: 0} → the panel binds to a detached
            // root → grey screen with zero errors). Write it through SerializedObject so the save is
            // guaranteed to carry the reference.
            var so = new SerializedObject(doc);
            so.FindProperty("m_PanelSettings").objectReferenceValue = panelSettings;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(doc);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("RoomStudioUiSceneSetup: saved " + ScenePath
                      + " — open it and press Play. RoomStudio.unity (legacy IMGUI) is untouched.");
            Selection.activeGameObject = studioObject;
        }
    }
}
