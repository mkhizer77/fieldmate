using Fieldmate.Assistant;
using UnityEngine;

namespace Fieldmate.Twin
{
    /// <summary>
    /// The discharge pressure gauge (#88): the needle follows the twin's line pressure with a gauge's damping and, while
    /// the pump runs, a slight flutter from the pump's pulsation; the digital window under the hub
    /// (<see cref="SegmentReadout"/>) shows the reading to a tenth of a bar in the status colour. The dial itself is
    /// built by the scene builder on <see cref="GaugeScale.Pressure"/>. No per-frame allocations.
    /// </summary>
    public sealed class PressureGauge : MonoBehaviour
    {
        // Same hues as the printed bands, bright enough to read on the dark window.
        private static readonly Color NormalColor = new(0.45f, 0.95f, 0.55f);
        private static readonly Color WarningColor = new(1f, 0.78f, 0.25f);
        private static readonly Color AlarmColor = new(1f, 0.35f, 0.3f);

        [SerializeField] private Transform needle;
        [SerializeField] private SegmentReadout readout;
        [SerializeField] private float smoothSeconds = 0.35f;
        [SerializeField] private float flutterBar = 0.04f;

        private MachineServices machine;
        private bool searched;
        private float shown;
        private float velocity;
        private ChannelStatus shownStatus = (ChannelStatus)(-1);

        /// <summary>The value the needle points at now, bar.</summary>
        public float NeedleValue => shown;

        /// <summary>The needle's angle, degrees clockwise from twelve o'clock.</summary>
        public float NeedleAngle => GaugeScale.Pressure.Angle(shown);

        public SegmentReadout Readout => readout;

        public void Configure(Transform needlePivot, SegmentReadout digitalReadout)
        {
            needle = needlePivot;
            readout = digitalReadout;
        }

        private void Update()
        {
            if (machine == null)
            {
                if (searched && Time.frameCount % 30 != 0)
                {
                    return; // the machine services may come later (scene load); look again twice a second
                }

                searched = true;
                machine = FindAnyObjectByType<MachineServices>();
                if (machine == null || machine.Telemetry == null)
                {
                    machine = null;
                    return;
                }

                shown = machine.Telemetry[TelemetryChannel.Pressure]; // start where the line is, don't swing up on load
            }

            var telemetry = machine.Telemetry;
            var reading = telemetry[TelemetryChannel.Pressure];
            var target = reading;
            if (telemetry.Inputs.Powered && reading > 0.1f)
            {
                target += flutterBar * (Mathf.PerlinNoise(Time.time * 6f, 0.37f) - 0.5f) * 2f;
            }

            shown = Mathf.SmoothDamp(shown, target, ref velocity, smoothSeconds);
            if (needle != null)
            {
                // The dial faces +Z and the user looks at it along -Z, so their right is -X: a positive turn about Z
                // swings the needle clockwise for them.
                needle.localRotation = Quaternion.Euler(0f, 0f, GaugeScale.Pressure.Angle(shown));
            }

            ShowReading(reading, telemetry.Status(TelemetryChannel.Pressure));
        }

        private void ShowReading(float reading, ChannelStatus status)
        {
            if (readout == null)
            {
                return;
            }

            readout.Show(reading);
            if (status != shownStatus)
            {
                shownStatus = status;
                readout.SetColour(status == ChannelStatus.Alarm ? AlarmColor : status == ChannelStatus.Warning ? WarningColor : NormalColor);
            }
        }
    }
}
