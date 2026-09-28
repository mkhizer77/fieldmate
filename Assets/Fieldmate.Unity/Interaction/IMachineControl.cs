using System;

namespace Fieldmate.Interaction;

/// <summary>A part the user operates. Raises named states (e.g. "closed", "locked") for the procedure and the twin.</summary>
public interface IMachineControl
{
    string PartId { get; }

    /// <summary>The last named state reached; null before the first one.</summary>
    string State { get; }

    /// <summary>0..1 position along the control's range (a removable part is 0 fitted, 1 removed).</summary>
    float Normalized { get; }

    /// <summary>Raised with (part id, state) when a named state is reached.</summary>
    event Action<string, string> StateReached;
}
