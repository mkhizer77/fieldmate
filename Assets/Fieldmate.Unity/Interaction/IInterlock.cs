namespace Fieldmate.Interaction;

/// <summary>
/// Decides whether a part may be operated right now (#65): a part held by a safety rule stays still, like a real
/// interlock. Controls ask on every grab and report refused attempts; <see cref="MachineControlRouter"/> answers from
/// the procedure runner.
/// </summary>
public interface IInterlock
{
    bool Allows(string partId);
}
