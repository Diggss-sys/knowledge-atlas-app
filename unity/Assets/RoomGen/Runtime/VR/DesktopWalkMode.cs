using System.Collections.Generic;
using RoomGen.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoomGen.VR
{
    /// <summary>
    /// First-person desktop walkthrough — mouse-look + WASD, no VR headset required.
    /// The desktop-first companion to <see cref="VrExplorationMode"/> (handoffs/UNITY_GENERATOR.md G1):
    /// VrExplorationMode needs an active XR loader, so it does nothing without a headset — this is
    /// the mode you actually walk the room in during development and for the Aug-1 demo.
    ///
    /// The control and treatment rooms are generated overlapping at the world origin (they differ only
    /// by layer + camera culling), so for a session we disable colliders that are NOT on the room's
    /// layer — you collide only with the room you're actually walking, never the phantom other one.
    /// </summary>
    public sealed class DesktopWalkMode : MonoBehaviour
    {
        const float EyeHeight = 1.62f;   // camera height above the rig floor; ~standing eye height
        const float MoveSpeed = 1.6f;    // metres/second — calm indoor pace (matches VR mode)
        const float LookSpeed = 0.11f;   // degrees per mouse-count; tune to taste
        const float Gravity = 9.81f;
        const float PitchLimit = 85f;

        Transform rig;
        CharacterController controller;
        Camera walkCamera;
        float pitch;
        float verticalVelocity;
        bool running;
        readonly List<Collider> suppressed = new List<Collider>();

        public bool IsRunning => running;

        /// <summary>Enter/exit a desktop walk of <paramref name="roomRoot"/> (culled to <paramref name="layer"/>).</summary>
        public void Toggle(int layer, GameObject roomRoot, RoomSpec spec = null)
        {
            if (running) StopSession();
            else StartSession(layer, roomRoot, spec);
        }

        void StartSession(int layer, GameObject roomRoot, RoomSpec spec)
        {
            if (roomRoot == null) return;          // room not built yet — nothing to walk

            EnsureColliders(roomRoot);             // belt-and-braces: colliders on any furniture mesh missing one
            SuppressOtherLayers(layer);            // disable the overlapping other room's colliders for this session
            RenderQualityProfiles.ApplyDesktop();
            CreateRig(layer, spec);

            running = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void StopSession()
        {
            foreach (var col in suppressed)
                if (col != null) col.enabled = true;
            suppressed.Clear();

            if (rig != null) Destroy(rig.gameObject);
            rig = null;
            controller = null;
            walkCamera = null;
            pitch = 0f;
            verticalVelocity = 0f;
            running = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void CreateRig(int layer, RoomSpec spec)
        {
            // Rig sits on the room's layer so the "suppress other layers" pass leaves its
            // CharacterController enabled, and it collides with the same-layer room geometry.
            var rigObject = new GameObject("Desktop Walk Rig") { layer = layer };
            rig = rigObject.transform;
            rig.SetParent(transform, false);
            var startZ = spec != null ? -spec.Geometry.LengthM * 0.32f : -2f;
            rig.localPosition = new Vector3(0f, 0.1f, startZ);   // start a bit back, just above the floor

            controller = rigObject.AddComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.25f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.3f;

            var cameraObject = new GameObject("Desktop Walk Camera") { layer = layer };
            cameraObject.transform.SetParent(rig, false);
            cameraObject.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            walkCamera = cameraObject.AddComponent<Camera>();
            walkCamera.nearClipPlane = 0.05f;
            walkCamera.farClipPlane = 40f;
            walkCamera.fieldOfView = 70f;
            walkCamera.cullingMask = 1 << layer;   // see only the room we're walking
            cameraObject.AddComponent<AudioListener>();
        }

        void Update()
        {
            if (!running || rig == null || controller == null) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                StopSession();
                return;
            }

            // Mouse-look: yaw the rig, pitch the camera (clamped).
            if (Mouse.current != null)
            {
                var look = Mouse.current.delta.ReadValue();
                rig.Rotate(0f, look.x * LookSpeed, 0f, Space.Self);
                pitch = Mathf.Clamp(pitch - look.y * LookSpeed, -PitchLimit, PitchLimit);
                walkCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // WASD (+ optional gamepad stick) → horizontal move in the rig's facing.
            var input = Vector2.zero;
            if (Keyboard.current != null)
            {
                input.x += (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
                input.y += (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
            }
            if (Gamepad.current != null) input += Gamepad.current.leftStick.ReadValue();
            input = Vector2.ClampMagnitude(input, 1f);

            var move = (rig.forward * input.y + rig.right * input.x) * MoveSpeed;

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -1f;   // stick to floor
            verticalVelocity -= Gravity * Time.deltaTime;
            move.y = verticalVelocity;

            controller.Move(move * Time.deltaTime);
        }

        static void EnsureColliders(GameObject roomRoot)
        {
            foreach (var filter in roomRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponent<Collider>() != null) continue;   // primitives + CreateMeshObject already have one
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
        }

        void SuppressOtherLayers(int keepLayer)
        {
            suppressed.Clear();
            foreach (var col in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (col == null || !col.enabled) continue;
                if (col.gameObject.layer == keepLayer) continue;
                col.enabled = false;
                suppressed.Add(col);
            }
        }

        void OnDestroy()
        {
            if (running) StopSession();
        }
    }
}
