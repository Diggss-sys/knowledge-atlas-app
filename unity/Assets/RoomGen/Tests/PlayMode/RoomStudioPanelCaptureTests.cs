using System.Collections;
using System.IO;
using NUnit.Framework;
using RoomGen.UI.Studio;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RoomGen.Tests.PlayMode
{
    /// <summary>
    /// PLAY-MODE capture of the UI Toolkit Room Studio — the acceptance gate the unit tests could not
    /// give: 123 green tests still let a full-screen legacy IMGUI box paint over this panel, because
    /// nothing ever looked at the pixels. Edit-mode batchmode lays a UITK panel out but never paints
    /// it to a targetTexture (the render pass rides the player loop), so this has to be play mode.
    ///
    /// Boots the REAL <see cref="RoomStudioPanelController"/> against the REAL RoomStudioPanel.uxml,
    /// then reads the RenderTexture back to captures/roomstudio-ui.png.
    /// </summary>
    public sealed class RoomStudioPanelCaptureTests
    {
        [UnityTest]
        public IEnumerator Renders_the_room_studio_panel_to_a_png()
        {
#if UNITY_EDITOR
            // A rendering capture, not a logic test: tolerate benign HDRP batchmode log noise.
            LogAssert.ignoreFailingMessages = true;

            // Tall on purpose: the control rail is a ScrollView, so a 1000 px capture hid the last
            // three panels below the fold and the evidence shot could not show all seven.
            const int w = 1600, h = 1800;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            var vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/RoomGen/UI/Studio/RoomStudioPanel.uxml");
            var uss = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/RoomGen/UI/Shared/base.uss");
            Assert.IsNotNull(vta, "RoomStudioPanel.uxml not found");

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            if (theme != null) ps.themeStyleSheet = theme;
            ps.targetTexture = rt;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.clearColor = true;
            ps.colorClearValue = new Color(0.968f, 0.956f, 0.937f, 1f); // --ka-cream

            // A UXML that fails to parse still loads as a NON-NULL VisualTreeAsset that clones nothing
            // — which is how RoomStudioPanel.uxml shipped with illegal "--" runs inside its section
            // comments and rendered a blank cream panel with zero errors. Catch that here, before the
            // capture, where the message can say what is actually wrong.
            var probe = new VisualElement();
            vta.CloneTree(probe);
            Assert.Greater(probe.childCount, 0,
                "RoomStudioPanel.uxml loaded but cloned nothing — the XML is malformed "
                + "(check for '--' inside comments, which is illegal in XML).");

            var go = new GameObject("RoomStudioCaptureDoc");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = vta;

            yield return null; // let UIDocument attach to the panel and produce a real root

            var root = doc.rootVisualElement;
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            // Only now add the controller: its Start() boots against doc.rootVisualElement, and
            // booting against an empty root would bind nothing and latch _booted.
            var controller = go.AddComponent<RoomStudioPanelController>();

            for (var i = 0; i < 20; i++) yield return null; // let the player loop lay out + paint

            Assert.IsNotNull(controller.ViewModel, "the controller never booted — no view-model");
            // Guard the blank-screen failure directly: a mean-brightness check alone passes on an
            // empty panel that only cleared to cream.
            Assert.IsNotNull(root.Q<VisualElement>("studio-root"), "the UXML never cloned into the panel");
            Assert.IsNotNull(root.Q<DropdownField>("room-select"), "the Room panel is missing");
            Assert.IsNotNull(root.Q<Label>("verdict"), "the Pair check panel is missing");
            Assert.Greater(root.Q<VisualElement>("studio-root").resolvedStyle.height, 100f,
                "the panel laid out with no height — nothing would be visible");

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "captures");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "roomstudio-ui.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            var px = tex.GetPixels();
            double sum = 0; foreach (var p in px) sum += p.r + p.g + p.b;
            var mean = sum / px.Length / 3.0;
            Debug.Log($"UICAP wrote {path} meanRGB={mean:0.000}");

            Object.Destroy(tex);
            Object.Destroy(go);
            Object.Destroy(ps);
            rt.Release();
            Object.Destroy(rt);

            Assert.Greater(mean, 0.05, "the panel rendered black — paint did not reach the target texture");
#else
            yield return null;
#endif
        }
    }
}
