using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    /// <summary>
    /// The tamper's pivot is the centre of its flat base. Its meshes live under <see cref="visual"/>,
    /// which <see cref="TamperPress"/> pushes back up while tamping so the base visibly resists the hand.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Tamper : MonoBehaviour
    {
        public Transform visual;

        public XRGrabInteractable Grab { get; private set; }

        public IXRSelectInteractor Holder => Grab.isSelected ? Grab.firstInteractorSelecting : null;

        void Awake()
        {
            Grab = GetComponent<XRGrabInteractable>();
            Grab.selectExited.AddListener(_ => SetVisualOffset(0f));
        }

        public void SetVisualOffset(float metres)
        {
            if (visual != null)
                visual.localPosition = Vector3.up * metres;
        }
    }
}
