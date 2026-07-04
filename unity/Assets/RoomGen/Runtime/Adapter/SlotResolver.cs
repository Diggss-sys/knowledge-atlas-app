using System;
using System.Collections.Generic;
using RoomGen.Contracts;

namespace RoomGen.Adapter
{
    /// <summary>
    /// G2 Part B — resolves the KA preset's NAMED layout slots into explicit positions/rotations,
    /// implementing the normative semantics in spec/PRESETS.md:
    ///
    ///  · Coordinate frame: origin = room centre at floor level, +x = right, +z = front/door wall.
    ///  · Absolute slots  {x_m, z_m, rotation_deg?, ceiling?}: place verbatim; ceiling:true mounts
    ///    the item's origin at ceiling height (the item hangs downward from its origin).
    ///  · Relative slots  {relative_to, side, index?}: the item's centre sits
    ///    (anchor_half_extent + CLEARANCE + item_half_extent) from the anchor centre along the side's
    ///    axis. All slots sharing (relative_to, side) form an ordered group; item k of n centres at
    ///    fraction (k+1)/(n+1) of the anchor footprint's extent along that side, measured from the
    ///    side's negative end. Group size n counts the SLOTS DEFINED IN THE LAYOUT, not the items
    ///    placed — so omitting chair_4 never shifts chair_3 (single-variable discipline: removing one
    ///    item must not move the others).
    ///  · Rotation faces the anchor: front→180°, back→0°, left→90°, right→−90°
    ///    (0° = facing +z, positive = clockwise about +y — Unity's yaw).
    ///  · Wall slots {wall, offset_m}: centred on the named wall, the item's back face offset_m from
    ///    the interior wall face, rotated to face into the room.
    /// </summary>
    public static class SlotResolver
    {
        public const float ClearanceM = 0.05f;

        public enum SlotKind { Absolute, Relative, Wall }

        public sealed class SlotDef
        {
            public SlotKind Kind;
            public float XM, ZM, RotationDeg;   // Absolute
            public bool Ceiling;                 // Absolute (ceiling-mounted)
            public string RelativeTo, Side;      // Relative
            public int Index;                    // Relative (default 0)
            public string Wall;                  // Wall
            public float OffsetM;                // Wall
        }

        public sealed class ResolvedSlot
        {
            public float X, Y, Z, RotationYDeg;
            public bool CeilingMounted;
        }

        /// <summary>The dining_room preset's layout_slots (spec/presets/dining_room.preset.json).</summary>
        public static readonly IReadOnlyDictionary<string, SlotDef> DiningRoomLayout =
            new Dictionary<string, SlotDef>(StringComparer.Ordinal)
            {
                { "table_center",   new SlotDef { Kind = SlotKind.Absolute, XM = 0f, ZM = 0f, RotationDeg = 0f } },
                { "above_table",    new SlotDef { Kind = SlotKind.Absolute, XM = 0f, ZM = 0f, Ceiling = true } },
                { "chair_1",        new SlotDef { Kind = SlotKind.Relative, RelativeTo = "table_center", Side = "back",  Index = 0 } },
                { "chair_2",        new SlotDef { Kind = SlotKind.Relative, RelativeTo = "table_center", Side = "back",  Index = 1 } },
                { "chair_3",        new SlotDef { Kind = SlotKind.Relative, RelativeTo = "table_center", Side = "front", Index = 0 } },
                { "chair_4",        new SlotDef { Kind = SlotKind.Relative, RelativeTo = "table_center", Side = "front", Index = 1 } },
                { "chair_5",        new SlotDef { Kind = SlotKind.Relative, RelativeTo = "table_center", Side = "left",  Index = 0 } },
                { "chair_6",        new SlotDef { Kind = SlotKind.Relative, RelativeTo = "table_center", Side = "right", Index = 0 } },
                { "long_wall_back", new SlotDef { Kind = SlotKind.Wall, Wall = "back", OffsetM = 0.3f } },
            };

        public static IReadOnlyDictionary<string, SlotDef> LayoutFor(string roomType) =>
            roomType == "dining_room" ? DiningRoomLayout : null;

