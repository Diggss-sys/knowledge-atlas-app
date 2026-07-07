using System.IO;
using RoomGen.Studio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomGen.Editor
{
    public static class StudioSceneSetup
    {
        [MenuItem("RoomGen/Setup Studio Scene")]
        public static void CreateAndSaveStudioScene()
        {
            // 1. Ensure the Scenes directory exists
            var scenesDir = "Assets/RoomGen/Scenes";
            if (!Directory.Exists(scenesDir))
            {
                Directory.CreateDirectory(scenesDir);
                AssetDatabase.Refresh();
            }

            // 2. Create a completely empty scene
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 3. Create the GameObject and attach our script
            var studioObject = new GameObject("Room Studio");
            studioObject.AddComponent<RoomStudioController>();

            // 4. Save the scene
            var scenePath = $"{scenesDir}/RoomStudio.unity";
            EditorSceneManager.SaveScene(newScene, scenePath);

            Debug.Log($"Successfully created and saved the Studio Scene to: {scenePath}");
            
            // 5. Highlight the object in the Hierarchy so the user sees it
            Selection.activeGameObject = studioObject;
        }
    }
}
