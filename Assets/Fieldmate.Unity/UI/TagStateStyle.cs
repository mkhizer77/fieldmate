using UnityEngine;

namespace Fieldmate.UI;

/// <summary>What a control state means at a glance, so its tag colours it consistently across machines.</summary>
public enum StateMeaning
{
    /// <summary>Energised, flowing, running: needs attention before work (amber).</summary>
    Live,

    /// <summary>De-energised, closed, stopped (neutral).</summary>
    Passive,

    /// <summary>Secured against restart: locked, tagged (green).</summary>
    Safe,

    /// <summary>Not a state, or unknown (muted).</summary>
    None,
}

public static class TagStateStyle
{
    public static StateMeaning Meaning(string state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return StateMeaning.None;
        }

        switch (state.Trim().ToLowerInvariant())
        {
            case "on":
            case "open":
            case "running":
            case "energised":
            case "energized":
            case "live":
                return StateMeaning.Live;
            case "locked":
            case "tagged":
            case "secured":
            case "isolated":
                return StateMeaning.Safe;
            case "off":
            case "closed":
            case "stopped":
            case "removed":
            case "fitted":
            case "seated":
                return StateMeaning.Passive;
            default:
                return StateMeaning.None;
        }
    }

    public static Color Color(string state) => Meaning(state) switch
    {
        StateMeaning.Live => Theme.Warning,
        StateMeaning.Safe => Theme.Success,
        StateMeaning.Passive => Theme.Neutral,
        _ => Theme.TextMuted,
    };

    public static string Hex(string state) => Meaning(state) switch
    {
        StateMeaning.Live => Theme.WarningHex,
        StateMeaning.Safe => Theme.SuccessHex,
        StateMeaning.Passive => Theme.SecondaryHex,
        _ => Theme.MutedHex,
    };
}
