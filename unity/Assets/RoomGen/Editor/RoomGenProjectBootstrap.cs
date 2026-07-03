using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace RoomGen.Editor
{
    [InitializeOnLoad]
    public static class RoomGenProjectBootstrap
    {
        const string ScenePath = "Assets/RoomGen/Scenes/RoomStudio.unity";
        const string PipelinePath = "Assets/RoomGen/Settings/RoomGenHDRP.asset";

        static RoomGenProjectBootstrap()
        {
            EditorApplication.delayCall += EnsureProject;
        }

        [MenuItem("RoomGen/Bootstrap Project")]
        public static void EnsureProject()
        {
            EnsureFolders();
            EnsurePipeline();
            EnsureBuiltInMaterials();
            EnsureLayers();
            EnsureScene();
            ConfigurePlayer();
            ConfigureOpenXr();
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets/RoomGen", "Scenes");
            EnsureFolder("Assets/RoomGen", "Settings");
            EnsureFolder("Assets/RoomGen", "Resources");
            EnsureFolder("Assets/RoomGen/Resources", "RoomGen");
            EnsureFolder("Assets/RoomGen/Resources/RoomGen", "Materials");
        }

        static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        static void EnsurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            if (GraphicsSettings.defaultRenderPipeline == null)
                GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        static void EnsureBuiltInMaterials()
        {
            CreateMaterial("builtin-oak", new Color(0.43f, 0.25f, 0.11f), 0.28f);
            CreateMaterial("builtin-walnut", new Color(0.2f, 0.09f, 0.045f), 0.26f);
            CreateMaterial("builtin-warm-white", new Color(0.82f, 0.8f, 0.75f), 0.12f);
            CreateMaterial("builtin-ceiling-white", new Color(0.93f, 0.93f, 0.91f), 0.08f);
            CreateMaterial("builtin-glass", new Color(0.52f, 0.72f, 0.78f), 0.8f);
            CreateMaterial("builtin-metal-black", new Color(0.13f, 0.14f, 0.15f), 0.48f);
            CreateMaterial("builtin-green", new Color(0.16f, 0.29f, 0.18f), 0.2f);
            CreateMaterial("builtin-fabric-charcoal", new Color(0.29f, 0.34f, 0.36f), 0.08f);
            CreateMaterial("builtin-missing", new Color(0.8f, 0.08f, 0.08f), 0f);
        }

        static void CreateMaterial(string name, Color color, float smoothness)
        {
            var path = $"Assets/RoomGen/Resources/RoomGen/Materials/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogWarning("RoomGen: HDRP/Lit shader is not available yet.");
                return;
            }
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
        }

        static void EnsureLayers()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(8).stringValue = "RoomGenControl";
            layers.GetArrayElementAtIndex(9).stringValue = "RoomGenTreatment";
            tagManager.ApplyModifiedProperties();
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SceneManager.SetActiveScene(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Room Studio";
            PlayerSettings.companyName = "COGS 160 Research Lab";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.SetApiCompatibilityLevel(
                BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_1);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[]
            {
                GraphicsDeviceType.Direct3D12,
                GraphicsDeviceType.Direct3D11
            });
        }

        static void ConfigureOpenXr()
        {
            var target = BuildTargetGroup.Standalone;
            var buildTargetSettings =
                XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(target);
            if (buildTargetSettings == null || buildTargetSettings.AssignedSettings == null)
            {
                Debug.LogWarning("RoomGen: XR settings are not ready yet. Run RoomGen > Bootstrap Project once packages finish importing.");
                return;
            }

            if (!XRPackageMetadataStore.AssignLoader(
                    buildTargetSettings.AssignedSettings,
                    "UnityEngine.XR.OpenXR.OpenXRLoader",
                    target))
                Debug.LogWarning("RoomGen: OpenXR loader could not be assigned automatically.");
        }
    }
}
