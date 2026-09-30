using UnityEngine.XR.ARFoundation;

namespace Fieldmate.XR;

public enum OcclusionMode
{
    Hard,
    Soft,
    Off,
}

/// <summary>
/// What each occlusion mode switches on (#6). Soft and Hard both use AR Foundation's hard-occlusion globals (no depth
/// preprocessing pass); <c>Fieldmate/OccludedLit</c> blends the edges itself when <see cref="SoftBlend"/> is 1. Off stops
/// environment depth entirely, so it costs nothing.
/// </summary>
public readonly struct OcclusionPlan
{
    public const string SoftBlendProperty = "_FieldmateSoftOcclusion";

    private OcclusionPlan(bool depth, AROcclusionShaderMode shaderMode, float softBlend)
    {
        EnvironmentDepth = depth;
        ShaderMode = shaderMode;
        SoftBlend = softBlend;
    }

    public bool EnvironmentDepth { get; }
    public AROcclusionShaderMode ShaderMode { get; }
    public float SoftBlend { get; }

    public static OcclusionPlan For(OcclusionMode mode) => mode switch
    {
        OcclusionMode.Hard => new OcclusionPlan(true, AROcclusionShaderMode.HardOcclusion, 0f),
        OcclusionMode.Soft => new OcclusionPlan(true, AROcclusionShaderMode.HardOcclusion, 1f),
        _ => new OcclusionPlan(false, AROcclusionShaderMode.None, 0f),
    };

    /// <summary>The button cycles Hard → Soft → Off → Hard.</summary>
    public static OcclusionMode Next(OcclusionMode mode) => mode switch
    {
        OcclusionMode.Hard => OcclusionMode.Soft,
        OcclusionMode.Soft => OcclusionMode.Off,
        _ => OcclusionMode.Hard,
    };

    public static string Label(OcclusionMode mode) => $"Occlusion: {mode}";
}
