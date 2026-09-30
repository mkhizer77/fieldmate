namespace Fieldmate.Interaction;

/// <summary>Text for a control's tag: "Main breaker" on the first line, "motor power · ON" on the second.</summary>
public static class ControlTagText
{
    public static string Format(string title, string purpose, string state)
    {
        var detail = string.IsNullOrEmpty(state)
            ? purpose ?? string.Empty
            : string.IsNullOrEmpty(purpose) ? state.ToUpperInvariant() : $"{purpose} · {state.ToUpperInvariant()}";
        return string.IsNullOrEmpty(detail) ? title : $"{title}\n<size=26>{detail}</size>";
    }
}
