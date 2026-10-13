using System;
using System.Collections.Generic;
using Fieldmate.AI;
using Fieldmate.Procedures;
using Fieldmate.Twin;

namespace Fieldmate.Net;

/// <summary>
/// The remote observer's wire format (design.md §5.7). One byte of message type, then the body. The trainee (headset)
/// is authoritative: it sends <see cref="MessageType.Hello"/> on join, a <see cref="MessageType.Snapshot"/> 10 times a
/// second and every transcript line as it happens; the observer can only send <see cref="MessageType.Command"/>.
/// </summary>
public static class ObserverProtocol
{
    /// <summary>Bump on any incompatible change; mismatched peers are told and ignored.</summary>
    public const ushort Version = 1;

    /// <summary>Named-message channel shared by both apps.</summary>
    public const string Channel = "fieldmate.observer";

    public const float SnapshotHz = 10f;
    public const int BacklogLines = 40;
    public const int MaxTextChars = 200;
    public const int MaxTranscriptChars = 600;
}

public enum MessageType : byte
{
    Hello = 1,
    Snapshot = 2,
    Transcript = 3,
    Command = 10,
    CommandResult = 11,
}

public enum ObserverCommandKind : byte
{
    InjectFault = 1,
    ClearFault = 2,
    HintStep = 3,
    SendMessage = 4,
}

/// <summary>What the observer asks the trainee's app to do. The trainee validates and may refuse.</summary>
public readonly struct ObserverCommand
{
    public ObserverCommand(ObserverCommandKind kind, string argument, uint id = 0)
    {
        Kind = kind;
        Argument = argument ?? string.Empty;
        Id = id;
    }

    public static ObserverCommand InjectFault(string faultId) => new(ObserverCommandKind.InjectFault, faultId);
    public static ObserverCommand ClearFault(string faultId) => new(ObserverCommandKind.ClearFault, faultId);
    public static ObserverCommand HintStep(string text = null) => new(ObserverCommandKind.HintStep, text);
    public static ObserverCommand SendMessage(string text) => new(ObserverCommandKind.SendMessage, text);

    public ObserverCommandKind Kind { get; }

    /// <summary>A fault id, or the hint / message text.</summary>
    public string Argument { get; }

    /// <summary>Chosen by the observer so it can match the <see cref="MessageType.CommandResult"/>.</summary>
    public uint Id { get; }

    public ObserverCommand WithId(uint id) => new(Kind, Argument, id);

    public void Write(NetWriter w)
    {
        w.Byte((byte)MessageType.Command);
        w.UInt(Id);
        w.Byte((byte)Kind);
        w.String(Argument, ObserverProtocol.MaxTextChars);
    }

    /// <summary>Reads the body after the type byte.</summary>
    public static ObserverCommand Read(NetReader r)
    {
        var id = r.UInt();
        var kind = (ObserverCommandKind)r.Byte();
        return new ObserverCommand(kind, r.String(), id);
    }
}

/// <summary>Sent once to each observer that joins: what it is watching.</summary>
public sealed class ObserverHello
{
    public ushort Version { get; set; } = ObserverProtocol.Version;
    public string MachineName { get; set; } = string.Empty;
    public List<(string Id, string Name)> Faults { get; } = new();

    public void Write(NetWriter w)
    {
        w.Byte((byte)MessageType.Hello);
        w.UShort(Version);
        w.String(MachineName, 120);
        w.Byte((byte)Math.Min(Faults.Count, 32));
        for (var i = 0; i < Math.Min(Faults.Count, 32); i++)
        {
            w.String(Faults[i].Id, 60);
            w.String(Faults[i].Name, 120);
        }
    }

    public static ObserverHello Read(NetReader r)
    {
        var hello = new ObserverHello { Version = r.UShort(), MachineName = r.String() };
        var count = r.Byte();
        for (var i = 0; i < count; i++)
        {
            hello.Faults.Add((r.String(), r.String()));
        }

        return hello;
    }
}

/// <summary>One transcript line with its sequence number, so a late joiner's backlog and live lines never duplicate.</summary>
public readonly struct TranscriptLine
{
    public TranscriptLine(uint sequence, TranscriptKind kind, string text)
    {
        Sequence = sequence;
        Kind = kind;
        Text = text ?? string.Empty;
    }

    public uint Sequence { get; }
    public TranscriptKind Kind { get; }
    public string Text { get; }

    public void Write(NetWriter w)
    {
        w.Byte((byte)MessageType.Transcript);
        w.UInt(Sequence);
        w.Byte((byte)Kind);
        w.String(Text, ObserverProtocol.MaxTranscriptChars);
    }

    public static TranscriptLine Read(NetReader r) => new(r.UInt(), (TranscriptKind)r.Byte(), r.String());
}

