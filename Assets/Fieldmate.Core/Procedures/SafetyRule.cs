using System;

namespace Fieldmate.Procedures;

/// <summary>
/// "<see cref="RequiredPartId"/> must be <see cref="RequiredState"/> before anyone acts on <see cref="GuardedPartId"/>",
/// e.g. the breaker must be locked before the inlet valve is touched. Checked on every event. With
/// <see cref="ExceptWhenGuardedIs"/> set, the rule lets the guarded part go while it is in that state: a closed outlet
/// may always be reopened, only closing it is held (#90).
/// </summary>
public sealed class SafetyRule
{
    public SafetyRule(string id, string description, string guardedPartId, string requiredPartId, string requiredState,
        string exceptWhenGuardedIs = null)
    {
        Id = Require(id, nameof(id));
        Description = description ?? id;
        GuardedPartId = Require(guardedPartId, nameof(guardedPartId));
        RequiredPartId = Require(requiredPartId, nameof(requiredPartId));
        RequiredState = Require(requiredState, nameof(requiredState));
        ExceptWhenGuardedIs = string.IsNullOrWhiteSpace(exceptWhenGuardedIs) ? null : exceptWhenGuardedIs;
    }

    public string Id { get; }
    public string Description { get; }
    public string GuardedPartId { get; }
    public string RequiredPartId { get; }
    public string RequiredState { get; }

    /// <summary>A state of the guarded part in which the rule does not hold it (moving out of it is safe), or null.</summary>
    public string ExceptWhenGuardedIs { get; }

    public override string ToString() =>
        $"{Id}: {RequiredPartId} must be {RequiredState} before {GuardedPartId}" +
        (ExceptWhenGuardedIs == null ? string.Empty : $" (unless it is {ExceptWhenGuardedIs})");

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;
}
