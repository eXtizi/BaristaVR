using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Barista
{
    /// <summary>
    /// Bayonet mount under the group head. A seated portafilter is held kinematically on the bayonet;
    /// the trainee swings its handle with <see cref="lockLever"/> (left to unlock, right to lock).
    /// Releasing a prepared portafilter close to the mount seats it.
    /// </summary>
    public class GroupHead : MonoBehaviour
    {
        public Transform bayonet;
        public DragControl lockLever;
        public Portafilter portafilter;
        [Tooltip("Handle yaw in degrees when unlocked (swung to the trainee's left).")]
        public float unlockedAngle = 45f;
        public float seatRadius = 0.1f;

        public bool IsSeated { get; private set; }
        public bool IsLocked { get; private set; }
        public Transform Seat => bayonet;

        ShiftFlow m_Flow;

        void Start()
        {
            m_Flow = ShiftFlow.Instance;
            // The portafilter locks up inside the group head, so it must never be pushed out of it.
            var pfColliders = portafilter.GetComponentsInChildren<Collider>(true);
            foreach (var head in GetComponentsInChildren<Collider>(true))
            {
                if (System.Array.IndexOf(pfColliders, head) >= 0)
                    continue;
                foreach (var pf in pfColliders)
                    Physics.IgnoreCollision(head, pf, true);
            }
            portafilter.Grab.selectExited.AddListener(OnPortafilterReleased);
            SeatPortafilter(locked: true);
        }

        void SeatPortafilter(bool locked)
        {
            portafilter.Grab.enabled = false;
            portafilter.Body.isKinematic = true;
            portafilter.transform.SetParent(bayonet, false);
            portafilter.transform.localPosition = Vector3.zero;
            portafilter.transform.localRotation = Quaternion.identity;
            IsSeated = true;
            IsLocked = locked;
            lockLever.SetValue(locked ? 1f : 0f);
            ApplyAngle();
        }

        void ReleasePortafilter(IXRSelectInteractor hand)
        {
            IsSeated = false;
            IsLocked = false;
            lockLever.gameObject.SetActive(false);
            portafilter.transform.SetParent(null, true);
            portafilter.Grab.enabled = true;
            StartCoroutine(HandOff(hand));
        }

        IEnumerator HandOff(IXRSelectInteractor hand)
        {
            yield return null;
            var manager = portafilter.Grab.interactionManager;
            if (hand != null && manager != null && hand.isSelectActive && !portafilter.Grab.isSelected)
                manager.SelectEnter(hand, portafilter.Grab);
            if (!portafilter.Grab.isSelected && portafilter.TryGetComponent<ToolSettle>(out var settle))
                settle.Settle();
        }

        void OnPortafilterReleased(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor || !m_Flow.IsAt(Step.Lock))
                return;
            if (Vector3.Distance(portafilter.transform.position, bayonet.position) > seatRadius)
                return;
            if (!portafilter.IsTamped)
            {
                m_Flow.Say("Dose and tamp before locking in.");
                return;
            }
            StartCoroutine(SeatNextFrame());
        }

        IEnumerator SeatNextFrame()
        {
            yield return null;
            if (portafilter.Grab.isSelected)
                yield break;
            SeatPortafilter(locked: false);
            AudioKit.PlayAt(Sfx.Knock, bayonet.position, 0.5f);
            m_Flow.Say("Seated. Now grab the handle and swing it RIGHT to lock.");
        }

        void Update()
        {
            if (!IsSeated)
                return;

            var canMove = (m_Flow.IsAt(Step.Detach) && IsLocked) || (m_Flow.IsAt(Step.Lock) && !IsLocked);
            if (lockLever.gameObject.activeSelf != canMove)
                lockLever.gameObject.SetActive(canMove);
            if (!canMove)
                return;

            ApplyAngle();

            if (m_Flow.IsAt(Step.Detach) && lockLever.value <= 0.03f)
            {
                var hand = lockLever.Holder;
                AudioKit.PlayAt(Sfx.Click, bayonet.position, 0.8f);
                Haptics.Pulse(hand, 0.6f, 0.08f);
                ReleasePortafilter(hand);
                m_Flow.Complete(Step.Detach);
            }
            else if (m_Flow.IsAt(Step.Lock) && lockLever.value >= 0.97f)
            {
                IsLocked = true;
                AudioKit.PlayAt(Sfx.Knock, bayonet.position, 0.8f);
                Haptics.Pulse(lockLever.Holder, 0.7f, 0.1f);
                lockLever.gameObject.SetActive(false);
                m_Flow.Complete(Step.Lock);
            }
        }

        void ApplyAngle() =>
            bayonet.localRotation = Quaternion.Euler(0f, Mathf.Lerp(unlockedAngle, 0f, lockLever.value), 0f);
    }
}
