using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Barista
{
    /// <summary>
    /// The portafilter carries the coffee bed between stations and remembers how it was prepared:
    /// dose, tamp force and tamp tilt all feed the extraction model.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Portafilter : MonoBehaviour
    {
        public const float MaxDoseGrams = 22f;

        [Tooltip("Coffee bed cylinder inside the basket (no collider).")]
        public Transform bed;
        [Tooltip("Local Y of the basket floor, relative to the portafilter pivot.")]
        public float basketFloorY = -0.012f;
        [Tooltip("Bed height in metres for 20 g of loose grounds.")]
        public float bedHeightPer20g = 0.02f;

        public float DoseGrams { get; private set; }
        public float TampKg { get; set; }
        public float TampTiltDeg { get; set; }
        public bool IsTamped { get; set; }

        public XRGrabInteractable Grab { get; private set; }
        public Rigidbody Body { get; private set; }

        float m_Compression;

        void Awake()
        {
            Grab = GetComponent<XRGrabInteractable>();
            Body = GetComponent<Rigidbody>();
            RefreshBed();
        }

        float BedHeight => DoseGrams / 20f * bedHeightPer20g * (1f - 0.2f * m_Compression);

        public Vector3 BedTopWorld => transform.TransformPoint(0f, basketFloorY + BedHeight, 0f);

        public void AddDose(float grams)
        {
            DoseGrams = Mathf.Min(DoseGrams + grams, MaxDoseGrams);
            RefreshBed();
        }

        /// <summary>0 = loose grounds, 1 = fully compressed puck.</summary>
        public void SetCompression(float amount)
        {
            m_Compression = Mathf.Clamp01(amount);
            RefreshBed();
        }

        void RefreshBed()
        {
            if (bed == null)
                return;
            var h = Mathf.Max(BedHeight, 0.0005f);
            bed.gameObject.SetActive(DoseGrams > 0.1f);
            bed.localPosition = new Vector3(0f, basketFloorY + h * 0.5f, 0f);
            var s = bed.localScale;
            s.y = h * 0.5f;
            bed.localScale = s;
        }
    }
}
