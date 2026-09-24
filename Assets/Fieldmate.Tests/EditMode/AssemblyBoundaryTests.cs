using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Fieldmate.Tests.EditMode;

/// <summary>Guards the module rule from design.md §5.2: Fieldmate.Core stays engine-light and XR-free.</summary>
public class AssemblyBoundaryTests
{
    private static readonly string[] ForbiddenPrefixes =
    {
        "UnityEditor", "Unity.", "UnityEngine.XR", "Fieldmate.Unity", "Fieldmate.Editor", "Fieldmate.Net",
    };

    // UnityEngine math types (Vector3, Quaternion, Mathf) live in CoreModule; every other engine module is off limits.
    private static readonly string[] AllowedEngineAssemblies = { "UnityEngine", "UnityEngine.CoreModule" };

    [Test]
    public void Core_ReferencesNoEditorXrOrPackageAssemblies()
    {
        var core = Assembly.Load("Fieldmate.Core");

        var offending = core.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(IsForbidden)
            .ToArray();

        Assert.That(offending, Is.Empty, "Fieldmate.Core must not depend on editor, XR, package or Unity-layer assemblies.");
    }

    [Test]
    public void CoreAsmdef_DeclaresNoReferences()
    {
        var path = Path.Combine(Application.dataPath, "Fieldmate.Core", "Fieldmate.Core.asmdef");
        var asmdef = JsonUtility.FromJson<AsmdefFile>(File.ReadAllText(path));

        Assert.That(asmdef.name, Is.EqualTo("Fieldmate.Core"));
        Assert.That(asmdef.references, Is.Empty);
    }

    private static bool IsForbidden(string name)
    {
        if (name.StartsWith("UnityEngine", StringComparison.Ordinal))
        {
            return !AllowedEngineAssemblies.Contains(name);
        }
        return ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    [Serializable]
    private sealed class AsmdefFile
    {
        public string name;
        public string[] references = Array.Empty<string>();
    }
}
