using System.Collections.Generic;
using UnityEngine;

namespace RoomGen.Generation
{
    /// <summary>
    /// Ear-clipping triangulation for simple (non-self-intersecting) polygons, including CONCAVE
    /// ones — required for floor/ceiling caps once walls can bow into the room (wall_bow &lt; 0),
    /// which makes the footprint non-convex. A center-fan (the previous approach) produces
    /// inverted/overlapping triangles on concave footprints; ear clipping does not.
    /// (RENDERING_RESEARCH.md §4 / review finding on GenerationUtil.BuildFootprintPrism.)
    /// </summary>
    public static class EarClip
    {
        const float Epsilon = 1e-7f;

        /// <summary>
        /// Triangulate a simple polygon. Returns indices into <paramref name="polygon"/> as CCW
        /// triangles (a, b, c). Winding of the input may be either direction; output triangles
        /// follow the input ring's orientation. Falls back to a fan if clipping stalls
        /// (degenerate input) so a room always builds.
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> polygon)
        {
            var count = polygon.Count;
            var triangles = new List<int>(Mathf.Max(0, (count - 2) * 3));
            if (count < 3) return triangles;

            // Work on a mutable index ring. Ensure CCW orientation for the ear test; remember if
            // we flipped so output matches the caller's winding.
            var indices = new List<int>(count);
            for (var i = 0; i < count; i++) indices.Add(i);

            var reversed = SignedArea(polygon) < 0f;
            if (reversed) indices.Reverse();

            var guard = 0;
            var maxIterations = count * count + 16;
            while (indices.Count > 3 && guard++ < maxIterations)
            {
                var clipped = false;
                for (var i = 0; i < indices.Count; i++)
                {
                    var prev = indices[(i - 1 + indices.Count) % indices.Count];
                    var curr = indices[i];
                    var next = indices[(i + 1) % indices.Count];

                    if (!IsEar(polygon, indices, prev, curr, next)) continue;

                    triangles.Add(prev);
                    triangles.Add(curr);
                    triangles.Add(next);
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped) break; // stalled — degenerate ring; fall through to fan the rest
            }

            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }
            else if (indices.Count > 3)
            {
                // Safety fallback: fan whatever remains so the mesh is never empty. Only reachable
                // on degenerate input (duplicate/collinear runs), which validators should prevent.
                for (var i = 1; i < indices.Count - 1; i++)
                {
                    triangles.Add(indices[0]);
                    triangles.Add(indices[i]);
                    triangles.Add(indices[i + 1]);
                }
            }

            if (reversed)
            {
                // Restore the caller's winding by swapping two indices of every triangle.
                for (var t = 0; t < triangles.Count; t += 3)
                {
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                }
            }

            return triangles;
        }

        public static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            var area = 0f;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        static bool IsEar(IReadOnlyList<Vector2> polygon, List<int> ring, int prev, int curr, int next)
        {
            var a = polygon[prev];
            var b = polygon[curr];
            var c = polygon[next];

            // Convex corner (CCW ring): cross product must be positive; collinear counts as
            // clippable (zero-area ear) to shed duplicate/collinear points.
            var cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (cross < -Epsilon) return false;

            // No other ring vertex may sit inside the candidate triangle.
            for (var i = 0; i < ring.Count; i++)
            {
                var index = ring[i];
                if (index == prev || index == curr || index == next) continue;
                if (PointInTriangle(polygon[index], a, b, c)) return false;
            }
            return true;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Sign(p, a, b);
            var d2 = Sign(p, b, c);
            var d3 = Sign(p, c, a);
            var hasNeg = d1 < -Epsilon || d2 < -Epsilon || d3 < -Epsilon;
            var hasPos = d1 > Epsilon || d2 > Epsilon || d3 > Epsilon;
            return !(hasNeg && hasPos);
        }

        static float Sign(Vector2 p, Vector2 a, Vector2 b) =>
            (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
