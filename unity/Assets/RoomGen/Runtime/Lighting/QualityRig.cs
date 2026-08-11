using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RoomGen.Lighting
{
    /// <summary>
    /// A persistent, global HDRP post/quality volume: ACES tonemapping, screen-space
    /// ambient occlusion, bloom, and (forward-wired) screen-space GI. Created once at
    /// runtime and marked DontDestroyOnLoad so it survives the generator rebuilding rooms
    /// wholesale — the room volumes (per-room Exposure at priority 50) override on top.
    /// This is the L0 "quality pass": all settings live in code because the bootstrap-
    /// generated HDRP asset and scene are never committed.
    /// </summary>
    public static class QualityRig
    {
        // Global base layer. Lower priority than the per-room Exposure volume (50) so
        // room-specific exposure always wins; nothing here touches exposure.
        public const float VolumePriority = 0f;

        static GameObject _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            if (_root != null) return;
            _root = new GameObject("RoomGen Quality Rig");
            Object.DontDestroyOnLoad(_root);

            var volume = _root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            var profile = BuildProfile();
            volume.profile = profile;
            _root.AddComponent<QualityRigAssetOwner>().Asset = profile;

            // TEMP DISABLED for diagnosis: the fetch itself ran with zero errors (Console confirmed
            // "built=9 soft-skipped=0"), but the sky went magenta right after. HDRP shares a GPU
            // atlas between reflection probes and the sky renderer, and a custom probe wired up at
            // runtime (not through Unity's normal bake workflow) can corrupt that shared atlas. This
            // isolates whether the probe is the cause before touching anything else.
            // InstallOutdoorReflectionProbe();
        }

        /// <summary>
        /// Additive-only: gives glass/floor/any reflective surface a real photographed exterior
        /// (clouds, horizon shape) to reflect, instead of only the procedural sky's smooth gradient.
        /// Deliberately does NOT touch PhysicallyBasedSky, exposure, or SunSkySystem — those stay the
        /// sole light-PHYSICS driver, so this cannot reopen the calibration/exposure incoherence from
        /// earlier rounds. If the HDRI hasn't been fetched yet (RoomGen ▸ Fetch Outdoor HDRI), this
        /// no-ops silently — a fresh clone renders exactly as before, same soft-failure contract as
        /// AssetFetcher's textures.
        /// </summary>
        static void InstallOutdoorReflectionProbe()
        {
            var cubemap = Resources.Load<Cubemap>("RoomGen/HDRI/kloofendal_48d_partly_cloudy");
            if (cubemap == null) return; // not fetched yet — procedural sky reflections alone, as before

            var probeGo = new GameObject("Outdoor HDRI Reflection Probe");
            probeGo.transform.SetParent(_root.transform, false);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cubemap;
            probe.boxProjection = false; // a distant sky has no meaningful parallax to project
            // Sized to safely contain the largest room the validator allows (12x12x6, generously
            // margined) so this never needs touching as room dimensions change per-spec.
            probe.size = new Vector3(20f, 10f, 20f);
            probe.intensity = 1f;
        }

        /// <summary>
        /// Builds the quality VolumeProfile. Pure CPU object construction (no GPU / no
        /// active pipeline required), so it is unit-testable headlessly.
        /// </summary>
        public static VolumeProfile BuildProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;

            // Ray tracing is DXR-only: Windows + DX12 + an RTX-class GPU. HDRP has NO Metal /
            // Apple-Silicon path, and the participant machines are M2/M3 Macs — so probe the
            // hardware ONCE here and fall back to the screen-space (RayMarching) path everywhere
            // DXR is absent. Raster stays the default and the data-collection path; RT is an
            // optional tier for the demo PC (handoffs/RENDER_PATH_NOTE.md).
            var rayTracing = SystemInfo.supportsRayTracing;
            var tracingMode = rayTracing ? RayCastingMode.RayTracing : RayCastingMode.RayMarching;

            // Filmic tonemapping — the single biggest step away from the flat, clipped
            // look of an untonemapped HDRP frame.
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            // Ambient occlusion — contact shadows / grounding in corners and under furniture.
            // Ray traced on DXR-capable hardware (real geometry queries, no screen-space halo);
            // cameras without the RayTracing frame setting fall back to the screen-space path.
            var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true);
            ao.intensity.Override(0.6f);
            ao.radius.Override(0.5f);
            ao.rayTracing.Override(rayTracing);
            // Quality preset drives the RT denoiser (VolumeComponentWithQuality defaults to
            // Medium). High = widest RTAO denoiser radius — no speckle in the corners.
            ao.quality.Override((int)ScalableSettingLevelParameter.Level.High);

            // Subtle bloom on the luminaires; kept low so it reads as real light, not glow.
            // Scatter dropped 0.6 -> 0.3: at 0.6 a bright window's bloom smeared across the whole
            // pane into one featureless glow (Diego: "blurry pane"). Tighter scatter keeps bloom
            // localized around the actual bright source so a gradient/sun position stays visible.
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.15f);
            bloom.scatter.Override(0.3f);

            // In-room bounce light. supportSSGI is enabled on the asset (L0); the per-camera
            // SSGI frame setting (off by default in HDRP 17.3) is force-enabled by
            // CameraRealism.Apply on every camera the app creates, so this override is live.
            var gi = profile.Add<GlobalIllumination>(true);
            gi.enable.Override(true);
            // Ray-traced GI (DX12 + supportRayTracing are on): real multi-bounce light transport
            // instead of the screen-space approximation — off-screen surfaces contribute bounce,
            // sunlight through the windows actually lights the room. Cameras without the
            // RayTracing frame setting (preview thumbnails) fall back to raster/SSGI path.
            gi.tracing.Override(tracingMode);
            // Default quality (Medium) runs RTGI at HALF RESOLUTION (RTGIFullResolution:
            // Low/Med=false, High=true) — the upscale is the dark blotchy "warping" patches on
            // curved walls. High = full-resolution RTGI + full-res denoise: clean bounce light.
            gi.quality.Override((int)ScalableSettingLevelParameter.Level.High);

            // Screen-space reflections — real reflections on the floor, table tops and glass panes
            // instead of a flat matte surface. PBR-accumulation algorithm (best quality, max-desktop
            // target). The SSR + TransparentSSR frame settings are force-enabled per camera in
            // CameraRealism (walk/VR only), same wiring pattern as SSGI, so this override is live.
            var ssr = profile.Add<ScreenSpaceReflection>(true);
            ssr.enabled.Override(true);
            ssr.usedAlgorithm.Override(ScreenSpaceReflectionAlgorithm.PBRAccumulation);
            ssr.reflectSky.Override(true);      // sky/window light reflects off interior surfaces
            // Ray-traced reflections: mirror off-screen geometry too (screen-space can only
            // reflect what the camera already sees — the big "fake reflections" tell).
            ssr.tracing.Override(tracingMode);
            // High quality tier: full-resolution ray-traced reflections + best denoiser preset
            // (Medium is the default tier and denoises at reduced resolution).
            ssr.quality.Override((int)ScalableSettingLevelParameter.Level.High);

            // Shared ray-tracing behaviour. Extended culling matters in a room this small:
            // geometry just outside the camera frustum (behind the walker, the wall behind a
            // preview camera) must still exist for RT reflections/shadows/GI to be correct.
            var rt = profile.Add<RayTracingSettings>(true);
            rt.extendCameraCulling.Override(true);
            rt.extendShadowCulling.Override(true);

            // Volumetric fog: thin indoor participating medium so the sun through the windows
            // draws visible shafts (god-rays) and the recessed spots get gentle halos.
            // meanFreePath=35 was tuned when LightingSystem.SunTargetLuxFactor was 2.5 (a dim
            // interior sun). In-scatter brightness scales with the light passing through the medium,
            // so once the sun became real daylight (12x-20x, Phase 1) the SAME fog density scattered
            // ~5-8x more light and hazed the whole room to a flat, low-contrast milky wash — every
            // surface reading close to the same pale tone, windows blown solid white with no visible
            // sun disk or shaft (Diego's screenshot). Longer mean free path = thinner medium = less
            // in-scatter per unit light, so it's raised to match the brighter sun instead of masking
            // it with fog density that no longer fits the room's actual illuminance.
            var fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.enableVolumetricFog.Override(true);
            fog.meanFreePath.Override(150f);
            fog.anisotropy.Override(0.4f);      // forward scattering — shafts brighten toward the sun

            // Contact shadows: short-range screen-space occlusion exactly where geometry meets —
            // chair/table legs on the floor, furniture against walls. Shadowmaps blur out at this
            // scale, so without contact shadows furniture reads as floating (toy look). Length is
            // furniture-leg scale. Lights opt in per-light (LightingSystem).
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.Override(true);
            contact.length.Override(0.6f);
            contact.opacity.Override(0.7f);

            // Micro-shadowing: the sun's direction + the fetched normal/mask maps produce tiny
            // self-shadows inside wood grain, plaster and fabric weave — surface detail that flat
            // lighting cannot fake. Only meaningful now that AssetFetcher materials carry maps.
            var micro = profile.Add<MicroShadowing>(true);
            micro.enable.Override(true);
            micro.opacity.Override(0.85f);

            // Sky: Physically Based Sky (Diego's pick over gradient/HDRI). Code-only, no .exr asset,
            // VR-safe. Renders a real atmosphere + sun disk driven by the scene's directional light
            // (LightingSystem flags it interactsWithSky), so windows admit real daylight and the sun
            // reads as an actual disk — the foundation for light shafts through the openings.
            var env = profile.Add<VisualEnvironment>(true);
            env.skyType.Override((int)SkyType.PhysicallyBased);
            var sky = profile.Add<PhysicallyBasedSky>(true);
            sky.type.Override(PhysicallyBasedSkyModel.EarthSimple); // Earth atmosphere, sensible defaults
            // REVERTED a same-session sky.exposure override (was -3.5, meant to be a "sky-only"
            // dimmer). It is NOT sky-only: HDRP's sky exposure scales the cubemap that feeds the
            // ambient probe AND the sky reflection probe, so it also dims real skylight/GI.
            // LightingCalibrator.MatchTargetLux has no idea that happened and keeps reporting Ok=true
            // while windowed rooms actually sit under target lux at the fixed EV100 — silently
            // breaking the matched-luminance guarantee the whole single-variable comparison depends
            // on. It also fights SunTargetLuxFactor (LightingSystem's own "one legal knob" for
            // daylight scale) instead of moving with it, and at sun-hour extremes stacks with
            // SunSkySystem's elevation taper to black out the windows entirely. Back to 0 — daylight
            // scale has exactly one legal knob (SunTargetLuxFactor), not two independent ones.
            sky.exposure.Override(0f);

            return profile;
        }
    }

    /// <summary>Destroys the runtime-created VolumeProfile when the rig is torn down.</summary>
    sealed class QualityRigAssetOwner : MonoBehaviour
    {
        public Object Asset;

        void OnDestroy()
        {
            if (Asset == null) return;
            if (Application.isPlaying) Destroy(Asset);
            else DestroyImmediate(Asset);
        }
    }
}
