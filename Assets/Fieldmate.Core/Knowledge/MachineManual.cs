using System;
using System.Collections.Generic;
using Fieldmate.Json;

namespace Fieldmate.Knowledge;

public enum SectionKind
{
    Overview,
    Safety,
    Part,
    Fault,
    Procedure,
}

public sealed class PartInfo
{
    public PartInfo(string id, string name, string description, string safety)
    {
        Id = id;
        Name = name;
        Description = description;
        Safety = safety;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    /// <summary>Hazard note for the part, or empty.</summary>
    public string Safety { get; }
}

public sealed class FaultInfo
{
    public FaultInfo(string id, string name, string symptoms, string cause, string procedureId)
    {
        Id = id;
        Name = name;
        Symptoms = symptoms;
        Cause = cause;
        ProcedureId = procedureId;
    }

    public string Id { get; }
    public string Name { get; }
    public string Symptoms { get; }
    public string Cause { get; }

    /// <summary>Procedure that fixes the fault, or null.</summary>
    public string ProcedureId { get; }
}

public sealed class ProcedureStepInfo
{
    public ProcedureStepInfo(string id, string title, string partId)
    {
        Id = id;
        Title = title;
        PartId = partId;
    }

    public string Id { get; }
    public string Title { get; }
    public string PartId { get; }
}

public sealed class ProcedureInfo
{
    public ProcedureInfo(string id, string title, IReadOnlyList<ProcedureStepInfo> steps)
    {
        Id = id;
        Title = title;
        Steps = steps;
    }

    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<ProcedureStepInfo> Steps { get; }
}

/// <summary>A citable unit of manual text. Answers must quote its <see cref="Id"/> (design.md §5.4).</summary>
public sealed class ManualSection
{
    public ManualSection(string id, SectionKind kind, string title, string text, IReadOnlyList<string> partIds,
        IReadOnlyList<string> stepIds, IReadOnlyList<string> keywords)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Text = text;
        PartIds = partIds;
        StepIds = stepIds;
        Keywords = keywords;
    }

    public string Id { get; }
    public SectionKind Kind { get; }
    public string Title { get; }
    public string Text { get; }
    public IReadOnlyList<string> PartIds { get; }
    public IReadOnlyList<string> StepIds { get; }
    public IReadOnlyList<string> Keywords { get; }
}

/// <summary>Manual content that could not be read, with a JSON-path-like location.</summary>
public sealed class ManualFormatException : Exception
{
    public ManualFormatException(string message) : base(message)
    {
    }
}

/// <summary>
/// The machine manual (manual.json): parts, faults, procedures and citable sections. The assistant is grounded in it
/// via <see cref="ManualRetriever"/>.
/// </summary>
public sealed class MachineManual
{
    private readonly Dictionary<string, PartInfo> partsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManualSection> sectionsById = new(StringComparer.Ordinal);

    public MachineManual(string machineName, IReadOnlyList<PartInfo> parts, IReadOnlyList<FaultInfo> faults,
        IReadOnlyList<ProcedureInfo> procedures, IReadOnlyList<ManualSection> sections)
    {
        MachineName = machineName ?? string.Empty;
        Parts = parts ?? throw new ArgumentNullException(nameof(parts));
        Faults = faults ?? throw new ArgumentNullException(nameof(faults));
        Procedures = procedures ?? throw new ArgumentNullException(nameof(procedures));
        Sections = sections ?? throw new ArgumentNullException(nameof(sections));
        foreach (var part in parts)
        {
            partsById.TryAdd(part.Id, part);
        }

        foreach (var section in sections)
        {
            sectionsById.TryAdd(section.Id, section);
        }
    }

    public string MachineName { get; }
    public IReadOnlyList<PartInfo> Parts { get; }
    public IReadOnlyList<FaultInfo> Faults { get; }
    public IReadOnlyList<ProcedureInfo> Procedures { get; }
    public IReadOnlyList<ManualSection> Sections { get; }

    public bool TryGetPart(string id, out PartInfo part) => partsById.TryGetValue(id ?? string.Empty, out part);

    public bool TryGetSection(string id, out ManualSection section) => sectionsById.TryGetValue(id ?? string.Empty, out section);

