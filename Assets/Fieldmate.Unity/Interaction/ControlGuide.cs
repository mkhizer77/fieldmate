using Fieldmate.UI;
using UnityEngine;

namespace Fieldmate.Interaction
{
    /// <summary>
    /// Shows how to operate the current step's control: an arc with an arrowhead from the control's current angle to the
    /// target (which way and how far to turn), an arrow pulling a cover off, or a line carrying a tool to its socket.
    /// One LineRenderer, positions written into a preallocated buffer every frame; no allocations.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class ControlGuide : MonoBehaviour
    {
        private const int ArcPoints = 28;
        private const int ArrowPoints = 3;
        private const float MaxDrawnSweep = 330f; // multi-turn handwheels: show the direction, not every turn

        private enum Kind
        {
            None,
            Rotary,
            Pull,
            Carry,
        }

        private readonly Vector3[] points = new Vector3[ArcPoints + ArrowPoints];
        private LineRenderer line;
        private Kind kind;
        private RotaryInteractable rotary;
        private float targetAngle;
        private float radius;
        private Transform from;
        private Transform to;
        private Vector3 pullDirection;

        public bool IsShowing => kind != Kind.None;
        public int PointCount => line != null ? line.positionCount : 0;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.008f;
            line.startColor = line.endColor = Theme.Accent;
            line.numCapVertices = 2;
            Hide();
        }

        public void ShowRotary(RotaryInteractable control, float target, float arcRadius)
        {
            kind = Kind.Rotary;
            rotary = control;
            targetAngle = target;
            radius = arcRadius;
            line.enabled = true;
        }

        public void ShowPull(Transform part, Vector3 worldDirection)
        {
            kind = Kind.Pull;
            from = part;
            pullDirection = worldDirection.normalized;
            line.enabled = true;
        }

        public void ShowCarry(Transform tool, Transform socket)
        {
            kind = Kind.Carry;
            from = tool;
            to = socket;
            line.enabled = true;
        }

        public void Hide()
        {
            kind = Kind.None;
            line.enabled = false;
            line.positionCount = 0;
        }

        private void LateUpdate()
        {
            switch (kind)
            {
                case Kind.Rotary when rotary != null:
                    DrawArc();
                    break;
                case Kind.Pull when from != null:
                    DrawArrow(from.position, from.position + pullDirection * 0.25f);
                    break;
                case Kind.Carry when from != null && to != null:
                    DrawArrow(from.position, to.position);
                    break;
            }
        }

        private void DrawArc()
        {
            var space = rotary.Handle.parent;
            var axis = rotary.Axis.normalized;
            var reference = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var sweep = Mathf.Clamp(targetAngle - rotary.Angle, -MaxDrawnSweep, MaxDrawnSweep);
            if (Mathf.Abs(sweep) < 1f)
            {
                line.positionCount = 0;
                return;
            }

            var center = rotary.Handle.localPosition;
            for (var i = 0; i < ArcPoints; i++)
            {
                var angle = rotary.Angle + sweep * i / (ArcPoints - 1);
                points[i] = space.TransformPoint(center + Quaternion.AngleAxis(angle, axis) * reference * radius);
            }

            // Arrowhead at the target end: two short strokes back along the arc.
            var tip = points[ArcPoints - 1];
            var back = (points[ArcPoints - 3] - tip).normalized;
            var side = space.TransformDirection(axis).normalized * 0.025f;
            points[ArcPoints] = tip + back * 0.03f + side;
            points[ArcPoints + 1] = tip;
            points[ArcPoints + 2] = tip + back * 0.03f - side;
            line.positionCount = ArcPoints + ArrowPoints;
            line.SetPositions(points);
        }

        private void DrawArrow(Vector3 start, Vector3 end)
        {
            var back = (start - end).normalized;
            var side = Vector3.Cross(back, Vector3.up);
            side = (side.sqrMagnitude < 1e-4f ? Vector3.right : side.normalized) * 0.025f;
            points[0] = start;
            points[1] = end;
            points[2] = end + back * 0.04f + side;
            points[3] = end;
            points[4] = end + back * 0.04f - side;
            line.positionCount = 5;
            for (var i = 0; i < 5; i++)
            {
                line.SetPosition(i, points[i]);
            }
        }
    }
}
