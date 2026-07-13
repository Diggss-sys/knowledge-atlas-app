using System.Collections.Generic;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Generation
{
    /// <summary>
    /// One sample point on the footprint ring (CCW viewed from above, +Y up, XZ plane as XY here).
    /// Carries everything the wall-band mesh needs: analytic outward normal, cumulative perimeter
    /// arc length (for stretch-free U coordinates), and whether the join INTO this sample is a hard
    /// edge (vertex column must be split) or smooth (column shared, normals interpolated).
    /// </summary>
    public readonly struct FootprintSample
    {
        public readonly Vector2 Position;
        public readonly Vector2 OutwardNormal;
        public readonly float ArcLength;
        public readonly bool HardEdge;

        public FootprintSample(Vector2 position, Vector2 outwardNormal, float arcLength, bool hardEdge)
        {
            Position = position;
            OutwardNormal = outwardNormal;
            ArcLength = arcLength;
            HardEdge = hardEdge;
        }
    }

    /// <summary>
    /// Builds the room footprint as a closed CCW ring of samples: four edges (straight, or bowed
    /// into/out of the room as circular arcs via wall_bow), joined by quarter-arc corners
    /// (corner_radius_m). Arc tessellation is chord-error driven (max sagitta ~2 mm), so large
    /// gentle bows get more segments and small fillets fewer (RENDERING_RESEARCH.md §4 recipe).
    /// </summary>
    public static class FootprintPath
    {
        public const float ChordErrorM = 0.002f;   // max deviation of chord from true arc
        const int MinArcSegments = 2;
        const int MaxArcSegments = 64;
        const float FlatBowEpsilon = 0.001f;

        /// <summary>Legacy shape: positions only. Used by caps, metrics, furniture bounds.</summary>
        public static List<Vector2> Build(GeometrySpec geometry, int segmentsPerCorner = 0)
        {
            var samples = BuildSamples(geometry);
            var path = new List<Vector2>(samples.Count);
            foreach (var sample in samples) path.Add(sample.Position);
            return path;
        }

        /// <summary>Full ring with normals + arc length, CCW, no duplicated closing sample.</summary>
        public static List<FootprintSample> BuildSamples(GeometrySpec geometry)
        {
            var halfW = geometry.WidthM * 0.5f;
            var halfL = geometry.LengthM * 0.5f;
            var radius = Mathf.Clamp(geometry.CornerRadiusM, 0f, Mathf.Min(halfW, halfL) - 0.01f);
            if (radius < 0.0001f) radius = 0f;
            var bow = geometry.WallBow ?? new WallBowSpec();
            var bowMax = Mathf.Max(0f, geometry.BowMaxM);

            // Corner anchor points (where edges start/end), CCW starting at front-right corner.
            // front = +Z (top in XY here, y == world z), right = +X.
            var frontRight = new Vector2(halfW - radius, halfL - radius);
            var frontLeft = new Vector2(-halfW + radius, halfL - radius);
            var backLeft = new Vector2(-halfW + radius, -halfL + radius);
            var backRight = new Vector2(halfW - radius, -halfL + radius);

            var ring = new List<FootprintSample>(96);

            // CCW (viewed from above with +Y here = +Z world): front edge runs +X -> -X.
            // Edge endpoints ON the wall lines (offset from anchors by radius toward the wall).
            var fr = new Vector2(frontRight.x, halfL);
            var fl = new Vector2(frontLeft.x, halfL);
            var lf = new Vector2(-halfW, frontLeft.y);
            var lb = new Vector2(-halfW, backLeft.y);
            var bl = new Vector2(backLeft.x, -halfL);
            var br = new Vector2(backRight.x, -halfL);
            var rb = new Vector2(halfW, backRight.y);
            var rf = new Vector2(halfW, frontRight.y);

            // Each edge: outward normal of the FLAT wall; bow sign: + = outward (convex).
            AddEdge(ring, fr, fl, new Vector2(0f, 1f), bow.For("front"), bowMax);
            AddCorner(ring, frontLeft, radius, 90f, 180f);
            AddEdge(ring, lf, lb, new Vector2(-1f, 0f), bow.For("left"), bowMax);
            AddCorner(ring, backLeft, radius, 180f, 270f);
            AddEdge(ring, bl, br, new Vector2(0f, -1f), bow.For("back"), bowMax);
            AddCorner(ring, backRight, radius, 270f, 360f);
            AddEdge(ring, rb, rf, new Vector2(1f, 0f), bow.For("right"), bowMax);
            AddCorner(ring, frontRight, radius, 0f, 90f);

            return FinalizeRing(ring);
        }

        /// <summary>Samples for ONE wall's span (edge only, endpoints included). For per-wall bands.</summary>
        public static List<FootprintSample> BuildWallSpan(GeometrySpec geometry, string wall)
        {
            var halfW = geometry.WidthM * 0.5f;
            var halfL = geometry.LengthM * 0.5f;
            var radius = Mathf.Clamp(geometry.CornerRadiusM, 0f, Mathf.Min(halfW, halfL) - 0.01f);
            if (radius < 0.0001f) radius = 0f;
            var bowMax = Mathf.Max(0f, geometry.BowMaxM);
            var bowAmount = (geometry.WallBow ?? new WallBowSpec()).For(wall);

            Vector2 start, end, outward;
            switch (wall)
            {
                case "front":
                    start = new Vector2(halfW - radius, halfL);
                    end = new Vector2(-halfW + radius, halfL);
                    outward = new Vector2(0f, 1f);
                    break;
                case "left":
                    start = new Vector2(-halfW, halfL - radius);
                    end = new Vector2(-halfW, -halfL + radius);
                    outward = new Vector2(-1f, 0f);
                    break;
                case "back":
                    start = new Vector2(-halfW + radius, -halfL);
                    end = new Vector2(halfW - radius, -halfL);
                    outward = new Vector2(0f, -1f);
                    break;
                default:
                    start = new Vector2(halfW, -halfL + radius);
                    end = new Vector2(halfW, halfL - radius);
                    outward = new Vector2(1f, 0f);
                    break;
            }

            var span = new List<FootprintSample>(34);
            AddEdge(span, start, end, outward, bowAmount, bowMax, includeStart: true);
            return FinalizeRing(span, closed: false);
        }

        /// <summary>
        /// Corner quarter-arc as full samples (radial outward normals, cumulative arc length) —
        /// feeds WallBand so rounded corners are ONE smooth curved mesh, not a fan of boxes.
        /// </summary>
        public static List<FootprintSample> BuildCornerSamples(
            Vector2 center, float radius, float startDegrees, float endDegrees)
        {
            var arcAngle = Mathf.Abs(endDegrees - startDegrees) * Mathf.Deg2Rad;
            var segments = SegmentsForArc(radius, arcAngle);
            var samples = new List<FootprintSample>(segments + 1);
            var arcLength = 0f;
            var step = radius * (arcAngle / segments);
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments) * Mathf.Deg2Rad;
                var radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (i > 0) arcLength += step;
                samples.Add(new FootprintSample(center + radial * radius, radial, arcLength, i == 0 || i == segments));
            }
            return samples;
        }

        /// <summary>Corner arc positions (legacy helper kept for callers/tests).</summary>
        public static List<Vector2> BuildCornerArc(
            Vector2 center, float radius, float startDegrees, float endDegrees, int segments = 10)
        {
            segments = Mathf.Max(1, segments);
            var points = new List<Vector2>(segments + 1);
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments) * Mathf.Deg2Rad;
                points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            return points;
        }

        /// <summary>
        /// Wall-local coordinate used for opening placement: openings store CenterM as world x on
        /// front/back walls and world z (footprint y) on side walls — same convention as
        /// OpeningGenerator.OpeningPosition and the validator's bounds check.
        /// </summary>
        public static float WallCoord(FootprintSample sample, bool sideWall) =>
            sideWall ? sample.Position.y : sample.Position.x;

        /// <summary>
        /// Extract the part of a wall span between wall-local coordinates [lo, hi] (opening cuts on
        /// a bowed wall). Boundary samples are interpolated onto the arc (position lerp, normal
        /// renormalised, arc length lerped so UVs stay continuous across the segments of one wall).
        /// Works whether the span walks its coordinate ascending or descending; result keeps span
        /// order. Interpolated cut points are hard edges — they become band ends with end caps.
        /// </summary>
        public static List<FootprintSample> SubSpan(
            IReadOnlyList<FootprintSample> span, bool sideWall, float lo, float hi)
        {
            var result = new List<FootprintSample>();
            if (span.Count < 2 || hi <= lo) return result;

            for (var i = 0; i < span.Count - 1; i++)
            {
                var a = span[i];
                var b = span[i + 1];
                var ca = WallCoord(a, sideWall);
                var cb = WallCoord(b, sideWall);

                if (ca >= lo && ca <= hi) AddSubSpanSample(result, a);

                // Boundary crossings strictly inside this segment, ordered along the walk.
                if (Mathf.Abs(cb - ca) > 1e-6f)
                {
                    var tLo = (lo - ca) / (cb - ca);
                    var tHi = (hi - ca) / (cb - ca);
                    var first = Mathf.Min(tLo, tHi);
                    var second = Mathf.Max(tLo, tHi);
                    if (first > 0f && first < 1f) AddSubSpanSample(result, Interpolate(a, b, first));
                    if (second > 0f && second < 1f) AddSubSpanSample(result, Interpolate(a, b, second));
                }
            }

            var last = span[span.Count - 1];
            var cl = WallCoord(last, sideWall);
            if (cl >= lo && cl <= hi) AddSubSpanSample(result, last);
            return result;
        }

        /// <summary>
        /// Same samples pushed INTO the room by <paramref name="inward"/> meters (along -outward).
        /// Curved trim/glass bands are built from these so they hug the bowed inner face.
        /// </summary>
        public static List<FootprintSample> OffsetInward(IReadOnlyList<FootprintSample> span, float inward)
        {
            var list = new List<FootprintSample>(span.Count);
            foreach (var s in span)
                list.Add(new FootprintSample(
                    s.Position - s.OutwardNormal * inward, s.OutwardNormal, s.ArcLength, s.HardEdge));
            return list;
        }

        static void AddSubSpanSample(List<FootprintSample> list, FootprintSample sample)
        {
            if (list.Count > 0 &&
                Vector2.Distance(list[list.Count - 1].Position, sample.Position) < 0.0005f)
                return;
            list.Add(sample);
        }

        static FootprintSample Interpolate(FootprintSample a, FootprintSample b, float t)
        {
            var normal = Vector2.Lerp(a.OutwardNormal, b.OutwardNormal, t);
            if (normal.sqrMagnitude < 1e-10f) normal = a.OutwardNormal;
            return new FootprintSample(
                Vector2.Lerp(a.Position, b.Position, t),
                normal.normalized,
                Mathf.Lerp(a.ArcLength, b.ArcLength, t),
                true);
        }

        // ---- edge / corner emission -------------------------------------------------------------

        static void AddEdge(
            List<FootprintSample> ring,
            Vector2 start,
            Vector2 end,
            Vector2 flatOutward,
            float bowAmount,
            float bowMax,
            bool includeStart = true)
        {
            bowAmount = Mathf.Clamp(bowAmount, -1f, 1f);
            var sagitta = bowAmount * bowMax;

            if (Mathf.Abs(sagitta) < FlatBowEpsilon || Vector2.Distance(start, end) < 0.02f)
            {
                // Straight edge: two samples, flat normal, hard edges at both joins.
                if (includeStart) ring.Add(new FootprintSample(start, flatOutward, 0f, true));
                ring.Add(new FootprintSample(end, flatOutward, 0f, true));
                return;
            }

            // Circular arc through start/end with mid-point displaced by sagitta along flatOutward.
            // (+sagitta = convex, bulges outward; -sagitta = concave, bows into the room.)
            var chord = Vector2.Distance(start, end);
            var h = chord * 0.5f;
            var s = Mathf.Abs(sagitta);
            var radius = (h * h + s * s) / (2f * s);

            var mid = (start + end) * 0.5f;
            // Arc center sits on the mid-perpendicular, on the opposite side of the bulge.
            var bulgeDir = flatOutward * Mathf.Sign(sagitta);
            var center = mid + bulgeDir * (sagitta > 0f ? -(radius - s) : radius - s);

            var angleStart = Mathf.Atan2(start.y - center.y, start.x - center.x);
            var angleEnd = Mathf.Atan2(end.y - center.y, end.x - center.x);
            // Walk the SHORT way around (bow arcs are always < pi for sagitta < half-chord).
            var delta = Mathf.DeltaAngle(angleStart * Mathf.Rad2Deg, angleEnd * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            var segments = SegmentsForArc(radius, Mathf.Abs(delta));

            for (var i = includeStart ? 0 : 1; i <= segments; i++)
            {
                var t = i / (float)segments;
                var angle = angleStart + delta * t;
                var radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var position = center + radial * radius;
                // Outward normal: radial away from center for convex, toward center for concave.
                var normal = sagitta > 0f ? radial : -radial;
                // Endpoints of a bowed edge meet corner arcs / straight walls at an angle: hard.
                var hard = i == 0 || i == segments;
                ring.Add(new FootprintSample(position, normal, 0f, hard));
            }
        }

        static void AddCorner(
            List<FootprintSample> ring, Vector2 center, float radius, float startDegrees, float endDegrees)
        {
            if (radius < 0.0001f) return; // angular room: edges already carry the corner points
            var arcAngle = Mathf.Abs(endDegrees - startDegrees) * Mathf.Deg2Rad;
            var segments = SegmentsForArc(radius, arcAngle);
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments) * Mathf.Deg2Rad;
                var radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // Corner arcs are tangent to their neighbouring FLAT walls: smooth joins, except
                // the very ends when the neighbour is bowed (the edge marks its own ends hard).
                ring.Add(new FootprintSample(center + radial * radius, radial, 0f, i == 0 || i == segments));
            }
        }

        /// <summary>Chord-error tessellation: e = r(1 - cos(dTheta/2)) => dTheta = 2 acos(1 - e/r).</summary>
        public static int SegmentsForArc(float radius, float arcAngleRad)
        {
            if (radius <= 0f || arcAngleRad <= 0f) return MinArcSegments;
            var ratio = Mathf.Clamp01(ChordErrorM / radius);
            var maxStep = 2f * Mathf.Acos(1f - ratio);
            if (maxStep < 1e-4f) maxStep = 1e-4f;
            return Mathf.Clamp(Mathf.CeilToInt(arcAngleRad / maxStep), MinArcSegments, MaxArcSegments);
        }

        // Deduplicate consecutive coincident samples (edge/corner junctions emit the same point
        // twice: keep ONE sample, merging flags — hard wins) and accumulate arc length.
        static List<FootprintSample> FinalizeRing(List<FootprintSample> raw, bool closed = true)
        {
            var ring = new List<FootprintSample>(raw.Count);
            foreach (var sample in raw)
            {
                if (ring.Count > 0 && Vector2.Distance(ring[ring.Count - 1].Position, sample.Position) < 0.0005f)
                {
                    var last = ring[ring.Count - 1];
                    // Coincident junction point: keep position, prefer the smooth normal of the arc
                    // side when either side is smooth; hard if EITHER says hard AND normals disagree.
                    var normalsAgree = Vector2.Dot(last.OutwardNormal, sample.OutwardNormal) > 0.999f;
                    var hard = (last.HardEdge || sample.HardEdge) && !normalsAgree;
                    var normal = normalsAgree ? last.OutwardNormal
                        : (last.OutwardNormal + sample.OutwardNormal).normalized;
                    ring[ring.Count - 1] = new FootprintSample(last.Position, normal, 0f, hard);
                    continue;
                }
                ring.Add(sample);
            }

            if (closed && ring.Count > 1 &&
                Vector2.Distance(ring[0].Position, ring[ring.Count - 1].Position) < 0.0005f)
                ring.RemoveAt(ring.Count - 1);

            // Cumulative arc length.
            var total = 0f;
            for (var i = 0; i < ring.Count; i++)
            {
                if (i > 0) total += Vector2.Distance(ring[i - 1].Position, ring[i].Position);
                ring[i] = new FootprintSample(ring[i].Position, ring[i].OutwardNormal, total, ring[i].HardEdge);
            }
            return ring;
        }
    }
}
