using System;
using System.Collections.Generic;

namespace Fieldmate.Knowledge;

/// <summary>
/// The machine's parts by id, as the twin, the procedures and the assistant refer to them. Built from the manual so
/// there is one source of truth for part ids and names.
/// </summary>
public sealed class PartCatalog
{
    private readonly Dictionary<string, PartInfo> parts = new(StringComparer.Ordinal);
    private readonly List<PartInfo> ordered = new();

    public PartCatalog(IEnumerable<PartInfo> parts)
    {
        foreach (var part in parts ?? throw new ArgumentNullException(nameof(parts)))
        {
            if (part == null || string.IsNullOrWhiteSpace(part.Id))
            {
                throw new ArgumentException("Parts need an id.", nameof(parts));
            }

            if (!this.parts.TryAdd(part.Id, part))
            {
                throw new ArgumentException($"Duplicate part id '{part.Id}'.", nameof(parts));
            }

            ordered.Add(part);
        }
    }

    public static PartCatalog FromManual(MachineManual manual) =>
        new((manual ?? throw new ArgumentNullException(nameof(manual))).Parts);

    public IReadOnlyList<PartInfo> All => ordered;
    public int Count => ordered.Count;

    public bool Contains(string partId) => partId != null && parts.ContainsKey(partId);

    public bool TryGet(string partId, out PartInfo part)
    {
        part = null;
        return partId != null && parts.TryGetValue(partId, out part);
    }

    /// <summary>Human name for UI and speech; falls back to the id for unknown parts.</summary>
    public string DisplayName(string partId) => TryGet(partId, out var part) ? part.Name : partId ?? string.Empty;
}
