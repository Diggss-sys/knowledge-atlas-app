using System.Collections;
using System.IO;
using NUnit.Framework;
using RoomGen.Contracts;
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
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var flowGo = new GameObject("Participant Runner (test)");
            var flow = flowGo.AddComponent<ParticipantFlow>();
            flow.Boot(root, study.text, () => "2026-07-20T18:42:11Z", outDir);

            // Same display-camera gate as the operator studio: without the backdrop camera the UI
            // overlay never composites onto the camera-less display in the built scene.
            var anyDisplayCam = false;
            foreach (var cam in Camera.allCameras)
                if (cam.isActiveAndEnabled && cam.targetTexture == null) { anyDisplayCam = true; break; }
            Assert.IsTrue(anyDisplayCam, "no enabled camera renders to the display — the screens cannot composite");

            // ID entry -> instructions -> first rating screen (skip the walk itself in the test).
            var began = flow.Begin("P01");
            Assert.IsTrue(began, "study should run: " + flow.Session.Reason);
            Assert.IsTrue(File.Exists(flow.StudyCopyPath), "the exact study must be copied beside session output");
            Assert.AreEqual(study.text, File.ReadAllText(flow.StudyCopyPath));
            StringAssert.Contains(flow.Session.SessionId, Path.GetFileName(flow.Session.CsvPath),
                "session identity prevents same-participant, same-second output collisions");
            StringAssert.Contains(flow.Session.SessionId, Path.GetFileName(flow.StudyCopyPath));
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

            var savedStudy = File.ReadAllText(flow.StudyCopyPath);
            var controlSha = CanonicalJson.Sha256Property(savedStudy, "control_spec");
            var treatmentSha = CanonicalJson.Sha256Property(savedStudy, "treatment_spec");
            var responseLines = File.ReadAllLines(flow.Session.CsvPath.Replace(".csv", ".jsonl"));
            Assert.AreEqual(flow.Session.Written, responseLines.Length);
            foreach (var line in responseLines)
            {
                var expected = line.Contains("\"condition\":\"treatment\"") ? treatmentSha : controlSha;
                StringAssert.Contains("\"spec_sha256\":\"" + expected + "\"", line,
                    "the saved study copy must reproduce every emitted row's room provenance hash");
            }

            // This capture skips the walks. Zero-frame rows would look measured when they are not, so
            // the honest output is no perf file at all.
            var perfPath = flow.Session.CsvPath.Replace(".csv", ".perf.csv");
            Assert.IsFalse(File.Exists(perfPath), "no measured walk frames means no perf rows or file");
            Assert.IsFalse(File.Exists(flow.EventsPath), "a successful session has no non-response failure events");

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

        [UnityTest]
        public IEnumerator Failed_room_build_aborts_without_response_or_perf_rows_and_shows_error()
        {
#if UNITY_EDITOR
            LogAssert.ignoreFailingMessages = true;
            var vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/RoomGen/UI/Runner/ParticipantScreens.uxml");
            Assert.IsNotNull(vta);

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            var docGo = new GameObject("ParticipantFailureDoc");
            var doc = docGo.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = vta;
            var root = doc.rootVisualElement;

            var study = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-study");
            var outDir = Path.Combine(Application.temporaryCachePath, "participant-failure-ui");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var flowGo = new GameObject("Participant Runner (forced failure)");
            var flow = flowGo.AddComponent<ParticipantFlow>();
            flow.Boot(root, study.text, () => "2026-08-10T20:00:00Z", outDir, _ =>
            {
                var failed = new RoomBuildResult { Ok = false };
                failed.Warnings.Add("Missing furniture asset: forced_test_asset");
                return failed;
            });

            Assert.IsTrue(flow.Begin("FAIL01"), flow.Session.Reason);
            flow.BeginTrials();
            flow.EnterRoom();
            yield return null;

            Assert.IsTrue(flow.Session.IsAborted);
            StringAssert.Contains("forced_test_asset", flow.Session.AbortReason);
            Assert.AreEqual(0, flow.Session.Written);
            Assert.IsFalse(File.Exists(flow.Session.CsvPath), "aborted trial must not create a response CSV");
            Assert.IsFalse(File.Exists(flow.Session.CsvPath.Replace(".csv", ".jsonl")),
                "aborted trial must not enter the response JSONL mirror");
            Assert.IsFalse(File.Exists(flow.Session.CsvPath.Replace(".csv", ".perf.csv")),
                "aborted trial must not create a phantom perf row");
            Assert.IsTrue(File.Exists(flow.EventsPath), "the abort belongs in the events sidecar");
            Assert.AreEqual(1, File.ReadAllLines(flow.EventsPath).Length);
            StringAssert.Contains("trial_aborted", File.ReadAllText(flow.EventsPath));
            Assert.AreEqual(DisplayStyle.Flex, root.Q<VisualElement>("screen-error").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("screen-rating").resolvedStyle.display,
                "no rating path may be visible after a failed build");
            Assert.IsNull(root.Q<Button>("rate-1"), "rating buttons are never created for the failed trial");

            Object.Destroy(flowGo);
            Object.Destroy(docGo);
            Object.Destroy(ps);
#else
            yield return null;
#endif
        }

        [UnityTest]
        public IEnumerator Build_marked_ok_without_a_walkable_root_still_aborts_before_rating()
        {
#if UNITY_EDITOR
            LogAssert.ignoreFailingMessages = true;
            var vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/RoomGen/UI/Runner/ParticipantScreens.uxml");
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            var docGo = new GameObject("ParticipantMissingRootDoc");
            var doc = docGo.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = vta;
            var root = doc.rootVisualElement;

            var study = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-study");
            var outDir = Path.Combine(Application.temporaryCachePath, "participant-missing-root-ui");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var flowGo = new GameObject("Participant Runner (missing root)");
            var flow = flowGo.AddComponent<ParticipantFlow>();
            flow.Boot(root, study.text, () => "2026-08-10T20:00:00Z", outDir,
                _ => new RoomBuildResult { Ok = true, Root = null });

            Assert.IsTrue(flow.Begin("NOROOT"), flow.Session.Reason);
            flow.BeginTrials();
            flow.EnterRoom();
            yield return null;

            Assert.IsTrue(flow.Session.IsAborted);
            StringAssert.Contains("no walkable room", flow.Session.AbortReason);
            Assert.AreEqual(0, flow.Session.Written);
            Assert.IsFalse(File.Exists(flow.Session.CsvPath));
            Assert.IsFalse(File.Exists(flow.Session.CsvPath.Replace(".csv", ".jsonl")));
            Assert.IsFalse(File.Exists(flow.Session.CsvPath.Replace(".csv", ".perf.csv")));
            Assert.IsTrue(File.Exists(flow.EventsPath));
            Assert.AreEqual(DisplayStyle.Flex, root.Q<VisualElement>("screen-error").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("screen-rating").resolvedStyle.display);

            Object.Destroy(flowGo);
            Object.Destroy(docGo);
            Object.Destroy(ps);
#else
            yield return null;
#endif
        }
    }
}
