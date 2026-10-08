using UnityEngine;

namespace Barista
{
    /// <summary>
    /// Grinder setting from 1 (fine) to 10 (coarse). Grab the knob and drag sideways to turn it.
    /// The setting survives "pull another shot", so the trainee can dial in over several attempts.
    /// </summary>
    public class GrindDial : MonoBehaviour
    {
        public DragControl drag;
        public Transform knobPivot;
        public TextMesh label;
        public float min = 1f;
        public float max = 10f;
        [Tooltip("Deliberately coarse so the first shot runs fast and the trainee has something to correct.")]
        public float startSetting = 6.5f;

        public static float Current { get; private set; } = 6.5f;

        static float? s_Saved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Saved = null;

        void Start()
        {
            var setting = s_Saved ?? startSetting;
            drag.Changed += Apply;
            drag.SetValue(Mathf.InverseLerp(min, max, setting));
        }

        void Apply(float v)
        {
            var extraction = FindAnyObjectByType<ExtractionModel>();
            if (extraction != null && extraction.Running)
                return;

            Current = Mathf.Round(Mathf.Lerp(min, max, v) * 10f) / 10f;
            s_Saved = Current;
            if (knobPivot != null)
                knobPivot.localRotation = Quaternion.Euler(0f, 0f, -v * 270f);
            if (label != null)
                label.text = $"GRIND {Current:0.0}\n1 fine - 10 coarse";
        }
    }
}
