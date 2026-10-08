using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    public enum Verdict
    {
        Balanced,
        Sour,
        Bitter,
        Channeled,
    }

    public struct ShotResult
    {
        public float timeSeconds;
        public float pressureBar;
        public float grind;
        public float doseGrams;
        public float tampKg;
        public float tiltDeg;
        public float yieldGrams;
        public bool channeled;
        public Verdict verdict;
        public string advice;
    }

    /// <summary>
    /// Advanced feature: a deterministic model that turns the trainee's technique (grind, dose, tamp
    /// force, tamp level) into shot time and brew pressure, then runs the shot live on the gauge.
    ///
    /// time     = 27.5 s * e^(-0.22 * (grind - 5)) * (1 + 0.015 * (tamp - 15)) * (dose / 18)^1.6
    ///            (x0.8 for a tamp under 5 kg, x0.75 if tilt > 5 degrees: water channels through the gap)
    /// pressure = 9 bar * sqrt(time / 27.5), clamped to 2.5..11 bar (x0.8 when channeled)
    /// verdict  = Channeled if tilted, Sour if under 25 s, Bitter if over 30 s, otherwise Balanced
    /// </summary>
    public class ExtractionModel : MonoBehaviour
    {
        public const float TargetYield = 36f;
        public const float ReferenceTime = 27.5f;
        public const float TargetLow = 25f;
        public const float TargetHigh = 30f;
        public const float ChannelTiltDeg = 5f;

        public GroupHead groupHead;
        public PressButton brewButton;
        public MilkPour cup;
        public MachineGauge gauge;
        public GameObject espressoStream;

        public bool Running { get; private set; }

        ShiftFlow m_Flow;
        ShotResult m_Plan;
        float m_Elapsed;
        AudioSource m_Pump;
        AudioSource m_Drip;
        int m_AnnouncedSecond = -1;

        public static ShotResult Predict(float doseGrams, float grind, float tampKg, float tiltDeg)
        {
            var dose = Mathf.Max(doseGrams, 1f);
            var time = ReferenceTime
                       * Mathf.Exp(-0.22f * (grind - 5f))
                       * (1f + 0.015f * (tampKg - 15f))
                       * Mathf.Pow(dose / 18f, 1.6f);
            if (tampKg < 5f)
                time *= 0.8f;

            var channeled = tiltDeg > ChannelTiltDeg;
            if (channeled)
                time *= 0.75f;
            time = Mathf.Clamp(time, 6f, 60f);

            var pressure = Mathf.Clamp(9f * Mathf.Sqrt(time / ReferenceTime), 2.5f, 11f);
            if (channeled)
                pressure *= 0.8f;

            var r = new ShotResult
            {
                timeSeconds = time,
                pressureBar = pressure,
                grind = grind,
                doseGrams = doseGrams,
                tampKg = tampKg,
                tiltDeg = tiltDeg,
                yieldGrams = TargetYield,
                channeled = channeled,
            };

            if (channeled)
            {
                r.verdict = Verdict.Channeled;
                r.advice = $"The tamp was tilted {tiltDeg:0.0}°, so water rushed through the low side. Keep the tamper level.";
            }
            else if (time < TargetLow)
            {
                r.verdict = Verdict.Sour;
                r.advice = $"Ran fast ({time:0.0} s): under-extracted and sour. Grind finer (lower number)"
                           + (doseGrams < 17f ? " and dose closer to 18 g." : tampKg < 10f ? " and tamp firmer." : ".");
            }
            else if (time > TargetHigh)
            {
                r.verdict = Verdict.Bitter;
                r.advice = $"Ran slow ({time:0.0} s): over-extracted and bitter. Grind coarser (higher number)"
                           + (doseGrams > 19f ? " and dose closer to 18 g." : ".");
            }
            else
            {
                r.verdict = Verdict.Balanced;
                r.advice = "Balanced. Keep this grind setting for service.";
            }
            return r;
        }

        void Start()
        {
            m_Flow = ShiftFlow.Instance;
            m_Pump = AudioKit.Loop(groupHead.transform, Sfx.Pump, 0.45f);
            m_Drip = AudioKit.Loop(cup.transform, Sfx.Pour, 0.2f);
            brewButton.Pressed += OnBrewPressed;
            if (espressoStream != null)
            {
                espressoStream.SetActive(false);
                var scale = espressoStream.transform.localScale;
                espressoStream.transform.localScale = new Vector3(scale.x * 2.4f, scale.y, scale.z * 2.4f);
            }
        }

        void OnBrewPressed(IXRSelectInteractor _)
        {
            if (Running)
                return;
            if (!m_Flow.IsAt(Step.Brew))
            {
                m_Flow.Say("Not yet. Follow the ticket: " + ShiftFlow.Instruction(m_Flow.Current));
                return;
            }
            if (!groupHead.IsLocked)
            {
                m_Flow.Say("Lock the portafilter in first.");
                return;
            }
            if (!cup.IsUnder(groupHead.Seat.position))
            {
                m_Flow.Say("Move the white cup onto the dark ring under the spouts. A little off the ring is fine, then let go.");
                return;
            }

            var pf = groupHead.portafilter;
            m_Plan = Predict(pf.DoseGrams, GrindDial.Current, pf.TampKg, pf.TampTiltDeg);
            m_Elapsed = 0f;
            m_AnnouncedSecond = -1;
            Running = true;
            m_Pump.Play();
            m_Flow.PointAt(gauge.transform,
                $"Shot started. Goal is {TargetLow:0} to {TargetHigh:0} seconds at 9 bar. This one is headed for about {m_Plan.timeSeconds:0} seconds.");
        }

        void Update()
        {
            if (!Running)
                return;

            m_Elapsed += Time.deltaTime;
            var preinfusion = Mathf.Min(4f, m_Plan.timeSeconds * 0.3f);
            var pressure = m_Plan.pressureBar * Mathf.Clamp01(m_Elapsed / 3f)
                           + Mathf.Sin(m_Elapsed * 13f) * 0.08f;
            var yieldFraction = Mathf.Clamp01((m_Elapsed - preinfusion) / (m_Plan.timeSeconds - preinfusion));
            var flowing = yieldFraction > 0f;

            if (espressoStream != null && espressoStream.activeSelf != flowing)
                espressoStream.SetActive(flowing);
            if (flowing && !m_Drip.isPlaying)
                m_Drip.Play();

            cup.SetEspresso(yieldFraction);
            gauge.SetShot(Mathf.Max(0f, pressure), m_Elapsed, yieldFraction * TargetYield, GrindDial.Current, true, m_Plan.timeSeconds);

            var whole = Mathf.FloorToInt(m_Elapsed);
            if (whole != m_AnnouncedSecond)
            {
                m_AnnouncedSecond = whole;
                m_Flow.PointAt(gauge.transform,
                    $"Shot running, {whole} seconds. Goal is {TargetLow:0} to {TargetHigh:0} seconds, needle on 9 bar.");
            }

            if (m_Elapsed < m_Plan.timeSeconds)
                return;

            Running = false;
            m_Pump.Stop();
            m_Drip.Stop();
            if (espressoStream != null)
                espressoStream.SetActive(false);
            gauge.SetShot(0f, m_Plan.timeSeconds, TargetYield, GrindDial.Current, true, m_Plan.timeSeconds);
            m_Flow.RecordShot(m_Plan);
            m_Flow.Say($"Shot done: {TargetYield:0} g in {m_Plan.timeSeconds:0.0} s at {m_Plan.pressureBar:0.0} bar. Now steam the milk.");
            m_Flow.Complete(Step.Brew);
        }
    }
}
