namespace Fieldmate.Localization;

public enum GermanCase
{
    Nominative,
    Accusative,
    Dative,
}

/// <summary>The bits of German grammar the scripted lines need (#24): definite articles by gender and case.</summary>
public static class German
{
    /// <summary>Definite article for a noun of <paramref name="gender"/> (m, f, n, pl) in <paramref name="grammaticalCase"/>.</summary>
    public static string Article(string gender, GermanCase grammaticalCase) => (gender, grammaticalCase) switch
    {
        ("m", GermanCase.Nominative) => "der",
        ("m", GermanCase.Accusative) => "den",
        ("m", GermanCase.Dative) => "dem",
        ("f", GermanCase.Dative) => "der",
        ("f", _) => "die",
        ("pl", GermanCase.Dative) => "den",
        ("pl", _) => "die",
        (_, GermanCase.Dative) => "dem",
        _ => "das",
    };

    /// <summary>Accusative personal pronoun for a noun of <paramref name="gender"/>: ihn, sie, es.</summary>
    public static string Pronoun(string gender) => gender switch
    {
        "m" => "ihn",
        "f" => "sie",
        "pl" => "sie",
        _ => "es",
    };

    /// <summary>A number the German way: decimal comma, at most one decimal ("0,2", "4").</summary>
    public static string Number(float value) =>
        value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
}
