using System.IO;
using System.Reflection;
using RoomGen.Testing;
using RoomGen.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.Editor
{
    /// <summary>
    /// Headless preview of the operator UI: builds OperatorPanel.uxml + base.uss (bound to real preset
    /// data) and drives layout + the offscreen render pass by reflection. Editor-only evidence tooling.
    ///
    /// KNOWN LIMITATION: in a batchmode -executeMethod (edit mode) the UI Toolkit render chain lays out
    /// but does NOT paint to the targetTexture — the PNG comes out black (3D camera.Render works
    /// headlessly, UITK's deferred paint does not). A pixel-true screenshot needs a PLAY-MODE capture
    /// (enter play, let the player loop paint a few frames, then read the RT). That harness is the next
    /// step; until then, verify the panel via the binding tests + the design-preview mockup.
    /// </summary>
    public static class UiCapture
    {
        const string Uxml = "Assets/RoomGen/UI/Operator/OperatorPanel.uxml";
        const string Uss = "Assets/RoomGen/UI/Shared/base.uss";
        const string Theme = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        public static void CaptureOperator() => Capture(1360, 940, "ui-operator");

        static void Capture(int w, int h, string label)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Theme);
            if (theme != null) ps.themeStyleSheet = theme;
            else Debug.LogWarning("UICAPTURE: no theme style sheet — controls may render unstyled.");
            ps.targetTexture = rt;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.clearColor = true;
            ps.colorClearValue = new Color(0.97f, 0.956f, 0.937f, 1f); // cream, matches --ka-cream

            var go = new GameObject("UICaptureDoc");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Uxml);

            var root = doc.rootVisualElement;
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(Uss);
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            // Bind real content so sliders/labels are populated, not empty.
            var vm = new OperatorPanelViewModel(new MockSpecChannel(), () => 0.0);
            var preset = Resources.Load<TextAsset>("RoomGen/dining_room.preset");
            if (preset != null) { vm.LoadPreset(preset.text); OperatorPanelController.Bind(root, vm); }

            ForceRepaint(root, w, h);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "captures");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "capture-" + label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            var pixels = tex.GetPixels();
            double sum = 0; foreach (var p in pixels) sum += p.r + p.g + p.b;
            Debug.Log($"UICAPTURE wrote {path} meanRGB={(sum / pixels.Length / 3):0.000}");

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(ps);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        // Runtime (targetTexture) panels normally render on the player loop; in a batchmode
        // executeMethod we must drive layout + repaint ourselves. Reflect the concrete panel's
        // internal update so we don't depend on a frame tick. Logs the method it used.
        static void ForceRepaint(VisualElement root, int w, int h)
        {
            var panel = root.panel;
            if (panel == null) { Debug.LogWarning("UICAPTURE: root.panel is null."); return; }

            root.style.width = w;
            root.style.height = h;

            // 1) compute layout on this panel
            var pt = panel.GetType();
            var validate = pt.GetMethod("ValidateLayout",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
            validate?.Invoke(panel, null);

            // 2) drive the offscreen (targetTexture) render pass — the player loop does this normally,
            //    but a batchmode executeMethod has no frame tick, so invoke it directly by reflection.
            var asm = typeof(VisualElement).Assembly;
            var util = asm.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            if (util == null) { Debug.LogWarning("UICAPTURE: UIElementsRuntimeUtility not found."); return; }

            foreach (var name in new[] { "RenderOffscreenPanels", "RepaintOffscreenPanels", "UpdateRuntimePanels" })
            {
                var m = util.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, System.Type.EmptyTypes, null);
                if (m == null) continue;
                try { m.Invoke(null, null); Debug.Log($"UICAPTURE: invoked UIElementsRuntimeUtility.{name}()"); }
                catch (System.Exception e) { Debug.LogWarning($"UICAPTURE: {name} threw {e.InnerException?.Message ?? e.Message}"); }
            }
        }
    }
}
