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
                    // Bowed wall = one smooth curved band (openings are illegal on bowed walls —
                    // the validator reports opening_on_bowed_wall; we build honestly without them).
                    if (openings.Count > 0)
                        Debug.LogWarning($"RoomGen: wall '{wall}' is bowed but has {openings.Count} opening(s); " +
                                         "openings on bowed walls are not supported (v1) and were skipped.");
                    BuildBowedWall(spec, wall, walls.transform, wallMaterial, layer);
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
        /// A bowed wall (wall_bow != 0): one smooth curved band mesh — sagitta arc through the
        /// wall's corner points, analytic normals, arc-length UVs (WallBand). Concave (-) bows into
        /// the room, convex (+) bulges outward.
        /// </summary>
        static void BuildBowedWall(RoomSpec spec, string wall, Transform parent, Material material, int layer)
        {
            var span = FootprintPath.BuildWallSpan(spec.Geometry, wall);
            var mesh = WallBand.BuildMesh(span, 0f, spec.Geometry.CeilingHeightM,
                spec.Geometry.WallThicknessM, char.ToUpperInvariant(wall[0]) + wall.Substring(1) + " Bowed Wall Mesh");
            GenerationUtil.CreateMeshObject(
                char.ToUpperInvariant(wall[0]) + wall.Substring(1) + " Wall (Bowed)",
                parent, mesh, material, layer);
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
