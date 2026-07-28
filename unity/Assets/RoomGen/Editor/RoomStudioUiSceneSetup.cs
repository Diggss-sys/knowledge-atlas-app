using System.IO;
using System.Linq;
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

            // THE BUG THAT COST A DAY: load the assets only AFTER the new scene exists.
            // EditorSceneManager.NewScene tears down the previous scene and unloads unused assets, and
            // a PanelSettings/VisualTreeAsset held only by a local variable counts as unused — both
            // handles came back destroyed (fake-null), so every later assignment silently wrote null
            // and the scene saved {fileID: 0}. No exception, no warning, just a grey panel.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

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

            var studioObject = new GameObject("Room Studio (UI Toolkit)");
            // Add the controller FIRST: its [RequireComponent(typeof(UIDocument))] creates the
            // UIDocument itself, so there is only ever one document on the object.
            studioObject.AddComponent<RoomStudioPanelController>();
            var doc = studioObject.GetComponent<UIDocument>();

            Wire(doc, "in-memory");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Prove the references SURVIVED serialization rather than trusting the setters — a
            // dropped PanelSettings renders a grey screen with no errors at all, and a batchmode
            // save has been observed writing {fileID: 0} even when the setter appeared to take.
            // Re-open the saved scene from disk (a fresh deserialize, nothing cached) and, if the
            // refs are gone, write them again and re-save.
            var reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var reopenedDoc = Object.FindFirstObjectByType<UIDocument>();
            if (reopenedDoc == null)
            {
                Debug.LogError("RoomStudioUiSceneSetup: saved scene has no UIDocument at all.");
                return;
            }
            if (reopenedDoc.panelSettings == null || reopenedDoc.visualTreeAsset == null)
            {
                Wire(reopenedDoc, "re-opened");
                EditorSceneManager.MarkSceneDirty(reopened);
                EditorSceneManager.SaveScene(reopened, ScenePath);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                reopenedDoc = Object.FindFirstObjectByType<UIDocument>();
            }

            if (reopenedDoc.panelSettings == null)
                Debug.LogError("RoomStudioUiSceneSetup: PanelSettings did NOT persist — the panel would "
                               + "render nothing. Assign it by hand on the UIDocument component.");
            else
                Debug.Log("RoomStudioUiSceneSetup: verified on disk — panelSettings="
                          + reopenedDoc.panelSettings.name + " uxml="
                          + (reopenedDoc.visualTreeAsset != null ? reopenedDoc.visualTreeAsset.name : "NULL"));

            Debug.Log("RoomStudioUiSceneSetup: saved " + ScenePath
                      + " — open it and press Play. RoomStudio.unity (legacy IMGUI) is untouched.");

            AddToBuildSettings();
        }

        /// <summary>
        /// Writes both UIDocument references twice over: the public setters (which is what works in
        /// OperatorStudioSceneSetup) AND the serialized fields, then marks the component dirty.
        /// Logs what actually stuck, because the failure mode here is silent.
        /// </summary>
        static void Wire(UIDocument doc, string stage)
        {
            // Re-load here rather than passing handles in: any scene operation between the load and
            // the assignment can unload them out from under us (see CreateAndSaveScene).
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);

            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;   // base.uss rides in via the uxml's own <Style src=.../>

            var so = new SerializedObject(doc);
            so.FindProperty("m_PanelSettings").objectReferenceValue = panelSettings;
            so.FindProperty("sourceAsset").objectReferenceValue = uxml;
            so.ApplyModifiedPropertiesWithoutUndo();
            so.Update();

            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(doc.gameObject);

            Debug.Log($"RoomStudioUiSceneSetup [{stage}]: m_PanelSettings="
                      + (so.FindProperty("m_PanelSettings").objectReferenceValue?.name ?? "NULL")
                      + " sourceAsset="
                      + (so.FindProperty("sourceAsset").objectReferenceValue?.name ?? "NULL"));
        }

        /// <summary>
        /// Puts RoomStudioUI FIRST in the build settings so a built player — and "press Play" from a
        /// fresh checkout — lands on the new studio without hunting for a scene. RoomStudio.unity
        /// stays enabled behind it, so the legacy IMGUI studio remains reachable.
        /// Done through the API; the .asset YAML is never hand-edited.
        /// </summary>
        [MenuItem("RoomGen/Set Room Studio (UI Toolkit) as first build scene")]
        public static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.path != ScenePath)
                .ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("RoomStudioUiSceneSetup: build settings now start at " + ScenePath
                      + " (" + scenes.Count + " scenes).");
        }
    }
}