/// <summary>
/// The twin and the procedure at one moment. Mutable and reused by the trainee so sending allocates nothing.
/// </summary>
public sealed class ObserverSnapshot
{
    public uint Sequence;
    public float SessionSeconds;
    public readonly float[] Telemetry = new float[TelemetryModel.ChannelCount];
    public readonly ChannelStatus[] Status = new ChannelStatus[TelemetryModel.ChannelCount];
    public bool Powered;
    public float Inlet;
    public float Outlet;

    /// <summary>Bit i = fault i of the hello's list is active.</summary>
    public uint ActiveFaults;

    public RunnerState Procedure;
    public string ProcedureTitle = string.Empty;
    public short StepIndex = -1;
    public short StepCount;
    public string StepTitle = string.Empty;
    public short Errors;
    public short Violations;
    public short HelpRequests;

    /// <summary>The final score once completed, else -1.</summary>
    public short Score = -1;

    public bool Passed;

    /// <summary>Fills the snapshot from the twin and the runner (either may be null).</summary>
    public void Capture(TelemetryModel telemetry, ProcedureRunner runner, float sessionSeconds)
    {
        Sequence++;
        SessionSeconds = sessionSeconds;
        if (telemetry != null)
        {
            for (var i = 0; i < TelemetryModel.ChannelCount; i++)
            {
                Telemetry[i] = telemetry[(TelemetryChannel)i];
                Status[i] = telemetry.Status((TelemetryChannel)i);
            }

            Powered = telemetry.Inputs.Powered;
            Inlet = telemetry.Inputs.InletOpening;
            Outlet = telemetry.Inputs.OutletOpening;
            ActiveFaults = 0;
            var definitions = telemetry.Faults.Definitions;
            for (var i = 0; i < definitions.Count && i < 32; i++)
            {
                if (telemetry.Faults.IsActive(definitions[i].Id))
                {
                    ActiveFaults |= 1u << i;
                }
            }
        }

        if (runner == null)
        {
            return;
        }

        Procedure = runner.State;
        ProcedureTitle = runner.Definition.Title;
        StepCount = (short)runner.Definition.Steps.Count;
        StepIndex = (short)runner.CurrentStepIndex;
        StepTitle = runner.CurrentStep?.Title ?? string.Empty;
        Errors = (short)runner.Errors.Count;
        Violations = (short)runner.Violations.Count;
        HelpRequests = (short)runner.HelpRequests;
        Score = runner.Result != null ? (short)runner.Result.Score : (short)-1;
        Passed = runner.Result?.Passed ?? false;
    }

    public void Write(NetWriter w)
    {
        w.Byte((byte)MessageType.Snapshot);
        w.UInt(Sequence);
        w.Float(SessionSeconds);
        for (var i = 0; i < Telemetry.Length; i++)
        {
            w.Float(Telemetry[i]);
            w.Byte((byte)Status[i]);
        }

        w.Bool(Powered);
        w.Float(Inlet);
        w.Float(Outlet);
        w.UInt(ActiveFaults);
        w.Byte((byte)Procedure);
        w.String(ProcedureTitle, 120);
        w.Short(StepIndex);
        w.Short(StepCount);
        w.String(StepTitle, 160);
        w.Short(Errors);
        w.Short(Violations);
        w.Short(HelpRequests);
        w.Short(Score);
        w.Bool(Passed);
    }

    public void Read(NetReader r)
    {
        Sequence = r.UInt();
        SessionSeconds = r.Float();
        for (var i = 0; i < Telemetry.Length; i++)
        {
            Telemetry[i] = r.Float();
            Status[i] = (ChannelStatus)r.Byte();
        }

        Powered = r.Bool();
        Inlet = r.Float();
        Outlet = r.Float();
        ActiveFaults = r.UInt();
        Procedure = (RunnerState)r.Byte();
        ProcedureTitle = r.String();
        StepIndex = r.Short();
        StepCount = r.Short();
        StepTitle = r.String();
        Errors = r.Short();
        Violations = r.Short();
        HelpRequests = r.Short();
        Score = r.Short();
        Passed = r.Bool();
    }
}
