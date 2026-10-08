using UnityEngine;

namespace Barista
{
    /// <summary>
    /// Tamping at the mat. Pushing the tamper below the top of the coffee bed is read as spring
    /// compression: force = depth * stiffness. The visible tamper only sinks a fraction of the hand's
    /// travel (the rest is pushed back up), haptics scale with force, and the peak force and tilt are
    /// stored on the portafilter for the extraction model.
    /// </summary>
    public class TamperPress : MonoBehaviour
    {
        public ToolSocket mat;
        public Tamper tamper;
        public TextMesh readout;

        [Tooltip("Spring stiffness: kilograms of force per metre of hand travel below the bed.")]
        public float kgPerMetre = 1000f;
        [Tooltip("Fraction of the hand's downward travel the tamper base is allowed to follow.")]
        [Range(0.05f, 1f)] public float compliance = 0.15f;
        public float maxKg = 35f;
        public float captureRadius = 0.025f;
        public float minValidKg = 8f;
        [Tooltip("How long the press has to stay on the coffee before it counts. A tap does not tamp.")]
        public float holdSeconds = 1.05f;

        ShiftFlow m_Flow;
        bool m_Pressing;
        float m_PeakKg;
        float m_TiltAtPeak;
        float m_HapticTimer;
        float m_Held;

        void Start() => m_Flow = ShiftFlow.Instance;

        void Update()
        {
            var pf = mat.SeatedPortafilter;
            var active = m_Flow.IsAt(Step.Tamp) && pf != null;

            if (m_Flow.IsAt(Step.Tamp))
                m_Flow.PointAt(pf != null ? tamper.transform : mat.transform,
                    pf != null
                        ? "Pick up this brown handle. Keep it straight up. Press to about 15 kg and hold it there until the card fills. A quick touch does not tamp."
                        : "Put the metal basket down on this black square.");

            if (!active)
            {
                tamper.SetVisualOffset(0f);
                m_Pressing = false;
                m_Held = 0f;
                readout.text = m_Flow.IsAt(Step.Tamp) ? "TAMP\nseat the portafilter here" : "TAMP";
                return;
            }

            var tip = tamper.transform.position;
            var bedTop = pf.BedTopWorld;
            var horizontal = new Vector2(tip.x - bedTop.x, tip.z - bedTop.z).magnitude;
            var depth = bedTop.y - tip.y;
            var aligned = horizontal < captureRadius && tamper.Holder != null;
            // The first few millimetres are just contact. Force starts after that, so a brush stays at 0 kg.
            var travel = Mathf.Max(0f, depth - 0.004f);

            if (aligned && travel > 0f)
            {
                if (!m_Pressing)
                {
                    m_Pressing = true;
                    m_PeakKg = 0f;
                    m_Held = 0f;
                    AudioKit.PlayAt(Sfx.Knock, bedTop, 0.6f);
                }

                var kg = Mathf.Min(travel * kgPerMetre, maxKg);
                var tilt = Vector3.Angle(tamper.transform.up, pf.transform.up);
                if (kg > m_PeakKg)
                {
                    m_PeakKg = kg;
                    m_TiltAtPeak = tilt;
                }

                if (kg >= minValidKg)
                    m_Held += Time.deltaTime;

                pf.SetCompression(kg / 20f);
                tamper.SetVisualOffset(depth * (1f - compliance));
                var bar = Blocks(m_Held / holdSeconds);
                readout.text = kg >= minValidKg
                    ? $"TAMP {kg:0} kg  HOLD\n[{bar}]  aim 15 kg"
                    : $"TAMP {kg:0} kg\npress harder, aim 15 kg";

                m_HapticTimer -= Time.deltaTime;
                if (m_HapticTimer <= 0f)
                {
                    Haptics.Pulse(tamper.Holder, Mathf.Clamp01(kg / 25f), 0.06f);
                    m_HapticTimer = 0.05f;
                }

                if (m_Held < holdSeconds)
                    return;

                pf.TampKg = m_PeakKg;
                pf.TampTiltDeg = m_TiltAtPeak;
                pf.IsTamped = true;
                m_Pressing = false;
                m_Held = 0f;
                readout.text = $"TAMPED {m_PeakKg:0} kg";
                m_Flow.Say(m_TiltAtPeak > ExtractionModel.ChannelTiltDeg
                    ? $"Tamped {m_PeakKg:0} kg but tilted {m_TiltAtPeak:0.0}°. An uneven bed can channel."
                    : $"Tamped {m_PeakKg:0} kg, {m_TiltAtPeak:0.0}° tilt. Nice and level.");
                m_Flow.Complete(Step.Tamp);
                return;
            }

            tamper.SetVisualOffset(0f);
            if (!m_Pressing)
            {
                readout.text = "TAMP\npress, then hold";
                return;
            }

            if (depth < -0.008f || !aligned)
            {
                m_Pressing = false;
                var held = m_Held;
                m_Held = 0f;
                pf.SetCompression(0f);
                if (m_PeakKg < minValidKg)
                    m_Flow.Say($"Only {m_PeakKg:0} kg. Press to about 15 kg and hold until the card fills.");
                else
                    m_Flow.Say($"Held {held:0.0} s. Stay on it for a full second. A quick touch does not tamp.");
            }
        }

        static string Blocks(float amount)
        {
            const int cells = 8;
            var filled = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(amount) * cells), 0, cells);
            return new string('#', filled) + new string('-', cells - filled);
        }
    }
}
