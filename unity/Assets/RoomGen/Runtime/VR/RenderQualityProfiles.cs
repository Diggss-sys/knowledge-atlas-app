using UnityEngine;

namespace RoomGen.VR
{
    public static class RenderQualityProfiles
    {
        public static void ApplyDesktop()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            QualitySettings.lodBias = 1.5f;
            QualitySettings.shadowDistance = 70f;
            QualitySettings.antiAliasing = 4;
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
