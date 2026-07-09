using System;
using System.IO;
using System.Linq;
using KnowledgeAtlas.Seam;
using RoomGen.Adapter;
using RoomGen.Contracts;
using RoomGen.Export;
using RoomGen.Generation;
using RoomGen.Validation;
using RoomGen.VR;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoomGen.Studio
{
    public sealed class RoomStudioController : MonoBehaviour
    {
        const int ControlLayer = 8;
        const int TreatmentLayer = 9;
        const int WalkLayer = 10; // the seam-driven single-room walk view (separate from the two previews)

        ConditionPairSpec pair;
        RoomGenerator controlGenerator;
        RoomGenerator treatmentGenerator;
        RoomGenerator walkGenerator;
        RenderTexture controlPreview;
        RenderTexture treatmentPreview;
        ValidationReport report;
        VrExplorationMode vrMode;
        DesktopWalkMode desktopWalk;

        // G4/G5 seam: the walk view is driven through ISpecChannel so it exercises the real runtime
        // (atomic apply, gate, provenance log) exactly as the experiment runtime / networked VR will.
        LocalChannel seamChannel;
        RoomRuntime seamRuntime;
        string canonicalControlJson;
        string canonicalTreatmentJson;
        string activeWalkCondition = "control";
        bool walkingSeam;
        string seamStatus = "";
        int requestCounter;

        string status = "Ready";
        GUIStyle titleStyle;
        GUIStyle headingStyle;
        GUIStyle labelStyle;
        GUIStyle statusStyle;
        GUIStyle panelStyle;

        readonly string[] floorMaterials = { "builtin.oak", "builtin.walnut" };
        readonly string[] lightingPresets = { "builtin.recessed-neutral", "builtin.recessed-warm" };

        void Awake()
        {
            RenderQualityProfiles.ApplyDesktop();
            vrMode = gameObject.AddComponent<VrExplorationMode>();
            desktopWalk = gameObject.AddComponent<DesktopWalkMode>();
            LoadBundledPair();
            BuildPreviewWorld();
            SetUpSeam();
            Rebuild();
        }

        void SetUpSeam()
        {
            var schema = Resources.Load<TextAsset>("RoomGen/room_spec.schema");
            if (schema == null) return; // seam walk unavailable without the schema; previews still work
            seamRuntime = new RoomRuntime(walkGenerator, schema.text);
            var logPath = Path.Combine(Application.persistentDataPath, "seam-session.jsonl");
            seamChannel = new LocalChannel(seamRuntime, logPath);
            seamChannel.OnEvent += OnSeamEvent;
        }

        void OnSeamEvent(SeamEvent ev)
        {
            switch (ev.Kind)
            {
                case SeamCodes.PairLoaded:
                    seamStatus = ev.Ok
                        ? "Seam: pair PASS  ·  diff [" + string.Join(", ", ev.PairDiffPaths) + "]"
                        : "Seam: pair REFUSED  ·  [" + string.Join(", ", ev.PairViolationCodes) + "]";
                    break;
                case SeamCodes.ConditionSwitched:
                    seamStatus = $"Seam: showing {ev.Condition}  ·  {ev.Transition} {ev.Ms} ms";
                    break;
                case SeamCodes.SpecApplied:
                    if (!ev.Ok) seamStatus = "Seam: apply REFUSED  ·  [" + string.Join(", ", ev.ErrorCodes) + "]";
                    break;
            }
        }

        void Update()
        {
            if (walkingSeam && (desktopWalk == null || !desktopWalk.IsRunning))
                walkingSeam = false; // walker exited via Esc
            if (walkingSeam && desktopWalk.IsRunning &&
                Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                SwitchWalkCondition();
        }

        void OnDestroy()
        {
            if (controlPreview != null) controlPreview.Release();
            if (treatmentPreview != null) treatmentPreview.Release();
        }

        void LoadBundledPair()
        {
            var source = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            pair = source != null
                ? RoomJson.Deserialize<ConditionPairSpec>(source.text)
                : new ConditionPairSpec();
        }

        void BuildPreviewWorld()
        {
            controlGenerator = CreateGenerator("Control Generator", ControlLayer);
            treatmentGenerator = CreateGenerator("Treatment Generator", TreatmentLayer);
            walkGenerator = CreateGenerator("Walk Generator", WalkLayer);
            controlPreview = CreatePreviewCamera("Control Camera", ControlLayer);
            treatmentPreview = CreatePreviewCamera("Treatment Camera", TreatmentLayer);
        }

        RoomGenerator CreateGenerator(string objectName, int layer)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(transform, false);
            var generator = root.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(layer);
            return generator;
        }

        RenderTexture CreatePreviewCamera(string objectName, int layer)
        {
            var texture = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGBHalf)
            {
                name = objectName + " Texture",
                antiAliasing = 2
            };
            var root = new GameObject(objectName);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(0f, 1.58f, 2.48f);
            root.transform.LookAt(new Vector3(0f, 1.32f, -0.55f));
            var camera = root.AddComponent<Camera>();
            camera.fieldOfView = 72f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.06f, 0.065f);
            camera.cullingMask = 1 << layer;
            camera.targetTexture = texture;
            return texture;
        }

        void Rebuild()
        {
            var control = controlGenerator.Build(pair.Control);
            var treatment = treatmentGenerator.Build(pair.Treatment);
            report = PairValidator.Validate(pair);
            if (!control.Lighting.Ok || !treatment.Lighting.Ok)
                status = "Lighting calibration is outside tolerance.";
            else
                status = report.Ok ? "Pair valid and ready to export." : "Resolve validation errors before export.";
        }

        void OnGUI()
        {
            EnsureStyles();
            if (desktopWalk != null && desktopWalk.IsRunning)
            {
                var hint = walkingSeam
                    ? "Walking (seam · " + activeWalkCondition + ")   ·   WASD move   ·   mouse look   ·   Tab: switch condition (fade)   ·   Esc to exit"
                    : "Walking room (desktop)   ·   WASD move   ·   mouse look   ·   Esc to exit";
                GUI.Label(new Rect(24f, 20f, Screen.width - 48f, 26f), hint, labelStyle);
                return;
            }
            GUI.backgroundColor = new Color(0.08f, 0.09f, 0.1f);
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, panelStyle);

            GUI.Label(new Rect(24f, 15f, Screen.width - 48f, 38f), "Room Studio", titleStyle);
            var pairTitle = string.Equals(pair.ManipulatedVariable, "geometry.corner_radius_m", StringComparison.Ordinal)
                ? "Dining room / corner-radius pair" : "Dining room / ceiling-height pair";
            GUI.Label(new Rect(184f, 22f, Screen.width - 208f, 28f),
                pairTitle, labelStyle);

            var controlsWidth = Mathf.Clamp(Screen.width * 0.25f, 300f, 380f);
            var controlsRect = new Rect(20f, 66f, controlsWidth, Screen.height - 86f);
            GUI.Box(controlsRect, GUIContent.none, panelStyle);
            DrawControls(new Rect(controlsRect.x + 18f, controlsRect.y + 15f,
                controlsRect.width - 36f, controlsRect.height - 30f));

            var previewX = controlsRect.xMax + 18f;
            var previewWidth = Screen.width - previewX - 20f;
            var gap = 14f;
            var cardWidth = (previewWidth - gap) * 0.5f;
            var cardHeight = Screen.height - 86f;
            DrawPreview(new Rect(previewX, 66f, cardWidth, cardHeight),
                "CONTROL", pair.Control, controlPreview);
            DrawPreview(new Rect(previewX + cardWidth + gap, 66f, cardWidth, cardHeight),
                "TREATMENT", pair.Treatment, treatmentPreview);
        }

        void DrawControls(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Label("CONDITION PAIR", headingStyle);
            GUILayout.Space(8f);

            var isCeilingManipulation = string.Equals(pair.ManipulatedVariable, "geometry.ceiling_height_m", StringComparison.Ordinal);
            var isBowManipulation = string.Equals(pair.ManipulatedVariable, "geometry.wall_bow.back", StringComparison.Ordinal);
            var variableLabel = isCeilingManipulation ? "Variable: Ceiling Height"
                : isBowManipulation ? "Variable: Wall Bow (back)"
                : "Variable: Corner Radius";
            if (GUILayout.Button(variableLabel, GUILayout.Height(32f)))
            {
                // Cycle ceiling -> corner radius -> wall bow -> ceiling; reset the other variables
                // so the pair stays single-variable.
                if (isCeilingManipulation)
                {
                    pair.ManipulatedVariable = "geometry.corner_radius_m";
                    var maxRadius = CalculateMaxSafeRadius(pair.Control);
                    pair.Control.Geometry.CornerRadiusM = 0f;
                    pair.Treatment.Geometry.CornerRadiusM = maxRadius;
                    pair.Treatment.Geometry.CeilingHeightM = pair.Control.Geometry.CeilingHeightM;
                    ResetBow(pair.Control);
                    ResetBow(pair.Treatment);
                }
                else if (!isBowManipulation)
                {
                    pair.ManipulatedVariable = "geometry.wall_bow.back";
                    pair.Control.Geometry.CornerRadiusM = 0f;
                    pair.Treatment.Geometry.CornerRadiusM = 0f;
                    pair.Treatment.Geometry.CeilingHeightM = pair.Control.Geometry.CeilingHeightM;
                    ResetBow(pair.Control);
                    ResetBow(pair.Treatment);
                    pair.Treatment.Geometry.WallBow.Back = 1f; // start with a visible full convex bow
                }
                else
                {
                    pair.ManipulatedVariable = "geometry.ceiling_height_m";
                    pair.Control.Geometry.CornerRadiusM = 0f;
                    pair.Treatment.Geometry.CornerRadiusM = 0f;
                    ResetBow(pair.Control);
                    ResetBow(pair.Treatment);
                }
                Rebuild();
            }
            GUILayout.Space(8f);

            if (isBowManipulation)
            {
                var controlBow = pair.Control.Geometry.WallBow.Back;
                GUILayout.Label($"Control bow  {controlBow:0.00}  ({BowWord(controlBow)})", labelStyle);
                var nextControl = GUILayout.HorizontalSlider(controlBow, -1f, 1f, GUILayout.Height(24f));
                GUILayout.Space(8f);

                var treatmentBow = pair.Treatment.Geometry.WallBow.Back;
                GUILayout.Label($"Treatment bow  {treatmentBow:0.00}  ({BowWord(treatmentBow)})", labelStyle);
                var nextTreatment = GUILayout.HorizontalSlider(treatmentBow, -1f, 1f, GUILayout.Height(24f));

                if (!Mathf.Approximately(nextControl, controlBow) || !Mathf.Approximately(nextTreatment, treatmentBow))
                {
                    pair.Control.Geometry.WallBow.Back = Mathf.Round(nextControl * 20f) / 20f;
                    pair.Treatment.Geometry.WallBow.Back = Mathf.Round(nextTreatment * 20f) / 20f;
                    Rebuild();
                }
            }
            else if (!isCeilingManipulation)
            {
                var maxRadius = CalculateMaxSafeRadius(pair.Control);
                var controlRadius = pair.Control.Geometry.CornerRadiusM;
                GUILayout.Label($"Control radius  {controlRadius:0.00} m", labelStyle);
                var nextControl = GUILayout.HorizontalSlider(controlRadius, 0f, maxRadius, GUILayout.Height(24f));
                GUILayout.Space(8f);

                var treatmentRadius = pair.Treatment.Geometry.CornerRadiusM;
                GUILayout.Label($"Treatment radius  {treatmentRadius:0.00} m", labelStyle);
                var nextTreatment = GUILayout.HorizontalSlider(treatmentRadius, 0f, maxRadius, GUILayout.Height(24f));

                if (!Mathf.Approximately(nextControl, controlRadius) || !Mathf.Approximately(nextTreatment, treatmentRadius))
                {
                    pair.Control.Geometry.CornerRadiusM = Mathf.Round(nextControl * 20f) / 20f;
                    pair.Treatment.Geometry.CornerRadiusM = Mathf.Round(nextTreatment * 20f) / 20f;
                    Rebuild();
                }
            }
            else
            {
                var controlHeight = pair.Control.Geometry.CeilingHeightM;
                GUILayout.Label($"Control ceiling  {controlHeight:0.00} m", labelStyle);
                var nextControl = GUILayout.HorizontalSlider(controlHeight, 2.2f, 3.8f, GUILayout.Height(24f));
                GUILayout.Space(8f);

                var treatmentHeight = pair.Treatment.Geometry.CeilingHeightM;
                GUILayout.Label($"Treatment ceiling  {treatmentHeight:0.00} m", labelStyle);
                var nextTreatment = GUILayout.HorizontalSlider(treatmentHeight, 2.2f, 3.8f, GUILayout.Height(24f));

                if (!Mathf.Approximately(nextControl, controlHeight) || !Mathf.Approximately(nextTreatment, treatmentHeight))
                {
                    pair.Control.Geometry.CeilingHeightM = Mathf.Round(nextControl * 20f) / 20f;
                    pair.Treatment.Geometry.CeilingHeightM = Mathf.Round(nextTreatment * 20f) / 20f;
                    Rebuild();
                }
            }

            GUILayout.Space(14f);
            GUILayout.Label("LOCKED BASELINE", headingStyle);
            GUILayout.Label($"Room  {pair.Control.Geometry.WidthM:0.0} x {pair.Control.Geometry.LengthM:0.0} m", labelStyle);
            GUILayout.Label("Openings, furniture, player start and seed are matched", labelStyle);
            if (isCeilingManipulation)
                GUILayout.Label("Corner radius  0.00 m  ·  wall bow  0", labelStyle);
            else if (isBowManipulation)
                GUILayout.Label($"Ceiling {pair.Control.Geometry.CeilingHeightM:0.00} m  ·  corner radius 0.00 m", labelStyle);
            else
                GUILayout.Label($"Ceiling height  {pair.Control.Geometry.CeilingHeightM:0.00} m  ·  wall bow  0", labelStyle);

            GUILayout.Space(14f);
            GUILayout.Label("SHARED PRESETS", headingStyle);
            if (GUILayout.Button("Floor: " + Friendly(pair.Control.Surfaces.FloorMaterialId), GUILayout.Height(32f)))
                CycleFloor();
            if (GUILayout.Button("Lighting: " + Friendly(pair.Control.Lighting.PresetId), GUILayout.Height(32f)))
                CycleLighting();

            GUILayout.Space(14f);
            GUILayout.Label("PAIR CHECK", headingStyle);
            GUILayout.Label(report != null && report.Ok ? "VALID" : "BLOCKED", statusStyle);
            GUILayout.Label(status, labelStyle);
            if (report != null)
            {
                GUILayout.Label("Changed: " + string.Join(", ", report.ChangedFields), labelStyle);
                GUILayout.Label(
                    $"Volume: {report.ControlMetrics.VolumeM3:0.0} -> {report.TreatmentMetrics.VolumeM3:0.0} m3",
                    labelStyle);
                foreach (var issue in report.Issues.Take(3))
                    GUILayout.Label(issue.Path + ": " + issue.Message, labelStyle);
            }
            if (!string.IsNullOrEmpty(seamStatus))
                GUILayout.Label(seamStatus, labelStyle);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Load KA spec pair (adapter)", GUILayout.Height(32f)))
                LoadKaPair();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", GUILayout.Height(36f))) Save();
            if (GUILayout.Button("Load", GUILayout.Height(36f))) Load();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Walk control (desktop)", GUILayout.Height(36f)))
                desktopWalk.Toggle(ControlLayer, controlGenerator.LastResult?.Root, pair.Control);
            if (GUILayout.Button("Walk treatment (desktop)", GUILayout.Height(36f)))
                desktopWalk.Toggle(TreatmentLayer, treatmentGenerator.LastResult?.Root, pair.Treatment);
            if (seamChannel != null && canonicalControlJson != null)
            {
                GUI.enabled = walkGenerator.LastResult?.Root != null;
                if (GUILayout.Button(walkingSeam ? "Exit seam walk" : "Walk pair via seam", GUILayout.Height(36f)))
                    ToggleSeamWalk();
                GUI.enabled = true;
            }
            if (GUILayout.Button(vrMode.IsRunning ? "Exit VR exploration" : "Explore control in VR",
                    GUILayout.Height(36f)))
                vrMode.Toggle(ControlLayer, pair.Control);
            GUI.enabled = report != null && report.Ok;
            if (GUILayout.Button("Export research package", GUILayout.Height(42f))) Export();
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        void DrawPreview(Rect rect, string condition, RoomSpec spec, RenderTexture texture)
        {
            GUI.Box(rect, GUIContent.none, panelStyle);
            GUI.Label(new Rect(rect.x + 15f, rect.y + 11f, rect.width - 30f, 24f),
                condition, headingStyle);
            string text;
            if (string.Equals(pair.ManipulatedVariable, "geometry.corner_radius_m", StringComparison.Ordinal))
                text = $"{spec.Geometry.CornerRadiusM:0.00} m radius";
            else if (string.Equals(pair.ManipulatedVariable, "geometry.wall_bow.back", StringComparison.Ordinal))
                text = $"bow {spec.Geometry.WallBow.Back:0.00} ({BowWord(spec.Geometry.WallBow.Back)})";
            else
                text = $"{spec.Geometry.CeilingHeightM:0.00} m ceiling";
            GUI.Label(new Rect(rect.x + 15f, rect.y + 34f, rect.width - 30f, 24f),
                text, labelStyle);
            var imageRect = new Rect(rect.x + 12f, rect.y + 65f, rect.width - 24f, rect.height - 77f);
            GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleAndCrop, false);
        }

        static void ResetBow(RoomSpec spec)
        {
            spec.Geometry.WallBow ??= new WallBowSpec();
            spec.Geometry.WallBow.Front = 0f;
            spec.Geometry.WallBow.Back = 0f;
            spec.Geometry.WallBow.Left = 0f;
            spec.Geometry.WallBow.Right = 0f;
        }

        static string BowWord(float bow) =>
            bow < -0.025f ? "concave, bows in" : bow > 0.025f ? "convex, bulges out" : "flat";

        void CycleFloor()
        {
            var current = Array.IndexOf(floorMaterials, pair.Control.Surfaces.FloorMaterialId);
            var next = floorMaterials[(current + 1 + floorMaterials.Length) % floorMaterials.Length];
            pair.Control.Surfaces.FloorMaterialId = next;
            pair.Treatment.Surfaces.FloorMaterialId = next;
            Rebuild();
        }

        void CycleLighting()
        {
            var current = Array.IndexOf(lightingPresets, pair.Control.Lighting.PresetId);
            var next = lightingPresets[(current + 1 + lightingPresets.Length) % lightingPresets.Length];
            pair.Control.Lighting.PresetId = next;
            pair.Treatment.Lighting.PresetId = next;
            var temperature = next.EndsWith("warm", StringComparison.Ordinal) ? 3000f : 3500f;
            pair.Control.Lighting.ColorTemperatureK = temperature;
            pair.Treatment.Lighting.ColorTemperatureK = temperature;
            Rebuild();
        }

        void Save()
        {
            var path = Path.Combine(Application.persistentDataPath, "room-studio-pair.json");
            File.WriteAllText(path, RoomJson.Serialize(pair));
            status = "Saved to " + path;
        }

        void Load()
        {
            var path = Path.Combine(Application.persistentDataPath, "room-studio-pair.json");
            if (!File.Exists(path))
            {
                status = "No saved pair found.";
                return;
            }
            pair = RoomJson.Deserialize<ConditionPairSpec>(File.ReadAllText(path));
            Rebuild();
            status = "Saved pair loaded.";
        }

        void Export()
        {
            var result = RoomPackageExporter.Export(pair);
            status = result.Ok ? "Exported: " + result.PackagePath : result.Message;
        }

        // G2: build the rooms from the CANONICAL Knowledge-Atlas pair via RoomSpecAdapter —
        // end-to-end proof that Diego's contract drives this generator.
        void LoadKaPair()
        {
            var control = Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-control");
            var treatment = Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-treatment");
            if (control == null || treatment == null)
            {
                status = "KA spec fixtures not found under Resources/RoomGen/Examples.";
                return;
            }
            canonicalControlJson = control.text;
            canonicalTreatmentJson = treatment.text;
            pair = RoomSpecAdapter.AdaptPair(control.text, treatment.text);
            Rebuild();
            // Drive the SAME pair through the seam: gates it (pair_loaded) and builds the single-room
            // walk view in the walk generator. The dual preview above is the operator design view.
            activeWalkCondition = "control";
            seamChannel?.LoadPair(canonicalControlJson, canonicalTreatmentJson, NextRequestId());
            status = "KA pair adapted (spec -> adapter -> rooms). " + status;
        }

        void ToggleSeamWalk()
        {
            if (desktopWalk.IsRunning)
            {
                desktopWalk.StopSession();
                walkingSeam = false;
                return;
            }
            var spec = activeWalkCondition == "treatment" ? pair.Treatment : pair.Control;
            desktopWalk.Toggle(WalkLayer, walkGenerator.LastResult?.Root, spec);
            walkingSeam = desktopWalk.IsRunning;
        }

        // Seam switch_condition(fade): rebuild the other condition in the walk generator at the fade's
        // black midpoint, then re-target the walker to it — the swap is never seen.
        void SwitchWalkCondition()
        {
            if (!walkingSeam || !desktopWalk.IsRunning) return;
            var next = activeWalkCondition == "control" ? "treatment" : "control";
            var nextSpec = next == "treatment" ? pair.Treatment : pair.Control;
            desktopWalk.SwitchTo(WalkLayer, nextSpec, () =>
            {
                seamChannel.SwitchCondition(next, "fade", NextRequestId()); // runtime rebuilds walkGenerator
                activeWalkCondition = next;
                return walkGenerator.LastResult?.Root;
            });
        }

        string NextRequestId() => "r-" + (++requestCounter).ToString("0000");

        static string Friendly(string id) =>
            id.Replace("builtin.", "").Replace('-', ' ');

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 27,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.94f, 0.95f, 0.94f) }
            };
            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.52f, 0.82f, 0.72f) }
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.78f, 0.8f, 0.8f) }
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.52f, 0.82f, 0.72f) }
            };
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.whiteTexture }
            };
        }

        float CalculateMaxSafeRadius(RoomSpec spec)
        {
            var maxRadius = Mathf.Min(spec.Geometry.WidthM, spec.Geometry.LengthM) * 0.5f - 0.1f;
            foreach (var opening in spec.Openings)
            {
                var wallLength = IsSideWall(opening.Wall) ? spec.Geometry.LengthM : spec.Geometry.WidthM;
                var spaceNeededFromCenter = Mathf.Abs(opening.CenterM) + opening.WidthM * 0.5f;
                var safeForOpening = wallLength * 0.5f - spaceNeededFromCenter - 0.05f;
                maxRadius = Mathf.Min(maxRadius, Mathf.Max(0f, safeForOpening));
            }
            return maxRadius;
        }

        bool IsSideWall(string wall) =>
            string.Equals(wall, "left", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wall, "right", StringComparison.OrdinalIgnoreCase);
    }
}
