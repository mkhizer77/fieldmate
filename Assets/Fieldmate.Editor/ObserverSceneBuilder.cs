using System.Linq;
using Fieldmate.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fieldmate.Editor;

/// <summary>Generates the observer app's scene (#26): a camera and the dashboard. Nothing is hand-edited.</summary>
public static class ObserverSceneBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/Observer.unity";

    [MenuItem("Fieldmate/Build Observer Scene")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var type in new[] { typeof(ObserverNetwork), typeof(ObserverDashboard), typeof(ObserverPreview) })
        {
            if (!AssetDatabase.FindAssets($"t:MonoScript {type.Name}").Any())
            {
                throw new System.InvalidOperationException($"Script {type.Name} is not an imported asset yet; run the builder again.");
            }
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.04f, 0.055f);

        var observer = new GameObject("Observer", typeof(ObserverNetwork), typeof(ObserverDashboard), typeof(ObserverPreview));
        observer.GetComponent<ObserverDashboard>().Configure(observer.GetComponent<ObserverNetwork>());
        observer.GetComponent<ObserverPreview>().Configure(observer.GetComponent<ObserverDashboard>()); // -preview: no headset needed
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"Observer scene written to {ScenePath}");
    }
}
