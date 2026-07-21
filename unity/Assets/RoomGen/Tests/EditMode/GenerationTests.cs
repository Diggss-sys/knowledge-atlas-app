using System.Collections.Generic;
using NUnit.Framework;
using RoomGen.Contracts;
using RoomGen.Generation;
using UnityEngine;

namespace RoomGen.Tests
{
    public sealed class GenerationTests
    {
        readonly List<GameObject> cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in cleanup)
                if (item != null) Object.DestroyImmediate(item);
            cleanup.Clear();
            foreach (var mesh in cleanupMeshes)
                if (mesh != null) Object.DestroyImmediate(mesh);
            cleanupMeshes.Clear();
        }

        [Test]
        public void SameSpecProducesIdenticalHierarchyAndTransforms()
        {
            var pair = LoadExamplePair();
            var left = Build(pair.Control, "Left");
            var right = Build(pair.Control, "Right");
            CollectionAssert.AreEqual(Snapshot(left.transform), Snapshot(right.transform));
        }

        [Test]
        public void RoundedFootprintIsClosedAndWithinBounds()
        {
            var geometry = new GeometrySpec
            {
                WidthM = 5.4f,
                LengthM = 6.2f,
                CornerRadiusM = 0.8f
            };
            // Tessellation is chord-error-driven now (RENDERING_RESEARCH §4): assert geometry
            // properties, not a hardcoded segment count.
            var path = FootprintPath.Build(geometry);
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(4 * 3));
            Assert.That(path.TrueForAll(point =>
                Mathf.Abs(point.x) <= geometry.WidthM * 0.5f + 0.0001f &&
                Mathf.Abs(point.y) <= geometry.LengthM * 0.5f + 0.0001f), Is.True);
            Assert.That(Vector2.Distance(path[0], path[path.Count - 1]), Is.GreaterThan(0.001f));
            // Area must match the rounded-rect closed form within tessellation error.
            var expected = geometry.WidthM * geometry.LengthM
                           - (4f - Mathf.PI) * geometry.CornerRadiusM * geometry.CornerRadiusM;
            Assert.That(Mathf.Abs(EarClip.SignedArea(path)), Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void BowedWallChangesFootprintAreaInTheRightDirection()
        {
            var flat = new GeometrySpec { WidthM = 5f, LengthM = 6f };
            var concave = new GeometrySpec
            {
                WidthM = 5f, LengthM = 6f,
                WallBow = new WallBowSpec { Back = -1f }, BowMaxM = 0.6f
            };
            var convex = new GeometrySpec
            {
                WidthM = 5f, LengthM = 6f,
                WallBow = new WallBowSpec { Back = 1f }, BowMaxM = 0.6f
            };

            var flatArea = Mathf.Abs(EarClip.SignedArea(FootprintPath.Build(flat)));
            var concaveArea = Mathf.Abs(EarClip.SignedArea(FootprintPath.Build(concave)));
            var convexArea = Mathf.Abs(EarClip.SignedArea(FootprintPath.Build(convex)));

            Assert.That(concaveArea, Is.LessThan(flatArea - 0.1f), "concave bow must EAT floor area");
            Assert.That(convexArea, Is.GreaterThan(flatArea + 0.1f), "convex bow must ADD floor area");
        }

        [Test]
        public void EarClipTriangulatesConcaveFootprintCompletely()
        {
            var concave = new GeometrySpec
            {
                WidthM = 5f, LengthM = 6f,
                WallBow = new WallBowSpec { Back = -1f, Left = -0.5f }, BowMaxM = 0.6f
            };
            var ring = FootprintPath.Build(concave);
            var triangles = EarClip.Triangulate(ring);

            // n-2 triangles for a simple polygon, and triangle area must reconstruct ring area.
            Assert.That(triangles.Count, Is.EqualTo((ring.Count - 2) * 3));
            var triangleArea = 0f;
            for (var t = 0; t < triangles.Count; t += 3)
            {
                var a = ring[triangles[t]];
                var b = ring[triangles[t + 1]];
                var c = ring[triangles[t + 2]];
                triangleArea += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            }
            Assert.That(triangleArea, Is.EqualTo(Mathf.Abs(EarClip.SignedArea(ring))).Within(0.01f));
        }

        [Test]
        public void WallBandMeshHasUnitNormalsAndTangents()
        {
            var geometry = new GeometrySpec
            {
                WidthM = 5f, LengthM = 6f,
                WallBow = new WallBowSpec { Back = 1f }, BowMaxM = 0.6f
            };
            var span = FootprintPath.BuildWallSpan(geometry, "back");
            var mesh = WallBand.BuildMesh(span, 0f, 2.6f, 0.15f, "Test Band");
            cleanupMeshes.Add(mesh);

            Assert.That(span.Count, Is.GreaterThanOrEqualTo(3), "bowed wall must tessellate");
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.triangles.Length, Is.GreaterThan(0));
            Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.tangents.Length, Is.EqualTo(mesh.vertexCount));
            foreach (var normal in mesh.normals)
                Assert.That(normal.magnitude, Is.EqualTo(1f).Within(0.001f));
            // Arc-length UVs: u must be strictly non-decreasing along the span.
            var uv = mesh.uv;
            Assert.That(uv[uv.Length - 1].x, Is.GreaterThan(0f));
        }

        [Test]
        public void WallBandIsSealedWithEndCaps()
        {
            // A bowed band must be watertight: the two open ends (inner skin -> outer skin) are
            // capped so a neighbour meeting it at an angle never reveals the hollow ("crevice").
            var geometry = new GeometrySpec
            {
                WidthM = 5f, LengthM = 6f,
                WallBow = new WallBowSpec { Back = 1f }, BowMaxM = 0.6f
            };
            var span = FootprintPath.BuildWallSpan(geometry, "back");
            var capped = WallBand.BuildMesh(span, 0f, 2.6f, 0.15f, "Capped");
            cleanupMeshes.Add(capped);

            // Each end cap adds 4 verts + 6 indices over the uncapped inner/outer/top band.
            var bandVerts = span.Count * 6;          // inner(2) + outer(2) + top-cap(2) per sample
            Assert.That(capped.vertexCount, Is.EqualTo(bandVerts + 8), "two 4-vert end caps expected");
            // Both end-cap normals must be horizontal (point along the wall, not up/outward).
            var normals = capped.normals;
            for (var i = bandVerts; i < capped.vertexCount; i++)
                Assert.That(Mathf.Abs(normals[i].y), Is.LessThan(0.001f), "end-cap normal must be horizontal");
        }

        [Test]
        public void OpeningOnBowedWallIsAccepted()
        {
            // v1.1: openings on bowed walls are supported (arc-cut sill/header bands + curved
            // glass/trim), so the validator must NOT refuse them any more.
            var pair = LoadExamplePair();
            var spec = pair.Control;
            spec.Geometry.WallBow = new WallBowSpec { Left = -0.8f };
            spec.Geometry.BowMaxM = 0.6f;
            var hasLeftOpening = spec.Openings.Exists(o =>
                string.Equals(o.Wall, "left", System.StringComparison.OrdinalIgnoreCase));
            if (!hasLeftOpening)
                spec.Openings.Add(new OpeningSpec { OpeningId = "test-window", Kind = "window", Wall = "left", CenterM = 0f, WidthM = 1.2f, BottomM = 0.9f, TopM = 2f });

            var issues = Validation.RoomSpecValidator.Validate(spec);
            Assert.That(issues.Exists(issue => issue.Code == "OPENING_ON_BOWED_WALL"), Is.False,
                string.Join("\n", issues.ConvertAll(i => i.Code + " " + i.Path)));
        }

        [Test]
        public void SubSpanCutsOpeningRangeFromBowedWall()
        {
            var geometry = new GeometrySpec
            {
                WidthM = 5f, LengthM = 6f, CeilingHeightM = 2.6f, WallThicknessM = 0.15f,
                WallBow = new WallBowSpec { Left = -0.8f }, BowMaxM = 0.5f
            };
            var span = FootprintPath.BuildWallSpan(geometry, "left");
            // Window 1.2 m wide centred at z = 0.5 on the left wall (side wall coord = z).
            var sub = FootprintPath.SubSpan(span, true, -0.1f, 1.1f);

            Assert.That(sub.Count, Is.GreaterThanOrEqualTo(2), "sub-span must keep arc tessellation");
            // Interpolated cut points must sit exactly on the requested boundaries.
            var coords = sub.ConvertAll(s => s.Position.y);
            Assert.That(Mathf.Min(coords.ToArray()), Is.EqualTo(-0.1f).Within(0.002f));
            Assert.That(Mathf.Max(coords.ToArray()), Is.EqualTo(1.1f).Within(0.002f));
            // And the sub-span mesh must build watertight.
            var mesh = WallBand.BuildMesh(sub, 0.9f, 2f, 0.15f, "SubSpan Band");
            cleanupMeshes.Add(mesh);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.triangles.Length, Is.GreaterThan(0));
        }

        readonly List<Mesh> cleanupMeshes = new List<Mesh>();

        [Test]
        public void TreatmentRegeneratesCeilingWithoutScalingRoot()
        {
            var pair = LoadExamplePair();
            var generator = Build(pair.Treatment, "Treatment");
            Assert.That(generator.transform.localScale, Is.EqualTo(Vector3.one));
            var ceiling = generator.transform.Find("Generated Room/01 Shell/Ceiling");
            Assert.That(ceiling, Is.Not.Null);
            Assert.That(ceiling.localScale, Is.EqualTo(Vector3.one));
        }

        RoomGenerator Build(RoomSpec spec, string name)
        {
            var root = new GameObject(name);
            cleanup.Add(root);
            var generator = root.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            var result = generator.Build(spec);
            Assert.That(result.Root, Is.Not.Null, string.Join("\n", result.Warnings));
            return generator;
        }

        static List<string> Snapshot(Transform root)
        {
            var rows = new List<string>();
            // Snapshot under a constant label: the two builds are deliberately named "Left"/"Right",
            // so including the real root name would make every row differ by construction.
            AddRows(root, "ROOT", rows);
            return rows;
        }

        static void AddRows(Transform current, string path, List<string> rows)
        {
            rows.Add($"{path}|{current.localPosition:F5}|{current.localRotation:F5}|{current.localScale:F5}");
            for (var i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                AddRows(child, path + "/" + child.name, rows);
            }
        }

        static ConditionPairSpec LoadExamplePair()
        {
            var asset = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            Assert.That(asset, Is.Not.Null);
            return RoomJson.Deserialize<ConditionPairSpec>(asset.text);
        }
    }
}
