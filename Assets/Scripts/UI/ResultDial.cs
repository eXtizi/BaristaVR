using UnityEngine;

namespace Barista
{
    /// <summary>
    /// End-of-shift extraction dial. The needle sits on SOUR / BALANCED / BITTER according to shot
    /// time, and the report lists every measured variable against the house recipe.
    /// </summary>
    public class ResultDial : MonoBehaviour
    {
        public GameObject panelRoot;
        public Transform needlePivot;
        public TextMesh verdictText;
        public TextMesh detailText;
        public PressButton retryButton;
        public PressButton creditsButton;
        public GameObject creditsBoard;
        public int wrapChars = 44;

        float m_TargetAngle;

        void Start()
        {
            retryButton.Pressed += _ => ShiftFlow.Restart();
            creditsButton.Pressed += _ => creditsBoard.SetActive(!creditsBoard.activeSelf);
            panelRoot.SetActive(false);
            creditsBoard.SetActive(false);
        }

        public void Show(ShotResult shot, float milkC, float cupFill, int attempt)
        {
            panelRoot.SetActive(true);

            // Sour at the left (fast shots), bitter at the right (slow shots), balanced straight up.
            var t = Mathf.InverseLerp(15f, 40f, shot.timeSeconds);
            m_TargetAngle = Mathf.Lerp(70f, -70f, t);

            var milkOk = milkC >= 60f && milkC <= 70f;
            var pass = shot.verdict == Verdict.Balanced && milkOk;
            // One line, under the dial. Two lines climbed into the SOUR / BALANCED / BITTER face.
            verdictText.text = pass ? "READY TO SERVE" : "NOT YET, " + shot.verdict.ToString().ToUpperInvariant();
            verdictText.color = pass ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.75f, 0.35f);

            var milkNote = milkC > 70f ? "scalded" : milkC < 60f ? "too cool" : "good";
            detailText.text =
                $"Shot {attempt}\n" +
                $"Extraction time  {shot.timeSeconds:0.0} s   (target 25-30)\n" +
                $"Pressure  {shot.pressureBar:0.0} bar   (target about 9)\n" +
                $"Grind setting  {shot.grind:0.0}\n" +
                $"Dose  {shot.doseGrams:0.0} g   (target 18)\n" +
                $"Tamp  {shot.tampKg:0} kg, {shot.tiltDeg:0.0}° tilt\n" +
                $"Milk  {milkC:0}°C, {milkNote}   (target 63-67)\n" +
                $"Cup  {cupFill * 100f:0}% full\n\n" +
                TextUtil.Wrap(shot.advice, wrapChars);
        }

        void Update()
        {
            if (needlePivot != null && panelRoot.activeSelf)
                needlePivot.localRotation = Quaternion.Slerp(needlePivot.localRotation, Quaternion.Euler(0f, 0f, m_TargetAngle), 4f * Time.deltaTime);
        }
    }
}
