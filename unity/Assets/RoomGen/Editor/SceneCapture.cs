using System.IO;
using RoomGen.Contracts;
using RoomGen.Generation;
using RoomGen.Lighting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace RoomGen.Editor
{
    /// <summary>
    /// Headless still-image capture for fidelity evidence (L0 before/after, L4 gate renders)
    /// and — via mean-luminance readback — the L3 calibration check. Renders an HDRP camera
    /// to a RenderTexture in batchmode and writes a PNG.
    /// </summary>
    public static class SceneCapture
    {
        static string OutputDir
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath)!.FullName;
                var dir = Path.Combine(root, "captures");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        // Minimal render-capability probe: a lit cube on a plane. Confirms headless HDRP
        // can produce a non-black frame on this machine before we invest in room capture.
        public static void Probe()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(0f, 0.5f, 0f);

            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            lightGo.AddComponent<HDAdditionalLightData>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var meanLum = RenderToPng("probe", new Vector3(2.5f, 2f, -3f),
                Quaternion.LookRotation(new Vector3(-2.5f, -1.5f, 3f).normalized), 1280, 720);
            Debug.Log($"SCENECAPTURE probe meanLuminance={meanLum:0.0000}");
        }

        // L0 evidence: the bundled dining pair's control room rendered without, then with,
        // the quality rig (ACES + SSAO + bloom). Both share the room's calibrated lights and
        // fixed-exposure volume, so the difference is purely the quality pass.
        public static void CaptureDiningBeforeAfter()
        {
            CaptureRoom("l0-before", installQuality: false);
            CaptureRoom("l0-after", installQuality: true);
        }

        // L3 evidence: the matched-luminance check. Renders both conditions of the ceiling pair
        // straight down at the floor centre (identical framing) and compares the measured floor
        // luminance — if calibration is honest, a 2.4 m and a 3.2 m ceiling read the same brightness.
        public static void MeasurePairLuminance()
        {
            var source = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            if (source == null) { Debug.LogError("SCENECAPTURE: pair resource missing."); return; }
            var pair = RoomJson.Deserialize<ConditionPairSpec>(source.text);

            var control = MeasureProbePlaneLuminance("l3-control", pair.Control);
            var treatment = MeasureProbePlaneLuminance("l3-treatment", pair.Treatment);
            var avg = Mathf.Max(0.0001f, (control + treatment) * 0.5f);
            var delta = Mathf.Abs(control - treatment) / avg;

            var predControl = LightingCalibrator.MatchTargetLux(pair.Control);
            var predTreatment = LightingCalibrator.MatchTargetLux(pair.Treatment);
            var pass = delta <= 0.25f;

            var report =
                $"L3 matched-luminance report\n" +
                $"  control  ceiling={pair.Control.Geometry.CeilingHeightM:0.00} m  measured_card_luminance={control:0.0000}  predicted_mean_lux={predControl.MeanLux:0.0}  scale={predControl.IntensityScale:0.000}\n" +
                $"  treatment ceiling={pair.Treatment.Geometry.CeilingHeightM:0.00} m  measured_card_luminance={treatment:0.0000}  predicted_mean_lux={predTreatment.MeanLux:0.0}  scale={predTreatment.IntensityScale:0.000}\n" +
                $"  target_lux={pair.Control.Lighting.TargetLux:0.0}\n" +
                $"  measured match delta={delta:P1}  (tripwire <= 25%)  =>  {(pass ? "PASS" : "FAIL")}\n";
            File.WriteAllText(Path.Combine(OutputDir, "l3-calibration-report.txt"), report);
            Debug.Log("L3MEASURE " + report.Replace("\n", " | "));
        }

        static float MeasureProbePlaneLuminance(string label, RoomSpec spec)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var genGo = new GameObject("Capture Generator");
            var generator = genGo.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            generator.Build(spec);

            var qualityGo = new GameObject("Quality Rig");
            var volume = qualityGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = QualityRig.VolumePriority;
            var qualityProfile = QualityRig.BuildProfile();
            volume.profile = qualityProfile;

            // A neutral Lambertian reference card at the eye-centre probe plane (1.2 m) — a plane the
            // calibrator targets, above the tabletop (0.76 m) so furniture does not occlude it. Its
            // luminance tracks the illuminance there, so matched ceilings read the same brightness.
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "Probe Card";
            card.transform.position = new Vector3(0f, 1.2f, 0f);
            card.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // face up
            card.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            var cardMat = new Material(Shader.Find("HDRP/Lit")) { name = "ProbeCard" };
            cardMat.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.6f));
            cardMat.SetFloat("_Smoothness", 0f);
            card.GetComponent<MeshRenderer>().sharedMaterial = cardMat;

            // Straight down from a fixed height (below the lowest ceiling), framed so the card fills
            // the view — identical framing for both conditions.
            var position = new Vector3(0f, 2.1f, 0f);
            var rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var luminance = RenderToPng(label, position, rotation, 1024, 1024);

            Object.DestroyImmediate(cardMat);
            Object.DestroyImmediate(qualityProfile);
            Object.DestroyImmediate(qualityGo);
            Object.DestroyImmediate(genGo);
            return luminance;
        }

        // L4 evidence: the committed ceiling pair at eye height, full fidelity (textures + pendant +
        // quality pass), same camera and lighting — so the ONLY visible difference is the declared
        // variable (ceiling height) while realism and the single-variable rule both show at once.
        public static void CapturePairEvidence()
        {
            var source = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            if (source == null) { Debug.LogError("SCENECAPTURE: pair resource missing."); return; }
            var pair = RoomJson.Deserialize<ConditionPairSpec>(source.text);
            CaptureConditionEvidence("l4-control", pair.Control);
            CaptureConditionEvidence("l4-treatment", pair.Treatment);
        }

        // SSGI bounce off vs on — the in-room colour bleed the reference rooms have, now that real
        // albedos exist. Per-camera SSGI frame setting; the room + quality rig are identical.
        public static void CaptureSsgiComparison()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var source = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            if (source == null) { Debug.LogError("SCENECAPTURE: pair resource missing."); return; }
            var spec = RoomJson.Deserialize<ConditionPairSpec>(source.text).Control;
            spec.Furniture.Add(new FurniturePlacementSpec
            {
                AssetId = "builtin.pendant-light", SlotId = "pendant-center",
                PositionM = new Vec3 { X = 0f, Y = 0f, Z = 0f }, CeilingMounted = true
            });

            var genGo = new GameObject("Capture Generator");
            var generator = genGo.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            generator.Build(spec);

            var qualityGo = new GameObject("Quality Rig");
            var volume = qualityGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = QualityRig.VolumePriority;
            var qualityProfile = QualityRig.BuildProfile();
            volume.profile = qualityProfile;

            var g = spec.Geometry;
            var position = new Vector3(g.WidthM * 0.34f, 1.6f, -g.LengthM * 0.36f);
            var target = new Vector3(-g.WidthM * 0.1f, 1.35f, g.LengthM * 0.15f);
            var rotation = Quaternion.LookRotation((target - position).normalized);
            RenderToPng("ssgi-off", position, rotation, 1920, 1080, enableSsgi: false);
            RenderToPng("ssgi-on", position, rotation, 1920, 1080, enableSsgi: true);

            Object.DestroyImmediate(qualityProfile);
            Object.DestroyImmediate(qualityGo);
            Object.DestroyImmediate(genGo);
        }

        // Reverse angle: frames the south wall (sideboard + plant) to verify furniture models there.
        public static void CaptureSouthWall()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var source = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            if (source == null) { Debug.LogError("SCENECAPTURE: pair resource missing."); return; }
            var spec = RoomJson.Deserialize<ConditionPairSpec>(source.text).Control;

            var genGo = new GameObject("Capture Generator");
            var generator = genGo.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            generator.Build(spec);

            var qualityGo = new GameObject("Quality Rig");
            var volume = qualityGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = QualityRig.VolumePriority;
            var qualityProfile = QualityRig.BuildProfile();
            volume.profile = qualityProfile;

            var g = spec.Geometry;
            var position = new Vector3(0f, 1.5f, g.LengthM * 0.30f);
            var target = new Vector3(0f, 0.9f, -g.LengthM * 0.5f);
            var rotation = Quaternion.LookRotation((target - position).normalized);
            RenderToPng("south-wall", position, rotation, 1920, 1080);

            Object.DestroyImmediate(qualityProfile);
            Object.DestroyImmediate(qualityGo);
            Object.DestroyImmediate(genGo);
        }

        // Kirsh's other experiment: sharp vs curved (rounded-corner) walls, single-variable
        // (geometry.corner_radius_m). Framed toward a corner so the curve is visible.
        public static void CaptureCurvedWallPair()
        {
            var source = Resources.Load<TextAsset>("RoomGen/Examples/curved-wall-pair");
            if (source == null) { Debug.LogError("SCENECAPTURE: curved-wall pair missing."); return; }
            var pair = RoomJson.Deserialize<ConditionPairSpec>(source.text);
            CaptureCornerView("curved-control", pair.Control);
            CaptureCornerView("curved-treatment", pair.Treatment);
        }

        static void CaptureCornerView(string label, RoomSpec spec)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var genGo = new GameObject("Capture Generator");
            var generator = genGo.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            var build = generator.Build(spec);
            var warnings = string.Join("; ", build.Warnings);
            Debug.Log($"SCENECAPTURE build ok={build.Ok} label={label} warnings={warnings}");

            var qualityGo = new GameObject("Quality Rig");
            var volume = qualityGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = QualityRig.VolumePriority;
            var qualityProfile = QualityRig.BuildProfile();
            volume.profile = qualityProfile;

            var g = spec.Geometry;
            // Stand comfortably inside (clear of the rounded corners, which pull the wall in by the
            // radius) and look diagonally across at the far corner where the curve reads best.
            var position = new Vector3(-g.WidthM * 0.19f, 1.6f, -g.LengthM * 0.16f);
            var target = new Vector3(g.WidthM * 0.5f, 1.25f, g.LengthM * 0.5f);
            var rotation = Quaternion.LookRotation((target - position).normalized);
            RenderToPng(label, position, rotation, 1920, 1080);

            Object.DestroyImmediate(qualityProfile);
            Object.DestroyImmediate(qualityGo);
            Object.DestroyImmediate(genGo);
        }

        // Third experiment: lighting warmth (color temperature) at matched luminance. Renders the
        // warm and cool conditions from the same pose so the spectral shift is the only difference.
        public static void CaptureWarmthPair()
        {
            var source = Resources.Load<TextAsset>("RoomGen/Examples/warmth-pair");
            if (source == null) { Debug.LogError("SCENECAPTURE: warmth pair missing."); return; }
            var pair = RoomJson.Deserialize<ConditionPairSpec>(source.text);
            CaptureConditionEvidence("warmth-control-3000k", pair.Control);
            CaptureConditionEvidence("warmth-treatment-5000k", pair.Treatment);
        }

        static void CaptureConditionEvidence(string label, RoomSpec spec)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            spec.Furniture.Add(new FurniturePlacementSpec
            {
                AssetId = "builtin.pendant-light",
                SlotId = "pendant-center",
                PositionM = new Vec3 { X = 0f, Y = 0f, Z = 0f },
                CeilingMounted = true
            });

            var genGo = new GameObject("Capture Generator");
            var generator = genGo.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            generator.Build(spec);

            var qualityGo = new GameObject("Quality Rig");
            var volume = qualityGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = QualityRig.VolumePriority;
            var qualityProfile = QualityRig.BuildProfile();
            volume.profile = qualityProfile;

            var g = spec.Geometry;
            var position = new Vector3(g.WidthM * 0.34f, 1.6f, -g.LengthM * 0.36f);
            var target = new Vector3(-g.WidthM * 0.1f, 1.35f, g.LengthM * 0.15f);
            var rotation = Quaternion.LookRotation((target - position).normalized);
            RenderToPng(label, position, rotation, 1920, 1080);

            Object.DestroyImmediate(qualityProfile);
            Object.DestroyImmediate(qualityGo);
            Object.DestroyImmediate(genGo);
        }

        // L2 evidence: one dining room (with an injected pendant luminaire) under three lighting
        // moods, driven purely by the spec's color temperature / target lux / flux / exposure.
        public static void CaptureMoods()
        {
            // Exposure is held CONSTANT across moods on purpose: re-exposing each room would erase
            // the very lux differences that make the moods distinct (the matched-luminance principle).
            // So mood = color temperature + target lux (+ flux), read at one fixed exposure.
            CaptureMood("neutral-daylight", kelvin: 5200f, lux: 350f, flux: 3600f, ev: 8f);
            CaptureMood("warm-evening", kelvin: 2700f, lux: 150f, flux: 2600f, ev: 8f);
            CaptureMood("dim", kelvin: 3000f, lux: 70f, flux: 1800f, ev: 8f);
        }

        static void CaptureMood(string label, float kelvin, float lux, float flux, float ev)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var source = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            if (source == null) { Debug.LogError("SCENECAPTURE: pair resource missing."); return; }
            var spec = RoomJson.Deserialize<ConditionPairSpec>(source.text).Control;

            spec.Furniture.Add(new FurniturePlacementSpec
            {
                AssetId = "builtin.pendant-light",
                SlotId = "pendant-center",
                PositionM = new Vec3 { X = 0f, Y = 0f, Z = 0f },
                CeilingMounted = true
            });
            spec.Lighting.ColorTemperatureK = kelvin;
            spec.Lighting.TargetLux = lux;
            spec.Lighting.BaseLuminousFluxLm = flux;
            spec.Lighting.FixedExposureEv100 = ev;

            var genGo = new GameObject("Capture Generator");
            var generator = genGo.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            generator.Build(spec);

            var qualityGo = new GameObject("Quality Rig");
            var volume = qualityGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = QualityRig.VolumePriority;
            var qualityProfile = QualityRig.BuildProfile();
            volume.profile = qualityProfile;

            var g = spec.Geometry;
            var position = new Vector3(g.WidthM * 0.32f, 1.6f, -g.LengthM * 0.34f);
            var target = new Vector3(-g.WidthM * 0.12f, 1.25f, g.LengthM * 0.18f);
            var rotation = Quaternion.LookRotation((target - position).normalized);
            RenderToPng("l2-" + label, position, rotation, 1920, 1080);

            Object.DestroyImmediate(qualityProfile);
            Object.DestroyImmediate(qualityGo);
            Object.DestroyImmediate(genGo);
        }

        static void CaptureRoom(string label, bool installQuality)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var source = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            if (source == null)
            {
                Debug.LogError("SCENECAPTURE: ceiling-height-pair resource not found.");
                return;
            }
            var pair = RoomJson.Deserialize<ConditionPairSpec>(source.text);
            var spec = pair.Control;

            var genGo = new GameObject("Capture Generator");
            var generator = genGo.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            var build = generator.Build(spec);
            Debug.Log($"SCENECAPTURE build ok={build.Ok} label={label}");

            GameObject qualityGo = null;
            VolumeProfile qualityProfile = null;
            if (installQuality)
            {
                qualityGo = new GameObject("Quality Rig");
                var volume = qualityGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = QualityRig.VolumePriority;
                qualityProfile = QualityRig.BuildProfile();
                volume.profile = qualityProfile;
            }

            var g = spec.Geometry;
            var position = new Vector3(g.WidthM * 0.32f, 1.6f, -g.LengthM * 0.34f);
            var target = new Vector3(-g.WidthM * 0.12f, 1.05f, g.LengthM * 0.18f);
            var rotation = Quaternion.LookRotation((target - position).normalized);
            RenderToPng(label, position, rotation, 1920, 1080);

            if (qualityProfile != null) Object.DestroyImmediate(qualityProfile);
            if (qualityGo != null) Object.DestroyImmediate(qualityGo);
            Object.DestroyImmediate(genGo);
        }

        /// <summary>
        /// Renders from the given pose to a PNG named capture-&lt;label&gt;.png and returns the
        /// frame's mean luminance (0..~1+, HDR clamps on readback) for a quick brightness check.
        /// </summary>
        public static float RenderToPng(string label, Vector3 position, Quaternion rotation,
            int width, int height, bool enableSsgi = false)
        {
            var camGo = new GameObject("CaptureCamera");
            camGo.transform.SetPositionAndRotation(position, rotation);
            var cam = camGo.AddComponent<Camera>();
            var hdCam = camGo.AddComponent<HDAdditionalCameraData>();
            if (enableSsgi)
            {
                // Enable the SSGI camera frame setting directly (it defaults off and, in HDRP 17.3,
                // lives in GraphicsSettings). Per-camera override keeps this self-contained: the asset
                // supportSSGI flag (L0) + the GlobalIllumination volume override (QualityRig) do the rest.
                hdCam.customRenderingSettings = true;
                hdCam.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.SSGI] = true;
                hdCam.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.SSGI, true);
            }
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 1
            };
            cam.targetTexture = rt;

            // Warm-up frames: HDRP's first frames lack history buffers (TAA/SSAO/SSGI),
            // so render a few times before reading back.
            for (var i = 0; i < 4; i++)
                cam.Render();

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            var path = Path.Combine(OutputDir, "capture-" + label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            var pixels = tex.GetPixels();
            var sum = 0.0;
            foreach (var p in pixels)
                sum += 0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b;
            var mean = (float)(sum / pixels.Length);

            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log($"SCENECAPTURE wrote {path} ({width}x{height}) meanLuminance={mean:0.0000}");
            return mean;
        }
    }
}
