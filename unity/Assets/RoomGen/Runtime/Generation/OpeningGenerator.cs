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

                // Bowed wall: glass + trim must ride the arc, not sit on the flat wall plane.
                var bowAmount = spec.Geometry.WallBow?.For(opening.Wall.ToLowerInvariant()) ?? 0f;
                if (Mathf.Abs(bowAmount) * spec.Geometry.BowMaxM > 0.001f)
                {
                    BuildCurvedInsert(spec, opening, root.transform, trimMaterial, glassMaterial, layer);
                    continue;
                }

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

        /// <summary>
        /// Insert for an opening on a BOWED wall: every piece is a thin curved band (WallBand over
        /// an arc sub-span) instead of an axis-aligned box, so glass and trim hug the curve.
        /// Bands are offset INTO the room from the footprint line: OffsetInward(d) + thickness t
        /// occupies [-d, -d+t] relative to the inner wall face.
        /// </summary>
        static void BuildCurvedInsert(
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