    /// <summary>Parses manual.json. Throws <see cref="JsonException"/> or <see cref="ManualFormatException"/>.</summary>
    public static MachineManual Parse(string json)
    {
        var root = JsonReader.Parse(json);
        if (root.Kind != JsonKind.Object)
        {
            throw new ManualFormatException("The manual must be a JSON object.");
        }

        var parts = ReadList(root, "parts", (item, path) => new PartInfo(
            RequiredString(item, "id", path), RequiredString(item, "name", path),
            item["description"].AsString(string.Empty), item["safety"].AsString(string.Empty)));

        var faults = ReadList(root, "faults", (item, path) => new FaultInfo(
            RequiredString(item, "id", path), RequiredString(item, "name", path),
            item["symptoms"].AsString(string.Empty), item["cause"].AsString(string.Empty), item["procedure"].AsString()));

        var procedures = ReadList(root, "procedures", (item, path) => new ProcedureInfo(
            RequiredString(item, "id", path), RequiredString(item, "title", path),
            ReadList(item, "steps", (step, stepPath) => new ProcedureStepInfo(
                RequiredString(step, "id", stepPath), RequiredString(step, "title", stepPath), step["part"].AsString()), path)));

        var sections = ReadList(root, "sections", (item, path) => new ManualSection(
            RequiredString(item, "id", path), ParseKind(item["kind"].AsString("overview"), path),
            RequiredString(item, "title", path), RequiredString(item, "text", path),
            item["parts"].AsStringList(), item["steps"].AsStringList(), item["keywords"].AsStringList()));

        return new MachineManual(root["machine"].AsString(string.Empty), parts, faults, procedures, sections);
    }

    /// <summary>Broken references and duplicates; empty when the manual is consistent.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var partIds = CollectIds(Parts, p => p.Id, "part", problems);
        CollectIds(Faults, f => f.Id, "fault", problems);
        var procedureIds = CollectIds(Procedures, p => p.Id, "procedure", problems);
        CollectIds(Sections, s => s.Id, "section", problems);

        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var procedure in Procedures)
        {
            foreach (var step in procedure.Steps)
            {
                if (!stepIds.Add(step.Id))
                {
                    problems.Add($"Duplicate step id '{step.Id}'.");
                }

                if (step.PartId != null && !partIds.Contains(step.PartId))
                {
                    problems.Add($"Step '{step.Id}' references unknown part '{step.PartId}'.");
                }
            }
        }

        foreach (var fault in Faults)
        {
            if (fault.ProcedureId != null && !procedureIds.Contains(fault.ProcedureId))
            {
                problems.Add($"Fault '{fault.Id}' references unknown procedure '{fault.ProcedureId}'.");
            }
        }

        foreach (var section in Sections)
        {
            foreach (var part in section.PartIds)
            {
                if (!partIds.Contains(part))
                {
                    problems.Add($"Section '{section.Id}' references unknown part '{part}'.");
                }
            }

            foreach (var step in section.StepIds)
            {
                if (!stepIds.Contains(step))
                {
                    problems.Add($"Section '{section.Id}' references unknown step '{step}'.");
                }
            }
        }

        return problems;
    }

    /// <summary>Manual parts that have no counterpart among <paramref name="scenePartIds"/> (tagged scene objects).</summary>
    public IReadOnlyList<string> FindPartsMissingFrom(IEnumerable<string> scenePartIds)
    {
        var present = new HashSet<string>(scenePartIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var part in Parts)
        {
            if (!present.Contains(part.Id))
            {
                missing.Add(part.Id);
            }
        }

        return missing;
    }

    private static HashSet<string> CollectIds<T>(IEnumerable<T> items, Func<T, string> id, string label, List<string> problems)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!ids.Add(id(item)))
            {
                problems.Add($"Duplicate {label} id '{id(item)}'.");
            }
        }

        return ids;
    }

    private static IReadOnlyList<T> ReadList<T>(JsonValue parent, string name, Func<JsonValue, string, T> read, string parentPath = "")
    {
        var path = parentPath.Length == 0 ? name : $"{parentPath}.{name}";
        var array = parent[name];
        if (array.IsNull)
        {
            return Array.Empty<T>();
        }

        if (array.Kind != JsonKind.Array)
        {
            throw new ManualFormatException($"'{path}' must be an array.");
        }

        var result = new List<T>(array.Items.Count);
        for (var i = 0; i < array.Items.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            if (array.Items[i].Kind != JsonKind.Object)
            {
                throw new ManualFormatException($"'{itemPath}' must be an object.");
            }

            result.Add(read(array.Items[i], itemPath));
        }

        return result;
    }

    private static string RequiredString(JsonValue item, string name, string path)
    {
        var value = item[name].AsString();
        return string.IsNullOrWhiteSpace(value) ? throw new ManualFormatException($"'{path}.{name}' is required.") : value;
    }

    private static SectionKind ParseKind(string kind, string path) => kind switch
    {
        "overview" => SectionKind.Overview,
        "safety" => SectionKind.Safety,
        "part" => SectionKind.Part,
        "fault" => SectionKind.Fault,
        "procedure" => SectionKind.Procedure,
        _ => throw new ManualFormatException($"'{path}.kind' must be overview, safety, part, fault or procedure (was '{kind}')."),
    };
}
