namespace Fieldmate.AI;

/// <summary>The v1 tool set (design.md §5.4). Executors are MonoBehaviours registered at scene load.</summary>
public static class FieldmateTools
{
    public const string HighlightPart = "highlight_part";
    public const string StartProcedure = "start_procedure";
    public const string GoToStep = "go_to_step";
    public const string ShowManual = "show_manual";
    public const string ReadTelemetry = "read_telemetry";
    public const string ReportReading = "report_reading";
    public const string LogNote = "log_note";
    public const string SetLanguage = "set_language";
    public const string IdentifyView = "identify_view";

    /// <summary>Highest step index accepted by <see cref="GoToStep"/>; procedures have 6–8 steps.</summary>
    public const int MaxStepIndex = 20;

    public static ToolDefinition[] CreateV1() => new[]
    {
        new ToolDefinition(HighlightPart,
            "Highlight a machine part in the user's view so they can find it. Use the part id from the manual.",
            new ToolParameter("part_id", ToolParameterType.String, "Part id from the manual, e.g. relief_valve.")),
        new ToolDefinition(StartProcedure,
            "Start a maintenance procedure from its first step. Only when the user agrees to begin.",
            new ToolParameter("procedure_id", ToolParameterType.String, "Procedure id from the manual, e.g. relief_valve_replacement.")),
        new ToolDefinition(GoToStep,
            "Show the instructions of a step again on the panel. It never changes progress: the current step stays where it is.",
            new ToolParameter("index", ToolParameterType.Integer, "1-based step number.", minimum: 1, maximum: MaxStepIndex)),
        new ToolDefinition(ReportReading,
            "When the user reports a gauge reading or asks if the reading step is done: reads the machine's gauge and completes the current reading step if the value fits its target. Returns the value and what is next.",
            new ToolParameter("stated_value", ToolParameterType.Number, "The value the user said, if any.", required: false)),
        new ToolDefinition(ShowManual,
            "Open a manual section on the user's panel. Use when the user wants to read details.",
            new ToolParameter("section_id", ToolParameterType.String, "Section id as cited in the context, e.g. safety.loto.")),
        new ToolDefinition(ReadTelemetry,
            "Read current telemetry: pressure (bar), temperature (°C), vibration (mm/s), current (A), with status.",
            new ToolParameter("part_id", ToolParameterType.String, "Optional part to focus on, e.g. motor.", required: false)),
        new ToolDefinition(LogNote,
            "Add a note to the maintenance log for the debrief, e.g. an observation the user dictates.",
            new ToolParameter("text", ToolParameterType.String, "The note, one or two sentences.", maxLength: 500)),
        new ToolDefinition(SetLanguage,
            "Switch the assistant's speech and labels to German or English.",
            new ToolParameter("language", ToolParameterType.String, "Language code.", allowedValues: new[] { "de", "en" })),
        new ToolDefinition(IdentifyView,
            "Capture one camera frame of what the user is looking at and identify the part. Only on the user's request."),
    };

    /// <param name="includeVision">False until camera identification exists (M2): the model is never offered a tool
    /// that can only fail.</param>
    public static ToolRegistry CreateRegistry(bool includeVision = true)
    {
        var registry = new ToolRegistry();
        foreach (var definition in CreateV1())
        {
            if (!includeVision && definition.Name == IdentifyView)
            {
                continue;
            }

            registry.Register(definition);
        }

        return registry;
    }
}
