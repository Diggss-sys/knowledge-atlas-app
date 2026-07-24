using UnityEngine;
using UnityEngine.Rendering;

namespace RoomGen.VR
{
    public static class RenderQualityProfiles
    {
        /// <summary>
        /// Pick the profile the machine can actually sustain. On Metal (macOS) HDRP at a retina panel
        /// is far too heavy — Raven's M3 Air ran the desktop profile at 5.7 fps, every frame a hitch —
        /// so Macs get the portable profile (fewer pixels + cheaper shadows). Windows keeps the full
        /// desktop profile. Call this instead of ApplyDesktop from the studio/participant/walk entry.
        /// </summary>
        public static void ApplyAuto()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal)
                ApplyPortable();
            else
                ApplyDesktop();
        }

        public static void ApplyDesktop()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            QualitySettings.lodBias = 1.5f;
            QualitySettings.shadowDistance = 70f;
            QualitySettings.antiAliasing = 4;
        }

        /// <summary>
        /// Integrated-GPU / Apple-Silicon profile: the big lever is PIXELS — 1440x900 windowed is ~3x
        /// fewer than a retina Air panel — plus shorter shadows and lighter AA. If a Mac still misses
        /// the fps gate after this, the next levers are HDRP-asset-level (shadow atlas, screen-space
        /// effects) and touch shared rendering config — coordinate with the engine lane before that.
        /// </summary>
        public static void ApplyPortable()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            QualitySettings.lodBias = 1.0f;
            QualitySettings.shadowDistance = 25f;
            QualitySettings.antiAliasing = 2;
            Screen.SetResolution(1440, 900, FullScreenMode.Windowed);
        }

        public static void ApplyVr()
        {
            Application.targetFrameRate = 90;
            QualitySettings.vSyncCount = 0;
            QualitySettings.lodBias = 1.15f;
            QualitySettings.shadowDistance = 35f;
            QualitySettings.antiAliasing = 2;
        }
    }
}
