using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    public enum SocketAccepts
    {
        Portafilter,
        Pitcher,
    }

    /// <summary>
    /// XRI socket that only takes the tool it was built for (grinder fork, tamping mat, steam rest).
    /// </summary>
    public class ToolSocket : XRSocketInteractor
    {
        public SocketAccepts accepts = SocketAccepts.Portafilter;

        public Portafilter SeatedPortafilter =>
            hasSelection ? firstInteractableSelected.transform.GetComponent<Portafilter>() : null;

        public MilkPitcher SeatedPitcher =>
            hasSelection ? firstInteractableSelected.transform.GetComponent<MilkPitcher>() : null;

        public override bool CanHover(IXRHoverInteractable interactable) =>
            base.CanHover(interactable) && Accepts(interactable.transform);

        public override bool CanSelect(IXRSelectInteractable interactable) =>
            base.CanSelect(interactable) && Accepts(interactable.transform);

        bool Accepts(Transform t)
        {
            switch (accepts)
            {
                case SocketAccepts.Portafilter: return t.GetComponent<Portafilter>() != null;
                case SocketAccepts.Pitcher: return t.GetComponent<MilkPitcher>() != null;
                default: return false;
            }
        }
    }
}
