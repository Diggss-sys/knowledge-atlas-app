using System;
using System.Collections.Generic;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Validation
{
    public static class RoomSpecValidator
    {
        static readonly HashSet<string> Walls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "front", "back", "left", "right"
        };

        // Corner radius may not exceed this fraction of the shorter room span, or the footprint
        // collapses toward a circle ("blob"). Tunable knob for the realism/study bar.
        const float MaxCornerRadiusFraction = 0.25f;

        // The legal geometry envelope. PUBLIC so authoring UIs can bound their sliders to exactly
        // what the validator accepts instead of keeping a second copy of the numbers — the studio's
        // sliders used their own constants, so you could drag to a value the engine then refused,
        // which is what made the controls feel like they "broke the app".
        public const float MinWidthM = 3f;
        public const float MaxWidthM = 12f;
        public const float MinLengthM = 3f;
        public const float MaxLengthM = 12f;
        public const float MinCeilingHeightM = 2.1f;
        public const float MaxCeilingHeightM = 6f;

        /// <summary>Largest corner radius this spec may legally use (geometric AND blob limits).</summary>
        public static float MaxCornerRadiusM(GeometrySpec geometry)
        {
            var shortSpan = Mathf.Min(geometry.WidthM, geometry.LengthM);
            return Mathf.Max(0f, Mathf.Min(shortSpan * 0.5f - 0.1f, shortSpan * MaxCornerRadiusFraction));
        }

        public static List<ValidationIssue> Validate(RoomSpec spec, DiningRoomPreset preset = null)
        {
            var issues = new List<ValidationIssue>();
            if (spec == null)
            {
                issues.Add(Error("SPEC_NULL", "", "Room specification is missing."));
                return issues;
            }
            if (spec.Geometry == null || spec.Surfaces == null || spec.Lighting == null ||
                spec.Openings == null || spec.Furniture == null || spec.Provenance == null)
            {
                issues.Add(Error("SPEC_SECTION", "",
                    "Geometry, surfaces, openings, lighting, furniture, and provenance are required."));
                return issues;
            }

            if (spec.SpecVersion != RoomSpec.CurrentVersion)
                issues.Add(Error("SPEC_VERSION", "spec_version", $"Expected {RoomSpec.CurrentVersion}."));

            CheckRange(issues, "geometry.width_m", spec.Geometry.WidthM, MinWidthM, MaxWidthM);
            CheckRange(issues, "geometry.length_m", spec.Geometry.LengthM, MinLengthM, MaxLengthM);
            CheckRange(issues, "geometry.ceiling_height_m", spec.Geometry.CeilingHeightM,
                MinCeilingHeightM, MaxCeilingHeightM);
            CheckRange(issues, "geometry.wall_thickness_m", spec.Geometry.WallThicknessM, 0.08f, 0.4f);

            // Two limits, whichever binds: the GEOMETRIC one (opposite corner arcs must not meet) and
            // the PERCEPTUAL one (past ~1/4 of the shorter span the footprint collapses toward a
            // circle — a "blob" — and stops reading as a room with flat walls). Enforced here so
            // EVERY producer inherits it: the operator UI and the AI copilot, not just the legacy
            // studio that happened to seed a safe value.
            CheckRange(issues, "geometry.corner_radius_m", spec.Geometry.CornerRadiusM,
                0f, MaxCornerRadiusM(spec.Geometry));

            if (spec.Geometry.WallBow != null)
            {
                CheckRange(issues, "geometry.wall_bow.front", spec.Geometry.WallBow.Front, -1f, 1f);
                CheckRange(issues, "geometry.wall_bow.back", spec.Geometry.WallBow.Back, -1f, 1f);
                CheckRange(issues, "geometry.wall_bow.left", spec.Geometry.WallBow.Left, -1f, 1f);
                CheckRange(issues, "geometry.wall_bow.right", spec.Geometry.WallBow.Right, -1f, 1f);
                // A concave bow eats interior space: cap the sagitta well below half the room span
                // so opposite walls can never meet.
                var maxSagitta = Mathf.Min(spec.Geometry.WidthM, spec.Geometry.LengthM) * 0.25f;
                CheckRange(issues, "geometry.bow_max_m", spec.Geometry.BowMaxM, 0f, maxSagitta);
            }

            if (preset != null)
            {
                CheckPresetRange(issues, "geometry.width_m", spec.Geometry.WidthM, preset.WidthRangeM);
                CheckPresetRange(issues, "geometry.length_m", spec.Geometry.LengthM, preset.LengthRangeM);
                CheckPresetRange(issues, "geometry.ceiling_height_m", spec.Geometry.CeilingHeightM, preset.CeilingHeightRangeM);
                CheckPresetRange(issues, "geometry.corner_radius_m", spec.Geometry.CornerRadiusM, preset.CornerRadiusRangeM);
                CheckCatalog(issues, "surfaces.floor_material_id", spec.Surfaces.FloorMaterialId, preset.MaterialIds);
                CheckCatalog(issues, "surfaces.wall_material_id", spec.Surfaces.WallMaterialId, preset.MaterialIds);
                CheckCatalog(issues, "surfaces.ceiling_material_id", spec.Surfaces.CeilingMaterialId, preset.MaterialIds);
                CheckCatalog(issues, "lighting.preset_id", spec.Lighting.PresetId, preset.LightingPresetIds);
                foreach (var requiredSlot in preset.RequiredSlots)
                    if (!spec.Furniture.Exists(item => SlotIdsMatch(item.SlotId, requiredSlot)))
                        issues.Add(Error("REQUIRED_SLOT", "furniture",
                            $"Required furniture slot '{requiredSlot}' is missing."));
            }

            for (var i = 0; i < spec.Openings.Count; i++)
                ValidateOpening(spec, spec.Openings[i], i, issues);

            ValidateOpeningOverlap(spec, issues);
            ValidateFurniture(spec, issues);

            if (spec.Lighting.TargetLux <= 0f)
                issues.Add(Error("LIGHT_TARGET", "lighting.target_lux", "Target illuminance must be positive."));
            if (spec.Lighting.ToleranceLux <= 0f)
                issues.Add(Error("LIGHT_TOLERANCE", "lighting.tolerance_lux", "Lighting tolerance must be positive."));

            return issues;
        }

        static void ValidateOpening(RoomSpec room, OpeningSpec opening, int index, List<ValidationIssue> issues)
        {
            var path = $"openings[{index}]";
            if (!Walls.Contains(opening.Wall))
                issues.Add(Error("OPENING_WALL", path + ".wall", "Wall must be front, back, left, or right."));
            if (opening.WidthM <= 0.2f)
                issues.Add(Error("OPENING_WIDTH", path + ".width_m", "Opening width is too small."));
            if (opening.BottomM < 0f || opening.TopM <= opening.BottomM ||
                opening.TopM > room.Geometry.CeilingHeightM)
                issues.Add(Error("OPENING_HEIGHT", path, "Opening vertical bounds must remain inside the wall."));

            var wallLength = IsSideWall(opening.Wall) ? room.Geometry.LengthM : room.Geometry.WidthM;
            var straightLength = Mathf.Max(0f, wallLength - 2f * room.Geometry.CornerRadiusM);
            if (Mathf.Abs(opening.CenterM) + opening.WidthM * 0.5f > straightLength * 0.5f - 0.05f)
                issues.Add(Error("OPENING_BOUNDS", path, "Opening must remain inside a straight wall section."));

            // Openings on bowed walls are supported: ShellGenerator cuts sill/header/segment
            // bands along the arc and OpeningGenerator builds curved glass + trim (v1.1).
        }

        static void ValidateOpeningOverlap(RoomSpec room, List<ValidationIssue> issues)
        {
            for (var i = 0; i < room.Openings.Count; i++)
            for (var j = i + 1; j < room.Openings.Count; j++)
            {
                var a = room.Openings[i];
                var b = room.Openings[j];
                if (!string.Equals(a.Wall, b.Wall, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Mathf.Abs(a.CenterM - b.CenterM) < (a.WidthM + b.WidthM) * 0.5f + 0.05f)
                    issues.Add(Error("OPENING_OVERLAP", $"openings[{i}]",
                        $"Opening overlaps openings[{j}]."));
            }
        }

        static void ValidateFurniture(RoomSpec room, List<ValidationIssue> issues)
        {
            var slots = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < room.Furniture.Count; i++)
            {
                var item = room.Furniture[i];
                if (string.IsNullOrWhiteSpace(item.AssetId))
                    issues.Add(Error("ASSET_ID", $"furniture[{i}].asset_id", "Furniture asset ID is required."));
                else if (!item.AssetId.StartsWith("builtin.", StringComparison.Ordinal) &&
                         Resources.Load<GameObject>("RoomGen/Furniture/" + item.AssetId.Replace('.', '-')) == null)
                    issues.Add(Error("ASSET_MISSING", $"furniture[{i}].asset_id",
                        $"Furniture asset '{item.AssetId}' is not installed."));
                if (string.IsNullOrWhiteSpace(item.SlotId))
                    issues.Add(Error("SLOT_ID", $"furniture[{i}].slot_id", "Furniture slot ID is required."));
                else if (!slots.Add(item.SlotId))
                    issues.Add(Error("SLOT_DUPLICATE", $"furniture[{i}].slot_id",
                        $"Furniture slot '{item.SlotId}' is used more than once."));
                if (item.FootprintM.X <= 0f || item.FootprintM.Y <= 0f)
                    issues.Add(Error("FOOTPRINT", $"furniture[{i}].footprint_m", "Furniture footprint must be positive."));

                var halfWidth = room.Geometry.WidthM * 0.5f - room.Geometry.WallThicknessM;
                var halfLength = room.Geometry.LengthM * 0.5f - room.Geometry.WallThicknessM;
                if (Mathf.Abs(item.PositionM.X) + item.FootprintM.X * 0.5f > halfWidth ||
                    Mathf.Abs(item.PositionM.Z) + item.FootprintM.Y * 0.5f > halfLength)
                    issues.Add(Error("FURNITURE_WALL_COLLISION", $"furniture[{i}]",
                        "Furniture footprint crosses the room boundary."));
                else if (!FootprintInsideRoundedRoom(room, item))
                    issues.Add(Error("FURNITURE_CURVE_COLLISION", $"furniture[{i}]",
                        "Furniture footprint crosses a curved corner."));

                foreach (var opening in room.Openings)
                    if (!item.CeilingMounted && BlocksOpening(room, item, opening))
                        issues.Add(Error("FURNITURE_OPENING_COLLISION", $"furniture[{i}]",
                            $"Furniture blocks opening '{opening.OpeningId}'."));

                for (var j = i + 1; j < room.Furniture.Count; j++)
                {
                    var other = room.Furniture[j];
                    // Footprints only collide within the same plane: a ceiling-mounted pendant over
                    // the table is correct, not a collision.
                    if (item.CeilingMounted != other.CeilingMounted)
                        continue;
                    var overlapsX = Mathf.Abs(item.PositionM.X - other.PositionM.X) <
                                    (item.FootprintM.X + other.FootprintM.X) * 0.5f;
                    var overlapsZ = Mathf.Abs(item.PositionM.Z - other.PositionM.Z) <
                                    (item.FootprintM.Y + other.FootprintM.Y) * 0.5f;
                    if (overlapsX && overlapsZ)
                        issues.Add(Error("FURNITURE_COLLISION", $"furniture[{i}]",
                            $"Furniture footprint overlaps furniture[{j}]."));
                }
            }
        }

        static bool FootprintInsideRoundedRoom(RoomSpec room, FurniturePlacementSpec item)
        {
            var radius = room.Geometry.CornerRadiusM;
            if (radius <= 0f) return true;
            var halfWidth = room.Geometry.WidthM * 0.5f;
            var halfLength = room.Geometry.LengthM * 0.5f;
            foreach (var x in new[]
                     {
                         item.PositionM.X - item.FootprintM.X * 0.5f,
                         item.PositionM.X + item.FootprintM.X * 0.5f
                     })
            foreach (var z in new[]
                     {
                         item.PositionM.Z - item.FootprintM.Y * 0.5f,
                         item.PositionM.Z + item.FootprintM.Y * 0.5f
                     })
            {
                var dx = Mathf.Max(0f, Mathf.Abs(x) - (halfWidth - radius));
                var dz = Mathf.Max(0f, Mathf.Abs(z) - (halfLength - radius));
                if (dx * dx + dz * dz > radius * radius)
                    return false;
            }
            return true;
        }

        static bool BlocksOpening(RoomSpec room, FurniturePlacementSpec item, OpeningSpec opening)
        {
            var door = string.Equals(opening.Kind, "door", StringComparison.OrdinalIgnoreCase);
            var clearance = door ? 0.9f : 0.25f;
            var halfWidth = room.Geometry.WidthM * 0.5f;
            var halfLength = room.Geometry.LengthM * 0.5f;
            var itemMinX = item.PositionM.X - item.FootprintM.X * 0.5f;
            var itemMaxX = item.PositionM.X + item.FootprintM.X * 0.5f;
            var itemMinZ = item.PositionM.Z - item.FootprintM.Y * 0.5f;
            var itemMaxZ = item.PositionM.Z + item.FootprintM.Y * 0.5f;
            var openingMin = opening.CenterM - opening.WidthM * 0.5f;
            var openingMax = opening.CenterM + opening.WidthM * 0.5f;

            switch (opening.Wall.ToLowerInvariant())
            {
                case "front":
                    return halfLength - itemMaxZ < clearance &&
                           itemMaxX > openingMin && itemMinX < openingMax;
                case "back":
                    return itemMinZ + halfLength < clearance &&
                           itemMaxX > openingMin && itemMinX < openingMax;
                case "left":
                    return itemMinX + halfWidth < clearance &&
                           itemMaxZ > openingMin && itemMinZ < openingMax;
                case "right":
                    return halfWidth - itemMaxX < clearance &&
                           itemMaxZ > openingMin && itemMinZ < openingMax;
                default:
                    return false;
            }
        }

        static bool IsSideWall(string wall) =>
            string.Equals(wall, "left", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wall, "right", StringComparison.OrdinalIgnoreCase);

        // Slot ids appear in two dialects: the legacy internal "table-center" and the canonical
        // Knowledge-Atlas "table_center". Compare them dash/underscore-insensitively.
        static bool SlotIdsMatch(string a, string b) =>
            string.Equals(a?.Replace('-', '_'), b?.Replace('-', '_'), StringComparison.OrdinalIgnoreCase);

        static void CheckRange(List<ValidationIssue> issues, string path, float value, float min, float max)
        {
            if (value < min || value > max)
                issues.Add(Error("RANGE", path, $"Value must be between {min:0.##} and {max:0.##}."));
        }

        static void CheckPresetRange(List<ValidationIssue> issues, string path, float value, RangeSpec range)
        {
            if (range != null && !range.Contains(value))
                issues.Add(Error("PRESET_RANGE", path,
                    $"Value must be between {range.Min:0.##} and {range.Max:0.##} for this preset."));
        }

        static void CheckCatalog(List<ValidationIssue> issues, string path, string id, List<string> allowed)
        {
            if (allowed != null && allowed.Count > 0 && !allowed.Contains(id))
                issues.Add(Error("CATALOG_ID", path, $"'{id}' is not approved by this preset."));
        }

        static ValidationIssue Error(string code, string path, string message) =>
            new ValidationIssue("error", code, path, message);
    }
}
