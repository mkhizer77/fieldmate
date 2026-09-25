using System;
using System.Collections.Generic;

namespace Fieldmate.Twin;

/// <summary>How an active fault shifts the simulated channels while the machine runs.</summary>
public readonly struct FaultEffect
{
    public FaultEffect(float pressure = 0f, float temperature = 0f, float vibration = 0f, float current = 0f,
        bool defeatsReliefValve = false)
    {
        Pressure = pressure;
        Temperature = temperature;
        Vibration = vibration;
        Current = current;
        DefeatsReliefValve = defeatsReliefValve;
    }

    public float Pressure { get; }
    public float Temperature { get; }
    public float Vibration { get; }
    public float Current { get; }

    /// <summary>The relief valve no longer caps line pressure (stuck cartridge).</summary>
    public bool DefeatsReliefValve { get; }
}

/// <summary>A fault the twin can suffer: its symptom, telemetry effect and the repair that clears it.</summary>
public sealed class FaultDefinition
{
    public FaultDefinition(string id, string name, string symptom, string repairActionId, FaultEffect effect)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Fault id is required.", nameof(id)) : id;
        Name = name ?? id;
        Symptom = symptom ?? string.Empty;
        RepairActionId = string.IsNullOrWhiteSpace(repairActionId)
            ? throw new ArgumentException("Repair action is required.", nameof(repairActionId))
            : repairActionId;
        Effect = effect;
    }

    public string Id { get; }
    public string Name { get; }
    public string Symptom { get; }
    public string RepairActionId { get; }
    public FaultEffect Effect { get; }
}

public enum RepairOutcome
{
    /// <summary>The action cleared an active fault.</summary>
    Fixed,

    /// <summary>The action matches an active fault but the machine is not isolated; nothing changed.</summary>
    NotIsolated,

    /// <summary>The action is valid but no active fault needs it.</summary>
    NothingToRepair,

    /// <summary>No fault is repaired by this action.</summary>
    UnknownAction,
}

/// <summary>
/// Active faults of the skid (design.md §5.6). Faults are injected from the debug panel or observer and cleared only
/// by their repair action while the machine is isolated.
/// </summary>
public sealed class FaultModel
{
    public const string Overpressure = "overpressure";
    public const string Overheating = "overheating";
    public const string LooseMount = "loose_mount";

    private readonly List<FaultDefinition> definitions;
    private readonly bool[] active;

    public FaultModel(IEnumerable<FaultDefinition> faults)
    {
        if (faults == null)
        {
            throw new ArgumentNullException(nameof(faults));
        }

        definitions = new List<FaultDefinition>(faults);
        for (var i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] == null)
            {
                throw new ArgumentException("Fault definitions must not be null.", nameof(faults));
            }

            for (var j = 0; j < i; j++)
            {
                if (definitions[j].Id == definitions[i].Id)
                {
                    throw new ArgumentException($"Duplicate fault id '{definitions[i].Id}'.", nameof(faults));
                }
            }
        }

        active = new bool[definitions.Count];
    }

    /// <summary>Raised with (fault id, is active) whenever a fault is injected or cleared.</summary>
    public event Action<string, bool> FaultChanged;

    public IReadOnlyList<FaultDefinition> Definitions => definitions;

    public int ActiveCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < active.Length; i++)
            {
                if (active[i])
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The three faults of the relief-valve demo: overpressure, overheating and a loose motor mount.</summary>
    public static FaultModel CreateDefault() => new(new[]
    {
        new FaultDefinition(Overpressure, "Overpressure",
            "Line pressure above 6 bar: the relief valve cartridge is stuck closed.",
            "replace_relief_cartridge", new FaultEffect(pressure: 2.8f, current: 1.5f, defeatsReliefValve: true)),
        new FaultDefinition(Overheating, "Motor overheating",
            "Motor temperature above 80 °C and current rising: cooling fins are blocked.",
            "clean_cooling_fins", new FaultEffect(temperature: 45f, current: 3f)),
        new FaultDefinition(LooseMount, "Loose motor mount",
            "Vibration above 7 mm/s: motor mount bolts have worked loose.",
            "tighten_mount_bolts", new FaultEffect(vibration: 5.5f)),
    });

    public bool IsKnown(string faultId) => IndexOf(faultId) >= 0;

    public bool IsActive(string faultId)
    {
        var index = IndexOf(faultId);
        return index >= 0 && active[index];
    }

    public bool TryGetDefinition(string faultId, out FaultDefinition definition)
    {
        var index = IndexOf(faultId);
        definition = index >= 0 ? definitions[index] : null;
        return index >= 0;
    }

    /// <summary>Activates a fault. Returns false if it is unknown or already active.</summary>
    public bool Inject(string faultId) => SetActive(IndexOf(faultId), true);

    /// <summary>Clears a fault without repairing it (debug panel reset). Returns false if it was not active.</summary>
    public bool Clear(string faultId) => SetActive(IndexOf(faultId), false);

    /// <summary>Clears every active fault.</summary>
    public void ClearAll()
    {
        for (var i = 0; i < active.Length; i++)
        {
            SetActive(i, false);
        }
    }

    /// <summary>
    /// Applies a repair action. Only clears a fault when the machine is isolated (breaker open, no line pressure).
    /// </summary>
    public RepairOutcome TryRepair(string repairActionId, bool machineIsolated)
    {
        var known = false;
        for (var i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].RepairActionId != repairActionId)
            {
                continue;
            }

            known = true;
            if (!active[i])
            {
                continue;
            }

            if (!machineIsolated)
            {
                return RepairOutcome.NotIsolated;
            }

            SetActive(i, false);
            return RepairOutcome.Fixed;
        }

        return known ? RepairOutcome.NothingToRepair : RepairOutcome.UnknownAction;
    }

    /// <summary>Sum of the effects of all active faults. No allocations.</summary>
    public FaultEffect CombinedEffect()
    {
        float pressure = 0f, temperature = 0f, vibration = 0f, current = 0f;
        var defeatsRelief = false;
        for (var i = 0; i < definitions.Count; i++)
        {
            if (!active[i])
            {
                continue;
            }

            var effect = definitions[i].Effect;
            pressure += effect.Pressure;
            temperature += effect.Temperature;
            vibration += effect.Vibration;
            current += effect.Current;
            defeatsRelief |= effect.DefeatsReliefValve;
        }

        return new FaultEffect(pressure, temperature, vibration, current, defeatsRelief);
    }

    private int IndexOf(string faultId)
    {
        for (var i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].Id == faultId)
            {
                return i;
            }
        }

        return -1;
    }

    private bool SetActive(int index, bool value)
    {
        if (index < 0 || active[index] == value)
        {
            return false;
        }

        active[index] = value;
        FaultChanged?.Invoke(definitions[index].Id, value);
        return true;
    }
}
