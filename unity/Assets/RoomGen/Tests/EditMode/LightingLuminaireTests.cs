using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RoomGen.Contracts;
using RoomGen.Generation;
using UnityEngine;

namespace RoomGen.Tests
{
    public sealed class LightingLuminaireTests
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
        public void Room_has_a_directional_sun()
        {
            var generator = Build(LoadControl());
            var sun = generator.transform.GetComponentsInChildren<Light>(true)
                .FirstOrDefault(l => l.type == LightType.Directional);
            Assert.IsNotNull(sun, "Expected a directional Sun light in the room.");
        }

        [Test]
        public void Pendant_spec_produces_a_point_luminaire()
        {
            var spec = LoadControl();
            spec.Furniture.Add(new FurniturePlacementSpec
            {
                AssetId = "builtin.pendant-light",
                SlotId = "pendant-center",
                PositionM = new Vec3 { X = 0f, Y = 0f, Z = 0f },
                CeilingMounted = true
            });

            var pendantLight = PendantLight(Build(spec));
            Assert.IsNotNull(pendantLight, "A pendant in the spec must create a Pendant Light.");
            Assert.AreEqual(LightType.Point, pendantLight.type, "Pendant light should be a point light.");
        }

        [Test]
        public void No_pendant_spec_has_no_pendant_light()
        {
            // The bundled dining pair declares no pendant, so no pendant luminaire should appear.
            Assert.IsNull(PendantLight(Build(LoadControl())),
                "Rooms without a pendant must not create a pendant light.");
        }

        static Light PendantLight(RoomGenerator generator)
            => generator.transform.GetComponentsInChildren<Light>(true)
                .FirstOrDefault(l => l.gameObject.name == "Pendant Light");

        RoomGenerator Build(RoomSpec spec)
        {
            var root = new GameObject("LuminaireTest");
            cleanup.Add(root);
            var generator = root.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            var result = generator.Build(spec);
            Assert.That(result.Root, Is.Not.Null, string.Join("\n", result.Warnings));
            return generator;
        }

        static RoomSpec LoadControl()
        {
            var asset = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-height-pair");
            Assert.That(asset, Is.Not.Null);
            return RoomJson.Deserialize<ConditionPairSpec>(asset.text).Control;
        }
    }
}
