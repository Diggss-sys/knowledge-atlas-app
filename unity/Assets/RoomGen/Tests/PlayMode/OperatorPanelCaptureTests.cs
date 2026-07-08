using System.Collections;
using System.IO;
using NUnit.Framework;
using RoomGen.Testing;
using RoomGen.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RoomGen.Tests.PlayMode
{
    /// <summary>
    /// PLAY-MODE capture of the operator panel. Edit-mode batchmode lays a UITK panel out but never
    /// paints it to a targetTexture (the render pass is on the player loop). Play mode runs that loop,
    /// so a few yielded frames later the RenderTexture holds the real, themed panel — read it back to a
    /// PNG that opens in any image viewer. Doubles as the assertion that the panel renders non-black.
    /// Asset loads use AssetDatabase (the test runs in the editor's play mode, not a built player).
    /// </summary>
    public sealed class OperatorPanelCaptureTests
    {
        [UnityTest]
        public IEnumerator Renders_the_operator_panel_to_a_png()
        {
#if UNITY_EDITOR
            const int w = 1360, h = 940;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            var vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/RoomGen/UI/Operator/OperatorPanel.uxml");
            var uss = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/RoomGen/UI/Shared/base.uss");
            Assert.IsNotNull(vta, "operator UXML not found");

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            if (theme != null) ps.themeStyleSheet = theme;
            ps.targetTexture = rt;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.clearColor = true;
            ps.colorClearValue = new Color(0.968f, 0.956f, 0.937f, 1f); // --ka-cream

            var go = new GameObject("UiCaptureDoc");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = vta;

            var root = doc.rootVisualElement;
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            // Populate with real preset content so sliders/labels aren't empty.
            var vm = new OperatorPanelViewModel(new MockSpecChannel(), () => 0.0);
            var preset = Resources.Load<TextAsset>("RoomGen/dining_room.preset");
            if (preset != null) { vm.LoadPreset(preset.text); OperatorPanelController.Bind(root, vm); }

            for (var i = 0; i < 12; i++) yield return null; // let the player loop lay out + paint

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "captures");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "capture-ui-operator.png");
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
