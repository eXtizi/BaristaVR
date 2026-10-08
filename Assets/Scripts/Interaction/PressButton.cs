using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    /// <summary>
    /// A physical push button. Selecting it (grip / trigger through the XRI select action) presses it;
    /// it stays pressed for as long as the selection is held.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class PressButton : MonoBehaviour
    {
        public Transform cap;
        public float travel = 0.006f;

        public event Action<IXRSelectInteractor> Pressed;
        public event Action Released;

        public bool IsHeld => m_Holder != null;
        public IXRSelectInteractor Holder => m_Holder;

        XRSimpleInteractable m_Interactable;
        IXRSelectInteractor m_Holder;
        Vector3 m_CapRest;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            if (cap != null)
                m_CapRest = cap.localPosition;
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            m_Holder = null;
            if (cap != null)
                cap.localPosition = m_CapRest;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_Holder = args.interactorObject;
            if (cap != null)
                cap.localPosition = m_CapRest + Vector3.forward * travel;
            AudioKit.PlayAt(Sfx.Click, transform.position, 0.5f);
            Haptics.Pulse(m_Holder, 0.35f, 0.05f);
            Pressed?.Invoke(m_Holder);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            m_Holder = null;
            if (cap != null)
                cap.localPosition = m_CapRest;
            Released?.Invoke();
        }
    }
}
