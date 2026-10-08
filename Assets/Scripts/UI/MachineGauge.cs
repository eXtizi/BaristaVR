using UnityEngine;

namespace Barista
{
    /// <summary>World-space gauge on top of the machine: pressure needle plus shot and milk readouts.</summary>
    public class MachineGauge : MonoBehaviour
    {
        public Transform needlePivot;
        public TextMesh readout;
        public float maxBar = 12f;

        string m_ShotLine = "0.0 bar   0.0 s   0 g";
        string m_MilkLine = "MILK --";
        float m_Bar;

        public void SetShot(float bar, float seconds, float yieldGrams, float grind, bool showGoal = false, float shotSeconds = 0f)
        {
            m_Bar = bar;
            if (!showGoal)
            {
                m_ShotLine = $"{bar:0.0} bar   {seconds:0.0} s   {yieldGrams:0} g   grind {grind:0.0}";
                return;
            }

            m_ShotLine = $"{bar:0.0} bar   target 9\n{seconds:0.0} s   goal {ExtractionModel.TargetLow:0}-{ExtractionModel.TargetHigh:0}\n{yieldGrams:0} g";
        }

        public void SetMilk(float tempC) => m_MilkLine = $"MILK {tempC:0}°C";

        void LateUpdate()
        {
            if (needlePivot != null)
            {
                var target = Quaternion.Euler(0f, 0f, Mathf.Lerp(120f, -120f, Mathf.Clamp01(m_Bar / maxBar)));
                needlePivot.localRotation = Quaternion.Slerp(needlePivot.localRotation, target, 10f * Time.deltaTime);
            }
            if (readout != null)
                readout.text = m_ShotLine + "\n" + m_MilkLine;
        }
    }
}
