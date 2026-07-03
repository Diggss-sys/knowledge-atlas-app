using System.Collections;
using RoomGen.Contracts;
using RoomGen.Generation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace RoomGen.VR
{
    public sealed class VrExplorationMode : MonoBehaviour
    {
        Transform rig;
        Camera vrCamera;
        bool running;
        int roomLayer;

        public bool IsRunning => running;

        public void Toggle(int layer, RoomSpec spec = null)
        {
            if (running) StopSession();
            else StartCoroutine(StartSession(layer, spec));
        }

        IEnumerator StartSession(int layer, RoomSpec spec)
        {
            roomLayer = layer;
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
                yield break;

            if (settings.Manager.activeLoader == null)
                yield return settings.Manager.InitializeLoader();
            if (settings.Manager.activeLoader == null)
                yield break;

            settings.Manager.StartSubsystems();
            CreateRig(spec);
            RenderQualityProfiles.ApplyVr();
            running = true;
            Cursor.visible = false;
        }

        public void StopSession()
        {
            var settings = XRGeneralSettings.Instance;
            if (settings != null && settings.Manager != null)
            {
                settings.Manager.StopSubsystems();
                settings.Manager.DeinitializeLoader();
            }
            if (rig != null) Destroy(rig.gameObject);
            rig = null;
            vrCamera = null;
            running = false;
            RenderQualityProfiles.ApplyDesktop();
            Cursor.visible = true;
        }

        void CreateRig(RoomSpec spec)
        {
            var rigObject = new GameObject("VR Exploration Rig");
            rig = rigObject.transform;
            rig.SetParent(transform, false);
            
            if (spec != null)
            {
                // Dynamically start a bit back from the center of the room
                rig.localPosition = new Vector3(0f, 0f, -spec.Geometry.LengthM * 0.35f);
            }
            else
            {
                rig.localPosition = new Vector3(0f, 0f, 2.25f);
            }

            var cameraObject = new GameObject("VR Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(rig, false);
            vrCamera = cameraObject.AddComponent<Camera>();
            vrCamera.nearClipPlane = 0.05f;
            vrCamera.farClipPlane = 40f;
            vrCamera.cullingMask = 1 << roomLayer;
            vrCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            cameraObject.AddComponent<AudioListener>();
        }

        void Update()
        {
            if (!running || rig == null || vrCamera == null) return;

            var headPosition = InputTracking.GetLocalPosition(XRNode.Head);
            var headRotation = InputTracking.GetLocalRotation(XRNode.Head);
            vrCamera.transform.localPosition = headPosition;
            vrCamera.transform.localRotation = headRotation;

            var input = Vector2.zero;
            if (Gamepad.current != null)
                input += Gamepad.current.leftStick.ReadValue();
            if (Keyboard.current != null)
            {
                input.x += (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                           (Keyboard.current.aKey.isPressed ? 1f : 0f);
                input.y += (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                           (Keyboard.current.sKey.isPressed ? 1f : 0f);
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    StopSession();
                    return;
                }
            }

            var forward = Vector3.ProjectOnPlane(vrCamera.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(vrCamera.transform.right, Vector3.up).normalized;
            rig.position += (forward * input.y + right * input.x) * (1.5f * Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            if (running) StopSession();
        }
    }
}