        /// <summary>
        /// Resolve one named slot. <paramref name="anchorLookup"/> supplies the resolved position and
        /// footprint of an anchor slot (the item placed in it), for Relative slots.
        /// </summary>
        public static ResolvedSlot Resolve(
            string slotId,
            Vec2 itemFootprint,
            IReadOnlyDictionary<string, SlotDef> layout,
            GeometrySpec geometry,
            Func<string, (ResolvedSlot pos, Vec2 footprint)?> anchorLookup,
            List<string> warnings)
        {
            if (layout == null || !layout.TryGetValue(slotId, out var def))
            {
                warnings?.Add($"slot '{slotId}' is not defined in the preset layout; placed at origin.");
                return new ResolvedSlot();
            }

            switch (def.Kind)
            {
                case SlotKind.Absolute:
                    // Ceiling slots set the MOUNT FLAG rather than baking the ceiling height into Y:
                    // the builder derives the actual height, so pair diffs stay clean when the
                    // ceiling is the manipulated variable.
                    return new ResolvedSlot
                    {
                        X = def.XM,
                        Z = def.ZM,
                        Y = 0f,
                        RotationYDeg = def.RotationDeg,
                        CeilingMounted = def.Ceiling
                    };

                case SlotKind.Wall:
                    return ResolveWall(def, itemFootprint, geometry);

                case SlotKind.Relative:
                    return ResolveRelative(slotId, def, itemFootprint, layout, anchorLookup, warnings);

                default:
                    return new ResolvedSlot();
            }
        }

        static ResolvedSlot ResolveWall(SlotDef def, Vec2 fp, GeometrySpec g)
        {
            var halfW = g.WidthM * 0.5f;
            var halfL = g.LengthM * 0.5f;
            var depth = fp.Y;
            var result = new ResolvedSlot();
            switch (def.Wall)
            {
                case "back":   // wall at -z; face into the room (+z) -> rotation 0
                    result.Z = -halfL + def.OffsetM + depth * 0.5f;
                    result.RotationYDeg = 0f;
                    break;
                case "front":  // wall at +z; face -z -> rotation 180
                    result.Z = halfL - def.OffsetM - depth * 0.5f;
                    result.RotationYDeg = 180f;
                    break;
                case "left":   // wall at -x; face +x -> rotation 90
                    result.X = -halfW + def.OffsetM + depth * 0.5f;
                    result.RotationYDeg = 90f;
                    break;
                case "right":  // wall at +x; face -x -> rotation -90
                    result.X = halfW - def.OffsetM - depth * 0.5f;
                    result.RotationYDeg = -90f;
                    break;
            }
            return result;
        }

        static ResolvedSlot ResolveRelative(
            string slotId,
            SlotDef def,
            Vec2 itemFp,
            IReadOnlyDictionary<string, SlotDef> layout,
            Func<string, (ResolvedSlot pos, Vec2 footprint)?> anchorLookup,
            List<string> warnings)
        {
            var anchor = anchorLookup?.Invoke(def.RelativeTo);
            if (anchor == null)
            {
                warnings?.Add($"slot '{slotId}' is relative to '{def.RelativeTo}', which has no placed item; placed at origin.");
                return new ResolvedSlot();
            }
            var (anchorPos, anchorFp) = anchor.Value;

            // Group size n = slots DEFINED in the layout with the same (relative_to, side).
            var n = 0;
            foreach (var kv in layout)
                if (kv.Value.Kind == SlotKind.Relative &&
                    kv.Value.RelativeTo == def.RelativeTo && kv.Value.Side == def.Side)
                    n++;
            if (n == 0) n = 1;

            var fraction = (def.Index + 1f) / (n + 1f);       // (k+1)/(n+1), from the side's negative end
            var result = new ResolvedSlot();

            switch (def.Side)
            {
                case "front": // +z of the anchor; distribute along the anchor's width (x)
                    result.Z = anchorPos.Z + anchorFp.Y * 0.5f + ClearanceM + itemFp.Y * 0.5f;
                    result.X = anchorPos.X + (fraction - 0.5f) * anchorFp.X;
                    result.RotationYDeg = 180f;               // face the anchor (-z)
                    break;
                case "back":  // -z of the anchor
                    result.Z = anchorPos.Z - anchorFp.Y * 0.5f - ClearanceM - itemFp.Y * 0.5f;
                    result.X = anchorPos.X + (fraction - 0.5f) * anchorFp.X;
                    result.RotationYDeg = 0f;                 // face the anchor (+z)
                    break;
                case "left":  // -x of the anchor; distribute along the anchor's depth (z)
                    result.X = anchorPos.X - anchorFp.X * 0.5f - ClearanceM - itemFp.X * 0.5f;
                    result.Z = anchorPos.Z + (fraction - 0.5f) * anchorFp.Y;
                    result.RotationYDeg = 90f;                // face the anchor (+x)
                    break;
                case "right": // +x of the anchor
                    result.X = anchorPos.X + anchorFp.X * 0.5f + ClearanceM + itemFp.X * 0.5f;
                    result.Z = anchorPos.Z + (fraction - 0.5f) * anchorFp.Y;
                    result.RotationYDeg = -90f;               // face the anchor (-x)
                    break;
                default:
                    warnings?.Add($"slot '{slotId}' has unknown side '{def.Side}'; placed at the anchor.");
                    result.X = anchorPos.X;
                    result.Z = anchorPos.Z;
                    break;
            }
            return result;
        }
    }
}
