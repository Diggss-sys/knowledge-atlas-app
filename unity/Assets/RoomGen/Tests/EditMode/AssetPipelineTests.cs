using NUnit.Framework;
using RoomGen.Editor;
using UnityEngine;

namespace RoomGen.Tests
{
    public class AssetPipelineTests
    {
        [Test]
        public void PackMaskTexel_maps_channels_and_inverts_roughness()
        {
            // roughness 51 -> smoothness 204; AO passthrough; metalness passthrough; detail neutral 128.
            var c = AssetFetcher.PackMaskTexel(roughness: 51, ambientOcclusion: 200, metalness: 10);
            Assert.AreEqual(10, c.r, "R must be metalness.");
            Assert.AreEqual(200, c.g, "G must be ambient occlusion.");
            Assert.AreEqual(128, c.b, "B must be neutral detail mask.");
            Assert.AreEqual(204, c.a, "A must be smoothness = 255 - roughness.");
        }

        [Test]
        public void PackMaskTexel_defaults_are_dielectric_unoccluded()
        {
            // A material with no AO/metalness map packs metal=0, AO=255 (fully lit), smoothness from roughness.
            var c = AssetFetcher.PackMaskTexel(roughness: 0, ambientOcclusion: 255, metalness: 0);
            Assert.AreEqual(0, c.r);
            Assert.AreEqual(255, c.g);
            Assert.AreEqual(255, c.a, "roughness 0 -> smoothness 255.");
        }

        // Verifies the fetcher's output when materials have actually been generated locally.
        // Skips cleanly on a clone where the fetcher has not run (flat builtin, no BaseColorMap).
        [Test]
        public void Fetched_floor_material_is_triplanar_with_maskmap()
        {
            var mat = Resources.Load<Material>("RoomGen/Materials/builtin-oak");
            if (mat == null)
                Assert.Ignore("builtin-oak material not present (bootstrap not run).");
            if (mat.GetTexture("_BaseColorMap") == null)
                Assert.Ignore("builtin-oak is the flat builtin (AssetFetcher not run / no textures).");

            Assert.AreEqual(5f, mat.GetFloat("_UVBase"), "Expected Triplanar (_UVBase = 5).");
            Assert.IsTrue(mat.IsKeywordEnabled("_MAPPING_TRIPLANAR"), "Triplanar keyword missing.");
            Assert.AreEqual(0f, mat.GetFloat("_ObjectSpaceUVMapping"), "Expected world-space mapping.");
            Assert.IsTrue(mat.IsKeywordEnabled("_MASKMAP"), "MaskMap keyword missing.");
            Assert.IsNotNull(mat.GetTexture("_MaskMap"), "MaskMap texture missing.");
            Assert.IsNotNull(mat.GetTexture("_NormalMap"), "NormalMap texture missing.");
        }
    }
}
