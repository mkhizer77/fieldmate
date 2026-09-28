using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace Fieldmate.Tests.EditMode
{
    /// <summary>
    /// Unity maps a script file to its MonoBehaviour class by parsing the file, and it does not understand C# 10
    /// file-scoped namespaces. Such components are saved with in-memory script references and can load as
    /// "missing script". MonoBehaviour and ScriptableObject files therefore use block-scoped namespaces.
    /// </summary>
    public class ScriptMappingTests
    {
        private static readonly string[] Roots = { "Assets/Fieldmate.Unity", "Assets/Fieldmate.Editor" };
        private static readonly Regex ComponentClass = new(@"class\s+\w+\s*:\s*(MonoBehaviour|ScriptableObject)\b");

        [Test]
        public void EveryComponentScript_MapsToItsClass()
        {
            var unmapped = AssetDatabase.FindAssets("t:MonoScript", Roots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => (path, script: AssetDatabase.LoadAssetAtPath<MonoScript>(path)))
                .Where(x => ComponentClass.IsMatch(x.script.text))
                .Where(x => x.script.GetClass() == null)
                .Select(x => x.path)
                .ToArray();

            Assert.That(unmapped, Is.Empty, "Use a block-scoped namespace in MonoBehaviour/ScriptableObject files.");
        }
    }
}
