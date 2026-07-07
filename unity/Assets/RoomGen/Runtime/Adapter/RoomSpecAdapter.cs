using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Adapter
{
    /// <summary>
    /// Translates a canonical Knowledge-Atlas RoomSpec (the frozen team contract:
    /// spec/room_spec.schema.json — shell.* / surfaces.* / openings.* / lighting.* / furniture[])
    /// into the generator's internal <see cref="RoomSpec"/>. This is the reconciliation between
    /// Diego's contract and Paco's generator (handoffs/UNITY_GENERATOR.md G2, mapping table).
    ///
    /// PART A (this file): shell / surfaces / openings / lighting / provenance + furniture ITEMS.
    /// PART B (TODO — handoffs/spec/PRESETS.md, to be done with Fable): a SlotResolver that turns
    /// Diego's NAMED furniture slots ("chair_3" = first chair on the table's front side) into
    /// explicit x/z/rotation via the preset's layout_slots. Until then furniture is mapped as items
    /// (asset + footprint + slot name) but left at the origin — the diff/contract mapping is complete,
    /// the placement is not.
    /// </summary>
    public static class RoomSpecAdapter
    {
        public const float WallThicknessM = 0.15f;
        public const float DoorTopM = 2.05f;
        public const float DefaultSillM = 0.95f;
        public const float DefaultHeadM = 1.95f;

        public sealed class AdaptResult
        {
            public RoomSpec Spec = new RoomSpec();
            public List<string> Warnings = new List<string>();
        }

        /// <summary>Adapt one canonical RoomSpec JSON string into the internal RoomSpec.</summary>
        public static AdaptResult Adapt(string canonicalJson)
        {
            var root = JObject.Parse(canonicalJson);
            var result = new AdaptResult();
            var spec = result.Spec;

            spec.SpecVersion = "1.0";
            spec.RoomId = Str(root, "room_type", "room");
            spec.Seed = root.Value<int?>("seed") ?? 0;

            AdaptGeometry(root, spec);
            AdaptSurfaces(root, spec, result);
            AdaptOpenings(root, spec);
            AdaptLighting(root, spec);
            AdaptFurniture(root, spec, result);
            AdaptProvenance(root, spec);
            return result;
        }

        /// <summary>Adapt a KA control+treatment pair into the internal ConditionPairSpec.</summary>
        public static ConditionPairSpec AdaptPair(string controlJson, string treatmentJson)
        {
            var control = Adapt(controlJson);
            var treatment = Adapt(treatmentJson);
            var experiment = JObject.Parse(controlJson)["experiment"] as JObject;
            return new ConditionPairSpec
            {
                PairId = experiment?.Value<string>("pair_id") ?? "adapted-pair",
                ManipulatedVariable = TranslateVariablePath(FirstDeclaredVariable(experiment)),
                Control = control.Spec,
                Treatment = treatment.Spec
            };
        }

        static void AdaptGeometry(JObject root, RoomSpec spec)
        {
            var shell = root["shell"] as JObject ?? new JObject();
            var g = spec.Geometry;
            g.WidthM = Flt(shell, "width_m", 4.5f);
            g.LengthM = Flt(shell, "length_m", 6.0f);
            g.CeilingHeightM = Flt(shell, "ceiling_height_m", 2.8f);
            g.WallThicknessM = WallThicknessM;
            // contour: angular -> square (radius 0); curved -> a conservative rounded-corner radius.
            // (Precise curved+opening interplay is refined later; the ceiling pair is angular.)
            g.CornerRadiusM = string.Equals(Str(shell, "contour", "angular"), "curved", StringComparison.OrdinalIgnoreCase)
                ? Mathf.Max(0f, Mathf.Min(g.WidthM, g.LengthM) * 0.5f - 0.1f)
                : 0f;
        }

        static void AdaptSurfaces(JObject root, RoomSpec spec, AdaptResult r)
        {
            var s = root["surfaces"] as JObject ?? new JObject();
            spec.Surfaces.WallMaterialId = MapMaterial(MaterialOf(s, "wall"), false, r);
            spec.Surfaces.FloorMaterialId = MapMaterial(MaterialOf(s, "floor"), false, r);
            spec.Surfaces.CeilingMaterialId = MapMaterial(MaterialOf(s, "ceiling"), true, r);
            spec.Surfaces.TextureScaleM = 1f;
            // KA surfaces carry tint_hex; the internal SurfaceSpec has no tint field (v1 limitation) -> dropped.
            if (HasTint(s))
                r.Warnings.Add("surfaces.tint_hex is not yet supported by the generator's surface contract (dropped).");
        }

        static string MaterialOf(JObject surfaces, string key) => (surfaces[key] as JObject)?.Value<string>("material");

        static bool HasTint(JObject surfaces)
        {
            foreach (var k in new[] { "wall", "floor", "ceiling" })
                if ((surfaces[k] as JObject)?["tint_hex"] != null) return true;
            return false;
        }

        static string MapMaterial(string material, bool isCeiling, AdaptResult r)
        {
            switch (material)
            {
                case "wood": return "builtin.oak";
                case "glass": return "builtin.glass";
                case "carpet": return "builtin.fabric-charcoal";
                case "metal": return "builtin.metal-black";
                case "plaster":
                case "paint":
                case null:
                    return isCeiling ? "builtin.ceiling-white" : "builtin.warm-white";
                default:
                    // brick / concrete / tile / marble: no dedicated builtin yet -> nearest neutral.
                    r.Warnings.Add($"material '{material}' has no dedicated builtin; using a neutral surface.");
                    return isCeiling ? "builtin.ceiling-white" : "builtin.warm-white";
            }
        }

        static void AdaptOpenings(JObject root, RoomSpec spec)
        {
            spec.Openings = new List<OpeningSpec>();
            var openings = root["openings"] as JObject;
            if (openings == null) return;
            var radius = spec.Geometry.CornerRadiusM;

            if (openings["door"] is JObject door)
            {
                var wall = Str(door, "wall", "front");
                spec.Openings.Add(new OpeningSpec
                {
                    OpeningId = "door",
                    Kind = "door",
                    Wall = wall,
                    CenterM = CenterM(Flt(door, "position", 0.5f), wall, spec.Geometry, radius),
                    WidthM = Flt(door, "width_m", 1.0f),
                    BottomM = 0f,
                    TopM = DoorTopM
                });
            }

            if (openings["windows"] is JArray windows)
            {
                for (var i = 0; i < windows.Count; i++)
                {
                    if (!(windows[i] is JObject w)) continue;
                    var wall = Str(w, "wall", "left");
                    spec.Openings.Add(new OpeningSpec
                    {
                        OpeningId = "window-" + i,
                        Kind = "window",
                        Wall = wall,
                        CenterM = CenterM(Flt(w, "position", 0.5f), wall, spec.Geometry, radius),
                        WidthM = Flt(w, "width_m", 1.1f),
                        BottomM = Flt(w, "sill_m", DefaultSillM),
                        TopM = Flt(w, "head_m", DefaultHeadM)
                    });
                }
            }
        }

        // KA opening position is a 0..1 slide along the wall; the generator wants metres from wall centre.
        static float CenterM(float position01, string wall, GeometrySpec g, float radius)
        {
            var wallLength = IsSideWall(wall) ? g.LengthM : g.WidthM;
            var usable = Mathf.Max(0.1f, wallLength - 2f * radius);
            return (position01 - 0.5f) * usable;
        }

        static bool IsSideWall(string wall) =>
            string.Equals(wall, "left", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wall, "right", StringComparison.OrdinalIgnoreCase);

        static void AdaptLighting(JObject root, RoomSpec spec)
        {
            var l = root["lighting"] as JObject ?? new JObject();
            var preset = Str(l, "preset", "neutral_daylight");
            var (lux, exposure) = LightingPreset(preset);
            var intensity = Mathf.Clamp(Flt(l, "intensity", 1.0f), 0f, 2f);
            var warmth = Mathf.Clamp01(Flt(l, "warmth", 0.5f));

            var lig = spec.Lighting;
            lig.PresetId = "builtin." + preset;
            lig.TargetLux = lux * intensity;
            lig.ToleranceLux = 35f;
            // Normative warmth law from the KA docs (RENDERING_RESEARCH §2 / ROOMSPEC_V1_1): K = 6500 - 3800*warmth.
            // NOTE: the KA schema labels warmth "0=warm, 1=cool", which is inverted relative to this formula
            // (warmth 0 -> 6500 K = a COOL colour). Flagged as a contract ambiguity for the team; the documented
            // formula is implemented here as written.
            lig.ColorTemperatureK = 6500f - 3800f * warmth;
            lig.BaseLuminousFluxLm = 3200f;
            lig.FixedExposureEv100 = exposure;
        }

        static (float lux, float exposure) LightingPreset(string preset)
        {
            switch (preset)
            {
                case "bright_office": return (750f, 8.5f);
                case "warm_evening": return (200f, 7.0f);
                case "cool_clinical": return (500f, 8.0f);
                case "dim": return (80f, 6.0f);
                default: return (500f, 8.0f); // neutral_daylight
            }
        }

        static readonly Dictionary<string, (string asset, float footprintX, float footprintZ)> Catalog =
            new Dictionary<string, (string, float, float)>
            {
                { "dining_table_6", ("builtin.dining-table", 2.0f, 1.0f) },
                { "dining_chair",   ("builtin.dining-chair", 0.5f, 0.5f) },
                { "sideboard",      ("builtin.sideboard",    1.6f, 0.5f) },
                { "pendant_light",  ("builtin.pendant-light", 0.3f, 0.3f) },
            };

        static void AdaptFurniture(JObject root, RoomSpec spec, AdaptResult r)
        {
            spec.Furniture = new List<FurniturePlacementSpec>();
            if (!(root["furniture"] is JArray arr)) return;

            var layout = SlotResolver.LayoutFor(Str(root, "room_type", "room"));
            if (layout == null)
                r.Warnings.Add($"room_type '{Str(root, "room_type", "?")}' has no slot layout; slot-placed furniture lands at the origin.");

            // Pass 1 — map items (asset + footprint + slot), honour explicit coordinates immediately.
            foreach (var token in arr)
            {
                if (!(token is JObject f)) continue;
                var catalogId = f.Value<string>("catalog_id") ?? "";
                var placement = f["placement"] as JObject;

                var asset = catalogId;
                float footprintX = 0.6f, footprintZ = 0.6f;
                if (Catalog.TryGetValue(catalogId, out var entry))
                {
                    asset = entry.asset; footprintX = entry.footprintX; footprintZ = entry.footprintZ;
                }
                else
                {
                    r.Warnings.Add($"furniture catalog_id '{catalogId}' is not in the dining catalog; passed through unmapped.");
                }

                var item = new FurniturePlacementSpec
                {
                    AssetId = asset,
                    SlotId = placement?.Value<string>("slot") ?? "explicit",
                    FootprintM = new Vec2(footprintX, footprintZ),
                    PositionM = new Vec3(0f, 0f, 0f),
                    RotationYDeg = 0f
                };

                // KA also allows explicit-coordinate placement ({x_m, z_m, rotation_deg}).
                if (placement?["x_m"] != null)
                {
                    item.SlotId = "explicit";
                    item.PositionM = new Vec3(Flt(placement, "x_m", 0f), 0f, Flt(placement, "z_m", 0f));
                    item.RotationYDeg = Flt(placement, "rotation_deg", 0f);
                }

                spec.Furniture.Add(item);
            }

            if (layout == null) return;

            // Pass 2 — resolve ABSOLUTE and WALL slots (these can anchor others).
            var resolved = new Dictionary<string, (SlotResolver.ResolvedSlot pos, Vec2 footprint)>();
            foreach (var item in spec.Furniture)
            {
                if (item.SlotId == "explicit") continue;
                if (!layout.TryGetValue(item.SlotId, out var def) || def.Kind == SlotResolver.SlotKind.Relative)
                    continue;
                var slot = SlotResolver.Resolve(item.SlotId, item.FootprintM, layout, spec.Geometry, null, r.Warnings);
                Apply(item, slot);
                resolved[item.SlotId] = (slot, item.FootprintM);
            }

            // Pass 3 — resolve RELATIVE slots against the placed anchors.
            foreach (var item in spec.Furniture)
            {
                if (item.SlotId == "explicit") continue;
                if (!layout.TryGetValue(item.SlotId, out var def) || def.Kind != SlotResolver.SlotKind.Relative)
                {
                    if (!layout.ContainsKey(item.SlotId))
                        SlotResolver.Resolve(item.SlotId, item.FootprintM, layout, spec.Geometry, null, r.Warnings); // emits the warning
                    continue;
                }
                var slot = SlotResolver.Resolve(
                    item.SlotId, item.FootprintM, layout, spec.Geometry,
                    anchorId => resolved.TryGetValue(anchorId, out var a) ? a : ((SlotResolver.ResolvedSlot, Vec2)?)null,
                    r.Warnings);
                Apply(item, slot);
            }
        }

        static void Apply(FurniturePlacementSpec item, SlotResolver.ResolvedSlot slot)
        {
            item.PositionM = new Vec3(slot.X, slot.Y, slot.Z);
            item.RotationYDeg = slot.RotationYDeg;
            item.CeilingMounted = slot.CeilingMounted;
        }

        static void AdaptProvenance(JObject root, RoomSpec spec)
        {
            var p = root["provenance"] as JObject;
            spec.Provenance.PresetId = Str(root, "room_type", "room") + "-v1";
            spec.Provenance.GeneratorVersion = "1.0.0";
            spec.Provenance.Source = "ka-adapter:" + (p?.Value<string>("generated_by") ?? "unknown");
        }

        // KA declares manipulated variables as shell.* / surfaces.* / lighting.*; the generator's
        // studio addresses shell fields under geometry.*. Translate so the pair's declared variable lines up.
        static string TranslateVariablePath(string kaPath)
        {
            if (string.IsNullOrEmpty(kaPath)) return "geometry.ceiling_height_m";
            return kaPath
                .Replace("shell.ceiling_height_m", "geometry.ceiling_height_m")
                .Replace("shell.width_m", "geometry.width_m")
                .Replace("shell.length_m", "geometry.length_m")
                .Replace("shell.contour", "geometry.corner_radius_m");
        }

        static string FirstDeclaredVariable(JObject experiment)
        {
            if (experiment?["manipulated_variables"] is JArray arr && arr.Count > 0)
                return arr[0].Value<string>();
            return null;
        }

        static string Str(JObject o, string key, string fallback) => o?.Value<string>(key) ?? fallback;

        static float Flt(JObject o, string key, float fallback)
        {
            var t = o?[key];
            return t != null && t.Type != JTokenType.Null ? t.Value<float>() : fallback;
        }
    }
}
