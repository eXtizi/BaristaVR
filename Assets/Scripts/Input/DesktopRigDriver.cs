using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Barista
{
    /// <summary>
    /// Keyboard-and-mouse fallback for the exported Windows build, where neither a headset nor the
    /// XR Device Simulator is present. It adds mouse/keyboard bindings to the *same* XRI actions the
    /// interactors already read (Select, Activate, Snap Turn), then poses the head and right hand:
    /// the hand sits on the mouse ray at an adjustable reach, so near and far grabs work as in VR.
    /// </summary>
    public class DesktopRigDriver : MonoBehaviour
    {
        public InputActionAsset desktopActions;
        public GameObject desktopHelp;
        public float eyeHeight = 1.2f;
        public float lookSensitivity = 0.12f;
        public float startReach = 0.55f;
        public float briefingPitch = -20f;
        public float workPitch = 18f;
        [Tooltip("Run even inside the Editor when no headset is active (normally the Simulator is used).")]
        public bool forceInEditor;

        const string k_Group = "KeyboardMouse";

        XROrigin m_Origin;
        Transform m_Head;
        Camera m_Camera;
        Transform m_Right;
        Transform m_Left;
        InputAction m_Look, m_LookHold, m_Point, m_Reach, m_Tilt, m_OwnSnapTurn;
        float m_Yaw, m_Pitch, m_Reach01, m_TiltAngle;
        bool m_XriHandlesSnapTurn;

        void Start()
        {
            if (!ShouldRun())
            {
                if (desktopHelp != null)
                    desktopHelp.SetActive(false);
                enabled = false;
                return;
            }

            m_Origin = FindAnyObjectByType<XROrigin>();
            if (m_Origin == null || desktopActions == null)
            {
                Debug.LogWarning("DesktopRigDriver: no XR Origin or desktop actions; fallback disabled.");
                enabled = false;
                return;
            }

            m_Camera = m_Origin.Camera;
            m_Head = m_Camera.transform;
            foreach (var t in m_Origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Right Controller") m_Right = t;
                else if (t.name == "Left Controller") m_Left = t;
            }

            TakeOverTracking();
            AddFallbackBindings();

            var map = desktopActions.FindActionMap("Desktop", true);
            m_Look = map.FindAction("Look", true);
            m_LookHold = map.FindAction("LookHold", true);
            m_Point = map.FindAction("Point", true);
            m_Reach = map.FindAction("Reach", true);
            m_Tilt = map.FindAction("Tilt", true);
            m_OwnSnapTurn = map.FindAction("SnapTurn", true);
            map.Enable();

            m_Reach01 = startReach;

            // The welcome board sits above the machine; look up at it, then down at the counter after START.
            var flow = ShiftFlow.Instance;
            if (flow != null)
            {
                m_Pitch = flow.Current == Step.Briefing ? briefingPitch : workPitch;
                flow.StepChanged += OnStepChanged;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (desktopHelp != null)
                desktopHelp.SetActive(true);
        }

        void OnDestroy()
        {
            if (ShiftFlow.Instance != null)
                ShiftFlow.Instance.StepChanged -= OnStepChanged;
        }

        void OnStepChanged(Step step)
        {
            if (step == Step.Detach && m_Pitch < 0f)
                m_Pitch = workPitch;
        }

        bool ShouldRun()
        {
            if (XRSettings.isDeviceActive)
                return false;
            if (Application.isEditor && !forceInEditor)
            {
                foreach (var mb in FindObjectsByType<MonoBehaviour>())
                {
                    var n = mb.GetType().Name;
                    if (n == "XRDeviceSimulator" || n == "XRInteractionSimulator")
                        return false;
                }
            }
            return true;
        }

        void TakeOverTracking()
        {
            // Without tracked devices the input modality manager would hide the controllers.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb.GetType().Name == "XRInputModalityManager")
                    mb.enabled = false;
            }
            foreach (var tpd in m_Origin.GetComponentsInChildren<UnityEngine.InputSystem.XR.TrackedPoseDriver>(true))
                tpd.enabled = false;
            if (m_Right != null) m_Right.gameObject.SetActive(true);
            if (m_Left != null) m_Left.gameObject.SetActive(true);
        }

        void AddFallbackBindings()
        {
            var enabledActions = InputSystem.ListEnabledActions();
            AddBinding(enabledActions, "XRI Right Interaction", "Select", "<Mouse>/leftButton");
            AddBinding(enabledActions, "XRI Right Interaction", "Select Value", "<Mouse>/leftButton");
            AddBinding(enabledActions, "XRI Right Interaction", "Activate", "<Keyboard>/f");
            AddBinding(enabledActions, "XRI Right Interaction", "Activate Value", "<Keyboard>/f");

            var snap = Find(enabledActions, "XRI Right Locomotion", "Snap Turn");
            if (snap != null)
            {
                WithDisabled(snap, () => snap.AddCompositeBinding("2DVector")
                    .With("left", "<Keyboard>/q", k_Group)
                    .With("right", "<Keyboard>/e", k_Group));
                m_XriHandlesSnapTurn = true;
            }
        }

        static InputAction Find(List<InputAction> actions, string map, string name)
        {
            foreach (var a in actions)
            {
                if (a.actionMap != null && a.actionMap.name == map && a.name == name)
                    return a;
            }
            return null;
        }

        static void AddBinding(List<InputAction> actions, string map, string name, string path)
        {
            var action = Find(actions, map, name);
            if (action == null)
            {
                Debug.LogWarning($"DesktopRigDriver: action {map}/{name} not found.");
                return;
            }
            foreach (var b in action.bindings)
            {
                if (b.path == path)
                    return;
            }
            WithDisabled(action, () => action.AddBinding(path, groups: k_Group));
        }

        static void WithDisabled(InputAction action, System.Action change)
        {
            var wasEnabled = action.enabled;
            if (wasEnabled) action.Disable();
            change();
            if (wasEnabled) action.Enable();
        }

        void Update()
        {
            var originT = m_Origin.transform;

            if (!m_XriHandlesSnapTurn && m_OwnSnapTurn.WasPressedThisFrame())
                m_Origin.RotateAroundCameraUsingOriginUp(45f * Mathf.Sign(m_OwnSnapTurn.ReadValue<float>()));

            if (m_LookHold.IsPressed())
            {
                var d = m_Look.ReadValue<Vector2>();
                m_Yaw += d.x * lookSensitivity;
                m_Pitch = Mathf.Clamp(m_Pitch - d.y * lookSensitivity, -70f, 70f);
            }

            var scroll = m_Reach.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f)
                m_Reach01 = Mathf.Clamp(m_Reach01 + Mathf.Sign(scroll) * 0.03f, 0.2f, 1.1f);

            m_TiltAngle = Mathf.MoveTowards(m_TiltAngle, m_Tilt.IsPressed() ? 100f : 0f, 160f * Time.deltaTime);

            m_Head.SetPositionAndRotation(
                originT.position + originT.up * eyeHeight,
                originT.rotation * Quaternion.Euler(m_Pitch, m_Yaw, 0f));

            if (m_Right != null)
            {
                var ray = m_Camera.ScreenPointToRay(m_Point.ReadValue<Vector2>());
                m_Right.SetPositionAndRotation(
                    ray.GetPoint(m_Reach01),
                    Quaternion.LookRotation(ray.direction, m_Head.up) * Quaternion.Euler(m_TiltAngle, 0f, 0f));
            }

            if (m_Left != null)
                m_Left.SetPositionAndRotation(m_Head.position - m_Head.up * 0.5f - m_Head.right * 0.3f, m_Head.rotation);
        }
    }
}
