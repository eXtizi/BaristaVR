using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    /// <summary>
    /// Steam valve and wand. Heats the pitcher only while the valve is open and the wand tip is under
    /// the milk surface. The hiss is a 3D source at the tip whose pitch rises as the milk heats.
    /// </summary>
    public class SteamWand : MonoBehaviour
    {
        public PressButton valveButton;
        public Transform valveKnob;
        public Transform tip;
        public MilkPitcher pitcher;
        public ToolSocket steamRest;
        public MachineGauge gauge;
        public GameObject steamPuff;
        [Tooltip("Degrees Celsius per second with the tip fully immersed.")]
        public float heatRate = 4.5f;
        public float targetC = 65f;

        public bool Open { get; private set; }

        ShiftFlow m_Flow;
        AudioSource m_Hiss;
        bool m_TargetChimed;
        bool m_ScaldWarned;

        void Start()
        {
            m_Flow = ShiftFlow.Instance;
            m_Hiss = AudioKit.Loop(tip, Sfx.Steam, 0.7f);
            valveButton.Pressed += OnValvePressed;
            if (steamPuff != null)
                steamPuff.SetActive(false);
        }

        void OnValvePressed(IXRSelectInteractor _)
        {
            if (!Open && !m_Flow.IsAt(Step.Steam))
            {
                m_Flow.Say(m_Flow.Current < Step.Steam
                    ? "Steam the milk after the shot has been pulled."
                    : "The milk is already done.");
                return;
            }

            Open = !Open;
            if (valveKnob != null)
                valveKnob.localRotation = Quaternion.Euler(0f, 0f, Open ? -90f : 0f);

            if (Open)
            {
                m_Hiss.Play();
                return;
            }

            m_Hiss.Stop();
            if (steamPuff != null)
                steamPuff.SetActive(false);
            if (!m_Flow.IsAt(Step.Steam))
                return;

            if (pitcher.PeakTempC < 55f)
            {
                m_Flow.Say($"Milk is only {pitcher.TempC:0}°C. Keep the tip under the surface and steam to 65°C.");
                return;
            }

            m_Flow.RecordMilk(pitcher.TempC);
            m_Flow.Say(pitcher.TempC > 70f
                ? $"Milk at {pitcher.TempC:0}°C: scalded. Close the valve nearer 65°C next time."
                : pitcher.TempC < 60f
                    ? $"Milk at {pitcher.TempC:0}°C: a little cool. Aim for 65°C."
                    : $"Milk at {pitcher.TempC:0}°C. Silky. Now pour.");
            m_Flow.Complete(Step.Steam);
        }

        void Update()
        {
            if (m_Flow.IsAt(Step.Steam))
            {
                var onRest = steamRest.hasSelection || IsImmersed();
                m_Flow.PointAt(onRest ? valveButton.transform : steamRest.transform,
                    onRest
                        ? "Press STEAM. Watch the number on the jug. Press STEAM again when it says 65."
                        : "Put the silver jug on this black ring, so the thin pipe goes into the milk.");
                gauge.SetMilk(pitcher.TempC);
            }

            if (!Open)
                return;

            var immersed = IsImmersed();
            if (immersed)
            {
                pitcher.Heat(heatRate * Time.deltaTime, 0.08f * Time.deltaTime);
                m_Hiss.pitch = Mathf.Lerp(1.3f, 0.8f, Mathf.InverseLerp(5f, 70f, pitcher.TempC));
                m_Hiss.volume = 0.55f;
            }
            else
            {
                m_Hiss.pitch = 1.5f;
                m_Hiss.volume = 0.9f;
            }

            if (steamPuff != null && steamPuff.activeSelf == immersed)
                steamPuff.SetActive(!immersed);

            if (!m_TargetChimed && pitcher.TempC >= targetC)
            {
                m_TargetChimed = true;
                AudioKit.PlayAt(Sfx.Chime, pitcher.transform.position, 0.4f);
            }
            if (!m_ScaldWarned && pitcher.TempC >= 72f)
            {
                m_ScaldWarned = true;
                m_Flow.Say("Too hot! Milk scalds above 70°C. Close the valve.");
            }
        }

        bool IsImmersed()
        {
            var local = pitcher.transform.InverseTransformPoint(tip.position);
            return new Vector2(local.x, local.z).magnitude < pitcher.innerRadius * 0.9f
                   && local.y > 0.004f
                   && local.y < pitcher.SurfaceLocalY
                   && Vector3.Angle(pitcher.transform.up, Vector3.up) < 35f;
        }
    }
}
