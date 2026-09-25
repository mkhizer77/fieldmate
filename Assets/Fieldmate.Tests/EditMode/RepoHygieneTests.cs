using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fieldmate.Tests.EditMode;

/// <summary>Public-repo rules from design.md §8: secrets and paid assets stay out of git, the project builds without them.</summary>
public class RepoHygieneTests
{
    private const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    [TestCase("/Assets/ThirdParty/")]
    [TestCase("/Assets/_Project/Secrets/keys.json")]
    [TestCase("/context/")]
    [TestCase("/seed/")]
    public void Gitignore_CoversLocalOnlyPath(string pattern)
    {
        var lines = File.ReadAllLines(Path.Combine(ProjectRoot, ".gitignore")).Select(l => l.Trim());

        Assert.That(lines, Does.Contain(pattern));
    }

    [Test]
    public void KeysTemplate_HoldsOnlyAProxyUrl_AndNoToken()
    {
        // ADR-003: provider keys live in the proxy; keys.json carries the proxy URL and an optional dev token.
        var path = Path.Combine(Application.dataPath, "_Project", "Secrets", "keys.json.template");
        var keys = JsonUtility.FromJson<KeysFile>(File.ReadAllText(path));

        Assert.That(keys.proxyUrl, Does.StartWith("https://"));
        Assert.That(keys.devToken, Is.Empty, "The committed template must never contain a token.");
        Assert.That(File.ReadAllText(path), Does.Not.Contain("apiKey"));
    }

    [Test]
    public void BuildSettings_ContainMainSceneEnabled()
    {
        var scene = EditorBuildSettings.scenes.FirstOrDefault(s => s.path == MainScenePath);

        Assert.That(scene, Is.Not.Null, $"{MainScenePath} missing from Build Settings.");
        Assert.That(scene.enabled, Is.True);
    }

    [Serializable]
    private sealed class KeysFile
    {
        public string proxyUrl;
        public string devToken;
    }
}
