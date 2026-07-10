using RoomGen.Contracts;
using RoomGen.Generation;
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
            // Standing eye height with a LEVEL gaze: the ceiling line at the far wall stays in frame,
            // so a ceiling-height manipulation is visible in the preview (the old pose looked down
            // and cropped the ceiling — hiding the very variable the flagship pair manipulates).
            _camera.transform.position = new Vector3(0f, 1.6f, 2.6f);
            _camera.transform.LookAt(new Vector3(0f, 1.62f, -0.55f));
        }

        /// <summary>Rebuild the room from the spec and repaint the texture.</summary>
        public void Render(RoomSpec spec)
        {
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

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            if (Texture != null) { Texture.Release(); Object.Destroy(Texture); }
        }
    }
}
