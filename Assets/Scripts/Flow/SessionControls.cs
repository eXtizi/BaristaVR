using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Barista
{
    /// <summary>
    /// Always-available station controls: slide along the counter, snap turn, reset the shift, and exit.
    /// World buttons work in the simulator and on a headset. Arrow keys, Z/C, R and Esc do the same
    /// job when a keyboard is present.
    /// </summary>
    public class SessionControls : MonoBehaviour
    {
        public PressButton slideLeft;
        public PressButton slideRight;
        public PressButton turnLeft;
        public PressButton turnRight;
        public PressButton resetButton;
        public PressButton exitButton;
        public PressButton creditsButton;
        public GameObject creditsBoard;

        public float slideSpeed = 0.55f;
        public float minX = -1.05f;
        public float maxX = 1.05f;
        public float minZ = -0.15f;
        public float maxZ = 0.3f;
        public float turnDegrees = 45f;
        [Tooltip("Reset and Exit need a second press within this many seconds.")]
        public float confirmWindow = 3f;

        XROrigin m_Origin;
        PressButton m_Armed;
        float m_ArmedUntil;
        string m_ArmedLabel;

        IEnumerator Start()
        {
            m_Origin = FindAnyObjectByType<XROrigin>();
            if (resetButton != null)
                resetButton.Pressed += _ => Confirm(resetButton, ShiftFlow.Restart);
            if (exitButton != null)
                exitButton.Pressed += _ => Confirm(exitButton, Exit);
            if (turnLeft != null)
                turnLeft.Pressed += _ => Turn(-1f);
            if (turnRight != null)
                turnRight.Pressed += _ => Turn(1f);
            if (creditsButton != null && creditsBoard != null)
                creditsButton.Pressed += _ => creditsBoard.SetActive(!creditsBoard.activeSelf);

            // The simulator instruction sheet starts open and covers the controllers.
            // Close it after it has finished its own Start.
            yield return null;
            CollapseSimulatorPanel();
            yield return new WaitForSeconds(0.3f);
            CollapseSimulatorPanel();
        }

        void Confirm(PressButton button, System.Action action)
        {
            if (m_Armed == button && Time.time < m_ArmedUntil)
            {
                Disarm();
                action();
                return;
            }

            Disarm();
            var label = button.GetComponentInChildren<TextMesh>();
            if (label == null)
            {
                action();
                return;
            }
            m_Armed = button;
            m_ArmedUntil = Time.time + confirmWindow;
            m_ArmedLabel = label.text;
            label.text = "PRESS AGAIN";
        }

        void Disarm()
        {
            if (m_Armed != null)
            {
                var label = m_Armed.GetComponentInChildren<TextMesh>();
                if (label != null)
                    label.text = m_ArmedLabel;
            }
            m_Armed = null;
        }

        void Update()
        {
            if (m_Armed != null && Time.time >= m_ArmedUntil)
                Disarm();

            if (m_Origin == null)
                return;

            var slide = 0f;
            var depth = 0f;
            if (slideLeft != null && slideLeft.IsHeld)
                slide -= 1f;
            if (slideRight != null && slideRight.IsHeld)
                slide += 1f;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed)
                    slide -= 1f;
                if (keyboard.rightArrowKey.isPressed)
                    slide += 1f;
                if (keyboard.downArrowKey.isPressed)
                    depth -= 1f;
                if (keyboard.upArrowKey.isPressed)
                    depth += 1f;
                if (keyboard.zKey.wasPressedThisFrame)
                    Turn(-1f);
                if (keyboard.cKey.wasPressedThisFrame)
                    Turn(1f);
                if (keyboard.rKey.wasPressedThisFrame)
                    ShiftFlow.Restart();
                if (keyboard.escapeKey.wasPressedThisFrame)
                    Exit();
                if (keyboard.kKey.wasPressedThisFrame && creditsBoard != null)
                    creditsBoard.SetActive(!creditsBoard.activeSelf);
            }

            if (slide != 0f || depth != 0f)
            {
                var body = m_Origin.transform;
                var step = (body.right * slide + body.forward * depth).normalized * slideSpeed * Time.deltaTime;
                body.position += new Vector3(step.x, 0f, step.z);
            }
        }

        void LateUpdate()
        {
            if (m_Origin == null)
                return;

            var body = m_Origin.transform;
            var p = body.position;
            p.x = Mathf.Clamp(p.x, minX, maxX);
            p.y = 0f;
            p.z = Mathf.Clamp(p.z, minZ, maxZ);
            body.position = p;
        }

        void Turn(float sign)
        {
            if (m_Origin != null)
                m_Origin.RotateAroundCameraUsingOriginUp(turnDegrees * Mathf.Sign(sign));
        }

        public static void Exit()
        {
            StopPlayMode();
        }

        static void StopPlayMode()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void CollapseSimulatorPanel()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().Name == "XRDeviceSimulatorUI")
                    behaviour.SendMessage("OnClickCloseSimulatorUIPanel", SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
