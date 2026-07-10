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

            // Screen-space ambient occlusion — contact shadows / grounding in corners and
            // under furniture. SSAO frame setting is on by default, so this renders now.
            var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true);
            ao.intensity.Override(0.6f);
            ao.radius.Override(0.5f);

            // Subtle bloom on the luminaires; kept low so it reads as real light, not glow.
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.15f);
            bloom.scatter.Override(0.6f);

            // In-room bounce light. supportSSGI is enabled on the asset (L0), but the SSGI
            // camera frame setting is off by default in HDRP 17.3 (it moved to GraphicsSettings),
            // so this override stays inert until the SSGI activation follow-up — which is the
            // right sequencing, since bounce only reads well once real albedos land in L1.
            var gi = profile.Add<GlobalIllumination>(true);
            gi.enable.Override(true);

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
