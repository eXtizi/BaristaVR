using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace Barista
{
    /// <summary>
    /// Away from the tamping station the tamper is an ordinary physical tool: it follows the hand's
    /// rotation and collides with things. Over the mat or the seated basket the shaft is held vertical
    /// and pulled onto the basket axis, but the body stays dynamic, so the basket and its handle still
    /// stop it. Kinematic movement is not used here: a kinematic tamper does not collide with a seated
    /// portafilter, which is also kinematic.
    /// </summary>
    public class TamperGrabTransformer : XRBaseGrabTransformer
    {
        public ToolSocket mat;
        [Tooltip("Where the hand holds the tamper, relative to the centre of its base.")]
        public Vector3 gripLocal = new Vector3(0f, 0.085f, 0f);
        [Tooltip("Share of the wrist's tilt since the grab that tilts the tamper at the station.")]
        [Range(0f, 1f)] public float wristFollow = 0.3f;
        public float maxTiltDeg = 12f;
        [Tooltip("Horizontal distance from the mat or basket centre inside which the upright grip takes over.")]
        public float stationRadius = 0.09f;
        public float stationHeight = 0.16f;
        [Tooltip("Inside this horizontal distance from the basket centre, the tamper is pulled onto the axis.")]
        public float guideRadius = 0.05f;
        [Tooltip("How much of the tilt the basket walls take out once the tamper is guided.")]
        [Range(0f, 1f)] public float guideStraighten = 0.7f;

        XRGrabInteractable m_Grab;
        Rigidbody m_Body;
        Quaternion m_HandAtGrab = Quaternion.identity;
        Quaternion m_GripOffset = Quaternion.identity;
        float m_Yaw;
        float m_Station;
        bool m_Upright;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
        }

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable.interactorsSelecting.Count == 0)
                return;
            var hand = grabInteractable.interactorsSelecting[0].GetAttachTransform(grabInteractable);
            m_HandAtGrab = hand.rotation;
            m_Yaw = transform.eulerAngles.y;
            m_GripOffset = Quaternion.Inverse(hand.rotation) * Quaternion.Euler(0f, m_Yaw, 0f);
            m_Station = 0f;
            SetUpright(false);
        }

        void Update()
        {
            if (m_Grab == null || !m_Grab.isSelected)
            {
                if (m_Upright)
                    SetUpright(false);
                return;
            }

            var hand = m_Grab.interactorsSelecting[0].GetAttachTransform(m_Grab);
            var target = StationCentre(out _);
            var baseGuess = hand.position - Vector3.up * gripLocal.y;
            var horizontal = new Vector2(baseGuess.x - target.x, baseGuess.z - target.z).magnitude;
            var height = baseGuess.y - target.y;
            var inside = height < stationHeight && height > -0.06f
                ? Mathf.InverseLerp(stationRadius, stationRadius * 0.6f, horizontal)
                : 0f;
            m_Station = Mathf.MoveTowards(m_Station, inside, Time.deltaTime * 6f);

            // Hysteresis, so the upright lock does not flicker at the edge of the zone.
            if (!m_Upright && m_Station > 0.6f)
                SetUpright(true);
            else if (m_Upright && m_Station < 0.2f)
                SetUpright(false);
        }

        void FixedUpdate()
        {
            if (!m_Upright || m_Body == null || m_Body.isKinematic)
                return;
            // Hold the shaft vertical. Rotation is frozen, so a contact with the basket cannot tip it,
            // and position is still velocity-tracked, so the contact still stops the tamper.
            m_Body.angularVelocity = Vector3.zero;
            m_Body.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
        }

        void SetUpright(bool on)
        {
            m_Upright = on;
            if (m_Grab != null)
                m_Grab.trackRotation = !on;
            if (m_Body != null && !m_Body.isKinematic)
                m_Body.constraints = on ? RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.None;
        }

        Vector3 StationCentre(out Portafilter seated)
        {
            seated = mat != null ? mat.SeatedPortafilter : null;
            if (seated != null)
                return seated.BedTopWorld;
            return mat != null ? mat.transform.position : transform.position + Vector3.down * 10f;
        }

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            if (grabInteractable.interactorsSelecting.Count == 0)
                return;

            var hand = grabInteractable.interactorsSelecting[0].GetAttachTransform(grabInteractable);

            // Free: an ordinary held object.
            var free = hand.rotation * m_GripOffset;

            // Station: upright, with a little of the wrist's tilt.
            var tiltedUp = hand.rotation * Quaternion.Inverse(m_HandAtGrab) * Vector3.up;
            var axis = Vector3.Cross(Vector3.up, tiltedUp);
            var wristTilt = Vector3.Angle(Vector3.up, tiltedUp);

            var centre = StationCentre(out var pf);
            var guide = 0f;
            if (pf != null)
            {
                var baseGuess = hand.position - Vector3.up * gripLocal.y;
                var horizontal = new Vector2(baseGuess.x - centre.x, baseGuess.z - centre.z).magnitude;
                var height = baseGuess.y - centre.y;
                if (height < stationHeight && height > -0.05f)
                    guide = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(guideRadius, guideRadius * 0.4f, horizontal));
            }

            // Fully upright once the station grip has taken over; a little wrist tilt only on the way in.
            var tilt = Mathf.Min(wristTilt * wristFollow, maxTiltDeg) * (1f - Mathf.Max(guideStraighten * guide, m_Station));
            var upright = Quaternion.Euler(0f, m_Yaw, 0f);
            var station = axis.sqrMagnitude > 1e-8f
                ? Quaternion.AngleAxis(tilt, axis.normalized) * upright
                : upright;

            var rotation = Quaternion.Slerp(free, station, m_Station);
            var position = hand.position - rotation * gripLocal;
            if (guide > 0f)
            {
                var pull = guide * m_Station;
                position.x = Mathf.Lerp(position.x, centre.x, pull);
                position.z = Mathf.Lerp(position.z, centre.z, pull);
            }

            targetPose = new Pose(position, rotation);
        }
    }
}
