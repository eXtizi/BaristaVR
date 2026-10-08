using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    /// <summary>
    /// Tools never tumble. When let go, a tool sets down upright on the flat surface below it, or goes
    /// back to its home spot if there is none. Resting tools are kinematic, so a held tool brushing past
    /// can't knock them flying.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class ToolSettle : MonoBehaviour
    {
        [Tooltip("Height of the pivot above the bottom of the tool when it stands upright.")]
        public float baseHeight;
        [Tooltip("Where the tool goes when it is dropped with nothing flat below. Zero uses its starting spot.")]
        public Vector3 home;
        [Tooltip("If the tool is let go this close to its home spot, it slides onto that spot. Zero disables the assist.")]
        public float guideRadius;

        public bool IsResting { get; private set; } = true;

        XRGrabInteractable m_Grab;
        Rigidbody m_Body;
        Collider[] m_Own;
        float m_HomeYaw;
        Coroutine m_Ease;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
            m_Own = GetComponentsInChildren<Collider>(true);
            m_Grab.selectEntered.AddListener(_ =>
            {
                IsResting = false;
                if (m_Ease != null)
                {
                    StopCoroutine(m_Ease);
                    m_Ease = null;
                }
            });
            m_Grab.selectExited.AddListener(OnReleased);
        }

        void Start()
        {
            if (home == Vector3.zero)
                home = transform.position;
            m_HomeYaw = transform.eulerAngles.y;
            if (m_Body != null)
                m_Body.isKinematic = true;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor || !isActiveAndEnabled)
                return;
            StartCoroutine(SettleSoon());
        }

        IEnumerator SettleSoon()
        {
            // Give sockets and the group head a chance to take the tool first.
            yield return null;
            yield return null;
            if (m_Grab.isSelected || !m_Grab.enabled || transform.parent != null)
                yield break;
            Settle();
        }

        public void Settle()
        {
            var yaw = transform.eulerAngles.y;
            // Close to the marked spot: slide onto it. A few centimetres off still counts,
            // and the slide stays on the counter instead of leaping up onto the machine.
            if (guideRadius > 0f && NearHome())
            {
                m_Ease = StartCoroutine(EaseTo(home, m_HomeYaw));
                return;
            }

            if (TryFindSurface(out var point))
                Park(point + Vector3.up * baseHeight, yaw);
            else
                Park(home, m_HomeYaw);
        }

        bool NearHome()
        {
            var flat = new Vector2(transform.position.x - home.x, transform.position.z - home.z).magnitude;
            var vertical = transform.position.y - home.y;
            return flat <= guideRadius && vertical > -0.04f && vertical < 0.12f;
        }

        IEnumerator EaseTo(Vector3 position, float yaw)
        {
            var from = transform.position;
            var rotFrom = transform.rotation;
            var rotTo = Quaternion.Euler(0f, yaw, 0f);
            if (m_Body != null)
            {
                if (!m_Body.isKinematic)
                {
                    m_Body.linearVelocity = Vector3.zero;
                    m_Body.angularVelocity = Vector3.zero;
                }
                m_Body.isKinematic = true;
            }

            var t = 0f;
            const float duration = 0.22f;
            while (t < duration)
            {
                if (m_Grab.isSelected)
                {
                    m_Ease = null;
                    yield break;
                }
                t += Time.deltaTime;
                var u = Mathf.SmoothStep(0f, 1f, t / duration);
                transform.SetPositionAndRotation(Vector3.Lerp(from, position, u), Quaternion.Slerp(rotFrom, rotTo, u));
                yield return null;
            }

            m_Ease = null;
            Park(position, yaw);
        }

        bool TryFindSurface(out Vector3 point)
        {
            point = default;
            // Start the ray at the tool. A ray from above it hits the group head first and
            // parks the cup on top of the machine whenever the hand is a little high.
            var origin = transform.position + Vector3.up * 0.04f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 0.6f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (System.Array.IndexOf(m_Own, hit.collider) >= 0)
                    continue;
                if (hit.point.y > transform.position.y + 0.02f)
                    continue;
                // Never balance one tool on another.
                if (hit.collider.GetComponentInParent<XRGrabInteractable>() != null)
                    return false;
                if (Vector3.Angle(hit.normal, Vector3.up) > 20f)
                    return false;
                point = hit.point;
                return true;
            }
            return false;
        }

        void Park(Vector3 position, float yaw)
        {
            if (m_Body != null)
            {
                if (!m_Body.isKinematic)
                {
                    m_Body.linearVelocity = Vector3.zero;
                    m_Body.angularVelocity = Vector3.zero;
                }
                m_Body.isKinematic = true;
            }
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            IsResting = true;
        }
    }
}
