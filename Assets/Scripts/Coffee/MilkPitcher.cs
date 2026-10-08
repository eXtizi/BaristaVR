using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Barista
{
    /// <summary>Stainless pitcher with a clip-on digital thermometer. Pivot is the centre of its base.</summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class MilkPitcher : MonoBehaviour
    {
        public Transform milkSurface;
        public Transform spout;
        public TextMesh thermometer;
        public float innerRadius = 0.038f;
        public float height = 0.12f;
        public float startTempC = 5f;

        public float TempC { get; private set; }
        public float PeakTempC { get; private set; }
        public float Milk { get; private set; } = 1f;

        float m_Foam;

        public float SurfaceLocalY => Mathf.Lerp(0.008f, 0.07f, Milk) + m_Foam * 0.015f;

        void Awake()
        {
            TempC = startTempC;
            PeakTempC = startTempC;
        }

        void Start()
        {
            // Present even if the cafe scene was built before the pour guide existed.
            var guide = GetComponent<PitcherPourTransformer>();
            if (guide == null)
                guide = gameObject.AddComponent<PitcherPourTransformer>();
            guide.spout = spout;
            if (guide.cup == null)
                guide.cup = FindAnyObjectByType<MilkPour>();
            var grab = GetComponent<XRGrabInteractable>();
            if (grab != null)
                grab.useDynamicAttach = false;
        }

        public void Heat(float degrees, float foam)
        {
            TempC = Mathf.Min(TempC + degrees, 95f);
            PeakTempC = Mathf.Max(PeakTempC, TempC);
            m_Foam = Mathf.Clamp01(m_Foam + foam);
        }

        public float Drain(float amount)
        {
            var drained = Mathf.Min(Milk, amount);
            Milk -= drained;
            return drained;
        }

        void Update()
        {
            TempC = Mathf.MoveTowards(TempC, 20f, 0.05f * Time.deltaTime);

            if (milkSurface != null)
            {
                milkSurface.gameObject.SetActive(Milk > 0.02f);
                milkSurface.localPosition = new Vector3(0f, SurfaceLocalY, 0f);
            }

            if (thermometer != null)
            {
                thermometer.text = $"{TempC:0}°C";
                thermometer.color = TempC > 70f ? new Color(1f, 0.3f, 0.25f)
                    : TempC >= 63f ? new Color(0.35f, 1f, 0.45f)
                    : TempC >= 55f ? new Color(1f, 0.8f, 0.3f)
                    : Color.white;
            }
        }
    }
}
