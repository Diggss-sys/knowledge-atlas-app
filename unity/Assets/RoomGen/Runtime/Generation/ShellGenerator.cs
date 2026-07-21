using System;
using System.Collections.Generic;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Generation
{
    public static class ShellGenerator
    {
        public static void Build(RoomSpec spec, Transform parent, SurfaceResolver surfaces, int layer)
        {
            var shell = new GameObject("01 Shell");
            shell.layer = layer;
            shell.transform.SetParent(parent, false);

            var geometry = spec.Geometry;
            var path = FootprintPath.Build(geometry);
            var floor = GenerationUtil.BuildFootprintPrism(path, 0f, 0.1f, "Floor Mesh");
            var ceiling = GenerationUtil.BuildFootprintPrism(
                path, geometry.CeilingHeightM + 0.1f, 0.1f, "Ceiling Mesh");
            GenerationUtil.CreateMeshObject("Floor", shell.transform, floor,
                surfaces.Resolve(spec.Surfaces.FloorMaterialId), layer);
            GenerationUtil.CreateMeshObject("Ceiling", shell.transform, ceiling,
                surfaces.Resolve(spec.Surfaces.CeilingMaterialId), layer);

            var walls = new GameObject("Walls");
            walls.layer = layer;
            walls.transform.SetParent(shell.transform, false);
            var wallMaterial = surfaces.Resolve(spec.Surfaces.WallMaterialId);
            var radius = Mathf.Max(0f, geometry.CornerRadiusM);

            foreach (var wall in new[] { "front", "back", "left", "right" })
            {
                var length = (wall == "front" || wall == "back" ? geometry.WidthM : geometry.LengthM)
                             - 2f * radius;
                var openings = OpeningGenerator.ForWall(spec, wall);
                var bowAmount = geometry.WallBow?.For(wall) ?? 0f;

                if (Mathf.Abs(bowAmount) * geometry.BowMaxM > 0.001f)
                {
                    BuildBowedWall(spec, wall, openings, walls.transform, wallMaterial, layer);
                }
                else
                {
                    BuildStraightWall(spec, wall, length, openings, walls.transform, wallMaterial, layer);
                }
            }

            if (radius > 0.0001f)
                BuildRoundedCorners(spec, walls.transform, wallMaterial, layer);
        }

        static void BuildStraightWall(
            RoomSpec spec,
            string wall,
            float length,
            IReadOnlyList<OpeningSpec> openings,
            Transform parent,
            Material material,
            int layer)
        {
            var root = new GameObject(char.ToUpperInvariant(wall[0]) + wall.Substring(1) + " Wall");
            root.layer = layer;
            root.transform.SetParent(parent, false);
            var cursor = -length * 0.5f;

            foreach (var opening in openings)
            {
                var left = opening.CenterM - opening.WidthM * 0.5f;
                var right = opening.CenterM + opening.WidthM * 0.5f;
                AddWallBlock(spec, wall, root.transform, cursor, left, 0f,
                    spec.Geometry.CeilingHeightM, material, layer);
                AddWallBlock(spec, wall, root.transform, left, right, 0f,
                    opening.BottomM, material, layer);
                AddWallBlock(spec, wall, root.transform, left, right, opening.TopM,
                    spec.Geometry.CeilingHeightM, material, layer);
                cursor = right;
            }

            AddWallBlock(spec, wall, root.transform, cursor, length * 0.5f, 0f,
                spec.Geometry.CeilingHeightM, material, layer);
        }

        static void AddWallBlock(
            RoomSpec spec,
            string wall,
            Transform parent,
            float start,
            float end,
            float bottom,
            float top,
            Material material,
            int layer)
        {
            var span = end - start;
            var height = top - bottom;
            if (span <= 0.001f || height <= 0.001f) return;

            var center = (start + end) * 0.5f;
            var y = (bottom + top) * 0.5f;
            var halfWidth = spec.Geometry.WidthM * 0.5f;
            var halfLength = spec.Geometry.LengthM * 0.5f;
            var thickness = spec.Geometry.WallThicknessM;
            Vector3 size;
            Vector3 position;

            switch (wall)
            {
                case "front":
                    size = new Vector3(span, height, thickness);
                    position = new Vector3(center, y, halfLength + thickness * 0.5f);
                    break;
                case "back":
                    size = new Vector3(span, height, thickness);
                    position = new Vector3(center, y, -halfLength - thickness * 0.5f);
                    break;
                case "left":
                    size = new Vector3(thickness, height, span);
                    position = new Vector3(-halfWidth - thickness * 0.5f, y, center);
                    break;
                default:
                    size = new Vector3(thickness, height, span);
                    position = new Vector3(halfWidth + thickness * 0.5f, y, center);
                    break;
            }

            GenerationUtil.CreateBox(
                $"Section {start:0.00} to {end:0.00}",
                parent,
                size,
                position,
                material,
                layer);
        }

        /// <summary>
        /// A bowed wall (wall_bow != 0): smooth curved band mesh(es) — sagitta arc through the
        /// wall's corner points, analytic normals, arc-length UVs (WallBand). Concave (-) bows into
        /// the room, convex (+) bulges outward. Openings are cut the same way BuildStraightWall
        /// cuts them — full-height segments between openings plus a sill band below and a header
        /// band above each — but every piece is an arc SUB-SPAN of the wall's curve, so windows
        /// and doors ride the bow instead of forcing the wall straight.
        /// </summary>
        static void BuildBowedWall(
            RoomSpec spec,
            string wall,
            IReadOnlyList<OpeningSpec> openings,
            Transform parent,
            Material material,
            int layer)
        {
            var geometry = spec.Geometry;
            var span = FootprintPath.BuildWallSpan(geometry, wall);
            var title = char.ToUpperInvariant(wall[0]) + wall.Substring(1);

            if (openings.Count == 0)
            {
                var mesh = WallBand.BuildMesh(span, 0f, geometry.CeilingHeightM,
                    geometry.WallThicknessM, title + " Bowed Wall Mesh");
                GenerationUtil.CreateMeshObject(title + " Wall (Bowed)", parent, mesh, material, layer);
                return;
            }

            var root = new GameObject(title + " Wall (Bowed)");
            root.layer = layer;
            root.transform.SetParent(parent, false);
            var side = wall == "left" || wall == "right";

            // The span may walk its wall coordinate ascending or descending; segment cuts are
            // easiest in ascending coordinate space (same space openings' CenterM lives in).
            var cStart = FootprintPath.WallCoord(span[0], side);
            var cEnd = FootprintPath.WallCoord(span[span.Count - 1], side);
            var cursor = Mathf.Min(cStart, cEnd);
            var wallHi = Mathf.Max(cStart, cEnd);

            foreach (var opening in openings) // ForWall pre-sorts by CenterM ascending
            {
                var left = opening.CenterM - opening.WidthM * 0.5f;
                var right = opening.CenterM + opening.WidthM * 0.5f;

                AddBowedSegment(root.transform, span, side, cursor, left,
                    0f, geometry.CeilingHeightM, geometry, material, layer, "Segment");
                if (opening.BottomM > 0.001f)
                    AddBowedSegment(root.transform, span, side, left, right,
                        0f, opening.BottomM, geometry, material, layer, "Sill Band");
                if (opening.TopM < geometry.CeilingHeightM - 0.001f)
                    AddBowedSegment(root.transform, span, side, left, right,
                        opening.TopM, geometry.CeilingHeightM, geometry, material, layer, "Header Band");
                cursor = right;
            }

            AddBowedSegment(root.transform, span, side, cursor, wallHi,
                0f, geometry.CeilingHeightM, geometry, material, layer, "Segment");
        }

        static void AddBowedSegment(
            Transform parent,
            IReadOnlyList<FootprintSample> span,
            bool side,
            float lo,
            float hi,
            float bottom,
            float top,
            GeometrySpec geometry,
            Material material,
            int layer,
            string kind)
        {
            if (hi - lo < 0.005f || top - bottom < 0.005f) return;
            var sub = FootprintPath.SubSpan(span, side, lo, hi);
            if (sub.Count < 2) return;
            var name = $"{kind} {lo:0.00} to {hi:0.00}";
            var mesh = WallBand.BuildMesh(sub, bottom, top, geometry.WallThicknessM, name + " Mesh");
            GenerationUtil.CreateMeshObject(name, parent, mesh, material, layer);
        }

        /// <summary>
        /// Rounded corners as ONE smooth curved band per corner (replaces the old fan of overlapping
        /// boxes: hard-edged facets, z-fighting, box UVs — PR-review finding). Smooth radial normals
        /// + arc-length UVs come from WallBand.
        /// </summary>
        static void BuildRoundedCorners(RoomSpec spec, Transform parent, Material material, int layer)
        {
            var geometry = spec.Geometry;
            var radius = geometry.CornerRadiusM;
            var halfWidth = geometry.WidthM * 0.5f;
            var halfLength = geometry.LengthM * 0.5f;
            var corners = new (Vector2 center, float start, float end, string name)[]
            {
                (new Vector2(halfWidth - radius, halfLength - radius), 0f, 90f, "Front Right"),
                (new Vector2(-halfWidth + radius, halfLength - radius), 90f, 180f, "Front Left"),
                (new Vector2(-halfWidth + radius, -halfLength + radius), 180f, 270f, "Back Left"),
                (new Vector2(halfWidth - radius, -halfLength + radius), 270f, 360f, "Back Right")
            };

            foreach (var corner in corners)
            {
                var samples = FootprintPath.BuildCornerSamples(
                    corner.center, radius, corner.start, corner.end);
                var mesh = WallBand.BuildMesh(samples, 0f, geometry.CeilingHeightM,
                    geometry.WallThicknessM, corner.name + " Curve Mesh");
                GenerationUtil.CreateMeshObject(corner.name + " Curve", parent, mesh, material, layer);
            }
        }
    }
}
