using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace RoomGen.Lighting
{
    /// <summary>
    /// Per-camera HDRP realism switches, applied to every camera the app creates (previews,
    /// desktop walk, VR). Two things happen here:
    ///   1. SSGI is force-enabled via the camera's custom frame settings. QualityRig already
    ///      puts a GlobalIllumination override in the volume, but in HDRP 17.3 the SSGI frame
    ///      setting defaults OFF per camera, so without this the override is inert — no bounce
    ///      light, which is most of why the rooms read flat. With it on, light bounces off the
    ///      walls (including bowed walls, whose curvature visibly spreads the bounce).
    ///   2. Clear mode = Sky, so windows and the door show the sky instead of the solid void
    ///      color — the single largest immersion break before this.
    /// Scenes and pipeline assets are never committed (bootstrap-generated), so this lives in
    /// code like the rest of the quality pass.
    /// </summary>
    public static class CameraRealism
    {
        public static void Apply(Camera camera)
        {
            if (camera == null) return;
            if (!camera.TryGetComponent<HDAdditionalCameraData>(out var hd))
                hd = camera.gameObject.AddComponent<HDAdditionalCameraData>();

            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;

            hd.customRenderingSettings = true;
            var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
            mask.mask[(uint)FrameSettingsField.SSGI] = true;
            hd.renderingPathCustomFrameSettingsOverrideMask = mask;
            var settings = hd.renderingPathCustomFrameSettings;
            settings.SetEnabled(FrameSettingsField.SSGI, true);
            hd.renderingPathCustomFrameSettings = settings;
        }
    }
}
