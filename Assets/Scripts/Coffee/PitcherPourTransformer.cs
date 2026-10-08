using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace Barista
{
    /// <summary>
    /// Keeps ordinary free-hand movement until the trainee tips the pitcher near the cup. It then
    /// guides the spout over the cup and presents the pitcher sideways, to the camera's right, so
    /// the body and hand do not hide the cup or milk stream.
    /// </summary>
    public class PitcherPourTransformer : XRBaseGrabTransformer
    {
        public MilkPour cup;
        public Transform spout;
        public float activationTilt = 50f;
        public float activationRadius = 0.28f;
        public float blendSpeed = 8f;

        Vector3 m_HandPositionOffset;
        Quaternion m_HandRotationOffset = Quaternion.identity;
        float m_Guide;

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable.interactorsSelecting.Count == 0)
                return;

            var hand = grabInteractable.interactorsSelecting[0].GetAttachTransform(grabInteractable);
            m_HandPositionOffset = Quaternion.Inverse(hand.rotation) *
                                   (grabInteractable.transform.position - hand.position);
            m_HandRotationOffset = Quaternion.Inverse(hand.rotation) * grabInteractable.transform.rotation;
            m_Guide = 0f;
        }

        public override void Process(
            XRGrabInteractable grabInteractable,
            XRInteractionUpdateOrder.UpdatePhase updatePhase,
            ref Pose targetPose,
            ref Vector3 localScale)
        {
            if (grabInteractable.interactorsSelecting.Count == 0)
                return;

            var hand = grabInteractable.interactorsSelecting[0].GetAttachTransform(grabInteractable);
            var freeRotation = hand.rotation * m_HandRotationOffset;
            var freePosition = hand.position + hand.rotation * m_HandPositionOffset;

            var flow = ShiftFlow.Instance;
            var nearCup = cup != null &&
                          Vector3.Distance(freePosition, cup.transform.position) < activationRadius;
            var tipped = Vector3.Angle(freeRotation * Vector3.up, Vector3.up) >= activationTilt;
            var guide = flow != null && flow.IsAt(Step.Pour) && nearCup && tipped;
            m_Guide = Mathf.MoveTowards(m_Guide, guide ? 1f : 0f, blendSpeed * Time.deltaTime);

            if (m_Guide <= 0f || cup == null || spout == null)
            {
                targetPose = new Pose(freePosition, freeRotation);
                return;
            }

            var cam = Camera.main;
            var cameraRight = cam != null ? cam.transform.right : Vector3.right;
            cameraRight.y = 0f;
            cameraRight = cameraRight.sqrMagnitude > 0.001f ? cameraRight.normalized : Vector3.right;

            // The hand still aims the spout, exactly as before. Only the body turns sideways,
            // out to the camera's right, so the jug no longer hides the cup.
            var spoutLocal = grabInteractable.transform.InverseTransformPoint(spout.position);
            var aimedSpout = freePosition + freeRotation * spoutLocal;
            var sideRotation = Quaternion.LookRotation(Vector3.down, -cameraRight);
            var sidePosition = aimedSpout - sideRotation * spoutLocal;

            targetPose = new Pose(
                Vector3.Lerp(freePosition, sidePosition, Mathf.SmoothStep(0f, 1f, m_Guide)),
                Quaternion.Slerp(freeRotation, sideRotation, Mathf.SmoothStep(0f, 1f, m_Guide)));
        }
    }
}
