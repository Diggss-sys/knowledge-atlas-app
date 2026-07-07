using System;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Metrics
{
    public static class RoomMetrics
    {
        public static RoomMetricsRecord Calculate(RoomSpec spec)
        {
            var g = spec.Geometry;
            var radius = Mathf.Clamp(g.CornerRadiusM, 0f, Mathf.Min(g.WidthM, g.LengthM) * 0.5f);
            var area = g.WidthM * g.LengthM - (4f - Mathf.PI) * radius * radius;
            var perimeter = radius <= 0f
                ? 2f * (g.WidthM + g.LengthM)
                : 2f * (g.WidthM + g.LengthM - 4f * radius) + 2f * Mathf.PI * radius;
            var grossWallArea = perimeter * g.CeilingHeightM;
            var windowArea = 0f;
            var allOpeningArea = 0f;

            foreach (var opening in spec.Openings)
            {
                var openingArea = Mathf.Max(0f, opening.WidthM) *
                                  Mathf.Max(0f, opening.TopM - opening.BottomM);
                allOpeningArea += openingArea;
                if (string.Equals(opening.Kind, "window", StringComparison.OrdinalIgnoreCase))
                    windowArea += openingArea;
            }

            return new RoomMetricsRecord
            {
                FloorAreaM2 = area,
                VolumeM3 = area * g.CeilingHeightM,
                PerimeterM = perimeter,
                GrossWallAreaM2 = grossWallArea,
                NetWallAreaM2 = Mathf.Max(0f, grossWallArea - allOpeningArea),
                WindowAreaM2 = windowArea,
                WindowToWallRatio = grossWallArea > 0f ? windowArea / grossWallArea : 0f,
                MinimumFurnitureClearanceM = MinimumFurnitureClearance(spec)
            };
        }

        static float MinimumFurnitureClearance(RoomSpec spec)
        {
            var minimum = float.PositiveInfinity;
            var halfWidth = spec.Geometry.WidthM * 0.5f;
            var halfLength = spec.Geometry.LengthM * 0.5f;

            for (var i = 0; i < spec.Furniture.Count; i++)
            {
                var item = spec.Furniture[i];
                var wallClearance = Mathf.Min(
                    halfWidth - Mathf.Abs(item.PositionM.X) - item.FootprintM.X * 0.5f,
                    halfLength - Mathf.Abs(item.PositionM.Z) - item.FootprintM.Y * 0.5f);
                minimum = Mathf.Min(minimum, wallClearance);

                for (var j = i + 1; j < spec.Furniture.Count; j++)
                {
                    var other = spec.Furniture[j];
                    var dx = Mathf.Abs(item.PositionM.X - other.PositionM.X) -
                             (item.FootprintM.X + other.FootprintM.X) * 0.5f;
                    var dz = Mathf.Abs(item.PositionM.Z - other.PositionM.Z) -
                             (item.FootprintM.Y + other.FootprintM.Y) * 0.5f;
                    minimum = Mathf.Min(minimum, Mathf.Max(dx, dz));
                }
            }

            return float.IsPositiveInfinity(minimum) ? 0f : minimum;
        }
    }
}
