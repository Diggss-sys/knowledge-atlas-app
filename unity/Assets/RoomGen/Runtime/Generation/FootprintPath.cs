using System.Collections.Generic;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Generation
{
    public static class FootprintPath
    {
        public static List<Vector2> Build(GeometrySpec geometry, int segmentsPerCorner = 10)
        {
            var radius = Mathf.Clamp(geometry.CornerRadiusM, 0f,
                Mathf.Min(geometry.WidthM, geometry.LengthM) * 0.5f);
            if (radius <= 0.0001f)
                return new List<Vector2>
                {
                    new Vector2( geometry.WidthM * 0.5f,  geometry.LengthM * 0.5f),
                    new Vector2(-geometry.WidthM * 0.5f,  geometry.LengthM * 0.5f),
                    new Vector2(-geometry.WidthM * 0.5f, -geometry.LengthM * 0.5f),
                    new Vector2( geometry.WidthM * 0.5f, -geometry.LengthM * 0.5f)
                };

            var path = new List<Vector2>(segmentsPerCorner * 4 + 4);
            var halfWidth = geometry.WidthM * 0.5f;
            var halfLength = geometry.LengthM * 0.5f;
            AddArc(path, new Vector2(halfWidth - radius, halfLength - radius), radius, 0f, 90f, segmentsPerCorner);
            AddArc(path, new Vector2(-halfWidth + radius, halfLength - radius), radius, 90f, 180f, segmentsPerCorner);
            AddArc(path, new Vector2(-halfWidth + radius, -halfLength + radius), radius, 180f, 270f, segmentsPerCorner);
            AddArc(path, new Vector2(halfWidth - radius, -halfLength + radius), radius, 270f, 360f, segmentsPerCorner);
            if (path.Count > 1 && Vector2.Distance(path[0], path[path.Count - 1]) < 0.0001f)
                path.RemoveAt(path.Count - 1);
            return path;
        }

        public static List<Vector2> BuildCornerArc(
            Vector2 center,
            float radius,
            float startDegrees,
            float endDegrees,
            int segments = 10)
        {
            var points = new List<Vector2>(segments + 1);
            AddArc(points, center, radius, startDegrees, endDegrees, segments);
            return points;
        }

        static void AddArc(
            List<Vector2> points,
            Vector2 center,
            float radius,
            float startDegrees,
            float endDegrees,
            int segments)
        {
            segments = Mathf.Max(1, segments);
            for (var i = 0; i <= segments; i++)
            {
                if (points.Count > 0 && i == 0) continue;
                var angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments) * Mathf.Deg2Rad;
                points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }
    }
}
