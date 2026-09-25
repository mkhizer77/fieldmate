using System;

namespace Fieldmate.Procedures;

/// <summary>
/// "<see cref="RequiredPartId"/> must be <see cref="RequiredState"/> before anyone acts on <see cref="GuardedPartId"/>",
/// e.g. the breaker must be locked before the inlet valve is touched. Checked on every event.
/// </summary>
public sealed class SafetyRule
{
    public SafetyRule(string id, string description, string guardedPartId, string requiredPartId, string requiredState)
    {
        Id = Require(id, nameof(id));
        Description = description ?? id;
        GuardedPartId = Require(guardedPartId, nameof(guardedPartId));
        RequiredPartId = Require(requiredPartId, nameof(requiredPartId));
        RequiredState = Require(requiredState, nameof(requiredState));
    }

    public string Id { get; }
    public string Description { get; }
    public string GuardedPartId { get; }
    public string RequiredPartId { get; }
    public string RequiredState { get; }

    public override string ToString() => $"{Id}: {RequiredPartId} must be {RequiredState} before {GuardedPartId}";

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;
}
