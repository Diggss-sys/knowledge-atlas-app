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
        /// <param name="enableSsgi">
        /// SSGI is temporal — it needs several stable frames to denoise. The full-screen walk/VR
        /// cameras hold still, so it looks great there. The two studio PREVIEW thumbnails rebuild
        /// the whole room on every slider frame, so their SSGI history never converges → a
        /// permanent smear ("blurry"), made worse by their MSAA RenderTexture. Previews therefore
        /// get sky-clear only (windows show the exterior, not a black void) and skip SSGI.
        /// </param>
        public static void Apply(Camera camera, bool enableSsgi = true)
        {
            if (camera == null) return;
            if (!camera.TryGetComponent<HDAdditionalCameraData>(out var hd))
                hd = camera.gameObject.AddComponent<HDAdditionalCameraData>();

            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;

            if (!enableSsgi) return;
            hd.customRenderingSettings = true;
            var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
            mask.mask[(uint)FrameSettingsField.SSGI] = true;
            // SSR gives floors/tables/glass real screen-space reflections. Like SSGI it is temporal
            // (needs a stable history to denoise), so it rides the same walk/VR-only gate and is
            // skipped on the rebuilt-every-frame preview thumbnails to avoid a permanent smear.
            mask.mask[(uint)FrameSettingsField.SSR] = true;
            mask.mask[(uint)FrameSettingsField.TransparentSSR] = true; // reflections on the glass panes
            hd.renderingPathCustomFrameSettingsOverrideMask = mask;
            var settings = hd.renderingPathCustomFrameSettings;
            settings.SetEnabled(FrameSettingsField.SSGI, true);
            settings.SetEnabled(FrameSettingsField.SSR, true);
            settings.SetEnabled(FrameSettingsField.TransparentSSR, true);
            hd.renderingPathCustomFrameSettings = settings;
        }
    }
}
