using System.Linq;
using Fieldmate.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fieldmate.Tests.EditMode.XR;

/// <summary>Device test 2026-09-30: shaders found only by name at runtime were stripped from the APK.</summary>
public class RuntimeShaderTests
{
    [Test]
    public void Runtime_shaders_are_always_included_in_builds()
    {
        var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").First();
        var list = new SerializedObject(graphics).FindProperty("m_AlwaysIncludedShaders");
        var included = Enumerable.Range(0, list.arraySize)
            .Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as Shader)
            .Where(sh => sh != null).Select(sh => sh.name).ToList();
        foreach (var name in ProjectSetup.RuntimeShaders)
        {
            Assert.That(Shader.Find(name), Is.Not.Null, name);
            Assert.That(included, Does.Contain(name), $"{name}: run Fieldmate → Configure Project for Quest");
        }
    }
}
