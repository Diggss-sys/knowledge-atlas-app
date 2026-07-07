using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace RoomGen.Editor
{
    /// <summary>
    /// Applies RoomGen's quality settings to the bootstrap-generated HDRP asset. The asset
    /// is regenerated on every fresh clone and never committed, so these capability flags
    /// MUST be set in code here — a hand-edited asset would silently revert.
    ///
    /// This enables the *support* flags (SSAO/SSR/SSGI/shadow mask) and bumps shadow atlas
    /// resolution. What actually renders is driven by the runtime <see cref="RoomGen.Lighting.QualityRig"/>
    /// volume overrides; a support flag being off would make the matching override inert.
    /// All writes go through SerializedObject with verified paths for deterministic persistence.
    /// </summary>
    public static class HdrpQualityConfigurator
    {
        public const int PunctualShadowAtlasResolution = 4096;

        const string SupportSsaoPath = "m_RenderPipelineSettings.supportSSAO";
        const string SupportSsrPath = "m_RenderPipelineSettings.supportSSR";
        const string SupportSsgiPath = "m_RenderPipelineSettings.supportSSGI";
        const string SupportShadowMaskPath = "m_RenderPipelineSettings.supportShadowMask";
        const string PunctualShadowAtlasPath =
            "m_RenderPipelineSettings.hdShadowInitParams.punctualLightShadowAtlas.shadowAtlasResolution";

        public static void Configure(HDRenderPipelineAsset asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("RoomGen: HdrpQualityConfigurator got a null asset.");
                return;
            }

            var so = new SerializedObject(asset);
            SetBool(so, SupportSsaoPath, true);
            SetBool(so, SupportSsrPath, true);
            SetBool(so, SupportSsgiPath, true);
            SetBool(so, SupportShadowMaskPath, true);
            SetInt(so, PunctualShadowAtlasPath, PunctualShadowAtlasResolution);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
        }

        static void SetBool(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning("RoomGen: HDRP serialized path not found: " + path);
        }

        static void SetInt(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.intValue = value;
            else Debug.LogWarning("RoomGen: HDRP serialized path not found: " + path);
        }
    }
}
