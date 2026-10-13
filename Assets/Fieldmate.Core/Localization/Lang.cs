using System;
using System.Globalization;

namespace Fieldmate.Localization;

/// <summary>
/// The app's language (#24): "en" or "de", switched by the set_language tool or the hand menu. Short UI strings are
/// looked up by their English text in <see cref="GermanText"/>; sentences that need German grammar (articles, word
/// order) are built per language by their owners (Narration, StepInstructions, SetupScript, FallbackResponses).
/// </summary>
public static class Lang
{
    public const string English = "en";
    public const string German = "de";

    public static string Current { get; private set; } = English;

    public static bool IsGerman => Current == German;

    /// <summary>Raised after the language changed, with the new code. UI re-renders its labels here.</summary>
    public static event Action<string> Changed;

    /// <summary>"de" or "en"; anything else means English. False when nothing changed.</summary>
    public static bool Set(string code)
    {
        var next = Normalise(code);
        if (next == Current)
        {
            return false;
        }

        Current = next;
        Changed?.Invoke(next);
        return true;
    }

    public static string Normalise(string code) =>
        code != null && code.Trim().StartsWith(German, StringComparison.OrdinalIgnoreCase) ? German : English;

    /// <summary>The German for an English UI string while German is on; the English otherwise or when untranslated.</summary>
    public static string T(string english) =>
        IsGerman && english != null && GermanText.Table.TryGetValue(english, out var german) ? german : english;

    /// <summary><see cref="T"/> of a format string, then formatted (invariant culture for numbers).</summary>
    public static string F(string englishFormat, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(englishFormat), args);

    /// <summary>For tests: back to English without notifying anyone.</summary>
    public static void ResetForTests() => Current = English;
}
