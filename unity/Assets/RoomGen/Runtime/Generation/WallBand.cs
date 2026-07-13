using System.Collections.Generic;
using UnityEngine;

namespace RoomGen.Generation
{
    /// <summary>
    /// Builds a smooth extruded WALL BAND mesh from a span of footprint samples — the true curved
    /// wall the box-fan corners could not provide (review finding on ShellGenerator; recipe in
    /// RENDERING_RESEARCH.md §4). One vertex column per sample, two rows (floor, ceiling), inner
    /// face + outer face + top cap. Normals are ANALYTIC (radial from the footprint math — no
    /// RecalculateNormals banding), UVs are arc-length (u = perimeter meters, v = height meters,
    /// zero stretch on curves), and tangents follow the path so PBR normal maps shade correctly.
    /// </summary>
    public static class WallBand
    {
        public static Mesh BuildMesh(
            IReadOnlyList<FootprintSample> span,
            float bottomY,
            float topY,
            float thickness,
            string name,
            bool capBottom = false)
        {
            var count = span.Count;
            var mesh = new Mesh { name = name };
            if (count < 2) return mesh;

            var vertices = new List<Vector3>(count * 8);
            var normals = new List<Vector3>(count * 8);
            var tangents = new List<Vector4>(count * 8);
            var uv = new List<Vector2>(count * 8);
            var triangles = new List<int>(count * 18);

            // Column layout per sample i:
            //   innerBottom, innerTop  (face normal = -outward, into the room)
            //   outerBottom, outerTop  (face normal = +outward)
            // Top cap gets its own vertices (normal up) so the cap doesn't smooth into the faces.
            var innerStart = 0;
            var outerStart = count * 2;
            var capStart = count * 4;

            // Vertex blocks must stay contiguous per face (quad indexing below assumes it):
            // [0 .. count*2)         inner columns (bottom, top per sample)
            // [count*2 .. count*4)   outer columns
            // [count*4 .. count*8)   top-cap ring (inner, outer per sample)
            for (var i = 0; i < count; i++)
            {
                var sample = span[i];
                var inner = new Vector3(sample.Position.x, 0f, sample.Position.y);
                var outward3 = new Vector3(sample.OutwardNormal.x, 0f, sample.OutwardNormal.y);
                var pathDir = PathDirection(span, i);

                // Inner face (visible from inside the room).
                vertices.Add(new Vector3(inner.x, bottomY, inner.z));
                vertices.Add(new Vector3(inner.x, topY, inner.z));
                normals.Add(-outward3);
                normals.Add(-outward3);
                var innerTangent = TangentFor(-outward3, pathDir);
                tangents.Add(innerTangent);
                tangents.Add(innerTangent);
                uv.Add(new Vector2(sample.ArcLength, bottomY));
                uv.Add(new Vector2(sample.ArcLength, topY));
            }

            for (var i = 0; i < count; i++)
            {
                var sample = span[i];
                var inner = new Vector3(sample.Position.x, 0f, sample.Position.y);
                var outward3 = new Vector3(sample.OutwardNormal.x, 0f, sample.OutwardNormal.y);
                var outer = inner + outward3 * thickness;
                var pathDir = PathDirection(span, i);

                // Outer face.
                vertices.Add(new Vector3(outer.x, bottomY, outer.z));
                vertices.Add(new Vector3(outer.x, topY, outer.z));
                normals.Add(outward3);
                normals.Add(outward3);
                var outerTangent = TangentFor(outward3, pathDir);
                tangents.Add(outerTangent);
                tangents.Add(outerTangent);
                uv.Add(new Vector2(sample.ArcLength, bottomY));
                uv.Add(new Vector2(sample.ArcLength, topY));
            }

            // Top-cap vertices (inner ring + outer ring, both normal up).
            for (var i = 0; i < count; i++)
            {
                var sample = span[i];
                var inner = new Vector3(sample.Position.x, topY, sample.Position.y);
                var outward3 = new Vector3(sample.OutwardNormal.x, 0f, sample.OutwardNormal.y);
                var outer = inner + outward3 * thickness;
                var pathDir = PathDirection(span, i);
                var upTangent = TangentFor(Vector3.up, pathDir);

                vertices.Add(inner);
                vertices.Add(outer);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                tangents.Add(upTangent);
                tangents.Add(upTangent);
                uv.Add(new Vector2(sample.ArcLength, 0f));
                uv.Add(new Vector2(sample.ArcLength, thickness));
            }

            for (var i = 0; i < count - 1; i++)
            {
                var desiredInner = -new Vector3(span[i].OutwardNormal.x, 0f, span[i].OutwardNormal.y);
                var desiredOuter = -desiredInner;

                // Inner face quad: columns i, i+1 (bottom, top).
                EmitQuad(vertices, triangles,
                    innerStart + i * 2, innerStart + i * 2 + 1,
                    innerStart + (i + 1) * 2 + 1, innerStart + (i + 1) * 2,
                    desiredInner);

                // Outer face quad.
                EmitQuad(vertices, triangles,
                    outerStart + i * 2, outerStart + i * 2 + 1,
                    outerStart + (i + 1) * 2 + 1, outerStart + (i + 1) * 2,
                    desiredOuter);

                // Top cap quad (inner ring -> outer ring).
                EmitQuad(vertices, triangles,
                    capStart + i * 2, capStart + i * 2 + 1,
                    capStart + (i + 1) * 2 + 1, capStart + (i + 1) * 2,
                    Vector3.up);
            }

            // End caps: seal both open ends of the band. A neighbour that meets this band at a
            // different angle (bowed edge against a straight wall or a corner arc) does not cover
            // the hollow between the inner and outer skins — without caps you can see straight
            // into the wall (the "crevice"). Caps make every band watertight on its own.
            AddEndCap(span, 0, -1f, thickness, bottomY, topY, vertices, normals, tangents, uv, triangles);
            AddEndCap(span, count - 1, 1f, thickness, bottomY, topY, vertices, normals, tangents, uv, triangles);

            // Bottom cap (opt-in): a band suspended above the floor — a window HEADER or a trim
            // piece — shows its underside; without this ring you would look up into the hollow.
            if (capBottom)
            {
                var bottomCapStart = vertices.Count;
                for (var i = 0; i < count; i++)
                {
                    var sample = span[i];
                    var inner = new Vector3(sample.Position.x, bottomY, sample.Position.y);
                    var outward3 = new Vector3(sample.OutwardNormal.x, 0f, sample.OutwardNormal.y);
                    var outer = inner + outward3 * thickness;
                    var downTangent = TangentFor(Vector3.down, PathDirection(span, i));

                    vertices.Add(inner);
                    vertices.Add(outer);
                    normals.Add(Vector3.down);
                    normals.Add(Vector3.down);
                    tangents.Add(downTangent);
                    tangents.Add(downTangent);
                    uv.Add(new Vector2(sample.ArcLength, 0f));
                    uv.Add(new Vector2(sample.ArcLength, thickness));
                }
                for (var i = 0; i < count - 1; i++)
                    EmitQuad(vertices, triangles,
                        bottomCapStart + i * 2, bottomCapStart + i * 2 + 1,
                        bottomCapStart + (i + 1) * 2 + 1, bottomCapStart + (i + 1) * 2,
                        Vector3.down);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>One rectangular cap across the band's end (inner skin -> outer skin, floor -> top).</summary>
        static void AddEndCap(
            IReadOnlyList<FootprintSample> span,
            int index,
            float sign,
            float thickness,
            float bottomY,
            float topY,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<int> triangles)
        {
            var sample = span[index];
            var inner = new Vector3(sample.Position.x, 0f, sample.Position.y);
            var outward3 = new Vector3(sample.OutwardNormal.x, 0f, sample.OutwardNormal.y);
            var outer = inner + outward3 * thickness;
            var capNormal = PathDirection(span, index) * sign;
            var capTangent = TangentFor(capNormal, outward3);

            var start = vertices.Count;
            vertices.Add(new Vector3(inner.x, bottomY, inner.z)); // 0 inner bottom
            vertices.Add(new Vector3(inner.x, topY, inner.z));    // 1 inner top
            vertices.Add(new Vector3(outer.x, topY, outer.z));    // 2 outer top
            vertices.Add(new Vector3(outer.x, bottomY, outer.z)); // 3 outer bottom
            for (var i = 0; i < 4; i++)
            {
                normals.Add(capNormal);
                tangents.Add(capTangent);
            }
            uv.Add(new Vector2(0f, bottomY));
            uv.Add(new Vector2(0f, topY));
            uv.Add(new Vector2(thickness, topY));
            uv.Add(new Vector2(thickness, bottomY));

            EmitQuad(vertices, triangles, start, start + 1, start + 2, start + 3, capNormal);
        }

        /// <summary>Horizontal path direction at sample i (central difference where possible).</summary>
        static Vector3 PathDirection(IReadOnlyList<FootprintSample> span, int i)
        {
            var prev = span[Mathf.Max(0, i - 1)].Position;
            var next = span[Mathf.Min(span.Count - 1, i + 1)].Position;
            var dir = next - prev;
            if (dir.sqrMagnitude < 1e-10f) dir = Vector2.right;
            dir.Normalize();
            return new Vector3(dir.x, 0f, dir.y);
        }

        /// <summary>
        /// Tangent along the path with w chosen so the bitangent (cross(n, t) * w) points up —
        /// +V is height in our UVs, so normal maps read correctly on both faces.
        /// </summary>
        static Vector4 TangentFor(Vector3 normal, Vector3 pathDir)
        {
            var w = Vector3.Dot(Vector3.Cross(normal, pathDir), Vector3.up) >= 0f ? 1f : -1f;
            return new Vector4(pathDir.x, pathDir.y, pathDir.z, w);
        }

        /// <summary>
        /// Emit quad (a, b, c, d) as two triangles wound so the rendered face points along
        /// <paramref name="desiredNormal"/> (cross(v1-v0, v2-v0) points toward the visible side —
        /// verified against the existing floor-cap convention). Self-correcting: no winding guesswork.
        /// </summary>
        static void EmitQuad(
            List<Vector3> vertices, List<int> triangles, int a, int b, int c, int d, Vector3 desiredNormal)
        {
            var geometric = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (Vector3.Dot(geometric, desiredNormal) >= 0f)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
            else
            {
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(a); triangles.Add(d); triangles.Add(c);
            }
        }
    }
}
