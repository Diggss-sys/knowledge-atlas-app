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
    /// A2 UI: renders the neutral participant screens (proof they carry no brand personality) AND drives
    /// the MonoBehaviour → ParticipantSession path to completion, asserting a validated CSV lands. The
    /// EditMode ParticipantSessionTests own the pipeline correctness; this proves the wiring + the look.
    /// </summary>
    public sealed class ParticipantFlowCaptureTests
    {
        [UnityTest]
        public IEnumerator Participant_flow_renders_neutral_and_writes_a_csv_via_the_ui()
        {
#if UNITY_EDITOR
            LogAssert.ignoreFailingMessages = true;

            const int w = 1100, h = 760;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            var vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/RoomGen/UI/Runner/ParticipantScreens.uxml");
            var uss = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/RoomGen/UI/Runner/runner.uss");
            Assert.IsNotNull(vta, "participant UXML not found");

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            if (theme != null) ps.themeStyleSheet = theme;
            ps.targetTexture = rt;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.clearColor = true;
            ps.colorClearValue = new Color(0.98f, 0.98f, 0.98f, 1f);

            var docGo = new GameObject("ParticipantCaptureDoc");
            var doc = docGo.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = vta;
            var root = doc.rootVisualElement;
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            var study = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-study");
            Assert.IsNotNull(study, "ceiling-study resource missing");
            var outDir = Path.Combine(Application.temporaryCachePath, "participant-ui");
            Directory.CreateDirectory(outDir);

            var flowGo = new GameObject("Participant Runner (test)");
            var flow = flowGo.AddComponent<ParticipantFlow>();
            flow.Boot(root, study.text, () => "2026-07-20T18:42:11Z", outDir);

            // ID entry -> instructions -> first rating screen (skip the walk itself in the test).
            var began = flow.Begin("P01");
            Assert.IsTrue(began, "study should run: " + flow.Session.Reason);
            flow.BeginTrials();
            flow.ShowRating();

            for (var i = 0; i < 12; i++) yield return null;   // paint the rating screen

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            var dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "captures");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "capture-ui-participant.png"), tex.EncodeToPNG());
            Debug.Log("A2CAP wrote capture-ui-participant.png");

            var px = tex.GetPixels();
            double sum = 0; foreach (var p in px) sum += p.r + p.g + p.b;
            Assert.Greater(sum / px.Length / 3.0, 0.4, "neutral screen should render bright (near-white), not black");

            // Complete the session THROUGH the UI: rate every trial, asserting a CSV lands.
            var guard = 0;
            while (!flow.Session.IsComplete && guard++ < 32)
            {
                flow.ShowRating();
                flow.Rate(flow.Session.CurrentCondition == "treatment" ? flow.Session.ScaleMax : flow.Session.ScaleMin + 1);
            }
            Assert.IsTrue(flow.Session.IsComplete, "flow did not complete");
            Assert.AreEqual(4, flow.Session.Written, "four rows written via the UI path");
            Assert.IsTrue(flow.Session.AllValid, "every UI-written row must validate");
            Assert.IsTrue(File.Exists(flow.Session.CsvPath), "a CSV must exist on disk");

            Object.Destroy(tex);
            Object.Destroy(flowGo);
            Object.Destroy(docGo);
            Object.Destroy(ps);
            rt.Release();
            Object.Destroy(rt);
#else
            yield return null;
#endif
        }
    }
}
