using UnityEngine;

namespace Barista
{
    /// <summary>
    /// The cup. Collects the espresso from the group head, then milk whenever the pitcher is tilted
    /// past the pour angle with its spout over the rim. Pivot is the centre of the cup's base.
    /// </summary>
    public class MilkPour : MonoBehaviour
    {
        public MilkPitcher pitcher;
        public Transform liquid;
        public Transform pourStream;
        public float innerRadius = 0.038f;
        public float height = 0.075f;
        [Tooltip("Cup fill per second at a full pour.")]
        public float pourRate = 0.3f;
        public float pourAngle = 55f;
        public Color espressoColor = new Color(0.25f, 0.13f, 0.06f);
        public Color flatWhiteColor = new Color(0.82f, 0.68f, 0.5f);

        const float k_EspressoShare = 0.25f;

        public float Fill { get; private set; }

        ShiftFlow m_Flow;
        Renderer m_LiquidRenderer;
        AudioSource m_PourSound;
        float m_MilkShare;

        void Start()
        {
            m_Flow = ShiftFlow.Instance;
            if (liquid != null)
                m_LiquidRenderer = liquid.GetComponent<Renderer>();
            m_PourSound = AudioKit.Loop(transform, Sfx.Pour, 0.5f);
            if (pourStream != null)
                pourStream.gameObject.SetActive(false);
            // Let go near the drip-tray mark and the cup slides onto it, so a small miss still lines up.
            if (TryGetComponent<ToolSettle>(out var settle) && settle.guideRadius <= 0f)
                settle.guideRadius = 0.09f;
            Refresh();
        }

        public bool IsUnder(Vector3 spoutPosition)
        {
            var p = transform.position;
            return new Vector2(p.x - spoutPosition.x, p.z - spoutPosition.z).magnitude < 0.08f && spoutPosition.y > p.y;
        }

        public void SetEspresso(float fraction)
        {
            Fill = Mathf.Max(Fill, Mathf.Clamp01(fraction) * k_EspressoShare);
            Refresh();
        }

        void Update()
        {
            var pouring = false;
            if (m_Flow.IsAt(Step.Pour))
            {
                m_Flow.PointAt(pitcher.transform,
                    "Pick up the silver jug and tip it near the cup. It shifts sideways so the cup stays visible. Pour until full; on a keyboard, hold Space.");
                pouring = CanPour();
                if (pouring)
                {
                    var tilt = Vector3.Angle(pitcher.transform.up, Vector3.up);
                    var rate = pourRate * (0.3f + 0.7f * Mathf.InverseLerp(pourAngle, 110f, tilt));
                    var add = Mathf.Min(rate * Time.deltaTime, 1f - Fill);
                    pitcher.Drain(add * 1.25f);
                    Fill += add;
                    m_MilkShare = Mathf.InverseLerp(k_EspressoShare, 1f, Fill);
                    Refresh();
                }

                if (Fill >= 0.98f || (pitcher.Milk <= 0.01f && Fill >= 0.6f))
                {
                    pouring = false;
                    m_Flow.RecordPour(Fill);
                    m_Flow.Say("Flat white served.");
                    m_Flow.Complete(Step.Pour);
                }
            }

            if (pouring && !m_PourSound.isPlaying)
                m_PourSound.Play();
            else if (!pouring && m_PourSound.isPlaying)
                m_PourSound.Stop();

            UpdateStream(pouring);
        }

        bool CanPour()
        {
            if (pitcher.Milk <= 0.01f || Vector3.Angle(pitcher.transform.up, Vector3.up) < pourAngle)
                return false;
            var local = transform.InverseTransformPoint(pitcher.spout.position);
            return new Vector2(local.x, local.z).magnitude < innerRadius * 1.6f && local.y > 0f && local.y < 0.3f;
        }

        void UpdateStream(bool pouring)
        {
            if (pourStream == null)
                return;
            if (pourStream.gameObject.activeSelf != pouring)
                pourStream.gameObject.SetActive(pouring);
            if (!pouring)
                return;

            var top = pitcher.spout.position;
            var bottom = transform.TransformPoint(0f, LiquidY, 0f);
            bottom.x = top.x;
            bottom.z = top.z;
            var length = Mathf.Max(0.005f, top.y - bottom.y);
            pourStream.position = (top + bottom) * 0.5f;
            pourStream.rotation = Quaternion.identity;
            var s = pourStream.localScale;
            pourStream.localScale = new Vector3(s.x, length * 0.5f / Mathf.Max(0.0001f, transform.lossyScale.y), s.z);
        }

        float LiquidY => Mathf.Lerp(0.006f, height - 0.006f, Fill);

        void Refresh()
        {
            if (liquid == null)
                return;
            liquid.gameObject.SetActive(Fill > 0.01f);
            liquid.localPosition = new Vector3(0f, LiquidY, 0f);
            if (m_LiquidRenderer != null)
                m_LiquidRenderer.material.color = Color.Lerp(espressoColor, flatWhiteColor, m_MilkShare);
        }
    }
}
