using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    /// <summary>
    /// A 0..1 control the user grabs and drags along one axis (dials, the portafilter lock swing).
    /// Driven by hand position rather than wrist rotation so it is equally usable in the
    /// XR Device Simulator, with a headset, and with the desktop fallback.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class DragControl : MonoBehaviour
    {
        [Tooltip("Frame the drag axis is expressed in. Should not rotate with the control itself.")]
        public Transform dragFrame;
        public Vector3 localAxis = Vector3.right;
        public float metresForFullRange = 0.1f;
        [Range(0f, 1f)] public float value;
        [Tooltip("Haptic tick every time the value crosses this step. 0 disables ticks.")]
        public float tickStep = 0.1f;

        public event Action<float> Changed;

        public bool IsHeld => m_Holder != null;
        public IXRSelectInteractor Holder => m_Holder;

        XRSimpleInteractable m_Interactable;
        IXRSelectInteractor m_Holder;
        Vector3 m_StartHandPos;
        float m_StartValue;
        int m_LastTick;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(_ => m_Holder = null);
        }

        void OnDisable() => m_Holder = null;

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_Holder = args.interactorObject;
            m_StartHandPos = m_Holder.transform.position;
            m_StartValue = value;
            m_LastTick = TickIndex(value);
            Haptics.Pulse(m_Holder, 0.25f, 0.04f);
        }

        void Update()
        {
            if (m_Holder == null)
                return;

            var frame = dragFrame != null ? dragFrame : transform;
            var axis = frame.TransformDirection(localAxis).normalized;
            var travelled = Vector3.Dot(m_Holder.transform.position - m_StartHandPos, axis);
            var next = Mathf.Clamp01(m_StartValue + travelled / metresForFullRange);
            if (Mathf.Approximately(next, value))
                return;

            value = next;
            var tick = TickIndex(value);
            if (tick != m_LastTick)
            {
                m_LastTick = tick;
                Haptics.Pulse(m_Holder, 0.15f, 0.02f);
            }
            Changed?.Invoke(value);
        }

        public void SetValue(float v)
        {
            value = Mathf.Clamp01(v);
            Changed?.Invoke(value);
        }

        int TickIndex(float v) => tickStep > 0f ? Mathf.FloorToInt(v / tickStep) : 0;
    }
}
