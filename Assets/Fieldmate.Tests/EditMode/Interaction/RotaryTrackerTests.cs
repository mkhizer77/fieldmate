using Fieldmate.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.Interaction;

public class RotaryTrackerTests
{
    private static Vector3 At(float degrees, float radius = 0.08f) =>
        Quaternion.AngleAxis(degrees, Vector3.up) * new Vector3(0f, 0f, radius);

    /// <summary>Turns the hand around the axis in small steps, like a real grab.</summary>
    private static void Turn(RotaryTracker tracker, float from, float to, float step = 5f)
    {
        tracker.Begin(At(from));
        var direction = Mathf.Sign(to - from);
        for (var a = from; direction * (to - a) > 0f; a += direction * step)
        {
            tracker.Update(At(a));
        }

        tracker.Update(At(to));
    }

    [Test]
    public void FollowsTheHandAroundTheAxis()
    {
        var tracker = new RotaryTracker(Vector3.up, 0f, 90f, 0f);
        Turn(tracker, 0f, 45f);
        Assert.That(tracker.Angle, Is.EqualTo(45f).Within(0.01f));
        Assert.That(tracker.Normalized, Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void ClampsToItsRange()
    {
        var tracker = new RotaryTracker(Vector3.up, 0f, 90f, 0f);
        Turn(tracker, 0f, 150f);
        Assert.That(tracker.Angle, Is.EqualTo(90f));
        Turn(tracker, 150f, -60f);
        Assert.That(tracker.Angle, Is.EqualTo(0f));
    }

    [Test]
    public void MultiTurnHandwheel_CountsPast360()
    {
        var tracker = new RotaryTracker(Vector3.up, 0f, 1080f, 0f);
        Turn(tracker, 0f, 900f);
        Assert.That(tracker.Angle, Is.EqualTo(900f).Within(0.1f));
    }

    [Test]
    public void TenFullOpenCloseCycles_EndWhereTheyStarted()
    {
        // Device test for #9: ten consecutive valve turns without losing the grab.
        var tracker = new RotaryTracker(Vector3.up, 0f, 1080f, 0f);
        for (var i = 0; i < 10; i++)
        {
            Turn(tracker, 0f, 1080f);
            Assert.That(tracker.Angle, Is.EqualTo(1080f).Within(0.1f));
            Turn(tracker, 1080f, 0f);
            Assert.That(tracker.Angle, Is.EqualTo(0f).Within(0.1f));
        }
    }

    [Test]
    public void HandsOnTheAxis_AreIgnored()
    {
        var tracker = new RotaryTracker(Vector3.up, 0f, 90f, 0f);
        tracker.Begin(At(0f));
        tracker.Update(new Vector3(0f, 0.2f, 0.005f)); // right above the pivot: direction is meaningless
        tracker.Update(At(30f));
        Assert.That(tracker.Angle, Is.EqualTo(30f).Within(0.01f));
    }

    [Test]
    public void HeightAlongTheAxis_DoesNotTurn()
    {
        var tracker = new RotaryTracker(Vector3.up, 0f, 90f, 0f);
        tracker.Begin(At(10f));
        tracker.Update(At(10f) + Vector3.up * 0.3f);
        Assert.That(tracker.Angle, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void ARegrab_DoesNotJump()
    {
        var tracker = new RotaryTracker(Vector3.up, 0f, 90f, 0f);
        Turn(tracker, 0f, 30f);
        tracker.End();
        tracker.Begin(At(200f)); // grabbed again somewhere else on the rim
        tracker.Update(At(210f));
        Assert.That(tracker.Angle, Is.EqualTo(40f).Within(0.01f));
    }

    [Test]
    public void TwoHandGrip_TurnsWithTheLineBetweenTheHands()
    {
        // Breaker: the vector from the left to the right hand rotates with the bar.
        var tracker = new RotaryTracker(Vector3.back, 0f, 135f, 0f);
        Vector3 Bar(float degrees) => Quaternion.AngleAxis(degrees, Vector3.back) * new Vector3(0.25f, 0f, 0f);
        tracker.Begin(Bar(0f));
        for (var a = 5f; a <= 90f; a += 5f)
        {
            tracker.Update(Bar(a));
        }

        Assert.That(tracker.Angle, Is.EqualTo(90f).Within(0.01f));
    }

    [Test]
    public void Gain_scales_hand_travel_into_handle_degrees()
    {
        var plain = new RotaryTracker(Vector3.up, 0f, 720f, 0f);
        var geared = new RotaryTracker(Vector3.up, 0f, 720f, 0f, turnGain: 1.5f);
        plain.Begin(Vector3.right);
        geared.Begin(Vector3.right);
        for (var a = 10f; a <= 60f; a += 10f)
        {
            plain.Update(Quaternion.AngleAxis(a, Vector3.up) * Vector3.right);
            geared.Update(Quaternion.AngleAxis(a, Vector3.up) * Vector3.right);
        }

        Assert.That(plain.Angle, Is.EqualTo(60f).Within(0.01f));
        Assert.That(geared.Angle, Is.EqualTo(90f).Within(0.01f), "1.5 handle degrees per hand degree");
    }

    [Test]
    public void A_tracking_jump_between_updates_does_not_swing_the_handle()
    {
        // Device test 2026-10-02 (#90): the breaker went on → locked in 0.4 s; the two grips' line flipped as they crossed.
        var tracker = new RotaryTracker(Vector3.back, 0f, 135f, 90f, turnGain: 1.25f);
        Vector3 Bar(float degrees) => Quaternion.AngleAxis(degrees, Vector3.back) * new Vector3(0.25f, 0f, 0f);
        tracker.Begin(Bar(90f));
        for (var a = 85f; a >= 10f; a -= 5f)
        {
            tracker.Update(Bar(a)); // turning back to ON
        }

        Assert.That(tracker.Angle, Is.EqualTo(0f).Within(0.01f), "on");
        tracker.Update(Bar(10f + 180f)); // the line between the grips flips in one frame
        Assert.That(tracker.Angle, Is.EqualTo(0f).Within(0.01f), "a half-turn in one frame is a tracking jump, not a turn");
        tracker.Update(Bar(10f + 185f));
        Assert.That(tracker.Angle, Is.EqualTo(5f * 1.25f).Within(0.01f), "re-anchored: real motion afterwards still turns it");
    }
}
