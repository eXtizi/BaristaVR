using UnityEngine;

namespace Barista
{
    /// <summary>
    /// Shows the extraction formula and the shot it predicts from the current grind, dose and tamp,
    /// so the model is visible while playing and not only in the source.
    /// </summary>
    public class ExtractionLab : MonoBehaviour
    {
        public TextMesh live;
        public GroupHead groupHead;

        void LateUpdate()
        {
            if (live == null || groupHead == null || groupHead.portafilter == null)
                return;

            var pf = groupHead.portafilter;
            var grind = GrindDial.Current;
            var dose = pf.DoseGrams;
            var tamped = pf.IsTamped;
            var tamp = tamped ? pf.TampKg : 0f;
            var tilt = tamped ? pf.TampTiltDeg : 0f;

            string predict;
            if (dose < 1f)
            {
                predict = "predict   dose first";
            }
            else
            {
                var shot = ExtractionModel.Predict(dose, grind, tamp, tilt);
                predict = $"predict   {shot.timeSeconds:0.0} s   {shot.pressureBar:0.0} bar\nverdict   {shot.verdict.ToString().ToUpperInvariant()}";
            }

            live.text =
                $"grind    {grind:0.0}\n" +
                $"dose     {dose:0.0} g\n" +
                (tamped ? $"tamp     {tamp:0} kg\ntilt     {tilt:0.0}°\n" : "tamp     not yet\ntilt     --\n") +
                predict;
        }
    }
}
