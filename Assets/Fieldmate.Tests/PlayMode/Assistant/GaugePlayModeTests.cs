using System.Collections;
using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Twin;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

/// <summary>
/// #88: the gauge reads the twin. Checked from the user's side of the machine: a camera in front of the dial sees the
/// needle at the reading's angle, clockwise, and the window shows the same value.
/// </summary>
public class GaugePlayModeTests
{
    private MachineServices machine;
    private PressureGauge gauge;
    private Camera eye;

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
        yield return ProcedurePlayModeTests.PlaceMachine();
        machine = Object.FindAnyObjectByType<MachineServices>();
        gauge = Object.FindAnyObjectByType<PressureGauge>();

        // 60 cm in front of the dial, looking at it the way the user does (the skid's front is +Z).
        var dial = gauge.transform.Find("Dial");
        var skid = gauge.transform.root;
        eye = new GameObject("Test eye", typeof(Camera)).GetComponent<Camera>();
        eye.transform.SetPositionAndRotation(dial.position + skid.forward * 0.6f, Quaternion.LookRotation(-skid.forward, skid.up));
    }

    [TearDown]
    public void RemoveEye()
    {
        if (eye != null)
        {
            Object.Destroy(eye.gameObject);
        }
    }

    // The needle's direction on screen, degrees clockwise from straight up.
    private float ScreenAngle()
    {
        var needle = gauge.transform.Find("Needle");
        var hub = eye.WorldToScreenPoint(needle.position);
        var tip = eye.WorldToScreenPoint(needle.position + needle.up * 0.05f);
        return Mathf.Atan2(tip.x - hub.x, tip.y - hub.y) * Mathf.Rad2Deg;
    }

    [UnityTest]
    public IEnumerator Needle_and_window_show_the_stuck_relief_valve_in_the_red()
    {
        yield return new WaitForSeconds(1.5f); // the needle settles from where the line was on load
        var reading = machine.Telemetry[TelemetryChannel.Pressure];
        Assert.That(reading, Is.GreaterThan(6f), "the demo starts with the overpressure fault");

        Assert.That(ScreenAngle(), Is.EqualTo(GaugeScale.Pressure.Angle(reading)).Within(6f),
            "seen from the front, the needle points at the reading (right of twelve for high pressure)");
        Assert.That(ScreenAngle(), Is.GreaterThan(30f), "6+ bar is clockwise past twelve, in the red");
        Assert.That(gauge.Readout.ShownTenths / 10f, Is.EqualTo(reading).Within(0.11f), "the window shows the twin's reading to a tenth");
        Assert.That(gauge.Readout.IsLit(0, 0) || gauge.Readout.IsLit(0, 1), Is.False, "no leading zero under 10 bar");
        Assert.That(gauge.Readout.Colour.r, Is.GreaterThan(gauge.Readout.Colour.g), "alarm colour");
    }

    [UnityTest]
    public IEnumerator Needle_falls_anticlockwise_to_zero_when_the_pump_is_isolated()
    {
        Object.FindObjectsByType<Fieldmate.Interaction.RotaryInteractable>(FindObjectsSortMode.None)
            .Single(r => r.PartId == "main_breaker").SetAngle(135f); // locked out
        yield return new WaitForSeconds(6f); // the line bleeds down over a few seconds
        var reading = machine.Telemetry[TelemetryChannel.Pressure];
        Assert.That(reading, Is.LessThan(0.2f));
        Assert.That(ScreenAngle(), Is.EqualTo(GaugeScale.Pressure.Angle(0f)).Within(6f), "pinned at zero, lower left");
        Assert.That(gauge.Readout.ShownTenths, Is.LessThanOrEqualTo(1), "0.0 or 0.1 in the window");
        Assert.That(gauge.Readout.Colour.g, Is.GreaterThan(gauge.Readout.Colour.r), "normal colour");
    }

    [UnityTest]
    public IEnumerator Dial_print_and_readout_are_big_enough_to_read_from_a_step_back()
    {
        yield return null;
        var dial = gauge.transform.Find("Dial");
        var diameter = dial.GetComponent<Renderer>().bounds.size.x;
        Assert.That(diameter, Is.InRange(0.14f, 0.18f), "about 16 cm across in the room");
        Assert.That(dial.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null, "the printed scale");

        var digits = gauge.Readout.GetComponent<Renderer>().bounds.size.y;
        Assert.That(digits, Is.InRange(0.008f, 0.02f), "readout digits 8–20 mm tall");
        var toEye = (eye.transform.position - dial.position).normalized;
        Assert.That(Vector3.Dot(dial.forward, toEye), Is.GreaterThan(0.99f), "the dial faces the user");
    }
}
