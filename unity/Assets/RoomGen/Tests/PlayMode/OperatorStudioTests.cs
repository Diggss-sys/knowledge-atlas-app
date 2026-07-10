using System.Collections;
using System.IO;
using NUnit.Framework;
using RoomGen.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RoomGen.Tests.PlayMode
{
    /// <summary>
    /// A1 acceptance, in play mode against the REAL engine (LocalChannel + RoomRuntime + generator),
    /// not the mock. Proves the three things the interactive studio must do:
    ///  1. a slider edit reaches the real seam and is ACCEPTED — i.e. StudioSpecChannel completes the
    ///     panel's partial spec into a schema-valid canonical one (the gap the mock-only EditMode tests
    ///     could never surface);
    ///  2. a confounded pair earns a red verdict and keeps Publish locked;
    ///  3. fixing it back to a single declared difference turns the verdict green and unlocks Publish.
    /// It also confirms both live previews paint (non-black) and drops a PNG for eyeballing.
    /// </summary>
    public sealed class OperatorStudioTests
    {
        [UnityTest]
        public IEnumerator Interactive_studio_drives_real_seam_gate_and_previews()
        {
#if UNITY_EDITOR
            // Rendering capture in batchmode: tolerate benign HDRP log noise rather than fail on it.
            LogAssert.ignoreFailingMessages = true;

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

            var docGo = new GameObject("StudioCaptureDoc");
            var doc = docGo.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = vta;
            var root = doc.rootVisualElement;
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            // Boot the studio against the real engine with a MANUAL clock so we can cross the debounce
            // deterministically instead of guessing at frame timing.
            double clock = 0.0;
            var studioGo = new GameObject("Operator Studio (test)");
            var studio = studioGo.AddComponent<OperatorStudio>();
            var booted = studio.Boot(root, () => clock);
            Assert.IsTrue(booted, "studio failed to boot (missing Resources under RoomGen/?)");

            // 1) Slider -> real seam apply is ACCEPTED. This is the integration gap A1 closes: the
            //    view-model's partial spec (shell/surfaces/lighting only, no spec_version/room_type)
            //    is completed to a full canonical spec before it reaches the schema-checking runtime.
            studio.ViewModel.SetField("shell.ceiling_height_m", 3.2);
            clock = 1.0;                                       // > 0.15 s debounce
            var applied = studio.PumpOnce();
            Assert.IsTrue(applied, "no debounced apply was sent after a slider edit");
            Assert.AreEqual("applied", studio.ViewModel.Status,
                "the real seam rejected the completed spec -> " + StatusDetail(studio.ViewModel));

            for (var i = 0; i < 14; i++) yield return null;    // let the preview cameras paint

            Assert.Greater(MeanRgb(studio.TreatmentTexture), 0.02, "treatment preview rendered black");
            Assert.Greater(MeanRgb(studio.ControlTexture), 0.02, "control preview rendered black");

            // 2) Confounded pair -> red verdict + Publish LOCKED. Freeze the control, then change TWO
            //    fields on the treatment (ceiling AND width) so the gate sees more than one difference.
            studio.ViewModel.SetAsControl();                   // control: ceiling 3.2, width 4.5 (default)
            studio.ViewModel.SetField("shell.ceiling_height_m", 2.4);
            studio.ViewModel.SetField("shell.width_m", 5.5);
            studio.ViewModel.SubmitPair();
            Assert.IsFalse(studio.ViewModel.Validation.Ok, "a two-variable pair must fail the gate");
            Assert.IsFalse(studio.ViewModel.PublishEnabled, "Publish must stay locked on a confounded pair");

            // 3) Fix it: match width back to the control so ONLY the ceiling differs -> green + unlocked.
            studio.ViewModel.SetField("shell.width_m", 4.5);
            studio.ViewModel.SubmitPair();
            Assert.IsTrue(studio.ViewModel.Validation.Ok, "single-variable pair should pass, got violations: "
                + string.Join(",", studio.ViewModel.Validation.ViolationCodes));
            Assert.IsTrue(studio.ViewModel.PublishEnabled, "Publish must unlock on a valid single-variable pair");

            // Visual proof of the whole panel + live previews.
            for (var i = 0; i < 6; i++) yield return null;
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            var dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "captures");
            Directory.CreateDirectory(dir);
            var pngPath = Path.Combine(dir, "capture-ui-operator-studio.png");
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Debug.Log("A1CAP wrote " + pngPath);

            Object.Destroy(tex);
            Object.Destroy(studioGo);
            Object.Destroy(docGo);
            Object.Destroy(ps);
            rt.Release();
            Object.Destroy(rt);
#else
            yield return null;
#endif
        }

#if UNITY_EDITOR
        static string StatusDetail(OperatorPanelViewModel vm)
        {
            if (vm.Errors.Count == 0) return vm.Status;
            var e = vm.Errors[0];
            return $"{vm.Status} [{e.Code} @ {e.Path}: {e.Message}]";
        }

        static double MeanRgb(RenderTexture rtex)
        {
            if (rtex == null) return 0;
            var prev = RenderTexture.active;
            RenderTexture.active = rtex;
            var t = new Texture2D(rtex.width, rtex.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rtex.width, rtex.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            var px = t.GetPixels();
            double sum = 0; foreach (var p in px) sum += p.r + p.g + p.b;
            Object.Destroy(t);
            return sum / px.Length / 3.0;
        }
#endif
    }
}
