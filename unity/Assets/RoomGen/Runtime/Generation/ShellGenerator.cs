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
            BuildStraightWall(spec, "front", geometry.WidthM - 2f * radius,
                OpeningGenerator.ForWall(spec, "front"), walls.transform, wallMaterial, layer);
            BuildStraightWall(spec, "back", geometry.WidthM - 2f * radius,
                OpeningGenerator.ForWall(spec, "back"), walls.transform, wallMaterial, layer);
            BuildStraightWall(spec, "left", geometry.LengthM - 2f * radius,
                OpeningGenerator.ForWall(spec, "left"), walls.transform, wallMaterial, layer);
            BuildStraightWall(spec, "right", geometry.LengthM - 2f * radius,
                OpeningGenerator.ForWall(spec, "right"), walls.transform, wallMaterial, layer);

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

        static void BuildRoundedCorners(RoomSpec spec, Transform parent, Material material, int layer)
        {
            var geometry = spec.Geometry;
            var radius = geometry.CornerRadiusM;
            var halfWidth = geometry.WidthM * 0.5f;
            var halfLength = geometry.LengthM * 0.5f;
            var corners = new[]
            {
                new Corner(new Vector2(halfWidth - radius, halfLength - radius), 0f, 90f, "Front Right"),
                new Corner(new Vector2(-halfWidth + radius, halfLength - radius), 90f, 180f, "Front Left"),
                new Corner(new Vector2(-halfWidth + radius, -halfLength + radius), 180f, 270f, "Back Left"),
                new Corner(new Vector2(halfWidth - radius, -halfLength + radius), 270f, 360f, "Back Right")
            };

            foreach (var corner in corners)
            {
                var root = new GameObject(corner.Name + " Curve");
                root.layer = layer;
                root.transform.SetParent(parent, false);
                var arc = FootprintPath.BuildCornerArc(corner.Center, radius,
                    corner.StartDegrees, corner.EndDegrees);
                for (var i = 0; i < arc.Count - 1; i++)
                {
                    var a = arc[i];
                    var b = arc[i + 1];
                    var midpoint = (a + b) * 0.5f;
                    var outward = (midpoint - corner.Center).normalized;
                    var segment = Vector2.Distance(a, b) + 0.003f;
                    var position = new Vector3(
                        midpoint.x + outward.x * geometry.WallThicknessM * 0.5f,
                        geometry.CeilingHeightM * 0.5f,
                        midpoint.y + outward.y * geometry.WallThicknessM * 0.5f);
                    var rotation = Quaternion.LookRotation(
                        new Vector3(outward.x, 0f, outward.y), Vector3.up);
                    GenerationUtil.CreateBox($"Curve {i:00}", root.transform,
                        new Vector3(segment, geometry.CeilingHeightM, geometry.WallThicknessM),
                        position, material, layer, rotation);
                }
            }
        }

        readonly struct Corner
        {
            public readonly Vector2 Center;
            public readonly float StartDegrees;
            public readonly float EndDegrees;
            public readonly string Name;

            public Corner(Vector2 center, float startDegrees, float endDegrees, string name)
            {
                Center = center;
                StartDegrees = startDegrees;
                EndDegrees = endDegrees;
                Name = name;
            }
        }
    }
}
