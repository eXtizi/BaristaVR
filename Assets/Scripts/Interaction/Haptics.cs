using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    public static class Haptics
    {
        public static void Pulse(IXRInteractor interactor, float amplitude, float duration)
        {
            if (interactor is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, duration);
        }
    }
}
