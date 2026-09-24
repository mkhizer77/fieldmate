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
    public void KeysTemplate_ParsesAndContainsNoKeys()
    {
        var path = Path.Combine(Application.dataPath, "_Project", "Secrets", "keys.json.template");
        var keys = JsonUtility.FromJson<KeysFile>(File.ReadAllText(path));

        var providers = new[] { keys.chat, keys.stt, keys.tts, keys.vision };
        Assert.That(providers, Has.None.Null);
        Assert.That(providers.Select(p => p.provider), Has.None.Empty);
        Assert.That(providers.Select(p => p.apiKey), Has.All.Empty, "The committed template must never contain a key.");
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
        public ProviderKey chat;
        public ProviderKey stt;
        public ProviderKey tts;
        public ProviderKey vision;
    }

    [Serializable]
    private sealed class ProviderKey
    {
        public string provider;
        public string apiKey;
    }
}
