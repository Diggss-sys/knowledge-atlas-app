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
        }

        /// <summary>
        /// Builds the quality VolumeProfile. Pure CPU object construction (no GPU / no
        /// active pipeline required), so it is unit-testable headlessly.
        /// </summary>
        public static VolumeProfile BuildProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;

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
            ao.rayTracing.Override(true);
            // Quality preset drives the RT denoiser (VolumeComponentWithQuality defaults to
            // Medium). High = widest RTAO denoiser radius — no speckle in the corners.
            ao.quality.Override((int)ScalableSettingLevelParameter.Level.High);

            // Subtle bloom on the luminaires; kept low so it reads as real light, not glow.
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.15f);
            bloom.scatter.Override(0.6f);

            // In-room bounce light. supportSSGI is enabled on the asset (L0); the per-camera
            // SSGI frame setting (off by default in HDRP 17.3) is force-enabled by
            // CameraRealism.Apply on every camera the app creates, so this override is live.
            var gi = profile.Add<GlobalIllumination>(true);
            gi.enable.Override(true);
            // Ray-traced GI (DX12 + supportRayTracing are on): real multi-bounce light transport
            // instead of the screen-space approximation — off-screen surfaces contribute bounce,
            // sunlight through the windows actually lights the room. Cameras without the
            // RayTracing frame setting (preview thumbnails) fall back to raster/SSGI path.
            gi.tracing.Override(RayCastingMode.RayTracing);
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
            ssr.tracing.Override(RayCastingMode.RayTracing);
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
            // draws visible shafts (god-rays) and the recessed spots get gentle halos. Mean free
            // path is short enough to read indoors, long enough not to haze the whole room.
            var fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.enableVolumetricFog.Override(true);
            fog.meanFreePath.Override(35f);
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
            // Interior exposure is FIXED (LightingSystem, per room). If the calibrated recessed spots
            // still wash out, nudge this sky exposure DOWN (was the gradient's job at 13.5) — physical
            // luminance means the sky is already bright without a large compensation.
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
