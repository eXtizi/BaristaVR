using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Barista
{
    /// <summary>
    /// On-demand grinder. Grinds into the portafilter only while it is seated in the fork and the
    /// GRIND button is held; dosing completes when the portafilter is lifted out with enough coffee.
    /// </summary>
    public class GrinderStation : MonoBehaviour
    {
        public ToolSocket fork;
        public PressButton grindButton;
        public TextMesh display;
        public GameObject groundsStream;
        public float gramsPerSecond = 2.4f;
        public float targetGrams = 18f;

        ShiftFlow m_Flow;
        AudioSource m_Motor;
        float m_HapticTimer;
        float m_LastGrams;

        void Start()
        {
            m_Flow = ShiftFlow.Instance;
            m_Motor = AudioKit.Loop(grindButton.transform.parent != null ? grindButton.transform.parent : transform, Sfx.Grinder, 0.8f);
            fork.selectExited.AddListener(OnPortafilterRemoved);
            grindButton.Pressed += _ => OnGrindPressed();
            if (groundsStream != null)
                groundsStream.SetActive(false);
        }

        void OnGrindPressed()
        {
            if (!m_Flow.IsAt(Step.Dose))
                m_Flow.Say("Not yet. Follow the ticket: " + ShiftFlow.Instruction(m_Flow.Current));
            else if (fork.SeatedPortafilter == null)
                m_Flow.Say("Seat the portafilter in the grinder fork first.");
        }

        void Update()
        {
            var pf = fork.SeatedPortafilter;
            var grinding = grindButton.IsHeld && m_Flow.IsAt(Step.Dose) && pf != null && pf.DoseGrams < Portafilter.MaxDoseGrams;

            if (grinding)
            {
                pf.AddDose(gramsPerSecond * Time.deltaTime);
                m_HapticTimer -= Time.deltaTime;
                if (m_HapticTimer <= 0f)
                {
                    Haptics.Pulse(grindButton.Holder, 0.2f, 0.1f);
                    m_HapticTimer = 0.1f;
                }
                if (pf.DoseGrams >= Portafilter.MaxDoseGrams)
                    m_Flow.Say("The basket is overflowing. Lift it out.");
            }

            if (grinding && !m_Motor.isPlaying)
                m_Motor.Play();
            else if (!grinding && m_Motor.isPlaying)
                m_Motor.Stop();

            if (groundsStream != null && groundsStream.activeSelf != grinding)
                groundsStream.SetActive(grinding);

            if (m_Flow.IsAt(Step.Dose))
                m_Flow.PointAt(pf != null ? grindButton.transform : fork.transform,
                    pf != null
                        ? "Hold this green button. Coffee falls into the basket. Let go near 18 g, then lift the basket out."
                        : "Put the metal basket into these two prongs. The basket is the thing you took off the machine.");

            if (pf != null)
                m_LastGrams = pf.DoseGrams;
            // Keep the last dose on screen after the basket is lifted. Clearing it to 0 made a
            // correct 18 g dose look like it had failed.
            display.text = pf != null
                ? $"DOSE {m_LastGrams:0.0} g\ntarget {targetGrams:0.0} g"
                : m_LastGrams > 0.1f
                    ? $"DOSE {m_LastGrams:0.0} g\nin the basket"
                    : $"DOSE 0.0 g\ntarget {targetGrams:0.0} g";
        }

        void OnPortafilterRemoved(SelectExitEventArgs args)
        {
            if (!m_Flow.IsAt(Step.Dose))
                return;
            var pf = args.interactableObject.transform.GetComponent<Portafilter>();
            if (pf == null)
                return;

            if (pf.DoseGrams < 8f)
            {
                m_Flow.Say($"Only {pf.DoseGrams:0.0} g. Put it back in the fork and keep grinding.");
                return;
            }

            var off = pf.DoseGrams - targetGrams;
            m_Flow.Say(Mathf.Abs(off) <= 1f
                ? $"{pf.DoseGrams:0.0} g. Right on the recipe."
                : $"{pf.DoseGrams:0.0} g. That is {(off > 0 ? "over" : "under")} the 18 g recipe; it will change how fast the shot runs.");
            m_Flow.Complete(Step.Dose);
        }
    }
}
