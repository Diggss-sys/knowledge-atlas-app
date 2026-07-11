using System;
using System.IO;
using System.Linq;
using KnowledgeAtlas.Seam;
using RoomGen.Adapter;
using RoomGen.Contracts;
using RoomGen.Export;
using RoomGen.Generation;
using RoomGen.Lighting;
using RoomGen.Metrics;
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
        Transform controlCameraRig;
        Transform treatmentCameraRig;
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

        // cc0.* floors resolve to the fetched ambientCG materials (AssetFetcher); on a machine that
        // hasn't fetched, SurfaceResolver falls back to a flat tint — graceful, never broken.
        readonly string[] floorMaterials = { "builtin.oak", "builtin.walnut", "cc0.carpet", "cc0.tile" };

        // The demo-able manipulations. Cycling equalises everything first so the pair always has
        // exactly ONE declared difference (the gate re-checks on every rebuild regardless).
        static readonly string[] VariablePaths =
            { "geometry.ceiling_height_m", "geometry.corner_radius_m", "geometry.width_m", "geometry.wall_bow" };
        static readonly string[] VariableLabels =
            { "Ceiling Height", "Corner Radius", "Room Width", "Wall Bow (all walls)" };

        const float MinRoomWidth = 5.0f;   // keeps the bundled front-door opening in bounds
        const float MaxRoomWidth = 7.0f;
        const float MinRoomLength = 5.6f;  // keeps the bundled window openings in bounds
        const float MaxRoomLength = 7.5f;

        // Time-of-day sun (SunSkySystem). Negative = off: the static calibrated sun from
        // LightingSystem is left untouched. Shared across both conditions (nuisance parameter),
        // never serialized into the pair spec.
        float sunHour = -1f;

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
            controlPreview = CreatePreviewCamera("Control Camera", ControlLayer, out controlCameraRig);
            treatmentPreview = CreatePreviewCamera("Treatment Camera", TreatmentLayer, out treatmentCameraRig);
        }

        /// <summary>
        /// Keep the preview camera INSIDE the room for the current geometry. The rig was placed
        /// once at a fixed z tuned for the default 6.2 m room; sliders move the walls: a concave
        /// front bow pulls the front wall's midpoint (x = 0 — exactly where the camera sits)
        /// inward by up to bow_max, and the Length slider moves it outright. A camera behind or
        /// inside a wall renders black. Recomputed on every rebuild instead.
        /// </summary>
        static void PositionPreviewCamera(Transform rig, RoomSpec spec)
        {
            if (rig == null) return;
            var g = spec.Geometry;
            var halfL = g.LengthM * 0.5f;
            var frontBow = g.WallBow?.Front ?? 0f;
            var frontInset = frontBow < 0f ? -frontBow * Mathf.Max(0f, g.BowMaxM) : 0f;
            var z = halfL - frontInset - 0.45f; // 0.45 m clear of the worst-case front wall face
            var eyeY = Mathf.Min(1.58f, g.CeilingHeightM - 0.25f);
            rig.position = new Vector3(0f, eyeY, z);
            rig.LookAt(new Vector3(0f, Mathf.Min(1.32f, g.CeilingHeightM * 0.45f), -halfL * 0.25f));
        }

        RoomGenerator CreateGenerator(string objectName, int layer)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(transform, false);
            var generator = root.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(layer);
            return generator;
        }

        RenderTexture CreatePreviewCamera(string objectName, int layer, out Transform rig)
        {
            var texture = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGBHalf)
            {
                name = objectName + " Texture",
                antiAliasing = 2
            };
            var root = new GameObject(objectName);
            root.transform.SetParent(transform, false);
            rig = root.transform; // positioned per-geometry by PositionPreviewCamera on rebuild
            var camera = root.AddComponent<Camera>();
            camera.fieldOfView = 72f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 25f;
            camera.cullingMask = 1 << layer;
            camera.targetTexture = texture;
            // Sky through openings, but NO SSGI on previews: they rebuild every slider frame, so
            // temporal SSGI would smear (the "blurry preview"). Walk/VR cameras get full SSGI.
            CameraRealism.Apply(camera, enableSsgi: false);
            return texture;
        }

        void Rebuild()
        {
            var control = controlGenerator.Build(pair.Control);
            var treatment = treatmentGenerator.Build(pair.Treatment);
            PositionPreviewCamera(controlCameraRig, pair.Control);
            PositionPreviewCamera(treatmentCameraRig, pair.Treatment);
            report = PairValidator.Validate(pair);
            // A refused build (spec validation error) returns Root/Lighting = null — surface the
            // reason instead of dereferencing it (was an every-frame NRE that froze the panel).
            if (control.Lighting == null || treatment.Lighting == null)
            {
                var refused = control.Lighting == null ? control : treatment;
                status = refused.Warnings.Count > 0
                    ? "Room refused: " + refused.Warnings[0]
                    : "Room refused by spec validation.";
            }
            else if (!control.Lighting.Ok || !treatment.Lighting.Ok)
                status = "Lighting calibration is outside tolerance.";
            else
                status = report.Ok ? "Pair valid and ready to export." : "Resolve validation errors before export.";
            ApplySunSky(); // rebuilds recreate the Sun lights with static values; re-aim if a time is set
        }

        // Re-apply the time-of-day sun to all three views (previews + walk). No-op when off.
        void ApplySunSky()
        {
            if (sunHour < 0f) return;
            SunSkySystem.Apply(controlGenerator.transform, sunHour, pair.Control.Lighting.TargetLux);
            SunSkySystem.Apply(treatmentGenerator.transform, sunHour, pair.Treatment.Lighting.TargetLux);
            SunSkySystem.Apply(walkGenerator.transform, sunHour, pair.Control.Lighting.TargetLux);
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
            var titleIndex = Mathf.Max(0, Array.IndexOf(VariablePaths, pair.ManipulatedVariable));
            var pairTitle = "Dining room / " + VariableLabels[titleIndex].ToLowerInvariant() + " pair";
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

            var variableIndex = Mathf.Max(0, Array.IndexOf(VariablePaths, pair.ManipulatedVariable));
            if (GUILayout.Button("Variable: " + VariableLabels[variableIndex], GUILayout.Height(32f)))
                SelectManipulatedVariable(VariablePaths[(variableIndex + 1) % VariablePaths.Length]);
            GUILayout.Space(8f);

            switch (pair.ManipulatedVariable)
            {
                case "geometry.corner_radius_m":
                {
                    var maxRadius = CalculateMaxSafeRadius(pair.Control);
                    DrawConditionSliders("radius", "0.00", " m", 0f, maxRadius, 20f,
                        pair.Control.Geometry.CornerRadiusM, pair.Treatment.Geometry.CornerRadiusM,
                        (c, t) => { pair.Control.Geometry.CornerRadiusM = c; pair.Treatment.Geometry.CornerRadiusM = t; });
                    break;
                }
                case "geometry.width_m":
                    DrawConditionSliders("width", "0.0", " m", MinRoomWidth, MaxRoomWidth, 10f,
                        pair.Control.Geometry.WidthM, pair.Treatment.Geometry.WidthM,
                        (c, t) => { pair.Control.Geometry.WidthM = c; pair.Treatment.Geometry.WidthM = t; });
                    break;
                case "geometry.wall_bow":
                {
                    // Curved-wall manipulation applied to ALL FOUR walls together: -1 bows every
                    // wall into the room (concave), +1 bulges every wall outward (convex). One
                    // slider drives front/back/left/right so the room stays symmetric and every
                    // wall still meets its neighbours at the shared corner points (WallBand seals
                    // each band's ends, so joins never gap).
                    pair.Control.Geometry.WallBow ??= new WallBowSpec();
                    pair.Treatment.Geometry.WallBow ??= new WallBowSpec();
                    DrawConditionSliders("bow", "0.00", "", -1f, 1f, 20f,
                        pair.Control.Geometry.WallBow.Back, pair.Treatment.Geometry.WallBow.Back,
                        (c, t) => { SetAllBow(pair.Control, c); SetAllBow(pair.Treatment, t); });
                    GUILayout.Label(
                        $"Control: {BowWord(pair.Control.Geometry.WallBow.Back)}  ·  Treatment: {BowWord(pair.Treatment.Geometry.WallBow.Back)}"
                        + "   (walls with a door/window stay straight)",
                        labelStyle);
                    break;
                }
                default:
                    DrawConditionSliders("ceiling", "0.00", " m", 2.2f, 3.8f, 20f,
                        pair.Control.Geometry.CeilingHeightM, pair.Treatment.Geometry.CeilingHeightM,
                        (c, t) => { pair.Control.Geometry.CeilingHeightM = c; pair.Treatment.Geometry.CeilingHeightM = t; });
                    break;
            }

            GUILayout.Space(14f);
            GUILayout.Label("SHARED ROOM (applies to both)", headingStyle);
            if (pair.ManipulatedVariable != "geometry.width_m")
                DrawSharedSlider("Width", "0.0", " m", MinRoomWidth, MaxRoomWidth, 10f,
                    pair.Control.Geometry.WidthM,
                    v => { pair.Control.Geometry.WidthM = v; pair.Treatment.Geometry.WidthM = v; });
            DrawSharedSlider("Length", "0.0", " m", MinRoomLength, MaxRoomLength, 10f,
                pair.Control.Geometry.LengthM,
                v => { pair.Control.Geometry.LengthM = v; pair.Treatment.Geometry.LengthM = v; });

            GUILayout.Space(14f);
            GUILayout.Label("SHARED LIGHTING (applies to both)", headingStyle);
            var temperature = pair.Control.Lighting.ColorTemperatureK;
            var mood = temperature < 3300f ? "warm" : temperature < 5000f ? "neutral" : "cool";
            DrawSharedSlider($"Color temp ({mood})", "0", " K", 2700f, 6500f, 0.02f,
                temperature,
                v => { pair.Control.Lighting.ColorTemperatureK = v; pair.Treatment.Lighting.ColorTemperatureK = v; });
            DrawSharedSlider("Brightness target", "0", " lux", 100f, 500f, 0.1f,
                pair.Control.Lighting.TargetLux,
                v => { pair.Control.Lighting.TargetLux = v; pair.Treatment.Lighting.TargetLux = v; });
            if (GUILayout.Button("Floor: " + Friendly(pair.Control.Surfaces.FloorMaterialId), GUILayout.Height(32f)))
                CycleFloor();

            // Time-of-day sun (visual only, shared by both conditions). Slider engages the solar
            // arc; the button restores the static calibrated sun exactly as LightingSystem built it.
            var sunLabel = sunHour < 0f ? "Sun: static (calibrated)" : $"Sun: {(int)sunHour:00}:{(int)((sunHour % 1f) * 60f):00}";
            GUILayout.Label(sunLabel, labelStyle);
            var nextHour = GUILayout.HorizontalSlider(
                sunHour < 0f ? SunSkySystem.PeakHour : sunHour,
                SunSkySystem.MinHour, SunSkySystem.MaxHour, GUILayout.Height(24f));
            if (sunHour >= 0f ? !Mathf.Approximately(nextHour, sunHour) : Mathf.Abs(nextHour - SunSkySystem.PeakHour) > 0.01f)
            {
                sunHour = Mathf.Round(nextHour * 4f) / 4f; // 15-minute steps
                ApplySunSky();
            }
            if (sunHour >= 0f && GUILayout.Button("Reset sun to calibrated", GUILayout.Height(24f)))
            {
                sunHour = -1f;
                SunSkySystem.Reset(controlGenerator.transform, pair.Control.Lighting.ColorTemperatureK, pair.Control.Lighting.TargetLux);
                SunSkySystem.Reset(treatmentGenerator.transform, pair.Treatment.Lighting.ColorTemperatureK, pair.Treatment.Lighting.TargetLux);
                SunSkySystem.Reset(walkGenerator.transform, pair.Control.Lighting.ColorTemperatureK, pair.Control.Lighting.TargetLux);
            }

            GUILayout.Space(10f);
            GUILayout.Label("Openings, furniture, player start and seed are always matched", labelStyle);

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
            if (GUILayout.Button("Load realism test room (windows)", GUILayout.Height(32f)))
                LoadRealismPair();
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
            var g = spec.Geometry;
            string varText;
            switch (pair.ManipulatedVariable)
            {
                case "geometry.corner_radius_m": varText = $"{g.CornerRadiusM:0.00} m radius"; break;
                case "geometry.width_m": varText = $"{g.WidthM:0.0} m wide"; break;
                case "geometry.wall_bow":
                    var bow = g.WallBow?.Back ?? 0f;
                    varText = $"bow {bow:0.00} ({BowWord(bow)})";
                    break;
                default: varText = $"{g.CeilingHeightM:0.00} m ceiling"; break;
            }
            var metrics = RoomMetrics.Calculate(spec);
            GUI.Label(new Rect(rect.x + 15f, rect.y + 34f, rect.width - 30f, 24f),
                $"{varText}   ·   {g.WidthM:0.0} × {g.LengthM:0.0} m   ·   {metrics.VolumeM3:0.0} m³   ·   {spec.Lighting.ColorTemperatureK:0} K / {spec.Lighting.TargetLux:0} lux",
                labelStyle);
            var imageRect = new Rect(rect.x + 12f, rect.y + 65f, rect.width - 24f, rect.height - 77f);
            GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleAndCrop, false);
        }

        static void ResetBow(RoomSpec spec) => SetAllBow(spec, 0f);

        // Apply one bow amount to every wall WITHOUT openings (openings on bowed walls are
        // unsupported in v1 — the validator refuses the whole room, which froze the studio).
        // Walls carrying the door/windows stay straight; the rest curve together, and corners
        // stay shared. Resetting (amount 0) clears all four unconditionally.
        static void SetAllBow(RoomSpec spec, float amount)
        {
            spec.Geometry.WallBow ??= new WallBowSpec();
            var bow = spec.Geometry.WallBow;
            bow.Front = CanBow(spec, "front") ? amount : 0f;
            bow.Back = CanBow(spec, "back") ? amount : 0f;
            bow.Left = CanBow(spec, "left") ? amount : 0f;
            bow.Right = CanBow(spec, "right") ? amount : 0f;
        }

        static bool CanBow(RoomSpec spec, string wall) =>
            !spec.Openings.Any(o => string.Equals(o.Wall, wall, StringComparison.OrdinalIgnoreCase));

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

        /// <summary>
        /// Switch which single variable the pair manipulates: equalise EVERY manipulable field first
        /// (so no stale second difference survives), then open a sensible starting gap on the new one.
        /// </summary>
        void SelectManipulatedVariable(string path)
        {
            pair.Treatment.Geometry.CeilingHeightM = pair.Control.Geometry.CeilingHeightM;
            pair.Control.Geometry.CornerRadiusM = 0f;
            pair.Treatment.Geometry.CornerRadiusM = 0f;
            pair.Treatment.Geometry.WidthM = pair.Control.Geometry.WidthM;
            ResetBow(pair.Control);
            ResetBow(pair.Treatment);
            pair.ManipulatedVariable = path;

            switch (path)
            {
                case "geometry.corner_radius_m":
                    // Seed a MODEST, visible fillet — not the max safe radius. Max safe for a ~6 m
                    // room is ~3 m, which pulls the corner anchors to the centre and collapses the
                    // footprint into a near-circular blob (the "landscape" deformation). A gentle
                    // default rounds the corners noticeably while the room still reads as a room.
                    pair.Treatment.Geometry.CornerRadiusM =
                        Mathf.Min(0.6f, CalculateMaxSafeRadius(pair.Control));
                    break;
                case "geometry.width_m":
                    pair.Treatment.Geometry.WidthM =
                        Mathf.Min(pair.Control.Geometry.WidthM + 1f, MaxRoomWidth);
                    break;
                case "geometry.wall_bow":
                    SetAllBow(pair.Treatment, 1f); // start with a visible full convex bow on all walls
                    break;
                default:
                    pair.Treatment.Geometry.CeilingHeightM =
                        Mathf.Min(pair.Control.Geometry.CeilingHeightM + 0.8f, 3.8f);
                    break;
            }
            Rebuild();
        }

        // Control + treatment sliders for the active manipulated variable. `step` is the rounding
        // divisor (20 = 0.05m increments, 10 = 0.1m, 0.02 = 50K, 0.1 = 10lux).
        void DrawConditionSliders(string name, string fmt, string unit, float min, float max, float step,
            float control, float treatment, Action<float, float> apply)
        {
            GUILayout.Label($"Control {name}  {control.ToString(fmt)}{unit}", labelStyle);
            var nextControl = GUILayout.HorizontalSlider(control, min, max, GUILayout.Height(24f));
            GUILayout.Space(8f);
            GUILayout.Label($"Treatment {name}  {treatment.ToString(fmt)}{unit}", labelStyle);
            var nextTreatment = GUILayout.HorizontalSlider(treatment, min, max, GUILayout.Height(24f));

            if (!Mathf.Approximately(nextControl, control) || !Mathf.Approximately(nextTreatment, treatment))
            {
                apply(Mathf.Round(nextControl * step) / step, Mathf.Round(nextTreatment * step) / step);
                Rebuild();
            }
        }

        // One slider whose value is applied to BOTH conditions (a shared/nuisance parameter — it can
        // never create a pair difference, so the gate stays green by construction).
        void DrawSharedSlider(string name, string fmt, string unit, float min, float max, float step,
            float value, Action<float> apply)
        {
            GUILayout.Label($"{name}  {value.ToString(fmt)}{unit}", labelStyle);
            var next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(24f));
            if (!Mathf.Approximately(next, value))
            {
                apply(Mathf.Round(next * step) / step);
                Rebuild();
            }
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

        // Realism lab: same dining baseline but glazed on three walls (front/right/left), so the
        // SunSkySystem arc throws direct light patches at every hour and SSGI bounce + sky
        // through the openings can be judged at their strongest.
        void LoadRealismPair()
        {
            var source = Resources.Load<TextAsset>("RoomGen/Examples/realism-test-pair");
            if (source == null)
            {
                status = "realism-test-pair not found under Resources/RoomGen/Examples.";
                return;
            }
            pair = RoomJson.Deserialize<ConditionPairSpec>(source.text);
            Rebuild();
            status = "Realism test room loaded — try the Sun slider and walk it. " + status;
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
            id.Replace("builtin.", "").Replace("cc0.", "").Replace('-', ' ');

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
