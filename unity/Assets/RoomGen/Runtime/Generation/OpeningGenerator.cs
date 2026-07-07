using System;
using System.Collections.Generic;
using System.Linq;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Generation
{
    public static class OpeningGenerator
    {
        public static List<OpeningSpec> ForWall(RoomSpec spec, string wall) =>
            spec.Openings
                .Where(opening => string.Equals(opening.Wall, wall, StringComparison.OrdinalIgnoreCase))
                .OrderBy(opening => opening.CenterM)
                .ToList();

        public static void BuildInserts(
            RoomSpec spec,
            Transform parent,
            SurfaceResolver surfaces,
            int layer)
        {
            var trimMaterial = surfaces.Resolve("builtin.walnut");
            var glassMaterial = surfaces.Resolve("builtin.glass");
            foreach (var opening in spec.Openings)
            {
                var root = new GameObject("Opening " + opening.OpeningId);
                root.layer = layer;
                root.transform.SetParent(parent, false);
                var side = IsSideWall(opening.Wall);
                var center = OpeningPosition(spec, opening, -0.01f);
                var verticalCenter = (opening.BottomM + opening.TopM) * 0.5f;
                center.y = verticalCenter;

                if (string.Equals(opening.Kind, "window", StringComparison.OrdinalIgnoreCase))
                {
                    var glassSize = side
                        ? new Vector3(0.025f, opening.TopM - opening.BottomM, opening.WidthM)
                        : new Vector3(opening.WidthM, opening.TopM - opening.BottomM, 0.025f);
                    GenerationUtil.CreateBox("Glass", root.transform, glassSize, center, glassMaterial, layer);
                    AddTrim(opening, spec, root.transform, trimMaterial, layer);
                }
                else
                {
                    AddTrim(opening, spec, root.transform, trimMaterial, layer, false);
                }
            }
        }

        static void AddTrim(
            OpeningSpec opening,
            RoomSpec spec,
            Transform parent,
            Material material,
            int layer,
            bool includeSill = true)
        {
            const float trim = 0.07f;
            const float depth = 0.06f;
            var side = IsSideWall(opening.Wall);
            var vertical = opening.TopM - opening.BottomM;
            var center = OpeningPosition(spec, opening, -0.045f);

            if (side)
            {
                GenerationUtil.CreateBox("Trim A", parent, new Vector3(depth, vertical + trim, trim),
                    center + new Vector3(0f, (opening.BottomM + opening.TopM) * 0.5f, -opening.WidthM * 0.5f), material, layer);
                GenerationUtil.CreateBox("Trim B", parent, new Vector3(depth, vertical + trim, trim),
                    center + new Vector3(0f, (opening.BottomM + opening.TopM) * 0.5f, opening.WidthM * 0.5f), material, layer);
                GenerationUtil.CreateBox("Trim Top", parent, new Vector3(depth, trim, opening.WidthM + trim),
                    center + new Vector3(0f, opening.TopM, 0f), material, layer);
                if (includeSill)
                    GenerationUtil.CreateBox("Trim Bottom", parent, new Vector3(depth, trim, opening.WidthM + trim),
                        center + new Vector3(0f, opening.BottomM, 0f), material, layer);
            }
            else
            {
                GenerationUtil.CreateBox("Trim A", parent, new Vector3(trim, vertical + trim, depth),
                    center + new Vector3(-opening.WidthM * 0.5f, (opening.BottomM + opening.TopM) * 0.5f, 0f), material, layer);
                GenerationUtil.CreateBox("Trim B", parent, new Vector3(trim, vertical + trim, depth),
                    center + new Vector3(opening.WidthM * 0.5f, (opening.BottomM + opening.TopM) * 0.5f, 0f), material, layer);
                GenerationUtil.CreateBox("Trim Top", parent, new Vector3(opening.WidthM + trim, trim, depth),
                    center + new Vector3(0f, opening.TopM, 0f), material, layer);
                if (includeSill)
                    GenerationUtil.CreateBox("Trim Bottom", parent, new Vector3(opening.WidthM + trim, trim, depth),
                        center + new Vector3(0f, opening.BottomM, 0f), material, layer);
            }
        }

        static Vector3 OpeningPosition(RoomSpec spec, OpeningSpec opening, float inwardOffset)
        {
            var halfWidth = spec.Geometry.WidthM * 0.5f;
            var halfLength = spec.Geometry.LengthM * 0.5f;
            switch (opening.Wall.ToLowerInvariant())
            {
                case "front": return new Vector3(opening.CenterM, 0f, halfLength + inwardOffset);
                case "back": return new Vector3(opening.CenterM, 0f, -halfLength - inwardOffset);
                case "left": return new Vector3(-halfWidth - inwardOffset, 0f, opening.CenterM);
                default: return new Vector3(halfWidth + inwardOffset, 0f, opening.CenterM);
            }
        }

        static bool IsSideWall(string wall) =>
            string.Equals(wall, "left", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wall, "right", StringComparison.OrdinalIgnoreCase);
    }
}
