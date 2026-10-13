using Fieldmate.Localization;
using Fieldmate.UI;

namespace Fieldmate.Interaction;

/// <summary>Text for a control's tag: "Main breaker" on the first line, "motor power · ON" on the second.</summary>
public static class ControlTagText
{
    public static string Format(string title, string purpose, string state)
    {
        var detail = string.IsNullOrEmpty(state)
            ? purpose ?? string.Empty
            : string.IsNullOrEmpty(purpose) ? StateWord(state) : $"{purpose} · {StateWord(state)}";
        return string.IsNullOrEmpty(detail) ? title : $"{title}\n<size=26>{detail}</size>";
    }

    /// <summary>The same text with the purpose in the secondary colour and the state in its meaning's colour.</summary>
    public static string Rich(string title, string purpose, string state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return string.IsNullOrEmpty(purpose) ? title : $"{title}\n<size=26><color={Theme.SecondaryHex}>{purpose}</color></size>";
        }

        var stateText = $"<color={TagStateStyle.Hex(state)}>{StateWord(state)}</color>";
        return string.IsNullOrEmpty(purpose)
            ? $"{title}\n<size=26>{stateText}</size>"
            : $"{title}\n<size=26><color={Theme.SecondaryHex}>{purpose} · </color>{stateText}</size>";
    }

    /// <summary>The state as the tag shows it: in the current language (#24), upper case ("ON", "GESPERRT").</summary>
    private static string StateWord(string state) => Lang.T(state).ToUpperInvariant();
}
