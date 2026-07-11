using NUnit.Framework;
using RoomGen.Editor;
using RoomGen.Lighting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace RoomGen.Tests
{
    public class QualityRigTests
    {
        [Test]
        public void BuildProfile_has_aces_tonemapping_ssao_bloom_gi_ssr_and_pbr_sky()
        {
            var profile = QualityRig.BuildProfile();
            try
            {
                Assert.IsNotNull(profile, "BuildProfile returned null.");

                Assert.IsTrue(profile.TryGet<Tonemapping>(out var tone), "Tonemapping override missing.");
                Assert.AreEqual(TonemappingMode.ACES, tone.mode.value, "Tonemapping should be ACES.");

                Assert.IsTrue(profile.TryGet<ScreenSpaceAmbientOcclusion>(out _), "SSAO override missing.");
                Assert.IsTrue(profile.TryGet<Bloom>(out _), "Bloom override missing.");
                Assert.IsTrue(profile.TryGet<GlobalIllumination>(out _), "GlobalIllumination override missing.");

                Assert.IsTrue(profile.TryGet<ScreenSpaceReflection>(out var ssr), "SSR override missing.");
                Assert.AreEqual(ScreenSpaceReflectionAlgorithm.PBRAccumulation, ssr.usedAlgorithm.value,
                    "SSR should use PBR accumulation.");

                Assert.IsTrue(profile.TryGet<VisualEnvironment>(out var env), "VisualEnvironment override missing.");
                Assert.AreEqual((int)SkyType.PhysicallyBased, env.skyType.value, "Sky should be the physically based sky.");
                Assert.IsTrue(profile.TryGet<PhysicallyBasedSky>(out _), "PhysicallyBasedSky override missing.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Configure_enables_support_flags_and_bumps_shadow_atlas()
        {
            var asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            try
            {
                HdrpQualityConfigurator.Configure(asset);

                var so = new SerializedObject(asset);
                AssertBool(so, "m_RenderPipelineSettings.supportSSGI");
                AssertBool(so, "m_RenderPipelineSettings.supportSSAO");
                AssertBool(so, "m_RenderPipelineSettings.supportSSR");
                AssertBool(so, "m_RenderPipelineSettings.supportShadowMask");

                var atlas = so.FindProperty(
                    "m_RenderPipelineSettings.hdShadowInitParams.punctualLightShadowAtlas.shadowAtlasResolution");
                Assert.IsNotNull(atlas, "Shadow atlas resolution property missing.");
                Assert.AreEqual(HdrpQualityConfigurator.PunctualShadowAtlasResolution, atlas.intValue);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        static void AssertBool(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            Assert.IsNotNull(p, "Serialized property missing: " + path);
            Assert.IsTrue(p.boolValue, "Expected true: " + path);
        }
    }
}
