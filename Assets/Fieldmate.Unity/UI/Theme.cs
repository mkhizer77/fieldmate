using TMPro;
using UnityEngine;

namespace Fieldmate.UI;

/// <summary>
/// The one visual language for every panel, tag, label and button (#53): a dark translucent surface with a hairline
/// stroke, one accent, semantic colours for state, and a small type scale in Inter. Canvas units are millimetres
/// (world canvases are scaled 0.001), so a size-26 body line is legible at about 1.5 m on Quest 3.
/// </summary>
public static class Theme
{
    // Surfaces
    public static readonly Color Surface = new(0.055f, 0.065f, 0.085f, 0.92f);
    public static readonly Color SurfaceRaised = new(1f, 1f, 1f, 0.07f);
    public static readonly Color Stroke = new(1f, 1f, 1f, 0.12f);
    public static readonly Color Scrim = new(0.03f, 0.035f, 0.05f, 0.7f);

    // Text
    public static readonly Color TextPrimary = new(0.96f, 0.97f, 0.98f);
    public static readonly Color TextSecondary = new(0.68f, 0.72f, 0.78f);
    public static readonly Color TextMuted = new(0.46f, 0.50f, 0.56f);
    public static readonly Color TextOnAccent = new(0.03f, 0.08f, 0.11f);

    // Accent and semantics
    public static readonly Color Accent = new(0.21f, 0.82f, 1f);
    public static readonly Color AccentSoft = new(0.21f, 0.82f, 1f, 0.18f);
    public static readonly Color Success = new(0.36f, 0.89f, 0.54f);
    public static readonly Color Warning = new(1f, 0.77f, 0.30f);
    public static readonly Color Danger = new(1f, 0.42f, 0.38f);
    public static readonly Color Neutral = new(0.62f, 0.66f, 0.72f);

    // Buttons (physical, in the scene)
    public static readonly Color ButtonPrimary = Accent;
    public static readonly Color ButtonSecondary = new(0.20f, 0.22f, 0.26f);
    public static readonly Color ButtonMuted = new(0.13f, 0.14f, 0.17f);
    public static readonly Color ButtonBezel = new(0.07f, 0.075f, 0.09f);

    // Type scale (canvas units)
    public const float Eyebrow = 20f;
    public const float Caption = 22f;
    public const float Body = 26f;
    public const float Title = 32f;
    public const float Display = 72f;

    // Layout (canvas units)
    public const float Radius = 22f;
    public const float Pad = 36f;
    public const float Gap = 16f;
    public const float HairLine = 2f;

    public const string AccentHex = "#36D1FF";
    public const string MutedHex = "#767F8F";
    public const string SecondaryHex = "#ADB8C7";
    public const string DangerHex = "#FF6B61";
    public const string SuccessHex = "#5CE38A";
    public const string WarningHex = "#FFC44D";

    private static TMP_FontAsset regular;
    private static TMP_FontAsset semiBold;

    public static TMP_FontAsset Regular => regular != null ? regular : regular = Load("Fonts/InterRegular SDF");
    public static TMP_FontAsset SemiBold => semiBold != null ? semiBold : semiBold = Load("Fonts/InterSemiBold SDF");

    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    private static TMP_FontAsset Load(string path)
    {
        var font = Resources.Load<TMP_FontAsset>(path);
        if (font == null)
        {
            Debug.LogWarning($"[Theme] {path} missing; run Fieldmate → Build UI Fonts. Falling back to the TMP default.");
            font = TMP_Settings.defaultFontAsset;
        }

        return font;
    }
}
