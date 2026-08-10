using RoomGen.Contracts;
using RoomGen.Generation;
using RoomGen.Lighting;
using UnityEngine;

namespace RoomGen.UI
{
    /// <summary>
    /// Renders one RoomSpec to a RenderTexture for an operator preview pane: a generator + a camera on
    /// a dedicated layer, rendered on demand. Two of these (control + treatment) drive the side-by-side
    /// live preview — the panel VIEW stays pure and only displays the textures.
    ///
    /// This is the composition point where the UI surface meets the generator (E1). It uses the
    /// generator directly for a responsive live feed rather than round-tripping screenshots through the
    /// seam; if strict lane-purity is wanted later it can move to an integration assembly without
    /// changing the panel. Camera pose matches the legacy IMGUI studio's preview.
    /// </summary>
    public sealed class PreviewRenderer
    {
        public RenderTexture Texture { get; }

        /// <summary>The last built room's GameObject (null before the first Render). The studio hands
        /// this to DesktopWalkMode so the operator can walk exactly the room the preview shows.</summary>
        public GameObject Root => _generator.LastResult?.Root;

        readonly GameObject _root;
        readonly RoomGenerator _generator;
        readonly Camera _camera;

        public PreviewRenderer(string label, int layer, int width = 720, int height = 460)
        {
            Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = label + " Preview RT",
                antiAliasing = 2
            };
            Texture.Create();

            _root = new GameObject("Preview [" + label + "]");
            var genGo = new GameObject("Generator");
            genGo.transform.SetParent(_root.transform, false);
            _generator = genGo.AddComponent<RoomGenerator>();
            _generator.SetGeneratedLayer(layer);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(_root.transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.cullingMask = 1 << layer;          // see only this preview's room
            _camera.targetTexture = Texture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.055f, 0.06f, 0.065f);
            _camera.fieldOfView = 74f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 25f;
            // Position is set per-spec in Render() below, NOT here — see PositionCamera for why a
            // fixed pose is a real bug, not just a framing choice.

            // Without this the preview camera never turns on SSGI/SSR/ray tracing at all — every
            // window rendered flat white with zero reflection, in every room, always, because nothing
            // ever enabled it here (Walk mode calls this and looks correct; the preview pane simply
            // never did). The legacy IMGUI studio deliberately left its OWN previews flat for a
            // different reason — they rebuilt the room on every single slider FRAME, so a reflection's
            // temporal history never had a stable frame to denoise against and it smeared. This studio
            // throttles rebuilds instead (RoomStudioViewModel.MinRebuildInterval, ~20 Hz) rather than
            // rebuilding every frame, so the room is stable far more often than it is mid-rebuild —
            // reflections should read correctly at rest. If dragging a slider shows visible flicker in
            // the reflection specifically (not the whole image), that is this tradeoff; report it and
            // it can be scoped back to SSR-without-full-RT.
            CameraRealism.Apply(_camera);
        }

        /// <summary>Rebuild the room from the spec and repaint the texture.</summary>
        public void Render(RoomSpec spec)
        {
            PositionCamera(spec);
            _generator.Build(spec);

            // HDRP allows only ONE shadow-casting directional light across the whole scene, but the
            // studio shows two rooms (control + treatment) at once, each with a sun. Drop the
            // directional shadow in previews (the recessed-spot shadows still ground the room) so two
            // live panes don't fight over the single directional shadow atlas.
            foreach (var light in _root.GetComponentsInChildren<Light>())
                if (light.type == LightType.Directional)
                    light.shadows = LightShadows.None;

            _camera.Render();
        }

        /// <summary>
        /// Stand just inside the back wall, facing the front (glazed) wall. Was a HARDCODED pose —
        /// (0, 1.6, 2.6) regardless of room size — which put the camera physically OUTSIDE the room
        /// (behind/through the front wall) the moment Length was dragged toward
        /// RoomSpecValidator.MinLengthM (3 m gives a 1.5 m half-length; the wall's interior face then
        /// sits in FRONT of where the fixed camera stood). That is what read as "the wall we're
        /// looking at doesn't have a wall, and the sun just comes through it" — the camera wasn't
        /// behind a missing wall, it was standing where the wall itself should have been. Recomputed
        /// every rebuild from the spec's actual length/bow, matching RoomStudioController's same fix.
        /// </summary>
        void PositionCamera(RoomSpec spec)
        {
            var g = spec.Geometry;
            var halfL = g.LengthM * 0.5f;
            var backBow = g.WallBow?.Back ?? 0f;
            var backInset = backBow < 0f ? -backBow * Mathf.Max(0f, g.BowMaxM) : 0f;
            var z = -(halfL - backInset - 0.45f); // 0.45 m clear of the worst-case back wall face
            var eyeY = Mathf.Min(1.58f, g.CeilingHeightM - 0.25f);
            _camera.transform.position = new Vector3(0f, eyeY, z);
            _camera.transform.LookAt(new Vector3(0f, Mathf.Min(1.32f, g.CeilingHeightM * 0.45f), halfL * 0.25f));
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            if (Texture != null) { Texture.Release(); Object.Destroy(Texture); }
        }
    }
}
