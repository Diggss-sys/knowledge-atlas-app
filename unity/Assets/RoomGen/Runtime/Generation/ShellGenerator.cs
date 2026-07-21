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

            // EVERY wall — flat or bowed — is built the same way, from its footprint span through
            // WallBand. A flat wall is just a 2-sample straight span, so it gets the same arc-length
            // UVs and analytic normals as a bowed one. This is deliberate: when the two conditions of
            // a wall_bow pair differ only by the bow, they must NOT also differ by mesh construction
            // (box UVs/normals vs band) — that would be a second, invisible visual variable riding
            // the manipulation. One path = one appearance basis for both conditions.
            foreach (var wall in new[] { "front", "back", "left", "right" })
            {
                var openings = OpeningGenerator.ForWall(spec, wall);
                BuildWall(spec, wall, openings, walls.transform, wallMaterial, layer);
            }

            if (radius > 0.0001f)
                BuildRoundedCorners(spec, walls.transform, wallMaterial, layer);
        }

        /// <summary>
        /// Build one wall as smooth band mesh(es) from its footprint span (WallBand): sagitta arc
        /// through the wall's corner points when bowed, a straight 2-sample span when flat — either
        /// way analytic normals + arc-length UVs, so flat and bowed walls share ONE appearance basis
        /// (no box-vs-band confound). Concave bow (-) curves into the room, convex (+) bulges out.
        /// Openings cut the span into full-height segments between them plus a sill band below and a
        /// header band above each; every piece is an arc SUB-SPAN, so windows and doors ride the bow.
        /// </summary>
        static void BuildWall(
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
                    geometry.WallThicknessM, title + " Wall Mesh");
                GenerationUtil.CreateMeshObject(title + " Wall", parent, mesh, material, layer);
                return;
            }

            var root = new GameObject(title + " Wall");
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
