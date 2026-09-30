using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.XR
{
    public class PlacementMathTests
    {
        [Test]
        public void FloorPlane_IsHitByADownwardRay()
        {
            var ray = new Ray(new Vector3(0f, 1.2f, 0f), new Vector3(0f, -1f, 1f));

            Assert.That(PlacementMath.TryHitFloorPlane(ray, 0f, out var point), Is.True);
            Assert.That(point.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(point.y, Is.EqualTo(0f));
            Assert.That(point.z, Is.EqualTo(1.2f).Within(1e-4f));
        }

        [Test]
        public void FloorPlane_RejectsUpwardFlatBelowAndFarRays()
        {
            Assert.That(PlacementMath.TryHitFloorPlane(new Ray(Vector3.up, Vector3.forward), 0f, out _), Is.False, "horizontal");
            Assert.That(PlacementMath.TryHitFloorPlane(new Ray(Vector3.up, new Vector3(0, 1, 1)), 0f, out _), Is.False, "upward");
            Assert.That(PlacementMath.TryHitFloorPlane(new Ray(Vector3.down, Vector3.down), 0f, out _), Is.False, "starts below floor");
            Assert.That(PlacementMath.TryHitFloorPlane(new Ray(Vector3.up * 1.5f, new Vector3(0f, -0.1f, 1f)), 0f, out _), Is.False, "beyond max distance");
        }

        [TestCase(0f, 1f, 0f, true)]
        [TestCase(0.2f, 0.98f, 0f, true)]
        [TestCase(0f, 0f, 1f, false)]
        [TestCase(0.7f, 0.7f, 0f, false)]
        public void FloorLike_OnlyForNearlyHorizontalSurfaces(float x, float y, float z, bool expected)
        {
            Assert.That(PlacementMath.IsFloorLike(new Vector3(x, y, z)), Is.EqualTo(expected));
        }

        [Test]
        public void FacingViewer_TurnsTheFrontTowardsTheUser_Upright()
        {
            var rotation = PlacementMath.FacingViewer(new Vector3(0f, 0f, 2f), new Vector3(0f, 1.6f, 0f), 0f);
            var front = rotation * Vector3.forward;

            Assert.That(front.z, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That((rotation * Vector3.up).y, Is.EqualTo(1f).Within(1e-4f), "never tilted");
            var turned = PlacementMath.FacingViewer(new Vector3(0f, 0f, 2f), new Vector3(0f, 1.6f, 0f), 90f) * Vector3.forward;
            Assert.That(turned.x, Is.EqualTo(-1f).Within(1e-4f));
        }

        [TestCase(190f, -170f)]
        [TestCase(-190f, 170f)]
        [TestCase(540f, 180f)]
        [TestCase(45f, 45f)]
        public void WrapYaw(float input, float expected)
        {
            Assert.That(PlacementMath.WrapYaw(input), Is.EqualTo(expected).Within(1e-4f));
        }
    }
}
