using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace RoomGen.Tests
{
    /// <summary>
    /// Regression gate for the walk-blanking bug (Fable review, 2026-07-10): a screen-space
    /// PanelSettings with clearColor=true clears the whole backbuffer every frame — even when the UI
    /// root is display:none — so entering DesktopWalkMode from either additive scene showed a solid
    /// cream/off-white screen instead of the room. The production panel assets must never clear color;
    /// the RT-targeted PanelSettings the capture tests build for themselves are exempt (a render
    /// texture genuinely needs the clear).
    /// </summary>
    public sealed class ScenePanelSettingsTests
    {
        [TestCase("Assets/RoomGen/UI/Operator/OperatorPanelSettings.asset")]
        [TestCase("Assets/RoomGen/UI/Runner/ParticipantPanelSettings.asset")]
        public void Scene_panel_settings_do_not_clear_the_screen(string path)
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            Assert.IsNotNull(ps, "panel settings asset missing: " + path);
            Assert.IsNull(ps.targetTexture, "scene panels render to the screen, not a render texture");
            Assert.IsFalse(ps.clearColor,
                "clearColor must stay FALSE on screen-space panels or the walk view is wiped (see A1_A2_REVIEW.md)");
        }
    }
}
