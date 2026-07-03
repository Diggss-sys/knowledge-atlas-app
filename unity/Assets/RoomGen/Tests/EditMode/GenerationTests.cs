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
            var path = FootprintPath.Build(geometry, 12);
            Assert.That(path.Count, Is.EqualTo(48));
            Assert.That(path.TrueForAll(point =>
                Mathf.Abs(point.x) <= geometry.WidthM * 0.5f + 0.0001f &&
                Mathf.Abs(point.y) <= geometry.LengthM * 0.5f + 0.0001f), Is.True);
            Assert.That(Vector2.Distance(path[0], path[path.Count - 1]), Is.GreaterThan(0.001f));
        }

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
            AddRows(root, root.name, rows);
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
