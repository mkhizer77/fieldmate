using System;
using System.Collections.Generic;
using Fieldmate.Localization;

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

    /// <summary>Human name for UI and speech in the current language (#24); falls back to the id for unknown parts.</summary>
    public string DisplayName(string partId) => TryGet(partId, out var part) ? part.In(Lang.Current).Name : partId ?? string.Empty;

    /// <summary>
    /// The German name with its article in <paramref name="grammaticalCase"/> ("den Hauptschalter", "das Manometer");
    /// the bare English name when German is off or the part has no German.
    /// </summary>
    public string WithArticle(string partId, GermanCase grammaticalCase)
    {
        if (!TryGet(partId, out var part))
        {
            return partId ?? string.Empty;
        }

        var german = part.German;
        return Lang.IsGerman && german != null ? German.Article(german.Gender, grammaticalCase) + " " + german.Name : part.Name;
    }
}
