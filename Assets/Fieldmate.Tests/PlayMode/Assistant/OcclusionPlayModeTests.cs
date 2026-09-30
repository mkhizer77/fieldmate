using System.Collections;
using System.Linq;
using Fieldmate.Assistant;
using Fieldmate.Procedures;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode.Assistant;

public class OcclusionPlayModeTests
{
    private const string PrefsKey = "fieldmate.occlusion";

    [UnitySetUp]
    public IEnumerator LoadBench()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        yield return SceneManager.LoadSceneAsync("AssistantBench", LoadSceneMode.Single);
        yield return null;
    }

    [TearDown]
    public void ClearPrefs() => PlayerPrefs.DeleteKey(PrefsKey);

    [UnityTest]
    public IEnumerator EveryMachineMaterial_IsOccludable()
    {
        var skid = Object.FindAnyObjectByType<MachineServices>().Parts.Values.First().transform.root;
        var shaders = skid.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.GetComponentInParent<Canvas>() == null)
            .Select(r => r.sharedMaterial.shader.name).Distinct().ToArray();
        Assert.That(shaders, Is.EqualTo(new[] { "Fieldmate/OccludedLit" }), "real furniture must be able to hide every part");
        yield break;
    }

    [UnityTest]
    public IEnumerator Button_CyclesTheMode_LabelsIt_AndRemembersIt()
    {
        var settings = Object.FindAnyObjectByType<OcclusionSettings>();
        var button = Object.FindObjectsByType<PressButton>(FindObjectsSortMode.None).Single(b => b.name == "Occlusion Button");
        Assert.That(settings.Mode, Is.EqualTo(OcclusionMode.Hard), "default");
        Assert.That(button.Label, Is.EqualTo("Occlusion: Hard"));

        button.Press();
        yield return null;
        Assert.That(settings.Mode, Is.EqualTo(OcclusionMode.Soft));
        Assert.That(button.Label, Is.EqualTo("Occlusion: Soft"));
        Assert.That(Shader.GetGlobalFloat(OcclusionPlan.SoftBlendProperty), Is.EqualTo(1f));
        Assert.That(PlayerPrefs.GetInt(PrefsKey), Is.EqualTo((int)OcclusionMode.Soft), "kept between sessions");

        settings.SetMode(OcclusionMode.Off);
        Assert.That(Shader.GetGlobalFloat(OcclusionPlan.SoftBlendProperty), Is.Zero);
    }
}
