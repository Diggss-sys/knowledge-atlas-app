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
                // ONE insert path for every opening. Each piece is a thin band over the wall's arc
                // sub-span (WallBand); a flat wall is just a straight sub-span, so its glass and trim
                // are built exactly like a bowed wall's — no box-vs-band difference between the two
                // conditions of a wall_bow pair (same reasoning as ShellGenerator.BuildWall).
                BuildInsert(spec, opening, root.transform, trimMaterial, glassMaterial, layer);
            }
        }

        /// <summary>
        /// Insert for an opening: every piece is a thin curved band (WallBand over an arc sub-span)
        /// that hugs the wall — bowed or flat. Bands are offset INTO the room from the footprint
        /// line: OffsetInward(d) + thickness t occupies [-d, -d+t] relative to the inner wall face.
        /// </summary>
        static void BuildInsert(
            RoomSpec spec,
            OpeningSpec opening,
            Transform parent,
            Material trimMaterial,
            Material glassMaterial,
            int layer)
        {
            const float trim = 0.07f;
            const float depth = 0.06f;
            const float trimInset = 0.075f; // band spans [-0.075, -0.015] — matches the flat inserts

            var side = IsSideWall(opening.Wall);
            var span = FootprintPath.BuildWallSpan(spec.Geometry, opening.Wall.ToLowerInvariant());
            var left = opening.CenterM - opening.WidthM * 0.5f;
            var right = opening.CenterM + opening.WidthM * 0.5f;
            var isWindow = string.Equals(opening.Kind, "window", StringComparison.OrdinalIgnoreCase);

            if (isWindow)
            {
                var pane = FootprintPath.SubSpan(span, side, left, right);
                if (pane.Count >= 2)
                {
                    var mesh = WallBand.BuildMesh(FootprintPath.OffsetInward(pane, 0.005f),
                        opening.BottomM, opening.TopM, 0.025f, "Glass Mesh");
                    GenerationUtil.CreateMeshObject("Glass", parent, mesh, glassMaterial, layer);
                }
            }

            // Side trims (vertical), then top trim; sill only on windows (doors run to the floor).
            var trimBottom = isWindow ? opening.BottomM - trim * 0.5f : 0f;
            AddCurvedTrimBand(parent, span, side, left - trim * 0.5f, left + trim * 0.5f,
                trimBottom, opening.TopM + trim * 0.5f, trimInset, depth, trimMaterial, layer, "Trim A");
            AddCurvedTrimBand(parent, span, side, right - trim * 0.5f, right + trim * 0.5f,
                trimBottom, opening.TopM + trim * 0.5f, trimInset, depth, trimMaterial, layer, "Trim B");
            AddCurvedTrimBand(parent, span, side, left - trim * 0.5f, right + trim * 0.5f,
                opening.TopM - trim * 0.5f, opening.TopM + trim * 0.5f, trimInset, depth,
                trimMaterial, layer, "Trim Top");
            if (isWindow)
                AddCurvedTrimBand(parent, span, side, left - trim * 0.5f, right + trim * 0.5f,
                    opening.BottomM - trim * 0.5f, opening.BottomM + trim * 0.5f, trimInset, depth,
                    trimMaterial, layer, "Trim Bottom");
        }

        static void AddCurvedTrimBand(
            Transform parent,
            IReadOnlyList<FootprintSample> span,
            bool side,
            float lo,
            float hi,
            float bottom,
            float top,
            float inset,
            float thickness,
            Material material,
            int layer,
            string name)
        {
            if (hi - lo < 0.005f || top - bottom < 0.005f) return;
            var sub = FootprintPath.SubSpan(span, side, lo, hi);
            if (sub.Count < 2) return;
            var mesh = WallBand.BuildMesh(FootprintPath.OffsetInward(sub, inset),
                bottom, top, thickness, name + " Mesh");
            GenerationUtil.CreateMeshObject(name, parent, mesh, material, layer);
        }

        static bool IsSideWall(string wall) =>
            string.Equals(wall, "left", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(wall, "right", StringComparison.OrdinalIgnoreCase);
    }
}
