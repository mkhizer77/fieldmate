using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fieldmate.Tests.PlayMode;

public class MainSceneSmokeTests
{
    [UnityTest]
    public IEnumerator MainScene_LoadsWithCamera()
    {
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main"));
        Assert.That(Object.FindAnyObjectByType<Camera>(), Is.Not.Null);
    }
}
