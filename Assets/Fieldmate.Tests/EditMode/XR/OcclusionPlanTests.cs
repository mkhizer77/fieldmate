using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Fieldmate.Tests.EditMode.XR;

public class OcclusionPlanTests
{
    [Test]
    public void Hard_UsesDepth_WithHardShaderKeyword_AndNoBlend()
    {
        var plan = OcclusionPlan.For(OcclusionMode.Hard);
        Assert.That((plan.EnvironmentDepth, plan.ShaderMode, plan.SoftBlend), Is.EqualTo((true, AROcclusionShaderMode.HardOcclusion, 0f)));
    }

    [Test]
    public void Soft_BlendsInOurShader_WithoutARFoundationsPreprocessingPass()
    {
        var plan = OcclusionPlan.For(OcclusionMode.Soft);
        Assert.That(plan.ShaderMode, Is.EqualTo(AROcclusionShaderMode.HardOcclusion), "SoftOcclusion mode would add a GPU pass");
        Assert.That(plan.SoftBlend, Is.EqualTo(1f));
    }

    [Test]
    public void Off_StopsEnvironmentDepth()
    {
        var plan = OcclusionPlan.For(OcclusionMode.Off);
        Assert.That((plan.EnvironmentDepth, plan.ShaderMode), Is.EqualTo((false, AROcclusionShaderMode.None)));
    }

    [Test]
    public void Button_CyclesThroughAllThreeModes()
    {
        Assert.That(OcclusionPlan.Next(OcclusionMode.Hard), Is.EqualTo(OcclusionMode.Soft));
        Assert.That(OcclusionPlan.Next(OcclusionMode.Soft), Is.EqualTo(OcclusionMode.Off));
        Assert.That(OcclusionPlan.Next(OcclusionMode.Off), Is.EqualTo(OcclusionMode.Hard));
        Assert.That(OcclusionPlan.Label(OcclusionMode.Soft), Is.EqualTo("Occlusion: Soft"));
    }

    [Test]
    public void OccludedLitShader_Exists_AndSupportsBothKeywords()
    {
        var shader = Shader.Find("Fieldmate/OccludedLit");
        Assert.That(shader, Is.Not.Null);
        Assert.That(shader.isSupported, Is.True);
        var keywords = shader.keywordSpace.keywordNames;
        Assert.That(keywords, Does.Contain("XR_HARD_OCCLUSION"));
        Assert.That(keywords, Does.Contain("XR_SOFT_OCCLUSION"));
    }
}
