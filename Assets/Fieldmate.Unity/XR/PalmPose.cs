using UnityEngine;
using UnityEngine.XR.Hands;

namespace Fieldmate.XR
{
    /// <summary>
    /// Follows one hand's palm joint (#73, the hand menu's anchor). Sits under the XR origin's camera offset like the
    /// hands, so joint poses (origin space) are its local pose. <see cref="IsTracked"/> is false while the hand is lost.
    /// </summary>
    [RequireComponent(typeof(XRHandTrackingEvents))]
    public sealed class PalmPose : MonoBehaviour
    {
        private XRHandTrackingEvents events;

        public bool IsTracked { get; private set; }

        /// <summary>Out of the palm (the joint's up axis is the back of the hand).</summary>
        public Vector3 PalmNormal => -transform.up;

        private void Awake() => events = GetComponent<XRHandTrackingEvents>();

        private void OnEnable()
        {
            events.jointsUpdated.AddListener(OnJointsUpdated);
            events.trackingChanged.AddListener(OnTrackingChanged);
        }

        private void OnDisable()
        {
            events.jointsUpdated.RemoveListener(OnJointsUpdated);
            events.trackingChanged.RemoveListener(OnTrackingChanged);
        }

        private void OnTrackingChanged(bool tracked) => IsTracked = tracked;

        private void OnJointsUpdated(XRHandJointsUpdatedEventArgs args)
        {
            if (args.hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var pose))
            {
                transform.SetLocalPositionAndRotation(pose.position, pose.rotation);
                IsTracked = true;
            }
        }

        /// <summary>Sets the pose directly (tests: no hand subsystem in the editor).</summary>
        public void Simulate(Vector3 position, Quaternion rotation, bool tracked)
        {
            transform.SetPositionAndRotation(position, rotation);
            IsTracked = tracked;
        }
    }
}
